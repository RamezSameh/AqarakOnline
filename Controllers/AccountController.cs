using System.ComponentModel.DataAnnotations;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _configuration = configuration;
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
}
