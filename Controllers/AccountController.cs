using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace AqarakOnline.Controllers;

public class LoginViewModel
{
    [Required(ErrorMessage = "رقم الموبايل مطلوب")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "الاسم مطلوب")]
    [RegularExpression(@"^[\u0600-\u06FFa-zA-Z\s\-']{2,100}$", ErrorMessage = "الاسم يجب أن يكون بالعربي أو الإنجليزي فقط")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "العنوان مطلوب")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الموبايل مطلوب")]
    [RegularExpression(@"^01[0-9]{9}$", ErrorMessage = "رقم الموبايل يجب أن يكون 11 رقمًا بالضبط ويبدأ بـ 01")]
    public string PhoneNumber { get; set; } = string.Empty;

    // اختياري؛ يُستخدم فقط لمعرفة حساب الأدمن (إذا طابق AdminEmail).
    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "كلمة المرور يجب أن تكون 8 أحرف على الأقل، وتحتوي على حرف كبير وحرف صغير ورقم")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "كلمة المرور وتأكيدها غير متطابقين")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

/// <summary>
/// الدخول برقم الموبايل (11 رقمًا بالضبط)، وليس بالإيميل.
/// التوثيق الآن عن طريق الأدمن، وليس برسالة موبايل: الحساب الجديد يبدأ
/// قيد المراجعة (Pending) ولا يدخل حتى يوافق عليه الأدمن. حساب الأدمن
/// (الإيميل المطابق لـ AdminEmail) يُفعَّل فورًا حتى لا يُحبس خارج النظام.
/// الحساب الموقوف أو المحظور لا يدخل، مع رسالة توضح السبب.
/// التأكيد برسائل SMS موقوف مؤقتًا لحين ترتيب خدمة مدفوعة.
/// </summary>
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> _hub;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _configuration = configuration;
        _hub = hub;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var phone = PhoneHelper.Normalize(model.PhoneNumber);
        var user = await _userManager.FindByNameAsync(phone);
        if (user == null)
        {
            // رسالة عامة عمدًا: لا نكشف إن كان الرقم مسجَّلًا أم لا.
            ModelState.AddModelError(string.Empty, "رقم الموبايل أو كلمة المرور غير صحيحة.");
            return View(model);
        }

        switch (user.AccountStatus)
        {
            case AccountStatus.Pending:
                ModelState.AddModelError(string.Empty, "حسابك قيد المراجعة من الأدمن، وسيتم تفعيله بعد الموافقة.");
                return View(model);
            case AccountStatus.Suspended:
                ModelState.AddModelError(string.Empty, "حسابك موقوف مؤقتًا. تواصل مع إدارة الموقع.");
                return View(model);
            case AccountStatus.Blocked:
                ModelState.AddModelError(string.Empty, "حسابك محظور. تواصل مع إدارة الموقع.");
                return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            phone, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Home");
        }
        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "تم قفل الحساب مؤقتًا لكثرة محاولات الدخول الخاطئة. حاول بعد 15 دقيقة.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "رقم الموبايل أو كلمة المرور غير صحيحة.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register()
    {
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!NameHelper.IsValidName(model.FullName))
        {
            ModelState.AddModelError(nameof(model.FullName), "الاسم يجب أن يكون بالعربي أو الإنجليزي فقط");
            return View(model);
        }
        var phone = PhoneHelper.Normalize(model.PhoneNumber);
        if (!PhoneHelper.IsValidEgyptianMobile(phone))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل يجب أن يكون 11 رقمًا بالضبط ويبدأ بـ 01");
            return View(model);
        }
        if (await _userManager.FindByNameAsync(phone) != null)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل هذا مسجَّل بالفعل.");
            return View(model);
        }

        var adminEmail = _configuration["AdminEmail"];
        var isAdminAccount = !string.IsNullOrWhiteSpace(adminEmail)
            && !string.IsNullOrWhiteSpace(model.Email)
            && string.Equals(model.Email.Trim(), adminEmail, StringComparison.OrdinalIgnoreCase);

        var user = new ApplicationUser
        {
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = false,
            FullName = model.FullName.Trim(),
            Address = model.Address,
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
            AccountStatus = isAdminAccount ? AccountStatus.Active : AccountStatus.Pending
        };
        var result = await _userManager.CreateAsync(user, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }

        await _userManager.AddToRoleAsync(user, isAdminAccount ? "Admin" : "User");

        // حساب جديد قيد المراجعة = رقم جديد عند الأدمن، لحظيًا.
        if (!isAdminAccount)
        {
            await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
        }

        if (isAdminAccount)
        {
            TempData["Info"] = "تم إنشاء حساب الأدمن وتفعيله.";
            return RedirectToAction(nameof(Login));
        }
        TempData["Info"] = "تم إنشاء حسابك بنجاح، وهو الآن قيد مراجعة الأدمن، وسيتم تفعيله بعد الموافقة.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    /// <summary>
    /// الدخول بجوجل. يعمل فقط لو Google:ClientId/Secret متعبّيين (راجع Program.cs).
    /// حساب جوجل الجديد يبدأ قيد المراجعة مثل التسجيل العادي، إلا لو إيميله هو
    /// AdminEmail فيُفعَّل ويُمنح دور الأدمن. لو إيميل جوجل مربوط بحساب موجود
    /// بالفعل (مسجَّل برقم موبايل)، يُربط به بدل إنشاء حساب جديد.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ExternalLogin(string provider, string? returnUrl = null)
    {
        if (provider != "Google" || string.IsNullOrWhiteSpace(_configuration["Google:ClientId"]))
        {
            TempData["Error"] = "الدخول بجوجل غير مفعّل بعد — ضع ClientId وClientSecret في الإعدادات.";
            return RedirectToAction(nameof(Login));
        }
        var redirectUrl = Url.Action(nameof(ExternalLoginCallback), new { returnUrl });
        var properties = _signInManager.ConfigureExternalAuthenticationProperties("Google", redirectUrl);
        return Challenge(properties, "Google");
    }

    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null)
    {
        var info = await _signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            TempData["Error"] = "تعذّر إكمال الدخول بجوجل. حاول مرة أخرى.";
            return RedirectToAction(nameof(Login));
        }

        // حساب مربوط بجوجل من قبل.
        var signIn = await _signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, isPersistent: false);
        if (signIn.Succeeded)
        {
            var existing = await _userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (existing != null)
            {
                var blocked = AccountStatusMessage(existing);
                if (blocked != null)
                {
                    await _signInManager.SignOutAsync();
                    TempData["Error"] = blocked;
                    return RedirectToAction(nameof(Login));
                }
            }
            return RedirectToLocal(returnUrl);
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrWhiteSpace(email))
        {
            // إيميل جوجل مربوط بحساب موجود (مسجَّل برقم موبايل): اربطه به.
            var byEmail = await _userManager.FindByEmailAsync(email);
            if (byEmail != null)
            {
                var blocked = AccountStatusMessage(byEmail);
                if (blocked != null)
                {
                    TempData["Error"] = blocked;
                    return RedirectToAction(nameof(Login));
                }
                var link = await _userManager.AddLoginAsync(byEmail, info);
                if (link.Succeeded)
                {
                    await _signInManager.SignInAsync(byEmail, isPersistent: false);
                    return RedirectToLocal(returnUrl);
                }
            }
        }

        // حساب جديد من جوجل.
        if (string.IsNullOrWhiteSpace(email))
        {
            TempData["Error"] = "حساب جوجل هذا بلا إيميل، ولا يمكن إنشاء حساب به.";
            return RedirectToAction(nameof(Login));
        }
        var name = info.Principal.FindFirstValue(ClaimTypes.Name)
            ?? info.Principal.FindFirstValue(ClaimTypes.GivenName)
            ?? email.Split('@')[0];
        var adminEmail = _configuration["AdminEmail"];
        var isAdminAccount = !string.IsNullOrWhiteSpace(adminEmail)
            && string.Equals(email.Trim(), adminEmail, StringComparison.OrdinalIgnoreCase);
        var user = new ApplicationUser
        {
            UserName = email.Trim(),
            Email = email.Trim(),
            EmailConfirmed = true,
            FullName = name.Trim(),
            Address = string.Empty,
            AccountStatus = isAdminAccount ? AccountStatus.Active : AccountStatus.Pending
        };
        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            TempData["Error"] = "تعذّر إنشاء الحساب من جوجل. جرّب التسجيل برقم الموبايل.";
            return RedirectToAction(nameof(Login));
        }
        await _userManager.AddLoginAsync(user, info);
        await _userManager.AddToRoleAsync(user, isAdminAccount ? "Admin" : "User");
        if (!isAdminAccount)
        {
            await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
            TempData["Info"] = "تم إنشاء حسابك بجوجل، وهو الآن قيد مراجعة الأدمن، وسيتم تفعيله بعد الموافقة.";
            return RedirectToAction(nameof(Login));
        }
        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToLocal(returnUrl);
    }

    private static string? AccountStatusMessage(ApplicationUser user) => user.AccountStatus switch
    {
        AccountStatus.Pending => "حسابك قيد المراجعة من الأدمن، وسيتم تفعيله بعد الموافقة.",
        AccountStatus.Suspended => "حسابك موقوف مؤقتًا. تواصل مع إدارة الموقع.",
        AccountStatus.Blocked => "حسابك محظور. تواصل مع إدارة الموقع.",
        _ => null
    };

    private IActionResult RedirectToLocal(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}
