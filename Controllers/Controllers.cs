using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AqarakOnline.Data;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AqarakOnline.Controllers;

public class LoginViewModel
{
    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "الاسم الكامل مطلوب")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "البريد الإلكتروني مطلوب")]
    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة")]
    public string Email { get; set; } = string.Empty;

    [Phone(ErrorMessage = "صيغة رقم الهاتف غير صحيحة")]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "كلمة المرور مطلوبة")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "كلمة المرور يجب أن تكون 6 أحرف على الأقل")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "كلمة المرور وتأكيدها غير متطابقين")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

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

        var result = await _signInManager.PasswordSignInAsync(
            model.Email, model.Password, model.RememberMe, lockoutOnFailure: false);
        if (result.Succeeded)
        {
            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }
            return RedirectToAction("Index", "Home");
        }

        ModelState.AddModelError(string.Empty, "البريد الإلكتروني أو كلمة المرور غير صحيحة.");
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

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber
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

        // دور الأدمن يُمنح تلقائيًا للحساب المسجل بالإيميل الموجود في AdminEmail
        // (راجع Program.cs)، وباقي الحسابات تأخذ دور User.
        var adminEmail = _configuration["AdminEmail"];
        var role = !string.IsNullOrWhiteSpace(adminEmail)
            && string.Equals(model.Email, adminEmail, StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : "User";
        await _userManager.AddToRoleAsync(user, role);

        await _signInManager.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Home");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }
}

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var listings = await _context.Listings
            .Where(l => l.IsAvailable)
            .OrderByDescending(l => l.CreatedAt)
            .Take(6)
            .ToListAsync();
        return View(listings);
    }

    public IActionResult Error()
    {
        return View();
    }
}

public class LanguageController : Controller
{
    [HttpGet]
    public IActionResult Set(string lang, string? returnUrl = null)
    {
        var culture = lang == "en" ? "en" : "ar";
        Response.Cookies.Append("aqarak-lang", culture, new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            IsEssential = true
        });
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Home");
    }
}

public class ListingsController : Controller
{
    private readonly ApplicationDbContext _context;

    public ListingsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, ListingType? listingType, PropertyType? propertyType,
        decimal? minPrice, decimal? maxPrice, bool onlyAvailable = false)
    {
        var query = _context.Listings.Include(l => l.Owner).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(l => l.Title.Contains(search) || l.Area.Contains(search));
        }
        if (listingType.HasValue)
        {
            query = query.Where(l => l.ListingType == listingType.Value);
        }
        if (propertyType.HasValue)
        {
            query = query.Where(l => l.PropertyType == propertyType.Value);
        }
        if (minPrice.HasValue)
        {
            query = query.Where(l => l.Price >= minPrice.Value);
        }
        if (maxPrice.HasValue)
        {
            query = query.Where(l => l.Price <= maxPrice.Value);
        }
        if (onlyAvailable)
        {
            query = query.Where(l => l.IsAvailable);
        }

        ViewBag.Search = search;
        ViewBag.ListingType = listingType;
        ViewBag.PropertyType = propertyType;
        ViewBag.MinPrice = minPrice;
        ViewBag.MaxPrice = maxPrice;
        ViewBag.OnlyAvailable = onlyAvailable;

        var listings = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
        return View(listings);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var listing = await _context.Listings.Include(l => l.Owner)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (listing == null)
        {
            return NotFound();
        }
        return View(listing);
    }

    [Authorize]
    [HttpGet]
    public IActionResult Create()
    {
        return View(new Listing());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Listing listing)
    {
        if (!ModelState.IsValid)
        {
            return View(listing);
        }

        listing.OwnerId = CurrentUserId;
        listing.CreatedAt = DateTime.UtcNow;
        listing.IsAvailable = true;
        listing.AdNumber = "TEMP";
        await ReadPhotoAsync(listing);
        _context.Listings.Add(listing);
        await _context.SaveChangesAsync();

        // رقم الإعلان يتولد من الـ Id بعد الحفظ، فيبقى فريدًا ومتسلسلًا.
        listing.AdNumber = $"AQ-{listing.Id:D4}";
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = listing.Id });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        return View(listing);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Listing model)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        listing.Title = model.Title;
        listing.ListingType = model.ListingType;
        listing.PropertyType = model.PropertyType;
        listing.Area = model.Area;
        listing.Description = model.Description;
        listing.Features = model.Features;
        listing.Price = model.Price;
        listing.IsAvailable = model.IsAvailable;
        if (model.PhotoFile != null)
        {
            await ReadPhotoAsync(model);
            listing.Photo = model.Photo;
            listing.PhotoContentType = model.PhotoContentType;
        }

        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        return View(listing);
    }

    [Authorize]
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        _context.Listings.Remove(listing);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(MyListings));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MyListings()
    {
        var listings = await _context.Listings
            .Where(l => l.OwnerId == CurrentUserId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        return View(listings);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkUnavailable(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        listing.IsAvailable = false;
        await _context.SaveChangesAsync();
        return RedirectToAction("Listings", "Admin");
    }

    private static async Task ReadPhotoAsync(Listing listing)
    {
        if (listing.PhotoFile == null || listing.PhotoFile.Length == 0)
        {
            return;
        }
        if (!listing.PhotoFile.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        using var stream = new MemoryStream();
        await listing.PhotoFile.CopyToAsync(stream);
        listing.Photo = stream.ToArray();
        listing.PhotoContentType = listing.PhotoFile.ContentType;
    }
}

public class ContactController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ContactSettings _contact;

    public ContactController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IOptions<ContactSettings> contact)
    {
        _context = context;
        _userManager = userManager;
        _contact = contact.Value;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Contact(int listingId, string? intent = null)
    {
        var listing = await _context.Listings.Include(l => l.Owner)
            .FirstOrDefaultAsync(l => l.Id == listingId);
        if (listing == null)
        {
            return NotFound();
        }
        if (!listing.IsAvailable)
        {
            TempData["Error"] = "هذا الإعلان لم يعد متاحًا.";
            return RedirectToAction("Details", "Listings", new { id = listingId });
        }

        var user = await _userManager.GetUserAsync(User);
        var parsedIntent = intent?.ToLowerInvariant() switch
        {
            "buy" => ContactIntent.Buy,
            "rent" => ContactIntent.Rent,
            _ => listing.ListingType == ListingType.Sale ? ContactIntent.Buy : ContactIntent.Rent
        };

        ViewBag.Listing = listing;
        ViewBag.ContactPhone = _contact.Phone;
        ViewBag.ContactWhatsApp = _contact.WhatsApp;
        ViewBag.ContactEmail = _contact.Email;
        return View(new ContactRequest
        {
            ListingId = listing.Id,
            Intent = parsedIntent,
            BuyerId = CurrentUserId,
            BuyerName = user?.FullName ?? user?.UserName ?? string.Empty,
            BuyerPhone = user?.PhoneNumber ?? string.Empty
        });
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Contact(ContactRequest request)
    {
        var listing = await _context.Listings.FindAsync(request.ListingId);
        if (listing == null)
        {
            return NotFound();
        }
        if (!ModelState.IsValid)
        {
            ViewBag.Listing = listing;
            ViewBag.ContactPhone = _contact.Phone;
            ViewBag.ContactWhatsApp = _contact.WhatsApp;
            ViewBag.ContactEmail = _contact.Email;
            return View(request);
        }

        request.BuyerId = CurrentUserId;
        request.CreatedAt = DateTime.UtcNow;
        request.IsReplied = false;
        _context.ContactRequests.Add(request);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Success), new { id = request.Id });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Success(int id)
    {
        var request = await _context.ContactRequests.Include(r => r.Listing)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (request == null)
        {
            return NotFound();
        }
        if (request.BuyerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        // رسالة واتساب جاهزة: بيانات المشتري + رقم الإعلان + الغرض.
        // السيرفر لا يرسل واتساب بنفسه؛ الرابط يفتح محادثة جاهزة للإرسال من هاتف المشتري.
        var listing = request.Listing;
        var intentText = request.Intent == ContactIntent.Buy ? "شراء"
            : request.Intent == ContactIntent.Rent ? "استئجار" : "استفسار";
        var text = $"مرحبًا، أنا {request.BuyerName} ({request.BuyerPhone})، مهتم بـ{intentText} الإعلان رقم {listing?.AdNumber} - {listing?.Title}." +
            (string.IsNullOrWhiteSpace(request.Message) ? string.Empty : $" رسالتي: {request.Message}");
        ViewBag.WhatsAppUrl = $"https://wa.me/{_contact.WhatsAppDigits}?text={Uri.EscapeDataString(text)}";
        ViewBag.ContactPhone = _contact.Phone;
        ViewBag.ContactWhatsApp = _contact.WhatsApp;
        ViewBag.ContactEmail = _contact.Email;
        return View(request);
    }
}

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;

    public AdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Requests()
    {
        var requests = await _context.ContactRequests
            .Include(r => r.Listing)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return View(requests);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReplied(int id)
    {
        var request = await _context.ContactRequests.FindAsync(id);
        if (request == null)
        {
            return NotFound();
        }
        request.IsReplied = true;
        request.RepliedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Requests));
    }

    [HttpGet]
    public async Task<IActionResult> Listings()
    {
        var listings = await _context.Listings.Include(l => l.Owner)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        return View(listings);
    }
}
