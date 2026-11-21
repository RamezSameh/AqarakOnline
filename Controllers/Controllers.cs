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
            .OrderByDescending(l => l.IsFeatured)
            .ThenByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
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
        decimal? minPrice, decimal? maxPrice, bool onlyAvailable = false,
        int? minArea = null, int? maxArea = null, string? sort = null, int page = 1,
        int? minRooms = null, bool furnishedOnly = false, string? area = null)
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
        if (!string.IsNullOrWhiteSpace(area))
        {
            query = query.Where(l => l.Area == area);
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
        if (minArea.HasValue)
        {
            query = query.Where(l => l.AreaSqm >= minArea.Value);
        }
        if (maxArea.HasValue)
        {
            query = query.Where(l => l.AreaSqm <= maxArea.Value);
        }
        if (minRooms.HasValue)
        {
            query = query.Where(l => l.Rooms >= minRooms.Value);
        }
        if (furnishedOnly)
        {
            query = query.Where(l => l.IsFurnished);
        }

        // الترتيب: الافتراضي الأحدث. القيم المقبولة فقط هي المعرّفة هنا،
        // وأي قيمة غير معروفة تعود للأحدث بدل ما تكسر الاستعلام.
        var sortKey = (sort ?? "newest").ToLowerInvariant() switch
        {
            "priceasc" => "priceAsc",
            "pricedesc" => "priceDesc",
            "areadesc" => "areaDesc",
            "viewsdesc" => "viewsDesc",
            _ => "newest"
        };
        // الإعلانات المميزة أولًا دائمًا، ثم الترتيب المختار.
        query = sortKey switch
        {
            "priceAsc" => query.OrderByDescending(l => l.IsFeatured).ThenBy(l => l.Price).ThenByDescending(l => l.CreatedAt),
            "priceDesc" => query.OrderByDescending(l => l.IsFeatured).ThenByDescending(l => l.Price).ThenByDescending(l => l.CreatedAt),
            "areaDesc" => query.OrderByDescending(l => l.IsFeatured).ThenByDescending(l => l.AreaSqm).ThenByDescending(l => l.CreatedAt),
            "viewsDesc" => query.OrderByDescending(l => l.IsFeatured).ThenByDescending(l => l.ViewCount).ThenByDescending(l => l.CreatedAt),
            _ => query.OrderByDescending(l => l.IsFeatured).ThenByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
        };

        const int pageSize = 12;
        var totalCount = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Clamp(page, 1, totalPages);

        ViewBag.Search = search;
        ViewBag.ListingType = listingType;
        ViewBag.PropertyType = propertyType;
        ViewBag.MinPrice = minPrice;
        ViewBag.MaxPrice = maxPrice;
        ViewBag.OnlyAvailable = onlyAvailable;
        ViewBag.MinArea = minArea;
        ViewBag.MaxArea = maxArea;
        ViewBag.MinRooms = minRooms;
        ViewBag.FurnishedOnly = furnishedOnly;
        ViewBag.Area = area;
        ViewBag.Sort = sortKey;
        ViewBag.Page = page;
        ViewBag.TotalPages = totalPages;
        ViewBag.TotalCount = totalCount;

        var listings = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
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
        // إعلانات مشابهة: نفس المنطقة أولًا، ثم نفس النوع، وبسعر قريب.
        ViewBag.SimilarListings = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.Approval == ListingApproval.Approved && l.Id != listing.Id
                && (l.Area == listing.Area || l.PropertyType == listing.PropertyType))
            .OrderByDescending(l => l.Area == listing.Area)
            .ThenBy(l => Math.Abs(l.Price - listing.Price))
            .Take(4)
            .ToListAsync();
        return View(listing);
    }

    [Authorize]
    /// <summary>
    /// صفحة منطقة (للـ SEO): نفس نتائج البحث لكن برابط ثابت لكل منطقة،
    /// مثل /Listings/Area/مدينة-نصر،-القاهرة — وتظهر في sitemap.xml.
    /// </summary>
    [HttpGet]
    [Route("Listings/Area/{area}")]
    public async Task<IActionResult> Area(string area)
    {
        if (string.IsNullOrWhiteSpace(area) || !Models.ListingAreas.All.Contains(area))
        {
            return NotFound();
        }
        ViewBag.AreaPage = area;
        ViewBag.CanonicalUrl = $"{Request.Scheme}://{Request.Host}/Listings/Area/{Uri.EscapeDataString(area)}";
        return await Index(search: null, listingType: null, propertyType: null,
            minPrice: null, maxPrice: null, onlyAvailable: false,
            minArea: null, maxArea: null, sort: null, page: 1,
            minRooms: null, furnishedOnly: false, area: area);
    }

    /// <summary>مقارنة حتى 3 إعلانات مقبولة جنب بعض (ids مفصولة بفاصلة).</summary>
    [HttpGet]
    public async Task<IActionResult> Compare(string? ids)
    {
        var idList = (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var n) ? n : -1)
            .Where(n => n > 0).Distinct().Take(3).ToList();
        var found = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.Approval == ListingApproval.Approved && idList.Contains(l.Id))
            .ToListAsync();
        // نحافظ على ترتيب اختيار المستخدم.
        var listings = idList.Select(id => found.FirstOrDefault(l => l.Id == id))
            .Where(l => l != null).Cast<Listing>().ToList();
        return View(listings);
    }

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
        if (listing.PropertyType == PropertyType.Apartment && !listing.Floor.HasValue)
        {
            ModelState.AddModelError(nameof(Listing.Floor), "الدور مطلوب عند إضافة شقة.");
        }
        if (!ModelState.IsValid)
        {
            return View(listing);
        }

        listing.OwnerId = CurrentUserId;
        listing.CreatedAt = DateTime.UtcNow;
        listing.IsAvailable = true;
        // الدور خاص بالشقة فقط؛ أي قيمة مرسلة مع نوع آخر تُتجاهل.
        if (listing.PropertyType != PropertyType.Apartment)
        {
            listing.Floor = null;
        }
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
        await SavedSearchAlerts.NotifyMatchingAsync(_context, listing, _hub);
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
    public async Task<IActionResult> Edit(int id, Listing model, string? photoOrder)
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
        if (model.PropertyType == PropertyType.Apartment && !model.Floor.HasValue)
        {
            ModelState.AddModelError(nameof(Listing.Floor), "الدور مطلوب عند إضافة شقة.");
        }
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
        listing.AdvertiserType = model.AdvertiserType;
        listing.Rooms = model.Rooms;
        listing.Bathrooms = model.Bathrooms;
        listing.Floor = model.PropertyType == PropertyType.Apartment ? model.Floor : null;
        listing.IsFurnished = model.IsFurnished;
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
        // ترتيب الصور من السحب والإفلات في صفحة التعديل.
        if (!string.IsNullOrWhiteSpace(photoOrder))
        {
            var orderedIds = photoOrder.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => int.TryParse(v, out var n) ? n : -1)
                .Where(n => n > 0).ToList();
            var order = 0;
            foreach (var attachmentId in orderedIds)
            {
                var attachment = listing.Attachments
                    .FirstOrDefault(a => a.Id == attachmentId && a.Kind == AttachmentKind.Photo);
                if (attachment != null)
                {
                    attachment.Order = order++;
                }
            }
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

    /// <summary>
    /// تجديد الإعلان: يرفعه أول نتائج «الأحدث». للمالك فقط، ومرة كل 7 أيام.
    /// </summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Bump(int id, string? returnUrl = null)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId)
        {
            return Forbid();
        }
        if (listing.LastBumpedAt.HasValue && listing.LastBumpedAt.Value.AddDays(7) > DateTime.UtcNow)
        {
            TempData["Error"] = "تقدر تجدّد الإعلان مرة كل 7 أيام.";
        }
        else
        {
            listing.LastBumpedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["Info"] = "تم تجديد الإعلان — ظهر أول النتائج.";
        }
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(MyListings));
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Report(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId == CurrentUserId)
        {
            TempData["Error"] = "لا يمكنك الإبلاغ عن إعلانك.";
            return RedirectToAction(nameof(Details), new { id });
        }
        return View(listing);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(int id, ReportReason reason, string? details)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId == CurrentUserId)
        {
            return Forbid();
        }
        var already = await _context.ListingReports.AnyAsync(r =>
            r.ListingId == id && r.ReporterId == CurrentUserId && r.Status == ReportStatus.Pending);
        if (already)
        {
            TempData["Info"] = "بلّغت عن الإعلان ده قبل كده، ولسه قيد المراجعة.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var cleanDetails = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        if (cleanDetails != null && cleanDetails.Length > 1000)
        {
            cleanDetails = cleanDetails[..1000];
        }
        _context.ListingReports.Add(new ListingReport
        {
            ListingId = id,
            ReporterId = CurrentUserId,
            Reason = reason,
            Details = cleanDetails,
            Status = ReportStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
        TempData["Info"] = "تم إرسال البلاغ — هنراجعه في أقرب وقت.";
        return RedirectToAction(nameof(Details), new { id });
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
        if (listing.OwnerId == CurrentUserId)
        {
            TempData["Error"] = "لا يمكنك شراء أو استئجار إعلانك الخاص.";
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
        // صاحب الإعلان لا يشتري/يستأجر إعلانه الخاص — يُمنع من السيرفر أيضًا،
        // حتى لو وصل للصفحة برابط مباشر بعد إخفاء الأزرار في الواجهة.
        if (listing.OwnerId == CurrentUserId)
        {
            return Forbid();
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
    /// <summary>إعلانات مقبولة ومتاحة بقالها أكثر من 30 يومًا — تحتاج مراجعة/تحديث.</summary>
    public List<Listing> StaleListings { get; set; } = new();
    /// <summary>إعلانات جديدة في آخر 6 شهور (للرسم البياني).</summary>
    public List<ChartPoint> MonthlyListings { get; set; } = new();
    /// <summary>توزيع الإعلانات حسب المنطقة — أعلى 8 مناطق.</summary>
    public List<ChartPoint> ListingsByArea { get; set; } = new();
    /// <summary>أكثر 5 إعلانات مشاهدة.</summary>
    public List<Listing> TopViewed { get; set; } = new();
}

/// <summary>نقطة في رسم بياني: تسمية وعدد.</summary>
public class ChartPoint
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
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
            FilteredListings = filteredListings,
            StaleListings = await _context.Listings.Include(l => l.Owner)
                .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable
                    && l.CreatedAt <= DateTime.UtcNow.AddDays(-30))
                .OrderBy(l => l.CreatedAt)
                .ToListAsync()
        };
        // رسوم بيانية: إعلانات جديدة في آخر 6 شهور، وتوزيعها حسب المنطقة،
        // وأكثرها مشاهدة. تُحسب في الذاكرة لأن حجم البيانات صغير.
        var allListings = await _context.Listings.ToListAsync();
        var arabicMonths = new[] { "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
        model.MonthlyListings = Enumerable.Range(0, 6)
            .Select(i => DateTime.UtcNow.AddMonths(-5 + i))
            .Select(m => new ChartPoint
            {
                Label = $"{arabicMonths[m.Month - 1]} {m.Year}",
                Count = allListings.Count(l => l.CreatedAt.Year == m.Year && l.CreatedAt.Month == m.Month)
            }).ToList();
        model.ListingsByArea = allListings
            .GroupBy(l => l.Area)
            .Select(g => new ChartPoint { Label = g.Key, Count = g.Count() })
            .OrderByDescending(p => p.Count)
            .Take(8).ToList();
        model.TopViewed = allListings
            .Where(l => l.Approval == ListingApproval.Approved)
            .OrderByDescending(l => l.ViewCount)
            .Take(5).ToList();
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Users(string? search = null)
    {
        var usersQuery = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            usersQuery = usersQuery.Where(u =>
                (u.FullName != null && u.FullName.Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }
        var users = await usersQuery.OrderBy(u => u.FullName).ToListAsync();
        ViewBag.Search = search;
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
        await SavedSearchAlerts.NotifyMatchingAsync(_context, listing, _hub);
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleFeatured(int id)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        listing.IsFeatured = !listing.IsFeatured;
        await _context.SaveChangesAsync();
        TempData["Info"] = listing.IsFeatured ? "تم تثبيت الإعلان كمميز." : "تم إلغاء تمييز الإعلان.";
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

    [HttpGet]
    public async Task<IActionResult> Reports()
    {
        var reports = await _context.ListingReports
            .Include(r => r.Listing).Include(r => r.Reporter)
            .OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt)
            .ToListAsync();
        return View(reports);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReviewed(int id)
    {
        var report = await _context.ListingReports.FindAsync(id);
        if (report == null)
        {
            return NotFound();
        }
        report.Status = ReportStatus.Reviewed;
        await _context.SaveChangesAsync();
        await NotifyAdminsAsync();
        TempData["Info"] = "تمت مراجعة البلاغ.";
        return RedirectToAction(nameof(Reports));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReport(int id)
    {
        var report = await _context.ListingReports.FindAsync(id);
        if (report == null)
        {
            return NotFound();
        }
        _context.ListingReports.Remove(report);
        await _context.SaveChangesAsync();
        await NotifyAdminsAsync();
        TempData["Info"] = "تم حذف البلاغ.";
        return RedirectToAction(nameof(Reports));
    }

    /// <summary>مواعيد المعاينة — للأدمن فقط: هو من يؤكد أو يرفض وينسّق مع الطرفين.</summary>
    [HttpGet]
    public async Task<IActionResult> Viewings()
    {
        var viewings = await _context.ViewingAppointments
            .Include(v => v.Listing).Include(v => v.Requester)
            .OrderBy(v => v.Status).ThenByDescending(v => v.CreatedAt)
            .ToListAsync();
        return View(viewings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmViewing(int id) =>
        await DecideViewingAsync(id, ViewingStatus.Confirmed, "تم تأكيد المعاينة.");

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeclineViewing(int id) =>
        await DecideViewingAsync(id, ViewingStatus.Declined, "تم رفض المعاينة.");

    private async Task<IActionResult> DecideViewingAsync(int id, ViewingStatus status, string doneMessage)
    {
        var viewing = await _context.ViewingAppointments
            .Include(v => v.Listing).FirstOrDefaultAsync(v => v.Id == id);
        if (viewing == null)
        {
            return NotFound();
        }
        if (viewing.Status != ViewingStatus.Pending)
        {
            TempData["Error"] = "المعاينة دي اتحسمت بالفعل.";
            return RedirectToAction(nameof(Viewings));
        }
        viewing.Status = status;
        await _context.SaveChangesAsync();
        var verb = status == ViewingStatus.Confirmed ? "أكّدت إدارة الموقع" : "رفضت إدارة الموقع";
        _context.Notifications.Add(new Notification
        {
            Title = "تحديث على طلب المعاينة",
            Message = $"{verb} معاينة «{viewing.Listing?.Title}» يوم {viewing.RequestedAt:dd/MM/yyyy HH:mm}.",
            TargetUserId = viewing.RequesterId,
            Url = "/Viewings",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("refreshBell");
        TempData["Info"] = doneMessage;
        return RedirectToAction(nameof(Viewings));
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
    public IActionResult NavBadges(bool panelOnly = false) => ViewComponent("AdminNav", new { panelOnly });

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
        // التنبيه العام (بلا مستخدم مستهدف) يظهر للكل؛ والموجَّه يظهر لصاحبه فقط.
        var notifications = await _context.Notifications
            .Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == CurrentUserId))
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
        var exists = await _context.Notifications.AnyAsync(n => n.Id == id && n.IsActive
            && (n.TargetUserId == null || n.TargetUserId == CurrentUserId));
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
            .Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == CurrentUserId))
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
/// <summary>
/// تنبيهات البحث المحفوظ: عند نشر إعلان، يُنبَّه أصحاب البحوث المطابقة
/// بإشعار موجَّه لهم فقط (TargetUserId)، مع رابط لصفحة الإعلان.
/// </summary>
public static class SavedSearchAlerts
{
    public static bool Matches(SavedSearch s, Listing l)
    {
        if (!string.IsNullOrWhiteSpace(s.Search)
            && !(l.Title.Contains(s.Search) || l.Area.Contains(s.Search)))
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(s.Area) && l.Area != s.Area)
        {
            return false;
        }
        if (s.ListingType.HasValue && l.ListingType != s.ListingType.Value)
        {
            return false;
        }
        if (s.PropertyType.HasValue && l.PropertyType != s.PropertyType.Value)
        {
            return false;
        }
        if (s.MinPrice.HasValue && l.Price < s.MinPrice.Value)
        {
            return false;
        }
        if (s.MaxPrice.HasValue && l.Price > s.MaxPrice.Value)
        {
            return false;
        }
        if (s.MinAreaSqm.HasValue && l.AreaSqm < s.MinAreaSqm.Value)
        {
            return false;
        }
        if (s.MaxAreaSqm.HasValue && l.AreaSqm > s.MaxAreaSqm.Value)
        {
            return false;
        }
        if (s.MinRooms.HasValue && (l.Rooms ?? 0) < s.MinRooms.Value)
        {
            return false;
        }
        if (s.FurnishedOnly && !l.IsFurnished)
        {
            return false;
        }
        return true;
    }

    public static async Task NotifyMatchingAsync(ApplicationDbContext context, Listing listing,
        Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub)
    {
        var url = $"/Listings/Details/{listing.Id}";
        var searches = await context.SavedSearches.ToListAsync();
        var userIds = searches.Where(s => Matches(s, listing)).Select(s => s.UserId).Distinct().ToList();
        var created = false;
        foreach (var userId in userIds)
        {
            if (userId == listing.OwnerId)
            {
                continue; // لا تنبيه لصاحب الإعلان نفسه.
            }
            var exists = await context.Notifications.AnyAsync(n => n.TargetUserId == userId && n.Url == url);
            if (exists)
            {
                continue; // تنبيه واحد لكل إعلان لكل مستخدم، حتى لو اتوافق عليه مرتين.
            }
            context.Notifications.Add(new Notification
            {
                Title = "إعلان جديد مطابق لبحثك المحفوظ",
                Message = $"{listing.Title} — {listing.Area}، {listing.Price:N0} جنيه.",
                TargetUserId = userId,
                Url = url,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            created = true;
        }
        if (created)
        {
            await context.SaveChangesAsync();
            await hub.Clients.All.SendAsync("refreshBell");
        }
    }
}

[Authorize]
public class SavedSearchesController : Controller
{
    private readonly ApplicationDbContext _context;

    public SavedSearchesController(ApplicationDbContext context)
    {
        _context = context;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var searches = await _context.SavedSearches
            .Where(s => s.UserId == CurrentUserId)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();
        var approved = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved).ToListAsync();
        ViewBag.MatchCounts = searches.ToDictionary(
            s => s.Id, s => approved.Count(l => SavedSearchAlerts.Matches(s, l)));
        return View(searches);
    }

    /// <summary>حفظ البحث الحالي من صفحة الإعلانات (كل الشروط من الفورم هناك).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string? search, string? area,
        ListingType? listingType, PropertyType? propertyType,
        decimal? minPrice, decimal? maxPrice, int? minArea, int? maxArea,
        int? minRooms, bool furnishedOnly = false, string? returnUrl = null)
    {
        var candidate = new SavedSearch
        {
            UserId = CurrentUserId,
            Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
            Area = string.IsNullOrWhiteSpace(area) ? null : area.Trim(),
            ListingType = listingType,
            PropertyType = propertyType,
            MinPrice = minPrice,
            MaxPrice = maxPrice,
            MinAreaSqm = minArea,
            MaxAreaSqm = maxArea,
            MinRooms = minRooms,
            FurnishedOnly = furnishedOnly,
            CreatedAt = DateTime.UtcNow
        };
        var mine = await _context.SavedSearches.Where(s => s.UserId == CurrentUserId).ToListAsync();
        if (mine.Any(s => SameCriteria(s, candidate)))
        {
            TempData["Info"] = "البحث ده محفوظ عندك بالفعل.";
        }
        else
        {
            _context.SavedSearches.Add(candidate);
            await _context.SaveChangesAsync();
            TempData["Info"] = "تم حفظ البحث — هننبّهك لما ينزل إعلان جديد مطابق.";
        }
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction("Index", "Listings");
    }

    [HttpGet]
    public async Task<IActionResult> Run(int id)
    {
        var saved = await _context.SavedSearches
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == CurrentUserId);
        if (saved == null)
        {
            return NotFound();
        }
        return RedirectToAction("Index", "Listings", new
        {
            search = saved.Search,
            area = saved.Area,
            listingType = saved.ListingType,
            propertyType = saved.PropertyType,
            minPrice = saved.MinPrice,
            maxPrice = saved.MaxPrice,
            minArea = saved.MinAreaSqm,
            maxArea = saved.MaxAreaSqm,
            minRooms = saved.MinRooms,
            furnishedOnly = saved.FurnishedOnly
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var saved = await _context.SavedSearches
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == CurrentUserId);
        if (saved != null)
        {
            _context.SavedSearches.Remove(saved);
            await _context.SaveChangesAsync();
            TempData["Info"] = "تم حذف البحث المحفوظ.";
        }
        return RedirectToAction(nameof(Index));
    }

    private static bool SameCriteria(SavedSearch a, SavedSearch b) =>
        a.Search == b.Search && a.Area == b.Area && a.ListingType == b.ListingType
        && a.PropertyType == b.PropertyType && a.MinPrice == b.MinPrice && a.MaxPrice == b.MaxPrice
        && a.MinAreaSqm == b.MinAreaSqm && a.MaxAreaSqm == b.MaxAreaSqm
        && a.MinRooms == b.MinRooms && a.FurnishedOnly == b.FurnishedOnly;
}

/// <summary>ملفات الـ SEO: خريطة الموقع وrobots.txt — بروابط مطلقة من الدومين الحالي.</summary>
public class SeoController : Controller
{
    private readonly ApplicationDbContext _context;

    public SeoController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    [Route("sitemap.xml")]
    public async Task<IActionResult> Sitemap()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var urls = new List<(string Loc, string? LastMod)>
        {
            ($"{baseUrl}/", null),
            ($"{baseUrl}/Listings", null)
        };
        foreach (var area in Models.ListingAreas.All)
        {
            urls.Add(($"{baseUrl}/Listings/Area/{Uri.EscapeDataString(area)}", null));
        }
        var listings = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => new { l.Id, l.CreatedAt })
            .ToListAsync();
        foreach (var listing in listings)
        {
            urls.Add(($"{baseUrl}/Listings/Details/{listing.Id}", listing.CreatedAt.ToString("yyyy-MM-dd")));
        }
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var (loc, lastMod) in urls)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{System.Security.SecurityElement.Escape(loc)}</loc>");
            if (lastMod != null)
            {
                sb.AppendLine($"    <lastmod>{lastMod}</lastmod>");
            }
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        return Content(sb.ToString(), "application/xml", System.Text.Encoding.UTF8);
    }

    [HttpGet]
    [Route("robots.txt")]
    public IActionResult Robots()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        return Content($"User-agent: *\nAllow: /\nSitemap: {baseUrl}/sitemap.xml\n", "text/plain", System.Text.Encoding.UTF8);
    }
}

/// <summary>
/// حجز معاينة — عبر إدارة الموقع فقط (قاعدة الموقع: لا يوجد تواصل مباشر
/// بين المشتري والبائع إلا من خلال الأدمن). المشتري يطلب ميعادًا، والأدمن
/// هو من يؤكد أو يرفض وينسّق مع الطرفين بالتليفون. صاحب الإعلان لا يرى
/// طلبات المعاينة ولا بيانات المشتري، والمشتري لا يرى بيانات المعلن.
/// </summary>
[Authorize]
public class ViewingsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> _hub;
    private readonly IConfiguration _configuration;

    public ViewingsController(ApplicationDbContext context,
        Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub, IConfiguration configuration)
    {
        _context = context;
        _hub = hub;
        _configuration = configuration;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private async Task<string?> AdminUserIdAsync()
    {
        var adminEmail = _configuration["AdminEmail"];
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            return null;
        }
        return await _context.Users.Where(u => u.Email == adminEmail).Select(u => u.Id).FirstOrDefaultAsync();
    }

    private async Task NotifyAdminAsync(string title, string message)
    {
        var adminId = await AdminUserIdAsync();
        if (adminId != null)
        {
            _context.Notifications.Add(new Notification
            {
                Title = title,
                Message = message,
                TargetUserId = adminId,
                Url = "/Admin/Viewings",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await _context.SaveChangesAsync();
        }
        await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
        await _hub.Clients.All.SendAsync("refreshBell");
    }

    private async Task NotifyRequesterAsync(ViewingAppointment viewing, string title, string message)
    {
        _context.Notifications.Add(new Notification
        {
            Title = title,
            Message = message,
            TargetUserId = viewing.RequesterId,
            Url = "/Viewings",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("refreshBell");
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        // طلباتي أنا فقط — طلبات الآخرين لا تظهر لي.
        var mine = await _context.ViewingAppointments
            .Include(v => v.Listing)
            .Where(v => v.RequesterId == CurrentUserId)
            .OrderByDescending(v => v.CreatedAt).ToListAsync();
        return View(mine);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int listingId, DateTime requestedAt, string? message)
    {
        var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId == CurrentUserId)
        {
            return Forbid();
        }
        if (requestedAt <= DateTime.Now)
        {
            TempData["Error"] = "ميعاد المعاينة لازم يكون في المستقبل.";
            return RedirectToAction("Details", "Listings", new { id = listingId });
        }
        var cleanMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (cleanMessage != null && cleanMessage.Length > 500)
        {
            cleanMessage = cleanMessage[..500];
        }
        _context.ViewingAppointments.Add(new ViewingAppointment
        {
            ListingId = listingId,
            RequesterId = CurrentUserId,
            RequestedAt = requestedAt,
            Status = ViewingStatus.Pending,
            Message = cleanMessage,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        var requester = await _context.Users.FindAsync(CurrentUserId);
        var requesterName = (requester?.FullName ?? requester?.UserName ?? "مستخدم").Split(' ')[0];
        await NotifyAdminAsync("طلب معاينة جديد",
            $"{requesterName} طلب معاينة «{listing.Title}» يوم {requestedAt:dd/MM/yyyy HH:mm}.");
        TempData["Info"] = "تم إرسال طلب المعاينة — إدارة الموقع هتنسّق معاك الميعاد.";
        return RedirectToAction("Details", "Listings", new { id = listingId });
    }

    /// <summary>إلغاء المعاينة: الطالب نفسه أو الأدمن.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id)
    {
        var viewing = await _context.ViewingAppointments
            .Include(v => v.Listing).FirstOrDefaultAsync(v => v.Id == id);
        if (viewing == null)
        {
            return NotFound();
        }
        var isAdmin = User.IsInRole("Admin");
        if (viewing.RequesterId != CurrentUserId && !isAdmin)
        {
            return Forbid();
        }
        if (viewing.Status != ViewingStatus.Pending && viewing.Status != ViewingStatus.Confirmed)
        {
            TempData["Error"] = "المعاينة دي اتحسمت بالفعل.";
            return RedirectToAction(nameof(Index));
        }
        viewing.Status = ViewingStatus.Cancelled;
        await _context.SaveChangesAsync();
        if (isAdmin)
        {
            await NotifyRequesterAsync(viewing, "تحديث على طلب المعاينة",
                $"ألغت إدارة الموقع معاينة «{viewing.Listing?.Title}» يوم {viewing.RequestedAt:dd/MM/yyyy HH:mm}.");
            TempData["Info"] = "تم إلغاء المعاينة.";
            return RedirectToAction("Viewings", "Admin");
        }
        await NotifyAdminAsync("إلغاء طلب معاينة",
            $"ألغى مشتري معاينة «{viewing.Listing?.Title}» يوم {viewing.RequestedAt:dd/MM/yyyy HH:mm}.");
        TempData["Info"] = "تم إلغاء المعاينة.";
        return RedirectToAction(nameof(Index));
    }
}

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
            .Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == userId))
            .OrderByDescending(n => n.CreatedAt)
            .Take(5)
            .ToListAsync();
        var readIds = await _context.NotificationReads
            .Where(r => r.UserId == userId)
            .Select(r => r.NotificationId)
            .ToListAsync();
        var activeIds = await _context.Notifications
            .Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == userId))
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
    public int PendingReports { get; set; }
    public int PendingViewings { get; set; }
}

public class AdminNavViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;

    public AdminNavViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync(bool panelOnly = false)
    {
        if (!ViewContext.HttpContext.User.IsInRole("Admin"))
        {
            return Content(string.Empty);
        }
        ViewData["PanelOnly"] = panelOnly;
        // أرقام مصغرة للأدمن: حسابات بانتظار الموافقة، إعلانات بانتظار
        // الموافقة، وطلبات تواصل لم يُرد عليها بعد. تُحسب مع كل تحميل صفحة.
        var model = new AdminBadgeCounts
        {
            PendingUsers = await _context.Users.CountAsync(u => u.AccountStatus == AccountStatus.Pending),
            PendingListings = await _context.Listings.CountAsync(l => l.Approval == ListingApproval.Pending),
            UnrepliedRequests = await _context.ContactRequests.CountAsync(r => !r.IsReplied),
            PendingReports = await _context.ListingReports.CountAsync(r => r.Status == ReportStatus.Pending),
            PendingViewings = await _context.ViewingAppointments.CountAsync(v => v.Status == ViewingStatus.Pending)
        };
        return View(model);
    }
}
