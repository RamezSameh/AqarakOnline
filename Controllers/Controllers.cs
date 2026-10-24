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

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;

    public HomeController(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var listings = await _context.Listings.Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
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
    private readonly IWebHostEnvironment _env;

    public ListingsController(ApplicationDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
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
            TempData["Info"] = "تم إرسال إعلانك للمراجعة، وسيظهر للعامة بعد موافقة الأدمن.";
            return RedirectToAction(nameof(MyListings));
        }
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
        foreach (var attachment in listing.Attachments.ToList())
        {
            AttachmentStorage.DeleteFile(_env.WebRootPath, attachment.FilePath);
        }
        _context.Listings.Remove(listing);
        await _context.SaveChangesAsync();
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
}
