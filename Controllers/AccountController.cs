using System.ComponentModel.DataAnnotations;
using AqarakOnline.Data;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "العنوان مطلوب")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الموبايل مطلوب")]
    [RegularExpression(@"^(\+20|0020|0)?1[0-9]{9}$", ErrorMessage = "رقم الموبايل يجب أن يكون رقمًا مصريًا يبدأ بـ 01")]
    public string PhoneNumber { get; set; } = string.Empty;

    // اختياري؛ يُستخدم فقط لمعرفة حساب الأدمن (إذا طابق AdminEmail).
    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة المرور يجب أن تكون 6 أحرف على الأقل")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "كلمة المرور وتأكيدها غير متطابقين")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class VerifyPhoneViewModel
{
    [Required(ErrorMessage = "رقم الموبايل مطلوب")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "كود التأكيد مطلوب")]
    [StringLength(6, MinimumLength = 6, ErrorMessage = "كود التأكيد 6 أرقام")]
    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// الدخول برقم الموبايل الموثَّق، وليس بالإيميل.
/// التسجيل يتطلب الاسم والعنوان ورقم الموبايل، ثم تأكيد الرقم بكود
/// يُرسل برسالة (مزود SMS حقيقي عند ضبطه، وإلا يُكتب الكود في اللوج للتجربة).
/// لا يُسمح بالدخول قبل تأكيد رقم الموبايل.
/// </summary>
public class AccountController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _context;
    private readonly ISmsSender _smsSender;
    private readonly IConfiguration _configuration;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext context,
        ISmsSender smsSender,
        IConfiguration configuration)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
        _smsSender = smsSender;
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
            ModelState.AddModelError(string.Empty, "رقم الموبايل أو كلمة المرور غير صحيحة.");
            return View(model);
        }
        if (!user.PhoneNumberConfirmed)
        {
            return RedirectToAction(nameof(VerifyPhone), new { phone });
        }

        var result = await _signInManager.PasswordSignInAsync(
            phone, model.Password, model.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Home");
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

        var phone = PhoneHelper.Normalize(model.PhoneNumber);
        if (!PhoneHelper.IsValidEgyptianMobile(phone))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل يجب أن يكون رقمًا مصريًا يبدأ بـ 01");
            return View(model);
        }
        if (await _userManager.FindByNameAsync(phone) != null)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل هذا مسجَّل بالفعل.");
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = false,
            FullName = model.FullName,
            Address = model.Address,
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim()
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

        await SendVerificationCodeAsync(user);
        return RedirectToAction(nameof(VerifyPhone), new { phone });
    }

    [HttpGet]
    public async Task<IActionResult> VerifyPhone(string phone)
    {
        var normalized = PhoneHelper.Normalize(phone);
        var user = await _userManager.FindByNameAsync(normalized);
        if (user == null)
        {
            return RedirectToAction(nameof(Register));
        }
        if (user.PhoneNumberConfirmed)
        {
            return RedirectToAction(nameof(Login));
        }
        return View(new VerifyPhoneViewModel { PhoneNumber = normalized });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> VerifyPhone(VerifyPhoneViewModel model)
    {
        var phone = PhoneHelper.Normalize(model.PhoneNumber);
        var user = await _userManager.FindByNameAsync(phone);
        if (user == null)
        {
            return RedirectToAction(nameof(Register));
        }
        if (user.PhoneNumberConfirmed)
        {
            return RedirectToAction(nameof(Login));
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var now = DateTime.UtcNow;
        var codeRecord = await _context.PhoneVerificationCodes
            .Where(c => c.UserId == user.Id && !c.IsUsed && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync();
        if (codeRecord == null)
        {
            ModelState.AddModelError(string.Empty, "لا يوجد كود فعّال. اطلب كودًا جديدًا.");
            return View(model);
        }

        if (VerificationCodeHelper.Hash(model.Code.Trim(), user.Id) != codeRecord.CodeHash)
        {
            codeRecord.Attempts++;
            if (codeRecord.Attempts >= 5)
            {
                codeRecord.IsUsed = true;
            }
            await _context.SaveChangesAsync();
            ModelState.AddModelError(string.Empty, "الكود غير صحيح.");
            return View(model);
        }

        codeRecord.IsUsed = true;
        user.PhoneNumberConfirmed = true;
        await _context.SaveChangesAsync();
        await _userManager.UpdateAsync(user);

        // دور الأدمن يُمنح بعد التأكيد للحساب الذي إيميله يطابق AdminEmail،
        // وباقي الحسابات تأخذ دور User.
        var adminEmail = _configuration["AdminEmail"];
        var role = !string.IsNullOrWhiteSpace(adminEmail)
            && !string.IsNullOrWhiteSpace(user.Email)
            && string.Equals(user.Email, adminEmail, StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : "User";
        if (!await _userManager.IsInRoleAsync(user, role))
        {
            await _userManager.AddToRoleAsync(user, role);
        }

        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendCode(string phoneNumber)
    {
        var phone = PhoneHelper.Normalize(phoneNumber);
        var user = await _userManager.FindByNameAsync(phone);
        if (user == null || user.PhoneNumberConfirmed)
        {
            return RedirectToAction(nameof(Login));
        }
        await SendVerificationCodeAsync(user);
        TempData["Info"] = "تم إرسال كود تأكيد جديد.";
        return RedirectToAction(nameof(VerifyPhone), new { phone });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private async Task SendVerificationCodeAsync(ApplicationUser user)
    {
        var now = DateTime.UtcNow;
        var oldCodes = await _context.PhoneVerificationCodes
            .Where(c => c.UserId == user.Id && !c.IsUsed)
            .ToListAsync();
        foreach (var old in oldCodes)
        {
            old.IsUsed = true;
        }

        var code = VerificationCodeHelper.GenerateCode();
        _context.PhoneVerificationCodes.Add(new PhoneVerificationCode
        {
            UserId = user.Id,
            CodeHash = VerificationCodeHelper.Hash(code, user.Id),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(10),
            IsUsed = false,
            Attempts = 0
        });
        await _context.SaveChangesAsync();

        await _smsSender.SendVerificationCodeAsync(user.PhoneNumber ?? string.Empty, code);
    }
}
