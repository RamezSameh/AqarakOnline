using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AqarakOnline.Data;
using AqarakOnline.Models;
using Microsoft.EntityFrameworkCore;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Authorization;
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

    /// <summary>موافقة إجبارية على شروط الاستخدام قبل إنشاء الحساب.</summary>
    public bool AcceptTerms { get; set; }
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
    private readonly ApplicationDbContext _context;

    public AccountController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        IConfiguration configuration,
        Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub,
        ApplicationDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _configuration = configuration;
        _hub = hub;
        _context = context;
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

        if (!model.AcceptTerms)
        {
            ModelState.AddModelError(nameof(model.AcceptTerms), "يجب الموافقة على شروط الاستخدام للتسجيل.");
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
        if (await _context.BlockedPhones.AnyAsync(b => b.PhoneNumber == phone))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "هذا الرقم محظور من التسجيل.");
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

        // حساب جديد قيد المراجعة = تنبيه موجَّه للأدمن + رقم جديد عند الأدمن، لحظيًا.
        if (!isAdminAccount)
        {
            var admin = await _context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (admin != null)
            {
                _context.Notifications.Add(new Notification
                {
                    Title = "مستخدم جديد سجّل",
                    Message = $"{user.FullName} سجّل حسابًا جديدًا بانتظار الموافقة.",
                    TargetUserId = admin.Id,
                    Url = "/Admin/Users",
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
                await _context.SaveChangesAsync();
            }
            await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
            await _hub.Clients.All.SendAsync("refreshBell");
        }

        if (isAdminAccount)
        {
            TempData["Info"] = "تم إنشاء حساب الأدمن وتفعيله.";
            return RedirectToAction(nameof(Login));
        }
        TempData["Info"] = "تم إنشاء حسابك بنجاح، وهو الآن قيد مراجعة الأدمن، وسيتم تفعيله بعد الموافقة.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MyAccount()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var currentUser = await _userManager.FindByIdAsync(userId);
        var model = new MyAccountViewModel
        {
            ReceiveWhatsAppAlerts = currentUser?.ReceiveWhatsAppAlerts == true,
            MyListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == userId),
            ApprovedListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == userId && l.Approval == ListingApproval.Approved),
            PendingListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == userId && l.Approval == ListingApproval.Pending),
            FavoritesCount = await _context.Favorites.CountAsync(f => f.UserId == userId),
            SavedSearchesCount = await _context.SavedSearches.CountAsync(s => s.UserId == userId),
            UnreadNotificationsCount = await _context.Notifications
                .Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == userId)
                    && !_context.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == userId))
                .CountAsync(),
            RecentNotifications = await _context.Notifications
                .Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == userId))
                .OrderByDescending(n => n.CreatedAt).Take(5).ToListAsync(),
            LastActivityAt = new[]
            {
                await _context.Listings.Where(l => l.OwnerId == userId).MaxAsync(l => (DateTime?)l.CreatedAt),
                await _context.Notifications.Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == userId)).MaxAsync(n => (DateTime?)n.CreatedAt)
            }.Max()
        };
        ViewBag.TopViewedListings = await _context.Listings
            .Where(l => l.OwnerId == userId)
            .OrderByDescending(l => l.ViewCount).Take(5)
            .Select(l => new { l.Title, l.ViewCount, l.Id }).ToListAsync();
        return View(model);
    }

    /// <summary>
    /// طلب مراجعة حساب معلّق — من صفحة الدخول، بدون تسجيل دخول.
    /// رسالة عامة عمدًا: لا نكشف إن كان الرقم مسجَّلًا أم لا. تنبيه واحد
    /// للأدمن لكل حساب كل 24 ساعة كحد أقصى (لا تكرار).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestReview(string phoneNumber, string? reason = null)
    {
        var phone = PhoneHelper.Normalize(phoneNumber ?? string.Empty);
        var user = await _userManager.FindByNameAsync(phone);
        if (user != null && user.AccountStatus == AccountStatus.Pending)
        {
            var adminEmail = _configuration["AdminEmail"];
            var admin = !string.IsNullOrWhiteSpace(adminEmail)
                ? await _context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail)
                : null;
            if (admin != null)
            {
                var alreadyRequested = await _context.Notifications.AnyAsync(n =>
                    n.TargetUserId == admin.Id && n.Title == "طلب مراجعة حساب"
                    && n.CreatedAt >= DateTime.UtcNow.AddDays(-1)
                    && n.Message.Contains(user.PhoneNumber ?? phone));
                if (!alreadyRequested)
                {
                    _context.Notifications.Add(new Notification
                    {
                        Title = "طلب مراجعة حساب",
                        Message = $"{user.FullName} ({user.PhoneNumber}) طلب مراجعة حسابه." + (string.IsNullOrWhiteSpace(reason) ? "" : $" السبب: {reason.Trim()}"),
                        TargetUserId = admin.Id,
                        Url = "/Admin/Users",
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true
                    });
                    await _context.SaveChangesAsync();
                    await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
                    await _hub.Clients.All.SendAsync("refreshBell");
                }
            }
        }
        TempData["Info"] = "إذا كان الرقم مسجَّلًا وحسابه قيد المراجعة، سيصل طلبك للإدارة.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetWhatsAppAlerts(bool receiveWhatsAppAlerts)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }
        user.ReceiveWhatsAppAlerts = receiveWhatsAppAlerts;
        await _userManager.UpdateAsync(user);
        TempData["Info"] = receiveWhatsAppAlerts
            ? "تم تفعيل تنبيهات واتساب — هتوصلك رسالة لما ينزل إعلان مطابق لبحثك أو طلبك."
            : "تم إيقاف تنبيهات واتساب.";
        return RedirectToAction(nameof(MyAccount));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

}

public class MyAccountViewModel
{
    public int MyListingsCount { get; set; }
    public int ApprovedListingsCount { get; set; }
    public int PendingListingsCount { get; set; }
    public int FavoritesCount { get; set; }
    public int SavedSearchesCount { get; set; }
    public int UnreadNotificationsCount { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public List<Notification> RecentNotifications { get; set; } = new();

    /// <summary>تنبيهات واتساب مفعّلة؟ (Batch 41 — تميّز 4).</summary>
    public bool ReceiveWhatsAppAlerts { get; set; }
}
