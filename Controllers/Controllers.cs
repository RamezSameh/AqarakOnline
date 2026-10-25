using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AqarakOnline.Data;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AqarakOnline.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var listings = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
            .OrderByDescending(l => l.CreatedAt)
            .Take(6)
            .ToListAsync();
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.FavoriteListingIds = (await _context.Favorites
                .Where(f => f.UserId == userId)
                .Select(f => f.ListingId)
                .ToListAsync()).ToHashSet();
        }
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
    private readonly IWebHostEnvironment _env;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> _hub;

    public ListingsController(ApplicationDbContext context, IWebHostEnvironment env, Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub)
    {
        _context = context;
        _env = env;
        _hub = hub;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> Index(
        string? search, ListingType? listingType, PropertyType? propertyType,
        decimal? minPrice, decimal? maxPrice, bool onlyAvailable = false)
    {
        var query = _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.Approval == ListingApproval.Approved).AsQueryable();

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
        if (User.Identity?.IsAuthenticated == true)
        {
            ViewBag.FavoriteListingIds = (await _context.Favorites
                .Where(f => f.UserId == CurrentUserId)
                .Select(f => f.ListingId)
                .ToListAsync()).ToHashSet();
        }
        return View(listings);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var listing = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.Approval != ListingApproval.Approved
            && !User.IsInRole("Admin")
            && listing.OwnerId != CurrentUserId)
        {
            return NotFound();
        }
        // عدّ المشاهدة: الزائر العادي فقط، وليس الأدمن ولا صاحب الإعلان.
        if (!User.IsInRole("Admin") && listing.OwnerId != CurrentUserId)
        {
            listing.ViewCount++;
            await _context.SaveChangesAsync();
        }
        if (User.Identity?.IsAuthenticated == true)
        {
            ViewBag.IsFavorite = await _context.Favorites
                .AnyAsync(f => f.UserId == CurrentUserId && f.ListingId == listing.Id);
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
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Create(Listing listing)
    {
        ValidateAttachments(listing.PhotoFiles, listing.VideoFile, existingPhotoCount: 0, existingVideo: false);
        if (!ModelState.IsValid)
        {
            return View(listing);
        }

        listing.OwnerId = CurrentUserId;
        listing.CreatedAt = DateTime.UtcNow;
        listing.IsAvailable = true;
        listing.Approval = User.IsInRole("Admin") ? ListingApproval.Approved : ListingApproval.Pending;
        listing.AdNumber = "TEMP";
        listing.Attachments = new List<ListingAttachment>();
        _context.Listings.Add(listing);
        await _context.SaveChangesAsync();

        // رقم الإعلان يتولد من الـ Id بعد الحفظ، فيبقى فريدًا ومتسلسلًا.
        listing.AdNumber = $"AQ-{listing.Id:D4}";
        await SaveAttachmentsAsync(listing);
        await _context.SaveChangesAsync();

        if (listing.Approval == ListingApproval.Pending)
        {
            await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
            TempData["Info"] = "تم إرسال إعلانك للمراجعة، وسيظهر للعامة بعد موافقة الأدمن.";
            return RedirectToAction(nameof(MyListings));
        }
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction(nameof(Details), new { id = listing.Id });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var listing = await _context.Listings.Include(l => l.Attachments)
            .FirstOrDefaultAsync(l => l.Id == id);
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
    [RequestSizeLimit(100_000_000)]
    public async Task<IActionResult> Edit(int id, Listing model)
    {
        var listing = await _context.Listings.Include(l => l.Attachments)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var deleteIds = model.DeleteAttachmentIds ?? Array.Empty<int>();
        var existingPhotos = listing.Attachments.Count(a => a.Kind == AttachmentKind.Photo && !deleteIds.Contains(a.Id));
        var existingVideo = listing.Attachments.Any(a => a.Kind == AttachmentKind.Video && !deleteIds.Contains(a.Id));
        ValidateAttachments(model.PhotoFiles, model.VideoFile, existingPhotos, existingVideo);
        if (!ModelState.IsValid)
        {
            model.Attachments = listing.Attachments;
            return View(model);
        }

        listing.Title = model.Title;
        listing.ListingType = model.ListingType;
        listing.PropertyType = model.PropertyType;
        listing.Area = model.Area;
        listing.DetailedAddress = model.DetailedAddress;
        listing.Description = model.Description;
        listing.Features = model.Features;
        listing.Price = model.Price;
        listing.AreaSqm = model.AreaSqm;
        listing.IsAvailable = model.IsAvailable;
        if (!User.IsInRole("Admin"))
        {
            listing.Approval = ListingApproval.Pending;
        }

        foreach (var attachment in listing.Attachments.Where(a => deleteIds.Contains(a.Id)).ToList())
        {
            AttachmentStorage.DeleteFile(_env.WebRootPath, attachment.FilePath);
            _context.ListingAttachments.Remove(attachment);
        }
        await SaveAttachmentsAsync(listing, model.PhotoFiles, model.VideoFile);

        await _context.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("listingsChanged");
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
        var listing = await _context.Listings.Include(l => l.Attachments)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        var isOwn = listing.OwnerId == CurrentUserId;
        var wasPending = listing.Approval == ListingApproval.Pending;
        foreach (var attachment in listing.Attachments.ToList())
        {
            AttachmentStorage.DeleteFile(_env.WebRootPath, attachment.FilePath);
        }
        _context.Listings.Remove(listing);
        await _context.SaveChangesAsync();
        if (wasPending)
        {
            await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
        }
        await _hub.Clients.All.SendAsync("listingsChanged");
        if (!isOwn && User.IsInRole("Admin"))
        {
            return RedirectToAction("Listings", "Admin");
        }
        return RedirectToAction(nameof(MyListings));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> MyListings()
    {
        var listings = await _context.Listings.Include(l => l.Attachments)
            .Where(l => l.OwnerId == CurrentUserId)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        ViewBag.FavoriteListingIds = (await _context.Favorites
            .Where(f => f.UserId == CurrentUserId)
            .Select(f => f.ListingId)
            .ToListAsync()).ToHashSet();
        return View(listings);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAvailable(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        listing.IsAvailable = true;
        await _context.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction("Listings", "Admin");
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
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction("Listings", "Admin");
    }

    private void ValidateAttachments(
        List<IFormFile> photoFiles, IFormFile? videoFile, int existingPhotoCount, bool existingVideo)
    {
        var photos = (photoFiles ?? new List<IFormFile>()).Where(f => f.Length > 0).ToList();
        if (existingPhotoCount + photos.Count > AttachmentStorage.MaxPhotosPerListing)
        {
            ModelState.AddModelError(string.Empty, "عدد الصور التوضيحية لا يتعدى 5 صور للإعلان.");
        }
        foreach (var photo in photos)
        {
            var error = AttachmentStorage.ValidatePhoto(photo);
            if (error != null)
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }
        if (videoFile != null && videoFile.Length > 0)
        {
            if (existingVideo)
            {
                ModelState.AddModelError(string.Empty, "يوجد فيديو بالفعل للإعلان، احذفه أولًا قبل إضافة فيديو جديد.");
            }
            var error = AttachmentStorage.ValidateVideo(videoFile);
            if (error != null)
            {
                ModelState.AddModelError(string.Empty, error);
            }
        }
    }

    private async Task SaveAttachmentsAsync(Listing listing, List<IFormFile>? photoFiles = null, IFormFile? videoFile = null)
    {
        var photos = (photoFiles ?? listing.PhotoFiles).Where(f => f.Length > 0).ToList();
        var order = listing.Attachments.Where(a => a.Kind == AttachmentKind.Photo).Select(a => a.Order).DefaultIfEmpty(-1).Max() + 1;
        foreach (var photo in photos)
        {
            var attachment = await AttachmentStorage.SaveAsync(photo, AttachmentKind.Photo, listing.Id, _env.WebRootPath, order++);
            _context.ListingAttachments.Add(attachment);
        }
        var video = videoFile ?? listing.VideoFile;
        if (video != null && video.Length > 0)
        {
            var attachment = await AttachmentStorage.SaveAsync(video, AttachmentKind.Video, listing.Id, _env.WebRootPath, 0);
            _context.ListingAttachments.Add(attachment);
        }
    }
}

public class ContactController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ContactSettings _contact;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> _hub;

    public ContactController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        IOptions<ContactSettings> contact,
        Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub)
    {
        _context = context;
        _userManager = userManager;
        _contact = contact.Value;
        _hub = hub;
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
        if (!listing.IsAvailable || listing.Approval != ListingApproval.Approved)
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
        if (listing == null || listing.Approval != ListingApproval.Approved || !listing.IsAvailable)
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
        await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
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

public class AdminUserFormViewModel
{
    public string? Id { get; set; }

    [Required(ErrorMessage = "الاسم مطلوب")]
    [RegularExpression(@"^[\u0600-\u06FFa-zA-Z\s\-']{2,100}$", ErrorMessage = "الاسم يجب أن يكون بالعربي أو الإنجليزي فقط")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "العنوان مطلوب")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الموبايل مطلوب")]
    [RegularExpression(@"^01[0-9]{9}$", ErrorMessage = "رقم الموبايل يجب أن يكون 11 رقمًا بالضبط ويبدأ بـ 01")]
    public string PhoneNumber { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "صيغة البريد الإلكتروني غير صحيحة")]
    public string? Email { get; set; }

    // مطلوبة عند الإضافة، واختيارية عند التعديل (فراغ = الإبقاء على الحالية).
    [StringLength(100, MinimumLength = 8, ErrorMessage = "كلمة المرور يجب أن تكون 8 أحرف على الأقل، وتحتوي على حرف كبير وحرف صغير ورقم")]
    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public string Role { get; set; } = "User";
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Active;
}

public class AdminNotificationFormViewModel
{
    [Required(ErrorMessage = "عنوان التنبيه مطلوب")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "نص التنبيه مطلوب")]
    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;
}

public class AdminUserRow
{
    public ApplicationUser User { get; set; } = null!;
    public string Role { get; set; } = "User";
    public int ListingsCount { get; set; }
    public int ApprovedListingsCount { get; set; }
    public int PendingListingsCount { get; set; }
}

public class AdminDashboardViewModel
{
    public int TotalUsers { get; set; }
    public int PendingUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int SuspendedOrBlockedUsers { get; set; }
    public int TotalListings { get; set; }
    public int ApprovedListings { get; set; }
    public int PendingListings { get; set; }
    public int AvailableListings { get; set; }
    public long TotalViews { get; set; }
    public int TotalRequests { get; set; }
    public int UnrepliedRequests { get; set; }
    public string? FilterUserId { get; set; }
    public string? FilterArea { get; set; }
    public List<string> Areas { get; set; } = new();
    public List<ApplicationUser> Users { get; set; } = new();
    public List<AdminUserRow> UserRows { get; set; } = new();
    public List<Listing> FilteredListings { get; set; } = new();
}

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> _hub;

    public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub)
    {
        _context = context;
        _userManager = userManager;
        _env = env;
        _hub = hub;
    }

    // دفع لحظي: أرقام الأدمن لمجموعة الأدمن، والجرس لكل المتصلين.
    private Task NotifyAdminsAsync() =>
        _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");

    private Task NotifyBellAsync() =>
        _hub.Clients.All.SendAsync("refreshBell");

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> Dashboard(string? userId = null, string? area = null)
    {
        var users = await _userManager.Users.OrderBy(u => u.FullName).ToListAsync();
        var listingsQuery = _context.Listings.Include(l => l.Owner).AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId))
        {
            listingsQuery = listingsQuery.Where(l => l.OwnerId == userId);
        }
        if (!string.IsNullOrWhiteSpace(area))
        {
            listingsQuery = listingsQuery.Where(l => l.Area == area);
        }
        var filteredListings = await listingsQuery.OrderByDescending(l => l.CreatedAt).ToListAsync();

        var rows = new List<AdminUserRow>();
        foreach (var user in users)
        {
            if (!string.IsNullOrWhiteSpace(userId) && user.Id != userId)
            {
                continue;
            }
            var userListings = _context.Listings.Where(l => l.OwnerId == user.Id);
            if (!string.IsNullOrWhiteSpace(area))
            {
                userListings = userListings.Where(l => l.Area == area);
            }
            var roles = await _userManager.GetRolesAsync(user);
            rows.Add(new AdminUserRow
            {
                User = user,
                Role = roles.FirstOrDefault() ?? "User",
                ListingsCount = await userListings.CountAsync(),
                ApprovedListingsCount = await userListings.CountAsync(l => l.Approval == ListingApproval.Approved),
                PendingListingsCount = await userListings.CountAsync(l => l.Approval == ListingApproval.Pending)
            });
        }

        var model = new AdminDashboardViewModel
        {
            TotalUsers = users.Count,
            PendingUsers = users.Count(u => u.AccountStatus == AccountStatus.Pending),
            ActiveUsers = users.Count(u => u.AccountStatus == AccountStatus.Active),
            SuspendedOrBlockedUsers = users.Count(u => u.AccountStatus == AccountStatus.Suspended || u.AccountStatus == AccountStatus.Blocked),
            TotalListings = await _context.Listings.CountAsync(),
            ApprovedListings = await _context.Listings.CountAsync(l => l.Approval == ListingApproval.Approved),
            PendingListings = await _context.Listings.CountAsync(l => l.Approval == ListingApproval.Pending),
            AvailableListings = await _context.Listings.CountAsync(l => l.IsAvailable),
            TotalViews = await _context.Listings.SumAsync(l => (long)l.ViewCount),
            TotalRequests = await _context.ContactRequests.CountAsync(),
            UnrepliedRequests = await _context.ContactRequests.CountAsync(r => !r.IsReplied),
            FilterUserId = userId,
            FilterArea = area,
            Areas = await _context.Listings.Select(l => l.Area).Distinct().OrderBy(a => a).ToListAsync(),
            Users = users,
            UserRows = rows,
            FilteredListings = filteredListings
        };
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Users()
    {
        var users = await _userManager.Users.OrderBy(u => u.FullName).ToListAsync();
        var rows = new List<AdminUserRow>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            rows.Add(new AdminUserRow
            {
                User = user,
                Role = roles.FirstOrDefault() ?? "User",
                ListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == user.Id),
                ApprovedListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == user.Id && l.Approval == ListingApproval.Approved),
                PendingListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == user.Id && l.Approval == ListingApproval.Pending)
            });
        }
        return View(rows);
    }

    private async Task<IActionResult> SetUserStatus(string id, AccountStatus status, string doneMessage)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        if (user.Id == CurrentUserId && (status == AccountStatus.Suspended || status == AccountStatus.Blocked))
        {
            TempData["Error"] = "لا يمكنك إيقاف أو حظر حسابك أنت.";
            return RedirectToAction(nameof(Users));
        }
        user.AccountStatus = status;
        await _userManager.UpdateAsync(user);
        await NotifyAdminsAsync();
        TempData["Info"] = doneMessage;
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> ApproveUser(string id) =>
        SetUserStatus(id, AccountStatus.Active, "تم تفعيل الحساب.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> SuspendUser(string id) =>
        SetUserStatus(id, AccountStatus.Suspended, "تم إيقاف الحساب مؤقتًا.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UnsuspendUser(string id) =>
        SetUserStatus(id, AccountStatus.Active, "تم إعادة تفعيل الحساب.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> BlockUser(string id) =>
        SetUserStatus(id, AccountStatus.Blocked, "تم حظر الحساب.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public Task<IActionResult> UnblockUser(string id) =>
        SetUserStatus(id, AccountStatus.Active, "تم رفع الحظر عن الحساب.");

    // حذف حساب نهائيًا. لا يمكن للأدمن حذف حسابه هو (حتى لا يُحبس خارج
    // النظام). إعلانات المستخدم وطلبات تواصله تُحذف معه، لأن مفاتيحها
    // الأجنبية تمنع حذف المستخدم قبلها؛ ملفات مرفقات إعلاناته تُحذف من
    // القرص أيضًا، مثل حذف الإعلان نفسه.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "لا يمكنك حذف حسابك أنت.";
            return RedirectToAction(nameof(Users));
        }
        var listings = await _context.Listings
            .Include(l => l.Attachments)
            .Where(l => l.OwnerId == user.Id)
            .ToListAsync();
        foreach (var listing in listings)
        {
            foreach (var attachment in listing.Attachments.ToList())
            {
                AttachmentStorage.DeleteFile(_env.WebRootPath, attachment.FilePath);
            }
        }
        var requests = await _context.ContactRequests
            .Where(r => r.BuyerId == user.Id)
            .ToListAsync();
        _context.Listings.RemoveRange(listings);
        _context.ContactRequests.RemoveRange(requests);
        await _context.SaveChangesAsync();
        var result = await _userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            TempData["Error"] = "تعذر حذف الحساب.";
            return RedirectToAction(nameof(Users));
        }
        await NotifyAdminsAsync();
        TempData["Info"] = $"تم حذف الحساب نهائيًا (مع {listings.Count} إعلان و{requests.Count} طلب تواصل).";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public IActionResult AddUser()
    {
        return View(new AdminUserFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddUser(AdminUserFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(model.Password), "كلمة المرور مطلوبة عند إضافة حساب جديد.");
        }
        if (!NameHelper.IsValidName(model.FullName))
        {
            ModelState.AddModelError(nameof(model.FullName), "الاسم يجب أن يكون بالعربي أو الإنجليزي فقط");
        }
        var phone = PhoneHelper.Normalize(model.PhoneNumber);
        if (!PhoneHelper.IsValidEgyptianMobile(phone))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل يجب أن يكون 11 رقمًا بالضبط ويبدأ بـ 01");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        if (await _userManager.FindByNameAsync(phone) != null)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل هذا مسجَّل بالفعل.");
            return View(model);
        }

        var role = model.Role == "Admin" ? "Admin" : "User";
        var user = new ApplicationUser
        {
            UserName = phone,
            PhoneNumber = phone,
            PhoneNumberConfirmed = false,
            FullName = model.FullName.Trim(),
            Address = model.Address,
            Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim(),
            // الحساب الذي يضيفه الأدمن مفعَّل فورًا، لأنه موثَّق من الأدمن نفسه.
            AccountStatus = AccountStatus.Active
        };
        var result = await _userManager.CreateAsync(user, model.Password!);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return View(model);
        }
        await _userManager.AddToRoleAsync(user, role);
        TempData["Info"] = "تم إضافة الحساب وتفعيله.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> EditUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        var roles = await _userManager.GetRolesAsync(user);
        return View(new AdminUserFormViewModel
        {
            Id = user.Id,
            FullName = user.FullName ?? string.Empty,
            Address = user.Address ?? string.Empty,
            PhoneNumber = user.PhoneNumber ?? string.Empty,
            Email = user.Email,
            Role = roles.FirstOrDefault() ?? "User",
            AccountStatus = user.AccountStatus
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(AdminUserFormViewModel model)
    {
        var user = string.IsNullOrWhiteSpace(model.Id) ? null : await _userManager.FindByIdAsync(model.Id);
        if (user == null)
        {
            return NotFound();
        }
        if (!NameHelper.IsValidName(model.FullName))
        {
            ModelState.AddModelError(nameof(model.FullName), "الاسم يجب أن يكون بالعربي أو الإنجليزي فقط");
        }
        var phone = PhoneHelper.Normalize(model.PhoneNumber);
        if (!PhoneHelper.IsValidEgyptianMobile(phone))
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل يجب أن يكون 11 رقمًا بالضبط ويبدأ بـ 01");
        }
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        var other = await _userManager.FindByNameAsync(phone);
        if (other != null && other.Id != user.Id)
        {
            ModelState.AddModelError(nameof(model.PhoneNumber), "رقم الموبايل هذا مسجَّل لحساب آخر.");
            return View(model);
        }

        var isSelf = user.Id == CurrentUserId;
        if (isSelf && (model.AccountStatus == AccountStatus.Suspended || model.AccountStatus == AccountStatus.Blocked))
        {
            ModelState.AddModelError(string.Empty, "لا يمكنك إيقاف أو حظر حسابك أنت.");
            return View(model);
        }

        user.FullName = model.FullName.Trim();
        user.Address = model.Address;
        user.UserName = phone;
        user.PhoneNumber = phone;
        user.Email = string.IsNullOrWhiteSpace(model.Email) ? null : model.Email.Trim();
        user.AccountStatus = model.AccountStatus;
        await _userManager.UpdateAsync(user);

        // الدور: الأدمن لا يزيل دور Admin عن نفسه حتى لا يُحبس خارج لوحة الأدمن.
        var newRole = model.Role == "Admin" ? "Admin" : "User";
        var roles = await _userManager.GetRolesAsync(user);
        if (!(isSelf && newRole != "Admin" && roles.Contains("Admin")))
        {
            if (!roles.Contains(newRole))
            {
                await _userManager.RemoveFromRolesAsync(user, roles);
                await _userManager.AddToRoleAsync(user, newRole);
            }
        }

        if (!string.IsNullOrWhiteSpace(model.Password))
        {
            await _userManager.RemovePasswordAsync(user);
            var pwResult = await _userManager.AddPasswordAsync(user, model.Password);
            if (!pwResult.Succeeded)
            {
                foreach (var error in pwResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return View(model);
            }
        }

        TempData["Info"] = "تم حفظ تعديلات الحساب.";
        return RedirectToAction(nameof(Users));
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
        await NotifyAdminsAsync();
        return RedirectToAction(nameof(Requests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        listing.Approval = ListingApproval.Approved;
        await _context.SaveChangesAsync();
        await NotifyAdminsAsync();
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction(nameof(Listings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        listing.Approval = ListingApproval.Rejected;
        await _context.SaveChangesAsync();
        await NotifyAdminsAsync();
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction(nameof(Listings));
    }

    [HttpGet]
    public async Task<IActionResult> Listings()
    {
        var listings = await _context.Listings.Include(l => l.Owner)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        return View(listings);
    }

    // تنبيهات الأدمن: تظهر لكل المستخدمين على هيئة إشعارات.
    [HttpGet]
    public async Task<IActionResult> Notifications()
    {
        var notifications = await _context.Notifications
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
        return View(notifications);
    }

    // نسخة حديثة من روابط الأدمن بالأرقام، يعيد المتصفح جلبها عند إشارة التحديث.
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public IActionResult NavBadges() => ViewComponent("AdminNav");

    [HttpGet]
    public IActionResult CreateNotification()
    {
        return View(new AdminNotificationFormViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateNotification(AdminNotificationFormViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        _context.Notifications.Add(new Notification
        {
            Title = model.Title.Trim(),
            Message = model.Message.Trim(),
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await NotifyBellAsync();
        TempData["Info"] = "تم نشر التنبيه لكل المستخدمين.";
        return RedirectToAction(nameof(Notifications));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleNotification(int id)
    {
        var notification = await _context.Notifications.FindAsync(id);
        if (notification == null)
        {
            return NotFound();
        }
        notification.IsActive = !notification.IsActive;
        await _context.SaveChangesAsync();
        await NotifyBellAsync();
        TempData["Info"] = notification.IsActive ? "تم تفعيل التنبيه." : "تم إخفاء التنبيه عن المستخدمين.";
        return RedirectToAction(nameof(Notifications));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteNotification(int id)
    {
        var notification = await _context.Notifications.FindAsync(id);
        if (notification == null)
        {
            return NotFound();
        }
        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();
        await NotifyBellAsync();
        TempData["Info"] = "تم حذف التنبيه نهائيًا.";
        return RedirectToAction(nameof(Notifications));
    }
}

public class UserNotificationRow
{
    public Notification Notification { get; set; } = null!;
    public bool IsRead { get; set; }
}

public class BellViewModel
{
    public int UnreadCount { get; set; }
    public List<UserNotificationRow> Items { get; set; } = new();
}

[Authorize]
public class NotificationsController : Controller
{
    private readonly ApplicationDbContext _context;

    public NotificationsController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var notifications = await _context.Notifications
            .Where(n => n.IsActive)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
        var readIds = await _context.NotificationReads
            .Where(r => r.UserId == CurrentUserId)
            .Select(r => r.NotificationId)
            .ToListAsync();
        var rows = notifications.Select(n => new UserNotificationRow
        {
            Notification = n,
            IsRead = readIds.Contains(n.Id)
        }).ToList();
        return View(rows);
    }

    // نسخة حديثة من الجرس، يعيد المتصفح جلبها عند إشارة التحديث.
    [HttpGet]
    public IActionResult Bell() => ViewComponent("NotificationBell");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var exists = await _context.Notifications.AnyAsync(n => n.Id == id && n.IsActive);
        var already = await _context.NotificationReads
            .AnyAsync(r => r.NotificationId == id && r.UserId == CurrentUserId);
        if (exists && !already)
        {
            _context.NotificationReads.Add(new NotificationRead
            {
                NotificationId = id,
                UserId = CurrentUserId,
                ReadAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(string? returnUrl = null)
    {
        var activeIds = await _context.Notifications
            .Where(n => n.IsActive)
            .Select(n => n.Id)
            .ToListAsync();
        var readIds = await _context.NotificationReads
            .Where(r => r.UserId == CurrentUserId)
            .Select(r => r.NotificationId)
            .ToListAsync();
        foreach (var id in activeIds.Where(id => !readIds.Contains(id)))
        {
            _context.NotificationReads.Add(new NotificationRead
            {
                NotificationId = id,
                UserId = CurrentUserId,
                ReadAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
public class FavoritesController : Controller
{
    private readonly ApplicationDbContext _context;

    public FavoritesController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var listings = await _context.Favorites
            .Include(f => f.Listing).ThenInclude(l => l!.Owner)
            .Include(f => f.Listing).ThenInclude(l => l!.Attachments)
            .Where(f => f.UserId == CurrentUserId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.Listing!)
            .ToListAsync();
        ViewBag.FavoriteListingIds = listings.Select(l => l.Id).ToHashSet();
        return View(listings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int listingId, string? returnUrl = null)
    {
        var listing = await _context.Listings.FindAsync(listingId);
        if (listing == null)
        {
            return NotFound();
        }
        var existing = await _context.Favorites
            .FirstOrDefaultAsync(f => f.UserId == CurrentUserId && f.ListingId == listingId);
        if (existing != null)
        {
            _context.Favorites.Remove(existing);
            TempData["Info"] = "تمت الإزالة من المفضلة.";
        }
        else
        {
            _context.Favorites.Add(new Favorite
            {
                UserId = CurrentUserId,
                ListingId = listingId,
                CreatedAt = DateTime.UtcNow
            });
            TempData["Info"] = "تمت الإضافة إلى المفضلة.";
        }
        await _context.SaveChangesAsync();
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(Index));
    }
}

public class NotificationBellViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;

    public NotificationBellViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Content(string.Empty);
        }
        var userId = ViewContext.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var notifications = await _context.Notifications
            .Where(n => n.IsActive)
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .ToListAsync();
        var readIds = await _context.NotificationReads
            .Where(r => r.UserId == userId)
            .Select(r => r.NotificationId)
            .ToListAsync();
        var activeIds = await _context.Notifications
            .Where(n => n.IsActive)
            .Select(n => n.Id)
            .ToListAsync();
        var model = new BellViewModel
        {
            UnreadCount = activeIds.Count(id => !readIds.Contains(id)),
            Items = notifications.Select(n => new UserNotificationRow
            {
                Notification = n,
                IsRead = readIds.Contains(n.Id)
            }).ToList()
        };
        if (model.UnreadCount < 0)
        {
            model.UnreadCount = 0;
        }
        return View(model);
    }
}

public class AdminBadgeCounts
{
    public int PendingUsers { get; set; }
    public int PendingListings { get; set; }
    public int UnrepliedRequests { get; set; }
}

public class AdminNavViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;

    public AdminNavViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (!ViewContext.HttpContext.User.IsInRole("Admin"))
        {
            return Content(string.Empty);
        }
        // أرقام مصغرة للأدمن: حسابات بانتظار الموافقة، إعلانات بانتظار
        // الموافقة، وطلبات تواصل لم يُرد عليها بعد. تُحسب مع كل تحميل صفحة.
        var model = new AdminBadgeCounts
        {
            PendingUsers = await _context.Users.CountAsync(u => u.AccountStatus == AccountStatus.Pending),
            PendingListings = await _context.Listings.CountAsync(l => l.Approval == ListingApproval.Pending),
            UnrepliedRequests = await _context.ContactRequests.CountAsync(r => !r.IsReplied)
        };
        return View(model);
    }
}
