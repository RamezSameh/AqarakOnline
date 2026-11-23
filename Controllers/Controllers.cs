using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Security.Claims;
using AqarakOnline.Data;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AqarakOnline.Controllers;

public class HomeController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> _hub;
    private readonly TurnstileService _turnstile;

    public HomeController(ApplicationDbContext context, IConfiguration configuration, Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub, TurnstileService turnstile)
    {
        _context = context;
        _configuration = configuration;
        _hub = hub;
        _turnstile = turnstile;
    }

    /// <summary>تحقق Turnstile (إن كان مفعّلًا) — يرجع false مع خطأ في النموذج عند الفشل.</summary>
    private async Task<bool> CheckTurnstileAsync()
    {
        if (!await _turnstile.IsEnabledAsync())
        {
            return true;
        }
        var token = Request.Form["cf-turnstile-response"].ToString();
        if (string.IsNullOrWhiteSpace(token)
            || !await _turnstile.VerifyAsync(token, HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            ModelState.AddModelError(string.Empty, "تعذّر التحقق من أنك لست روبوتًا — حدّث الصفحة وحاول مجددًا.");
            return false;
        }
        return true;
    }

    public async Task<IActionResult> Index()
    {
        var listings = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
            .OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow))
            .ThenByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
            .Take(6)
            .ToListAsync();
        ViewBag.AvailableListingsCount = await _context.Listings
            .CountAsync(l => l.IsAvailable && l.Approval == ListingApproval.Approved);
        ViewBag.AreasCount = await _context.Listings
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
            .Select(l => l.Area).Distinct().CountAsync();
        ViewBag.MostViewed = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
            .OrderByDescending(l => l.ViewCount).ThenByDescending(l => l.CreatedAt)
            .Take(5).ToListAsync();
        ViewBag.FeaturedListings = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved && l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow))
            .OrderByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
            .Take(6).ToListAsync();
        ViewBag.LatestSale = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved && l.ListingType == ListingType.Sale)
            .OrderByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
            .Take(4).ToListAsync();
        ViewBag.LatestRent = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved && l.ListingType == ListingType.Rent)
            .OrderByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
            .Take(4).ToListAsync();
        ViewBag.TodayListingsCount = await _context.Listings
            .CountAsync(l => l.Approval == ListingApproval.Approved && l.CreatedAt >= DateTime.UtcNow.Date);
        ViewBag.PopularAreas = await _context.Listings
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
            .GroupBy(l => l.Area)
            .Select(g => new ChartPoint { Label = g.Key, Count = g.Count() })
            .OrderByDescending(pt => pt.Count)
            .Take(6).ToListAsync();
        if (User.Identity?.IsAuthenticated == true)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            ViewBag.FavoriteListingIds = (await _context.Favorites
                .Where(f => f.UserId == userId)
                .Select(f => f.ListingId)
                .ToListAsync()).ToHashSet();
        }
        // Batch 41 (5) — المجموعات المختارة: الفعّالة فقط، وبإعلانات معتمدة ومتاحة فقط.
        var allCollections = await _context.Collections.Include(c => c.Items).ThenInclude(i => i.Listing).ThenInclude(l => l.Attachments)
            .Where(c => c.IsActive)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
        foreach (var c in allCollections)
        {
            c.Items = c.Items
                .Where(i => i.Listing != null && i.Listing.IsAvailable && i.Listing.Approval == ListingApproval.Approved)
                .OrderBy(i => i.SortOrder)
                .ToList();
        }
        ViewBag.Collections = allCollections.Where(c => c.Items.Any()).ToList();
        return View(listings);
    }

    /// <summary>مؤشر الأسعار العلني (Batch 41 — تميّز 3): متوسط سعر المتر لكل منطقة واتجاهه.</summary>
    [OutputCache(PolicyName = "Static300")]
    public async Task<IActionResult> PriceIndex()
    {
        var rows = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.AreaSqm > 0)
            .Select(l => new { l.Area, l.Price, l.AreaSqm, l.CreatedAt })
            .ToListAsync();
        var now = DateTime.UtcNow;
        var areas = rows.GroupBy(r => r.Area)
            .Select(g =>
            {
                var recent = g.Where(r => r.CreatedAt >= now.AddDays(-30)).ToList();
                var previous = g.Where(r => r.CreatedAt < now.AddDays(-30) && r.CreatedAt >= now.AddDays(-60)).ToList();
                decimal? trend = null;
                if (recent.Any() && previous.Any())
                {
                    var recentAvg = recent.Average(r => r.Price / r.AreaSqm);
                    var prevAvg = previous.Average(r => r.Price / r.AreaSqm);
                    if (prevAvg > 0)
                    {
                        trend = (recentAvg - prevAvg) / prevAvg * 100;
                    }
                }
                return new PriceIndexRow
                {
                    Area = g.Key,
                    Count = g.Count(),
                    AvgPricePerSqm = g.Average(r => r.Price / r.AreaSqm),
                    AvgPrice = g.Average(r => r.Price),
                    TrendPct = trend
                };
            })
            .OrderByDescending(r => r.Count)
            .ToList();
        return View(areas);
    }

    /// <summary>المجموعات المختارة (Batch 41 — تميّز 5): كل المجموعات الفعّالة.</summary>
    public async Task<IActionResult> Collections()
    {
        var collections = await _context.Collections.Include(c => c.Items).ThenInclude(i => i.Listing).ThenInclude(l => l.Attachments)
            .Where(c => c.IsActive)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
        foreach (var c in collections)
        {
            c.Items = c.Items
                .Where(i => i.Listing != null && i.Listing.IsAvailable && i.Listing.Approval == ListingApproval.Approved)
                .OrderBy(i => i.SortOrder)
                .ToList();
        }
        return View(collections.Where(c => c.Items.Any()).ToList());
    }

    /// <summary>صفحة مجموعة واحدة بإعلاناتها.</summary>
    public async Task<IActionResult> Collection(int id)
    {
        var collection = await _context.Collections.Include(c => c.Items).ThenInclude(i => i.Listing).ThenInclude(l => l.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id && c.IsActive);
        if (collection == null)
        {
            return NotFound();
        }
        collection.Items = collection.Items
            .Where(i => i.Listing != null && i.Listing.IsAvailable && i.Listing.Approval == ListingApproval.Approved)
            .OrderBy(i => i.SortOrder)
            .ToList();
        return View(collection);
    }

    public IActionResult Error()
    {
        return View();
    }

    public async Task<IActionResult> Error404()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewBag.SuggestedListings = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
            .OrderByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
            .Take(3).ToListAsync();
        return View();
    }

    [HttpGet]
    public IActionResult Contact()
    {
        return View(new ContactMessage());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("contact")]
    public async Task<IActionResult> Contact(ContactMessage model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        if (!await CheckTurnstileAsync())
        {
            return View(model);
        }
        model.CreatedAt = DateTime.UtcNow;
        _context.ContactMessages.Add(model);
        await _context.SaveChangesAsync();
        var adminEmail = _configuration["AdminEmail"];
        var admin = !string.IsNullOrWhiteSpace(adminEmail)
            ? await _context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail)
            : null;
        if (admin != null)
        {
            _context.Notifications.Add(new Notification
            {
                Title = "رسالة تواصل جديدة",
                Message = $"{model.Name} أرسل رسالة عبر «تواصل معنا».",
                TargetUserId = admin.Id,
                Url = "/Admin/ContactMessages",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await _context.SaveChangesAsync();
        }
        await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
        await _hub.Clients.All.SendAsync("refreshBell");
        TempData["Info"] = "تم إرسال رسالتك، وسيتواصل معك فريق الموقع.";
        return RedirectToAction(nameof(Contact));
    }

    [HttpGet]
    [OutputCache(PolicyName = "Static300")]
    public IActionResult SellGuide()
    {
        return View();
    }

    [HttpGet]
    [OutputCache(PolicyName = "Static300")]
    public IActionResult Terms()
    {
        return View();
    }

    [HttpGet]
    [OutputCache(PolicyName = "Static300")]
    public IActionResult Privacy()
    {
        return View();
    }

    [HttpGet]
    [OutputCache(PolicyName = "Static300")]
    public IActionResult Tips()
    {
        return View();
    }

    [HttpGet]
    [OutputCache(PolicyName = "Static300")]
    public IActionResult PriceGuide()
    {
        return View();
    }

    [HttpGet]
    [OutputCache(PolicyName = "Static300")]
    public IActionResult Calculator()
    {
        return View();
    }

    [HttpGet]
    [OutputCache(PolicyName = "Static300")]
    public IActionResult BuyingMistakes()
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

[Authorize]
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
    [EnableRateLimiting("search")]
    public async Task<IActionResult> Index(
        string? search, ListingType? listingType, PropertyType? propertyType,
        decimal? minPrice, decimal? maxPrice, bool onlyAvailable = false,
        int? minArea = null, int? maxArea = null, string? sort = null, int page = 1,
        int? minRooms = null, string? furnishing = null, string? area = null,
        string? adNumber = null, string? recency = null, bool directOwnerOnly = false,
        FinishingLevel? finishing = null, int? minFloor = null, int? maxFloor = null,
        bool hasParking = false, bool hasElevator = false, bool hasBalcony = false,
        bool hasAirConditioning = false, bool hasSecurity = false, bool installmentsOnly = false,
        bool negotiableOnly = false, RentPeriod? rentPeriod = null,
        bool hasElectricityMeter = false, bool hasGasMeter = false, bool hasWaterMeter = false,
        int? minBuildingFloors = null, string? advertiserType = null)
    {
        var query = _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.Approval == ListingApproval.Approved).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(l => l.Title.Contains(search) || l.Area.Contains(search));
            var term = search.Trim();
            if (term.Length > 100)
            {
                term = term[..100];
            }
            _context.SearchLogs.Add(new SearchLog { Term = term });
            await _context.SaveChangesAsync();
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
        if (!string.IsNullOrWhiteSpace(furnishing))
        {
            if (furnishing == "Full")
            {
                query = query.Where(l => l.Furnishing == FurnishingLevel.Full);
            }
            else if (furnishing == "Partial")
            {
                query = query.Where(l => l.Furnishing == FurnishingLevel.Partial);
            }
            else if (furnishing == "Any")
            {
                query = query.Where(l => l.Furnishing != FurnishingLevel.None);
            }
        }
        if (minFloor.HasValue)
        {
            query = query.Where(l => l.Floor >= minFloor.Value);
        }
        if (maxFloor.HasValue)
        {
            query = query.Where(l => l.Floor <= maxFloor.Value);
        }
        if (hasParking)
        {
            query = query.Where(l => l.HasParking);
        }
        if (hasElevator)
        {
            query = query.Where(l => l.HasElevator);
        }
        if (hasBalcony)
        {
            query = query.Where(l => l.HasBalcony);
        }
        if (hasAirConditioning)
        {
            query = query.Where(l => l.HasAirConditioning);
        }
        if (hasSecurity)
        {
            query = query.Where(l => l.HasSecurity);
        }
        if (installmentsOnly)
        {
            query = query.Where(l => l.AcceptsInstallments);
        }
        if (negotiableOnly)
        {
            query = query.Where(l => l.IsPriceNegotiable);
        }
        if (rentPeriod.HasValue)
        {
            query = query.Where(l => l.RentPeriod == rentPeriod.Value);
        }
        if (hasElectricityMeter)
        {
            query = query.Where(l => l.HasElectricityMeter);
        }
        if (hasGasMeter)
        {
            query = query.Where(l => l.HasGasMeter);
        }
        if (hasWaterMeter)
        {
            query = query.Where(l => l.HasWaterMeter);
        }
        if (minBuildingFloors.HasValue)
        {
            query = query.Where(l => l.BuildingFloors >= minBuildingFloors.Value);
        }
        if (!string.IsNullOrWhiteSpace(adNumber))
        {
            var adTerm = adNumber.Trim().ToUpperInvariant();
            query = query.Where(l => l.AdNumber.ToUpper() == adTerm);
        }
        if (directOwnerOnly)
        {
            query = query.Where(l => l.AdvertiserType == AdvertiserType.Owner);
        }
        if (!string.IsNullOrWhiteSpace(advertiserType))
        {
            if (advertiserType == "Broker")
            {
                query = query.Where(l => l.AdvertiserType == AdvertiserType.Broker);
            }
            else if (advertiserType == "Owner")
            {
                query = query.Where(l => l.AdvertiserType == AdvertiserType.Owner);
            }
        }
        if (finishing.HasValue)
        {
            query = query.Where(l => l.Finishing == finishing.Value);
        }
        // فلتر «جديد»: اليوم = آخر 24 ساعة، الأسبوع = آخر 7 أيام، الشهر = آخر 30 يومًا.
        var recencyKey = (recency ?? "any").ToLowerInvariant() switch
        {
            "today" => "today",
            "week" => "week",
            "month" => "month",
            _ => "any"
        };
        if (recencyKey == "today")
        {
            query = query.Where(l => l.CreatedAt >= DateTime.UtcNow.AddDays(-1));
        }
        else if (recencyKey == "week")
        {
            query = query.Where(l => l.CreatedAt >= DateTime.UtcNow.AddDays(-7));
        }
        else if (recencyKey == "month")
        {
            query = query.Where(l => l.CreatedAt >= DateTime.UtcNow.AddDays(-30));
        }

        // الترتيب: الافتراضي الأحدث. القيم المقبولة فقط هي المعرّفة هنا،
        // وأي قيمة غير معروفة تعود للأحدث بدل ما تكسر الاستعلام.
        var sortKey = (sort ?? "newest").ToLowerInvariant() switch
        {
            "priceasc" => "priceAsc",
            "pricedesc" => "priceDesc",
            "areadesc" => "areaDesc",
            "viewsdesc" => "viewsDesc",
            "pricepersqmasc" => "pricePerSqmAsc",
            "pricepersqmdesc" => "pricePerSqmDesc",
            "roomsdesc" => "roomsDesc",
            "roomsasc" => "roomsAsc",
            "oldest" => "oldest",
            _ => "newest"
        };
        // الإعلانات المميزة أولًا دائمًا، ثم الترتيب المختار.
        query = sortKey switch
        {
            "priceAsc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenBy(l => l.Price).ThenByDescending(l => l.CreatedAt),
            "priceDesc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenByDescending(l => l.Price).ThenByDescending(l => l.CreatedAt),
            "areaDesc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenByDescending(l => l.AreaSqm).ThenByDescending(l => l.CreatedAt),
            "viewsDesc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenByDescending(l => l.ViewCount).ThenByDescending(l => l.CreatedAt),
            "pricePerSqmAsc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenBy(l => l.Price / l.AreaSqm).ThenByDescending(l => l.CreatedAt),
            "pricePerSqmDesc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenByDescending(l => l.Price / l.AreaSqm).ThenByDescending(l => l.CreatedAt),
            "roomsDesc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenByDescending(l => l.Rooms).ThenByDescending(l => l.CreatedAt),
            "roomsAsc" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenBy(l => l.Rooms).ThenByDescending(l => l.CreatedAt),
            "oldest" => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenBy(l => l.LastBumpedAt ?? l.CreatedAt),
            _ => query.OrderByDescending(l => l.IsFeatured && (l.FeaturedUntil == null || l.FeaturedUntil > DateTime.UtcNow)).ThenByDescending(l => l.LastBumpedAt ?? l.CreatedAt)
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
        ViewBag.Furnishing = furnishing;
        ViewBag.Area = area;
        ViewBag.AdNumber = adNumber;
        ViewBag.Recency = recencyKey;
        ViewBag.DirectOwnerOnly = directOwnerOnly;
        ViewBag.AdvertiserType = advertiserType;
        ViewBag.Finishing = finishing;
        ViewBag.MinFloor = minFloor;
        ViewBag.MaxFloor = maxFloor;
        ViewBag.HasParking = hasParking;
        ViewBag.HasElevator = hasElevator;
        ViewBag.HasBalcony = hasBalcony;
        ViewBag.HasAirConditioning = hasAirConditioning;
        ViewBag.HasSecurity = hasSecurity;
        ViewBag.InstallmentsOnly = installmentsOnly;
        ViewBag.NegotiableOnly = negotiableOnly;
        ViewBag.RentPeriod = rentPeriod;
        ViewBag.HasElectricityMeter = hasElectricityMeter;
        ViewBag.HasGasMeter = hasGasMeter;
        ViewBag.HasWaterMeter = hasWaterMeter;
        ViewBag.MinBuildingFloors = minBuildingFloors;
        // عدد الإعلانات المتاحة في كل منطقة — يظهر جنب اسم المنطقة في الفلتر.
        ViewBag.AreaCounts = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable)
            .GroupBy(l => l.Area)
            .Select(g => new { Area = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Area, g => g.Count);
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
        // Batch 45 — اسم الفيو صريح: الـ Area بيستدعي Index مباشرة، ولو
        // الفيو من غير اسم الـ ViewResult بياخد اسم الأكشن الحالي (Area)
        // فكان بيدوّر على Area.cshtml مش موجودة ويرمي 500 بدل صفحة المنطقة.
        return View("Index", listings);
    }

    /// <summary>اقتراحات أثناء الكتابة في البحث: مناطق مطابقة + عناوين إعلانات مطابقة.</summary>
    [HttpGet]
    [EnableRateLimiting("search")]
    public async Task<IActionResult> Suggest(string? term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Trim().Length < 2)
        {
            return Json(Array.Empty<object>());
        }
        var t = term.Trim();
        var areas = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.Area.Contains(t))
            .Select(l => l.Area).Distinct().OrderBy(a => a).Take(5).ToListAsync();
        var listings = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.Title.Contains(t))
            .OrderByDescending(l => l.CreatedAt).Take(5)
            .Select(l => new { l.Id, l.Title }).ToListAsync();
        var result = areas.Select(a => new { kind = "area", label = a, url = Url.Action(nameof(Index), new { area = a }) })
            .Concat(listings.Select(l => new { kind = "listing", label = l.Title, url = Url.Action(nameof(Details), new { id = l.Id }) }))
            .ToList();
        return Json(result);
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
        // متوسط سعر المتر في نفس المنطقة (من الإعلانات المعتمدة غير هذا الإعلان)،
        // وسعر المتر في هذا الإعلان — للمقارنة في صفحة التفاصيل.
        var areaSqmPrices = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.Area == listing.Area
                && l.Id != listing.Id && l.AreaSqm > 0)
            .Select(l => l.Price / l.AreaSqm).ToListAsync();
        ViewBag.AreaAvgPricePerSqm = areaSqmPrices.Any() ? areaSqmPrices.Average() : (decimal?)null;
        ViewBag.PricePerSqm = listing.AreaSqm > 0 ? listing.Price / listing.AreaSqm : (decimal?)null;
        // إعلانات مشابهة: نفس المنطقة أولًا، ثم نفس النوع، وبسعر قريب.
        ViewBag.SimilarListings = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.Approval == ListingApproval.Approved && l.Id != listing.Id
                && (l.Area == listing.Area || l.PropertyType == listing.PropertyType))
            .OrderByDescending(l => l.Area == listing.Area)
            .ThenBy(l => Math.Abs(l.Price - listing.Price))
            .Take(4)
            .ToListAsync();
        ViewBag.PreviousListing = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.CreatedAt < listing.CreatedAt)
            .OrderByDescending(l => l.CreatedAt).Select(l => new { l.Id, l.Title }).FirstOrDefaultAsync();
        ViewBag.NextListing = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.CreatedAt > listing.CreatedAt)
            .OrderBy(l => l.CreatedAt).Select(l => new { l.Id, l.Title }).FirstOrDefaultAsync();
        var areaPrices = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.Area == listing.Area && l.Id != listing.Id)
            .OrderBy(l => l.Price).Select(l => new { l.Id, l.Title, l.Price, l.AreaSqm }).Take(50).ToListAsync();
        ViewBag.SimilarPrices = areaPrices.OrderBy(l => Math.Abs(l.Price - listing.Price)).Take(5).ToList();
        // Batch 41 (7) — مواعيد المعاينة المتاحة التي حددها صاحب الإعلان.
        ViewBag.AvailableSlots = await _context.ViewingSlots
            .Where(s => s.ListingId == listing.Id && !s.IsBooked && s.SlotDate >= DateTime.Now.Date)
            .OrderBy(s => s.SlotDate).ThenBy(s => s.StartTime)
            .ToListAsync();
        // Batch 41 (2) — هل لي عرض سعر معلّق على هذا الإعلان؟
        if (User.Identity?.IsAuthenticated == true && listing.OwnerId != CurrentUserId)
        {
            ViewBag.MyPendingOffer = await _context.ListingOffers
                .Where(o => o.ListingId == listing.Id && o.BuyerId == CurrentUserId && o.Status == OfferStatus.Pending)
                .OrderByDescending(o => o.CreatedAt).FirstOrDefaultAsync();
        }
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
        // Batch 44 — كان الشرط `ListingAreas.All.Contains(area)` يرجّع 404 لأي
        // منطقة خارج قائمة الـ 26 (مثل روابط الفوتر المختصرة، أو منطقة من
        // استيراد CSV) رغم وجود إعلانات فيها. الآن أي منطقة غير فارغة تعرض
        // إعلاناتها، ولو بلا إعلانات تظهر حالة «لا توجد نتائج» بدل صفحة 404.
        if (string.IsNullOrWhiteSpace(area))
        {
            return NotFound();
        }
        ViewBag.AreaPage = area;
        ViewBag.CanonicalUrl = $"{Request.Scheme}://{Request.Host}/Listings/Area/{Uri.EscapeDataString(area)}";
        var areaSqmPrices = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.Area == area && l.AreaSqm > 0)
            .Select(l => l.Price / l.AreaSqm).ToListAsync();
        ViewBag.AreaAvgPricePerSqm = areaSqmPrices.Any() ? areaSqmPrices.Average() : (decimal?)null;
        // Batch 91 — اسم باراميتر الراوت «area» بيتصادم مع مفتاح الـ Area المحجوز في MVC:
        // محرك الفيو بيشوف RouteData["area"] = اسم المنطقة ويدوّر على الفيو تحت
        // /Areas/«اسم المنطقة»/Views/... بس، وعمره ما بيوصل لـ /Views/Listings/Index.cshtml
        // (ده اللي كان بيرمي 500 رغم إن اسم الفيو «Index» صح). بنشيل القيمة من الـ RouteData
        // قبل ما ننادي Index فالفيو يتلاقى بالمسار العادي. الـ area نفسه متخزّن خلاص في ViewBag.
        RouteData.Values.Remove("area");
        return await Index(search: null, listingType: null, propertyType: null,
            minPrice: null, maxPrice: null, onlyAvailable: false,
            minArea: null, maxArea: null, sort: null, page: 1,
            minRooms: null, furnishing: null, area: area);
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

    /// <summary>متوسط سعر المتر للإعلانات المقبولة والمتاحة في منطقة — لاقتراح سعر استرشادي في فورم الإضافة.</summary>
    [HttpGet]
    public async Task<IActionResult> AreaAverage(string area)
    {
        if (string.IsNullOrWhiteSpace(area) || !ListingAreas.All.Contains(area.Trim()))
        {
            return Json(new { avgPerSqm = 0 });
        }
        var prices = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.Area == area.Trim() && l.AreaSqm > 0)
            .Select(l => l.Price / l.AreaSqm)
            .ToListAsync();
        var avg = prices.Count == 0 ? 0 : (decimal)prices.Average();
        return Json(new { avgPerSqm = Math.Round(avg, 0) });
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var userId = User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier);
        ViewBag.MyListings = await _context.Listings
            .Where(l => l.OwnerId == userId)
            .OrderByDescending(l => l.CreatedAt).Take(20).ToListAsync();
        return View(new Listing());
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(100_000_000)]
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Create(Listing listing, string[]? selectedFeatures = null, string[]? customFeatures = null)
    {
        listing.Features = ListingFeatureComposer.ComposeFeatures(selectedFeatures, customFeatures);
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
        // السعر الأصلي = سعر الإنشاء؛ أي تخفيض لاحق يظهر شارة «تم تخفيض السعر».
        listing.OriginalPrice = listing.Price;
        // الدور خاص بالشقة فقط؛ أي قيمة مرسلة مع نوع آخر تُتجاهل.
        if (listing.PropertyType != PropertyType.Apartment)
        {
            listing.Floor = null;
        }
        // عدد الشقق خاص بالعمارة فقط، وعدد المصاعد خاص بوجود مصعد.
        if (listing.PropertyType != PropertyType.Building)
        {
            listing.ApartmentsCount = null;
        }
        if (!listing.HasElevator)
        {
            listing.ElevatorCount = null;
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
        await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId, "إنشاء", "تم إنشاء الإعلان.");

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
    [EnableRateLimiting("upload")]
    public async Task<IActionResult> Edit(int id, Listing model, string? photoOrder, string[]? selectedFeatures = null, string[]? customFeatures = null)
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
        model.Features = ListingFeatureComposer.ComposeFeatures(selectedFeatures, customFeatures);
        if (!ModelState.IsValid)
        {
            model.Attachments = listing.Attachments;
            return View(model);
        }

        var oldTitle = listing.Title;
        var oldPrice = listing.Price;
        var oldAreaSqm = listing.AreaSqm;
        var oldArea = listing.Area;
        var oldAvailable = listing.IsAvailable;
        var oldFinishing = listing.Finishing;
        var oldListingType = listing.ListingType;
        var oldPropertyType = listing.PropertyType;
        listing.Title = model.Title;
        listing.ListingType = model.ListingType;
        listing.PropertyType = model.PropertyType;
        listing.Area = model.Area;
        listing.DetailedAddress = model.DetailedAddress;
        listing.Description = model.Description ?? string.Empty;
        listing.Features = ListingFeatureComposer.ComposeFeatures(selectedFeatures, customFeatures);
        listing.Price = model.Price;
        // لو السعر الجديد أعلى من الأصلي، الأصلي يتحدث معه؛
        // ولو أقل، الأصلي يفضل كما هو فتظهر شارة «تم تخفيض السعر».
        if (listing.Price > listing.OriginalPrice)
        {
            listing.OriginalPrice = listing.Price;
        }
        listing.AreaSqm = model.AreaSqm;
        listing.AdvertiserType = model.AdvertiserType;
        listing.Rooms = model.Rooms;
        listing.Bathrooms = model.Bathrooms;
        listing.Floor = model.PropertyType == PropertyType.Apartment ? model.Floor : null;
        listing.ApartmentsCount = model.PropertyType == PropertyType.Building ? model.ApartmentsCount : null;
        listing.Furnishing = model.Furnishing;
        listing.SaleReason = model.ListingType == ListingType.Sale && !string.IsNullOrWhiteSpace(model.SaleReason)
            ? model.SaleReason.Trim()
            : null;
        listing.Finishing = model.Finishing;
        listing.HasParking = model.HasParking;
        listing.HasElevator = model.HasElevator;
        listing.ElevatorCount = model.HasElevator ? model.ElevatorCount : null;
        listing.HasBalcony = model.HasBalcony;
        listing.HasAirConditioning = model.HasAirConditioning;
        listing.HasSecurity = model.HasSecurity;
        listing.AcceptsInstallments = model.AcceptsInstallments;
        listing.IsPriceNegotiable = model.IsPriceNegotiable;
        listing.IsUrgent = model.IsUrgent;
        listing.RentPeriod = model.ListingType == ListingType.Rent ? model.RentPeriod : null;
        listing.DepositMonths = model.ListingType == ListingType.Rent ? model.DepositMonths : null;
        listing.HasElectricityMeter = model.HasElectricityMeter;
        listing.HasGasMeter = model.HasGasMeter;
        listing.HasWaterMeter = model.HasWaterMeter;
        listing.LandAreaSqm = model.LandAreaSqm;
        listing.BuildingFloors = model.BuildingFloors;
        listing.UpdatedAt = DateTime.UtcNow;
        listing.IsAvailable = model.IsAvailable;
        ClearFeaturedIfUnavailable(listing);
        if (!User.IsInRole("Admin"))
        {
            listing.Approval = ListingApproval.Pending;
            listing.RejectionReason = null;
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
        var changes = new List<string>();
        if (listing.Title != oldTitle) changes.Add($"العنوان: من «{oldTitle}» إلى «{listing.Title}»");
        if (listing.Price != oldPrice) changes.Add($"السعر: من {oldPrice:N0} إلى {listing.Price:N0} جنيه");
        if (listing.AreaSqm != oldAreaSqm) changes.Add($"المساحة: من {oldAreaSqm} إلى {listing.AreaSqm} م²");
        if (listing.Area != oldArea) changes.Add($"المنطقة: من {oldArea} إلى {listing.Area}");
        if (listing.IsAvailable != oldAvailable) changes.Add(listing.IsAvailable ? "أصبح متاحًا" : "أصبح غير متاح");
        if (listing.Finishing != oldFinishing) changes.Add("التشطيب اتغيّر");
        if (listing.ListingType != oldListingType || listing.PropertyType != oldPropertyType) changes.Add("نوع الإعلان أو العقار اتغيّر");
        await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId, "تعديل", changes.Any() ? string.Join("؛ ", changes) : "تعديل بدون تغيير في الحقول الرئيسية.");
        if (listing.Price < oldPrice)
        {
            var favoriterIds = await _context.Favorites
                .Where(f => f.ListingId == id && f.UserId != listing.OwnerId)
                .Select(f => f.UserId).Distinct().ToListAsync();
            foreach (var favoriterId in favoriterIds)
            {
                _context.Notifications.Add(new Notification
                {
                    Title = "انخفض سعر إعلان في مفضلتك",
                    Message = $"سعر «{listing.Title}» نزل من {oldPrice:N0} إلى {listing.Price:N0} جنيه.",
                    TargetUserId = favoriterId,
                    Url = $"/Listings/Details/{listing.Id}",
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                });
            }
            if (favoriterIds.Any())
            {
                await _context.SaveChangesAsync();
                await _hub.Clients.All.SendAsync("refreshBell");
            }
        }
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize]
    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var listing = await _context.Listings.Include(l => l.Attachments).FirstOrDefaultAsync(l => l.Id == id);
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
    /// <summary>إيقاف/تشغيل إعلان بسرعة من «إعلاناتي» — لصاحب الإعلان أو الأدمن.</summary>
    /// <summary>إعلان غير متاح يفقد التمييز تلقائيًا (Batch 69).</summary>
    private static void ClearFeaturedIfUnavailable(Listing listing)
    {
        if (!listing.IsAvailable && listing.IsFeatured)
        {
            listing.IsFeatured = false;
            listing.FeaturedUntil = null;
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleAvailability(int id)
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
        listing.IsAvailable = !listing.IsAvailable;
        ClearFeaturedIfUnavailable(listing);
        listing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId,
            listing.IsAvailable ? "تشغيل" : "إيقاف",
            listing.IsAvailable ? "تم تشغيل الإعلان من «إعلاناتي»." : "تم إيقاف الإعلان مؤقتًا من «إعلاناتي».");
        return RedirectToAction(nameof(MyListings));
    }

    /// <summary>تعيين صورة الغلاف للإعلان — لصاحب الإعلان أو الأدمن.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetCoverPhoto(int id, int attachmentId)
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
        var attachment = listing.Attachments
            .FirstOrDefault(a => a.Id == attachmentId && a.Kind == AttachmentKind.Photo);
        if (attachment == null)
        {
            return NotFound();
        }
        // الغلاف هو الصورة الأولى بالترتيب؛ -1 يضعها قبل كل الصور،
        // وأي غلاف سابق يعود لترتيبه الطبيعي.
        foreach (var photo in listing.Attachments.Where(a => a.Kind == AttachmentKind.Photo))
        {
            photo.Order = photo.Id == attachmentId ? -1 : (photo.Order < 0 ? 0 : photo.Order);
        }
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم تعيين صورة الغلاف.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    public async Task<IActionResult> MyListings(string? status = null, string? search = null, string? sort = null)
    {
        var query = _context.Listings.Include(l => l.Attachments)
            .Where(l => l.OwnerId == CurrentUserId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l => l.Title.Contains(term) || l.AdNumber.Contains(term));
        }
        var statusKey = (status ?? "all").ToLowerInvariant() switch
        {
            "available" => "available",
            "unavailable" => "unavailable",
            "pending" => "pending",
            _ => "all"
        };
        if (statusKey == "available")
        {
            query = query.Where(l => l.IsAvailable);
        }
        else if (statusKey == "unavailable")
        {
            query = query.Where(l => !l.IsAvailable);
        }
        else if (statusKey == "pending")
        {
            query = query.Where(l => l.Approval == ListingApproval.Pending);
        }
        var sortKey = (sort ?? "newest").ToLowerInvariant() switch
        {
            "priceasc" => "priceAsc",
            "pricedesc" => "priceDesc",
            "oldest" => "oldest",
            "viewsdesc" => "viewsDesc",
            _ => "newest"
        };
        query = sortKey switch
        {
            "priceAsc" => query.OrderBy(l => l.Price).ThenByDescending(l => l.CreatedAt),
            "priceDesc" => query.OrderByDescending(l => l.Price).ThenByDescending(l => l.CreatedAt),
            "oldest" => query.OrderBy(l => l.CreatedAt),
            "viewsDesc" => query.OrderByDescending(l => l.ViewCount).ThenByDescending(l => l.CreatedAt),
            _ => query.OrderByDescending(l => l.CreatedAt)
        };
        ViewBag.Status = statusKey;
        ViewBag.Search = search;
        ViewBag.Sort = sortKey;
        var listings = await query.ToListAsync();
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
            await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId, "تجديد", "تم تجديد الإعلان.");
            TempData["Info"] = "تم تجديد الإعلان — ظهر أول النتائج.";
        }
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return RedirectToAction(nameof(MyListings));
    }

    /// <summary>تجديد كل إعلاناتي اللي عدى عليها 7 أيام من آخر تجديد — كل إعلان لسه في فترة الحظر بيتخطى.</summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BumpAll()
    {
        var mine = await _context.Listings
            .Where(l => l.OwnerId == CurrentUserId)
            .ToListAsync();
        var bumped = 0;
        foreach (var listing in mine)
        {
            if (listing.LastBumpedAt.HasValue && listing.LastBumpedAt.Value.AddDays(7) > DateTime.UtcNow)
            {
                continue;
            }
            listing.LastBumpedAt = DateTime.UtcNow;
            await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId, "تجديد", "تم تجديد الإعلان.");
            bumped++;
        }
        await _context.SaveChangesAsync();
        TempData["Info"] = bumped > 0 ? $"تم تجديد {bumped} إعلان." : "مفيش إعلانات جاهزة للتجديد دلوقتي.";
        return RedirectToAction(nameof(MyListings));
    }

    /// <summary>
    /// نسخ الإعلان: نسخة جديدة بنفس البيانات والمرفقات (ملفات منسوخة)،
    /// معلّقة تنتظر موافقة الأدمن — وتفتح صفحة التعديل لمراجعتها.
    /// </summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Duplicate(int id)
    {
        var source = await _context.Listings.Include(l => l.Attachments)
            .FirstOrDefaultAsync(l => l.Id == id);
        if (source == null)
        {
            return NotFound();
        }
        if (source.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        var copy = new Listing
        {
            OwnerId = source.OwnerId,
            Title = source.Title + " (نسخة)",
            ListingType = source.ListingType,
            PropertyType = source.PropertyType,
            Area = source.Area,
            DetailedAddress = source.DetailedAddress,
            Description = source.Description,
            Features = source.Features,
            Price = source.Price,
            AreaSqm = source.AreaSqm,
            AdvertiserType = source.AdvertiserType,
            Rooms = source.Rooms,
            Bathrooms = source.Bathrooms,
            Floor = source.PropertyType == PropertyType.Apartment ? source.Floor : null,
            Furnishing = source.Furnishing,
            SaleReason = source.SaleReason,
            Finishing = source.Finishing,
            HasParking = source.HasParking,
            HasElevator = source.HasElevator,
            HasBalcony = source.HasBalcony,
            HasAirConditioning = source.HasAirConditioning,
            HasSecurity = source.HasSecurity,
            AcceptsInstallments = source.AcceptsInstallments,
            IsPriceNegotiable = source.IsPriceNegotiable,
            RentPeriod = source.RentPeriod,
            DepositMonths = source.DepositMonths,
            HasElectricityMeter = source.HasElectricityMeter,
            HasGasMeter = source.HasGasMeter,
            HasWaterMeter = source.HasWaterMeter,
            LandAreaSqm = source.LandAreaSqm,
            BuildingFloors = source.BuildingFloors,
            ApartmentsCount = source.ApartmentsCount,
            ElevatorCount = source.ElevatorCount,
            OriginalPrice = source.Price,
            UpdatedAt = null,
            IsAvailable = true,
            IsFeatured = false,
            FeaturedUntil = null,
            IsUrgent = false,
            ViewCount = 0,
            LastBumpedAt = null,
            Approval = User.IsInRole("Admin") ? ListingApproval.Approved : ListingApproval.Pending,
            CreatedAt = DateTime.UtcNow,
            AdNumber = "TEMP",
            Attachments = new List<ListingAttachment>()
        };
        _context.Listings.Add(copy);
        await _context.SaveChangesAsync();
        copy.AdNumber = $"AQ-{copy.Id:D4}";
        // نسخ ملفات المرفقات (صور/فيديو) إلى مجلد النسخة بأسماء جديدة،
        // حتى لا يتأثر أي إعلان بحذف مرفقات الآخر.
        foreach (var attachment in source.Attachments.OrderBy(a => a.Order))
        {
            var sourcePath = Path.Combine(_env.WebRootPath, attachment.FilePath.Replace('/', Path.DirectorySeparatorChar));
            if (!System.IO.File.Exists(sourcePath))
            {
                continue;
            }
            var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(attachment.FilePath);
            var folder = Path.Combine(_env.WebRootPath, "uploads", "listings", copy.Id.ToString());
            Directory.CreateDirectory(folder);
            System.IO.File.Copy(sourcePath, Path.Combine(folder, fileName));
            _context.ListingAttachments.Add(new ListingAttachment
            {
                ListingId = copy.Id,
                Kind = attachment.Kind,
                FilePath = $"uploads/listings/{copy.Id}/{fileName}",
                ContentType = attachment.ContentType,
                OriginalFileName = attachment.OriginalFileName,
                SizeBytes = attachment.SizeBytes,
                Order = attachment.Order
            });
        }
        await _context.SaveChangesAsync();
        await AuditLogHelper.AddAsync(_context, copy.Id, CurrentUserId, "نسخ", $"نسخة من الإعلان {source.AdNumber}.");
        if (copy.Approval == ListingApproval.Pending)
        {
            await _hub.Clients.Group(Services.RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
            TempData["Info"] = "تم نسخ الإعلان — راجعه وعدّل اللي تحتاجه، وهو مستني موافقة الأدمن.";
        }
        else
        {
            await SavedSearchAlerts.NotifyMatchingAsync(_context, copy, _hub);
            await _hub.Clients.All.SendAsync("listingsChanged");
            TempData["Info"] = "تم نسخ الإعلان ونشره.";
        }
        return RedirectToAction(nameof(Edit), new { id = copy.Id });
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
        ClearFeaturedIfUnavailable(listing);
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
    [EnableRateLimiting("contact")]
    public async Task<IActionResult> Contact(ContactRequest request, string? preferredTime = null)
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
        if (!string.IsNullOrWhiteSpace(preferredTime))
        {
            var timeNote = "أفضل وقت للتواصل: " + preferredTime;
            request.Message = string.IsNullOrEmpty(request.Message) ? timeNote : request.Message + " — " + timeNote;
            if (request.Message.Length > 2000) { request.Message = request.Message[..2000]; }
        }
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

public class ImportPreviewRow
{
    public int RowNumber { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public class AdminUserRow
{
    public ApplicationUser User { get; set; } = null!;
    public string Role { get; set; } = "User";
    public bool IsAdmin { get; set; }
    public int ListingsCount { get; set; }
    public int ApprovedListingsCount { get; set; }
    public int PendingListingsCount { get; set; }
    public DateTime? LastActivityAt { get; set; }
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
    public int SaleListings { get; set; }
    public int RentListings { get; set; }
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
    /// <summary>إعلانات معلّقة بقالها أكثر من 3 أيام — تحتاج قرار موافقة/رفض.</summary>
    public List<Listing> StalePendingListings { get; set; } = new();
    /// <summary>طلبات اتصال لم يُرد عليها منذ أكثر من 48 ساعة.</summary>
    public List<ContactRequest> StaleRequests { get; set; } = new();
    /// <summary>إعلانات مقبولة ومتاحة بقالها أكثر من 60 يومًا — تحتاج تنبيه تجديد لصاحبها.</summary>
    public List<Listing> RenewalListings { get; set; } = new();
    /// <summary>إعلانات جديدة في آخر 6 شهور (للرسم البياني).</summary>
    public List<ChartPoint> MonthlyListings { get; set; } = new();
    /// <summary>تسجيلات مستخدمين جديدة في آخر 6 شهور (للرسم البياني).</summary>
    public List<ChartPoint> MonthlyUsers { get; set; } = new();
    /// <summary>توزيع الإعلانات حسب المنطقة — أعلى 8 مناطق.</summary>
    public List<ChartPoint> ListingsByArea { get; set; } = new();
    /// <summary>أكثر 5 إعلانات مشاهدة.</summary>
    public List<Listing> TopViewed { get; set; } = new();
    /// <summary>أكثر كلمات البحث في آخر 30 يومًا (للرسم البياني).</summary>
    public List<ChartPoint> TopSearchTerms { get; set; } = new();
    /// <summary>أكثر 5 إعلانات بلاغًا (للرسم البياني).</summary>
    public List<ChartPoint> TopReported { get; set; } = new();
    /// <summary>وضع الصيانة مفعّل؟</summary>
    public bool MaintenanceEnabled { get; set; }
    /// <summary>رسالة وضع الصيانة المعروضة للزوار.</summary>
    public string? MaintenanceMessage { get; set; }
    /// <summary>أكثر 5 إعلانات طلب تواصل.</summary>
    public List<ChartPoint> TopContactedListings { get; set; } = new();
    /// <summary>أكثر 5 مستخدمين إضافة إعلانات.</summary>
    public List<ChartPoint> TopPosters { get; set; } = new();
    /// <summary>متوسط سعر المتر حسب المنطقة — من الإعلانات المقبولة والمتاحة.</summary>
    public List<AreaPricePoint> AreaPrices { get; set; } = new();
    /// <summary>توزيع الإعلانات حسب نوع العقار (شقة/بيت/عمارة/محل/فيلا).</summary>
    public List<ChartPoint> ListingsByPropertyType { get; set; } = new();
    /// <summary>توزيع الإعلانات حسب الغرض (بيع/إيجار).</summary>
    public List<ChartPoint> ListingsByListingType { get; set; } = new();
    /// <summary>توزيع الإعلانات حسب نوع المعلن (مالك/وسيط).</summary>
    public List<ChartPoint> ListingsByAdvertiserType { get; set; } = new();
    /// <summary>توزيع المستخدمين حسب حالة الحساب (معلّق/مفعّل/موقوف/محظور).</summary>
    public List<ChartPoint> UsersByStatus { get; set; } = new();
}

/// <summary>متوسط سعر المتر في منطقة: المنطقة ومتوسط السعر وعدد الإعلانات.</summary>
public class AreaPricePoint
{
    public string Area { get; set; } = string.Empty;
    public decimal AvgPricePerSqm { get; set; }
    public int ListingsCount { get; set; }
}

/// <summary>نقطة في رسم بياني: تسمية وعدد.</summary>
public class ChartPoint
{
    public string Label { get; set; } = string.Empty;
    public int Count { get; set; }
}

/// <summary>صف في مؤشر الأسعار العلني: متوسط سعر المتر واتجاهه في منطقة.</summary>
public class PriceIndexRow
{
    public string Area { get; set; } = string.Empty;
    public int Count { get; set; }
    public decimal AvgPricePerSqm { get; set; }
    public decimal AvgPrice { get; set; }

    /// <summary>نسبة تغيّر متوسط سعر المتر (آخر 30 يومًا مقابل الـ 30 يومًا قبلها) — null إن تعذّر الحساب.</summary>
    public decimal? TrendPct { get; set; }
}

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWebHostEnvironment _env;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> _hub;
    private readonly LanguageService _language;
    private readonly FacebookService _facebook;

    public AdminController(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment env, Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub, LanguageService language, FacebookService facebook)
    {
        _context = context;
        _userManager = userManager;
        _env = env;
        _hub = hub;
        _language = language;
        _facebook = facebook;
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
                IsAdmin = roles.Contains("Admin"),
                ListingsCount = await userListings.CountAsync(),
                ApprovedListingsCount = await userListings.CountAsync(l => l.Approval == ListingApproval.Approved),
                PendingListingsCount = await userListings.CountAsync(l => l.Approval == ListingApproval.Pending),
                LastActivityAt = await userListings.MaxAsync(l => (DateTime?)l.CreatedAt)
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
            MaintenanceEnabled = await SiteSettingsHelper.IsMaintenanceEnabledAsync(_context),
            MaintenanceMessage = await SiteSettingsHelper.GetMaintenanceMessageAsync(_context),
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
                .ToListAsync(),
            StalePendingListings = await _context.Listings.Include(l => l.Owner)
                .Where(l => l.Approval == ListingApproval.Pending
                    && l.CreatedAt <= DateTime.UtcNow.AddDays(-3))
                .OrderBy(l => l.CreatedAt)
                .ToListAsync(),
            StaleRequests = await _context.ContactRequests
                .Include(r => r.Listing)
                .Where(r => !r.IsReplied && r.CreatedAt <= DateTime.UtcNow.AddDays(-2))
                .OrderBy(r => r.CreatedAt)
                .ToListAsync(),
            RenewalListings = await _context.Listings.Include(l => l.Owner)
                .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable
                    && l.CreatedAt <= DateTime.UtcNow.AddDays(-60))
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
        var allUsers = await _context.Users.ToListAsync();
        model.MonthlyUsers = Enumerable.Range(0, 6)
            .Select(i => DateTime.UtcNow.AddMonths(-5 + i))
            .Select(m => new ChartPoint
            {
                Label = $"{arabicMonths[m.Month - 1]} {m.Year}",
                Count = allUsers.Count(u => u.CreatedAt.Year == m.Year && u.CreatedAt.Month == m.Month)
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
        // تنظيف تلقائي: حذف سجلات البحث الأقدم من 90 يومًا عند كل فتح للوحة.
        var logCutoff = DateTime.UtcNow.AddDays(-90);
        var oldLogs = await _context.SearchLogs.Where(s => s.CreatedAt < logCutoff).ToListAsync();
        if (oldLogs.Count > 0)
        {
            _context.SearchLogs.RemoveRange(oldLogs);
            await _context.SaveChangesAsync();
        }
        ViewBag.SearchLogsCleaned = oldLogs.Count;

        var searchSince = DateTime.UtcNow.AddDays(-30);
        model.TopSearchTerms = await _context.SearchLogs
            .Where(s => s.CreatedAt >= searchSince)
            .GroupBy(s => s.Term)
            .Select(g => new ChartPoint { Label = g.Key, Count = g.Count() })
            .OrderByDescending(pt => pt.Count)
            .Take(10).ToListAsync();
        var allRequests = await _context.ContactRequests.Include(r => r.Listing).ToListAsync();
        model.TopContactedListings = allRequests
            .GroupBy(r => r.ListingId)
            .Select(g => new ChartPoint
            {
                Label = $"{g.First().Listing?.Title ?? g.Key.ToString()} ({g.First().Listing?.AdNumber})",
                Count = g.Count()
            })
            .OrderByDescending(pt => pt.Count)
            .Take(5).ToList();
        model.TopPosters = allListings
            .GroupBy(l => l.OwnerId)
            .Select(g => new ChartPoint
            {
                Label = users.FirstOrDefault(u => u.Id == g.Key)?.FullName ?? "؟",
                Count = g.Count()
            })
            .OrderByDescending(pt => pt.Count)
            .Take(5).ToList();
        var reportCounts = await _context.ListingReports
            .GroupBy(r => r.ListingId)
            .Select(g => new { ListingId = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(5).ToListAsync();
        var reportedListings = await _context.Listings
            .Where(l => reportCounts.Select(c => c.ListingId).Contains(l.Id))
            .ToDictionaryAsync(l => l.Id);
        model.TopReported = reportCounts.Select(c => new ChartPoint
        {
            Label = reportedListings.TryGetValue(c.ListingId, out var reported)
                ? $"{reported.AdNumber} — {reported.Title}"
                : c.ListingId.ToString(),
            Count = c.Count
        }).ToList();
        model.AreaPrices = allListings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.AreaSqm > 0)
            .GroupBy(l => l.Area)
            .Select(g => new AreaPricePoint
            {
                Area = g.Key,
                AvgPricePerSqm = g.Average(l => l.Price / l.AreaSqm),
                ListingsCount = g.Count()
            })
            .OrderByDescending(p => p.AvgPricePerSqm)
            .ToList();
        model.ListingsByPropertyType = allListings
            .GroupBy(l => l.PropertyType)
            .Select(g => new ChartPoint { Label = _language.PropertyTypeText(g.Key), Count = g.Count() })
            .OrderByDescending(pt => pt.Count)
            .ToList();
        model.ListingsByListingType = allListings
            .GroupBy(l => l.ListingType)
            .Select(g => new ChartPoint { Label = _language.ListingTypeText(g.Key), Count = g.Count() })
            .OrderByDescending(pt => pt.Count)
            .ToList();
        model.SaleListings = allListings.Count(l => l.ListingType == ListingType.Sale);
        model.RentListings = allListings.Count(l => l.ListingType == ListingType.Rent);
        ViewBag.LatestListings = allListings.OrderByDescending(l => l.CreatedAt).Take(5).ToList();
        ViewBag.LatestUsers = users.OrderByDescending(u => u.CreatedAt).Take(5).ToList();
        ViewBag.LatestReports = await _context.ListingReports.Include(r => r.Listing)
            .OrderByDescending(r => r.CreatedAt).Take(5).ToListAsync();
        ViewBag.LatestRequests = await _context.ContactRequests.Include(r => r.Listing)
            .OrderByDescending(r => r.CreatedAt).Take(5).ToListAsync();
        ViewBag.AvailablePct = allListings.Count > 0
            ? (int)Math.Round((double)allListings.Count(l => l.IsAvailable) / allListings.Count * 100)
            : 0;
        model.ListingsByAdvertiserType = allListings
            .GroupBy(l => l.AdvertiserType)
            .Select(g => new ChartPoint { Label = _language.AdvertiserTypeText(g.Key), Count = g.Count() })
            .OrderByDescending(pt => pt.Count)
            .ToList();
        model.UsersByStatus = users
            .GroupBy(u => u.AccountStatus)
            .Select(g => new ChartPoint
            {
                Label = g.Key switch
                {
                    AccountStatus.Pending => "معلّق",
                    AccountStatus.Active => "مفعّل",
                    AccountStatus.Suspended => "موقوف",
                    AccountStatus.Blocked => "محظور",
                    _ => g.Key.ToString()
                },
                Count = g.Count()
            })
            .OrderByDescending(pt => pt.Count)
            .ToList();
        return View(model);
    }

    /// <summary>إعادة تعيين كلمة مرور مستخدم — كلمة مؤقتة تظهر للأدمن مرة واحدة ليبلغها للمستخدم.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "لا يمكنك إعادة تعيين كلمة مرورك من هنا.";
            return RedirectToAction(nameof(Users));
        }
        var temp = "Temp" + System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000);
        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, temp);
        if (!result.Succeeded)
        {
            TempData["Error"] = "تعذّرت إعادة تعيين كلمة المرور.";
            return RedirectToAction(nameof(Users));
        }
        TempData["Info"] = $"تم تعيين كلمة مرور مؤقتة لـ {user.FullName}: {temp} — أبلغه بها واطلب منه تغييرها بعد الدخول.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> ImportListings()
    {
        ViewBag.LastImport = await _context.ListingAudits
            .Where(a => a.Action == "استيراد")
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTime?)a.CreatedAt).FirstOrDefaultAsync();
        return View();
    }

    /// <summary>قالب CSV للاستيراد — بترميز UTF-8 مع BOM عشان Excel يعرض العربي صح.</summary>
    [HttpGet]
    public IActionResult ImportTemplate()
    {
        var header = "Title,ListingType,PropertyType,Area,Price,AreaSqm,Rooms,Bathrooms,Floor,Furnishing,Finishing,Description,Features,AdvertiserType,OwnerPhone";
        var example = "شقة للبيع في مدينة نصر,Sale,Apartment,مدينة نصر، القاهرة,2500000,120,3,2,4,yes,SuperLux,شقة مشطبة سوبر لوكس قريبة من المواصلات,مطبخ مجهز;أرضيات بورسلين;جبس بورد,Owner,01xxxxxxxxx";
        var csv = "\uFEFF" + header + "\r\n" + example + "\r\n";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "aqarak-import-template.csv");
    }

    /// <summary>
    /// استيراد إعلانات دفعة واحدة من ملف CSV (يحفظه Excel بصيغة CSV).
    /// كل صف إعلان مستقل: الصف الغلط بيتخطى مع سبب، والصح بيتنشر فورًا
    /// (الاستيراد من الأدمن = موافقة فورية زي الإنشاء اليدوي)، بلا صور —
    /// الصور تتضاف بعدها من التعديل. صاحب الإعلان لازم يكون مسجَّلًا.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> ImportListings(Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            ViewBag.Errors = new List<string> { "اختار ملف CSV أولًا." };
            ViewBag.Imported = 0;
            return View();
        }
        var lines = await ReadImportLinesAsync(file);
        var (valid, errors) = await ParseImportLinesAsync(lines);
        if (valid == null)
        {
            ViewBag.Errors = errors;
            ViewBag.Imported = 0;
            return View();
        }
        var count = await InsertImportedListingsAsync(valid);
        ViewBag.Imported = count;
        ViewBag.Errors = errors;
        if (count > 0)
        {
            TempData["Info"] = $"تم استيراد {count} إعلانًا ونشرهم فورًا.";
        }
        return View();
    }

    /// <summary>معاينة صفوف الاستيراد قبل التأكيد — بيحفظ الملف مؤقتًا وبيعرض الصفوف الصح والغلط.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> PreviewImport(Microsoft.AspNetCore.Http.IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            ViewBag.Errors = new List<string> { "اختار ملف CSV أولًا." };
            ViewBag.Imported = 0;
            return View("ImportListings");
        }
        var lines = await ReadImportLinesAsync(file);
        var (valid, errors) = await ParseImportLinesAsync(lines);
        if (valid == null)
        {
            ViewBag.Errors = errors;
            ViewBag.Imported = 0;
            return View("ImportListings");
        }
        var token = Guid.NewGuid();
        var tempPath = Path.Combine(Path.GetTempPath(), "aqarak-import-" + token + ".csv");
        using (var stream = new FileStream(tempPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        var rows = new List<ImportPreviewRow>();
        var rowNumber = 1;
        foreach (var line in lines.Skip(1))
        {
            rowNumber++;
            var cells = ParseCsvLine(line);
            var title = cells.Count > 0 ? cells[0] : "-";
            var rowError = errors.FirstOrDefault(e => e.StartsWith($"الصف {rowNumber}:"));
            rows.Add(new ImportPreviewRow
            {
                RowNumber = rowNumber,
                Title = title,
                Error = rowError == null ? null : rowError[$"الصف {rowNumber}: ".Length..]
            });
        }
        ViewBag.Token = token.ToString();
        ViewBag.ValidCount = valid.Count;
        ViewBag.ErrorCount = errors.Count;
        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmImport(string token)
    {
        if (!Guid.TryParse(token, out var guid))
        {
            TempData["Error"] = "رمز الاستيراد غير صالح.";
            return RedirectToAction(nameof(ImportListings));
        }
        var tempPath = Path.Combine(Path.GetTempPath(), "aqarak-import-" + guid + ".csv");
        if (!System.IO.File.Exists(tempPath))
        {
            TempData["Error"] = "ملف الاستيراد انتهت صلاحيته — ارفع الملف تاني.";
            return RedirectToAction(nameof(ImportListings));
        }
        var lines = new List<string>();
        using (var reader = new System.IO.StreamReader(tempPath, System.Text.Encoding.UTF8, true))
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line.TrimStart('\uFEFF'));
                }
            }
        }
        System.IO.File.Delete(tempPath);
        var (valid, errors) = await ParseImportLinesAsync(lines);
        if (valid == null)
        {
            ViewBag.Errors = errors;
            ViewBag.Imported = 0;
            return View("ImportListings");
        }
        var count = await InsertImportedListingsAsync(valid);
        if (count > 0)
        {
            TempData["Info"] = $"تم استيراد {count} إعلانًا ونشرهم فورًا.";
        }
        return RedirectToAction(nameof(ImportListings));
    }

    private static async Task<List<string>> ReadImportLinesAsync(Microsoft.AspNetCore.Http.IFormFile file)
    {
        var lines = new List<string>();
        using (var reader = new System.IO.StreamReader(file.OpenReadStream(), System.Text.Encoding.UTF8, true))
        {
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line.TrimStart('\uFEFF'));
                }
            }
        }
        return lines;
    }

    private async Task<(List<Listing>? Valid, List<string> Errors)> ParseImportLinesAsync(List<string> lines)
    {
        var errors = new List<string>();
        var imported = new List<Listing>();
        if (lines.Count < 2)
        {
            errors.Add("الملف فاضي أو فيه الهيدر بس — ضيف صف إعلان واحد على الأقل.");
            return (null, errors);
        }
        var expectedHeader = new[] { "Title", "ListingType", "PropertyType", "Area", "Price", "AreaSqm", "Rooms", "Bathrooms", "Floor", "Furnishing", "Finishing", "Description", "Features", "AdvertiserType", "OwnerPhone" };
        var headerCells = ParseCsvLine(lines[0]);
        var headerOk = headerCells.Count == expectedHeader.Length
            && !expectedHeader.Where((h, i) => !string.Equals(h, headerCells[i].Trim(), StringComparison.OrdinalIgnoreCase)).Any();
        if (!headerOk)
        {
            errors.Add("هيدر الملف غير مطابق للقالب — حمّل القالب من نفس الصفحة واملأه.");
            return (null, errors);
        }
        if (lines.Count - 1 > 200)
        {
            errors.Add("الحد الأقصى 200 إعلان في الملف الواحد.");
            return (null, errors);
        }
        var rowNumber = 1;
        foreach (var line in lines.Skip(1))
        {
            rowNumber++;
            var cells = ParseCsvLine(line);
            if (cells.Count != expectedHeader.Length)
            {
                errors.Add($"الصف {rowNumber}: عدد الأعمدة غلط (متوقع {expectedHeader.Length}).");
                continue;
            }
            var (listing, error) = await BuildImportedListingAsync(cells);
            if (error != null)
            {
                errors.Add($"الصف {rowNumber}: {error}");
                continue;
            }
            imported.Add(listing!);
        }
        return (imported, errors);
    }

    private async Task<int> InsertImportedListingsAsync(List<Listing> imported)
    {
        foreach (var listing in imported)
        {
            listing.CreatedAt = DateTime.UtcNow;
            listing.IsAvailable = true;
            listing.Approval = ListingApproval.Approved;
            listing.AdNumber = "TEMP";
            _context.Listings.Add(listing);
        }
        await _context.SaveChangesAsync();
        foreach (var listing in imported)
        {
            listing.AdNumber = $"AQ-{listing.Id:D4}";
            await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId, "استيراد", "تم استيراد الإعلان من ملف CSV.");
            await SavedSearchAlerts.NotifyMatchingAsync(_context, listing, _hub);
        }
        await _context.SaveChangesAsync();
        return imported.Count;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        cells.Add(current.ToString());
        return cells;
    }

    private async Task<(Listing? listing, string? error)> BuildImportedListingAsync(List<string> c)
    {
        string Cell(int i) => c[i].Trim();
        var title = Cell(0);
        if (string.IsNullOrWhiteSpace(title))
        {
            return (null, "العنوان مطلوب.");
        }
        if (title.Length > 200)
        {
            return (null, "العنوان أطول من 200 حرف.");
        }
        if (!TryParseListingType(Cell(1), out var listingType))
        {
            return (null, "نوع الإعلان لازم يكون Sale (بيع) أو Rent (إيجار).");
        }
        if (!TryParsePropertyType(Cell(2), out var propertyType))
        {
            return (null, "نوع العقار لازم يكون Apartment أو House أو Building أو Shop أو Villa.");
        }
        var area = Cell(3);
        if (!ListingAreas.All.Contains(area))
        {
            return (null, "المنطقة مش من قائمة المناطق المعتمدة.");
        }
        if (!decimal.TryParse(Cell(4), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var price) || price <= 0)
        {
            return (null, "السعر لازم يكون رقمًا أكبر من صفر.");
        }
        if (!int.TryParse(Cell(5), out var areaSqm) || areaSqm <= 0)
        {
            return (null, "المساحة لازم تكون رقمًا أكبر من صفر.");
        }
        if (!int.TryParse(Cell(6), out var rooms) || rooms < 0)
        {
            return (null, "عدد الغرف لازم يكون رقمًا.");
        }
        if (!int.TryParse(Cell(7), out var bathrooms) || bathrooms < 0)
        {
            return (null, "عدد الحمامات لازم يكون رقمًا.");
        }
        int? floor = null;
        if (propertyType == PropertyType.Apartment)
        {
            if (!int.TryParse(Cell(8), out var floorValue))
            {
                return (null, "الدور مطلوب للشقة.");
            }
            floor = floorValue;
        }
        if (!TryParseFurnishing(Cell(9), out var furnishing))
        {
            return (null, "مستوى الفرش لازم يكون Full أو Partial أو None (أو كامل/جزئي/غير مفروش).");
        }
        FinishingLevel? finishing = null;
        if (!string.IsNullOrWhiteSpace(Cell(10)))
        {
            if (!TryParseFinishing(Cell(10), out var finishingValue))
            {
                return (null, "التشطيب لازم يكون SuperLux أو Lux أو SemiFinished أو RedBrick أو فاضي.");
            }
            finishing = finishingValue;
        }
        var description = Cell(11);
        if (description.Length > 2000)
        {
            return (null, "الوصف أطول من 2000 حرف.");
        }
        var features = Cell(12);
        if (features.Length > 2000)
        {
            return (null, "المميزات أطول من 2000 حرف.");
        }
        if (!TryParseAdvertiserType(Cell(13), out var advertiserType))
        {
            return (null, "نوع المعلن لازم يكون Owner (مالك) أو Broker (وسيط).");
        }
        var ownerPhone = PhoneHelper.Normalize(Cell(14));
        if (!PhoneHelper.IsValidEgyptianMobile(ownerPhone))
        {
            return (null, "رقم موبايل صاحب الإعلان لازم يكون 11 رقمًا ويبدأ بـ 01.");
        }
        var owner = await _context.Users.FirstOrDefaultAsync(u => u.UserName == ownerPhone);
        if (owner == null)
        {
            return (null, "صاحب الإعلان مش مسجَّل — رقم الموبايل ده مالوش حساب.");
        }
        var listing = new Listing
        {
            Title = title,
            ListingType = listingType,
            PropertyType = propertyType,
            Area = area,
            Price = price,
            OriginalPrice = price,
            AreaSqm = areaSqm,
            Rooms = rooms,
            Bathrooms = bathrooms,
            Floor = floor,
            Furnishing = furnishing,
            Finishing = finishing,
            Description = description,
            Features = string.Join("\n", features.Split(new[] { ';', '،', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            AdvertiserType = advertiserType,
            OwnerId = owner.Id
        };
        return (listing, null);
    }

    private static bool TryParseListingType(string value, out ListingType result)
    {
        result = default;
        if (Enum.TryParse<ListingType>(value, true, out result))
        {
            return true;
        }
        if (value == "بيع") { result = ListingType.Sale; return true; }
        if (value == "إيجار") { result = ListingType.Rent; return true; }
        return false;
    }

    private static bool TryParsePropertyType(string value, out PropertyType result)
    {
        result = default;
        if (Enum.TryParse<PropertyType>(value, true, out result))
        {
            return true;
        }
        result = value switch
        {
            "شقة" => PropertyType.Apartment,
            "بيت" => PropertyType.House,
            "عمارة" => PropertyType.Building,
            "محل" => PropertyType.Shop,
            "فيلا" => PropertyType.Villa,
            _ => PropertyType.Apartment
        };
        return value is "شقة" or "بيت" or "عمارة" or "محل" or "فيلا";
    }

    private static bool TryParseFurnishing(string value, out FurnishingLevel result)
    {
        result = default;
        if (Enum.TryParse<FurnishingLevel>(value, true, out result))
        {
            return true;
        }
        // توافق مع القالب القديم: yes = كامل، no = غير مفروش.
        if (value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value == "1" || value == "نعم" || value == "كامل")
        {
            result = FurnishingLevel.Full;
            return true;
        }
        if (value.Equals("no", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value == "0" || value == "لا" || value == string.Empty || value == "غير مفروش")
        {
            result = FurnishingLevel.None;
            return true;
        }
        if (value == "جزئي")
        {
            result = FurnishingLevel.Partial;
            return true;
        }
        return false;
    }

    private static bool TryParseFinishing(string value, out FinishingLevel result)
    {
        result = default;
        if (Enum.TryParse<FinishingLevel>(value, true, out result))
        {
            return true;
        }
        result = value switch
        {
            "سوبر لوكس" => FinishingLevel.SuperLux,
            "لوكس" => FinishingLevel.Lux,
            "نصف تشطيب" => FinishingLevel.SemiFinished,
            "طوب أحمر" => FinishingLevel.RedBrick,
            _ => FinishingLevel.Lux
        };
        return value is "سوبر لوكس" or "لوكس" or "نصف تشطيب" or "طوب أحمر";
    }

    private static bool TryParseAdvertiserType(string value, out AdvertiserType result)
    {
        result = default;
        if (Enum.TryParse<AdvertiserType>(value, true, out result))
        {
            return true;
        }
        if (value == "مالك") { result = AdvertiserType.Owner; return true; }
        if (value == "وسيط") { result = AdvertiserType.Broker; return true; }
        return false;
    }

    [HttpGet]
    public async Task<IActionResult> BlockedPhones()
    {
        var phones = await _context.BlockedPhones.OrderByDescending(b => b.CreatedAt).ToListAsync();
        return View(phones);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBlockedPhone(string phoneNumber, string? reason = null)
    {
        var phone = PhoneHelper.Normalize(phoneNumber ?? string.Empty);
        if (!PhoneHelper.IsValidEgyptianMobile(phone))
        {
            TempData["Error"] = "رقم الموبايل لازم يكون 11 رقمًا ويبدأ بـ 01.";
            return RedirectToAction(nameof(BlockedPhones));
        }
        if (await _context.BlockedPhones.AnyAsync(b => b.PhoneNumber == phone))
        {
            TempData["Info"] = "الرقم محظور بالفعل.";
            return RedirectToAction(nameof(BlockedPhones));
        }
        _context.BlockedPhones.Add(new BlockedPhone
        {
            PhoneNumber = phone,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
        });
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم حظر الرقم.";
        return RedirectToAction(nameof(BlockedPhones));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBlockedPhone(int id)
    {
        var blocked = await _context.BlockedPhones.FindAsync(id);
        if (blocked != null)
        {
            _context.BlockedPhones.Remove(blocked);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(BlockedPhones));
    }

    [HttpGet]
    /// <summary>ترقية مستخدم لدور أدمن — مع تنبيه له.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PromoteToAdmin(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            TempData["Info"] = "هذا المستخدم أدمن بالفعل.";
            return RedirectToAction(nameof(Users));
        }
        await _userManager.AddToRoleAsync(user, "Admin");
        _context.Notifications.Add(new Notification
        {
            Title = "تمت ترقيتك لدور أدمن",
            Message = "إدارة الموقع منحتك دور أدمن.",
            TargetUserId = user.Id,
            Url = "/Admin/Dashboard",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await NotifyBellAsync();
        TempData["Info"] = $"تمت ترقية {user.FullName} لدور أدمن.";
        return RedirectToAction(nameof(Users));
    }

    /// <summary>سحب دور الأدمن من مستخدم — لا لنفسك ولا لآخر أدمن.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DemoteFromAdmin(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        if (user.Id == CurrentUserId)
        {
            TempData["Error"] = "لا يمكنك سحب دور الأدمن من نفسك.";
            return RedirectToAction(nameof(Users));
        }
        if (!await _userManager.IsInRoleAsync(user, "Admin"))
        {
            TempData["Info"] = "هذا المستخدم ليس أدمن.";
            return RedirectToAction(nameof(Users));
        }
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        if (admins.Count <= 1)
        {
            TempData["Error"] = "لا يمكن سحب دور الأدمن من آخر أدمن في الموقع.";
            return RedirectToAction(nameof(Users));
        }
        await _userManager.RemoveFromRoleAsync(user, "Admin");
        _context.Notifications.Add(new Notification
        {
            Title = "تم سحب دور الأدمن من حسابك",
            Message = "إدارة الموقع سحبت دور الأدمن من حسابك.",
            TargetUserId = user.Id,
            Url = "/Account/MyAccount",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await NotifyBellAsync();
        TempData["Info"] = $"تم سحب دور الأدمن من {user.FullName}.";
        return RedirectToAction(nameof(Users));
    }

    /// <summary>تشغيل/إيقاف وضع الصيانة + رسالته — من لوحة التحكم.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Maintenance(bool enabled, string? message = null)
    {
        await SiteSettingsHelper.SetAsync(_context, SiteSettingsHelper.MaintenanceEnabledKey,
            enabled ? "true" : "false");
        await SiteSettingsHelper.SetAsync(_context, SiteSettingsHelper.MaintenanceMessageKey,
            string.IsNullOrWhiteSpace(message) ? SiteSettingsHelper.DefaultMaintenanceMessage : message.Trim());
        TempData["Info"] = enabled ? "تم تفعيل وضع الصيانة." : "تم إيقاف وضع الصيانة.";
        return RedirectToAction(nameof(Dashboard));
    }

    public async Task<IActionResult> Users(string? search = null, string? status = null)
    {
        var usersQuery = _userManager.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            usersQuery = usersQuery.Where(u =>
                (u.FullName != null && u.FullName.Contains(term)) ||
                (u.PhoneNumber != null && u.PhoneNumber.Contains(term)));
        }
        if (Enum.TryParse<AccountStatus>(status, out var statusValue))
        {
            usersQuery = usersQuery.Where(u => u.AccountStatus == statusValue);
        }
        var users = await usersQuery.OrderBy(u => u.FullName).ToListAsync();
        ViewBag.Search = search;
        ViewBag.Status = status;
        var rows = new List<AdminUserRow>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            rows.Add(new AdminUserRow
            {
                User = user,
                Role = roles.FirstOrDefault() ?? "User",
                IsAdmin = roles.Contains("Admin"),
                ListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == user.Id),
                ApprovedListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == user.Id && l.Approval == ListingApproval.Approved),
                PendingListingsCount = await _context.Listings.CountAsync(l => l.OwnerId == user.Id && l.Approval == ListingApproval.Pending),
                LastActivityAt = await _context.Listings.Where(l => l.OwnerId == user.Id).MaxAsync(l => (DateTime?)l.CreatedAt)
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
        var wasPending = user.AccountStatus == AccountStatus.Pending;
        user.AccountStatus = status;
        await _userManager.UpdateAsync(user);
        if (wasPending && status == AccountStatus.Active)
        {
            _context.Notifications.Add(new Notification
            {
                Title = "تم تفعيل حسابك",
                Message = "حسابك اتفعّل، تقدر دلوقتي تضيف إعلاناتك وتستخدم كل مزايا الموقع.",
                TargetUserId = user.Id,
                Url = "/Listings/Create",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await _context.SaveChangesAsync();
            await NotifyBellAsync();
        }
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
    public async Task<IActionResult> Requests(string? search = null, string? status = null, int? listingId = null)
    {
        var requestsQuery = _context.ContactRequests
            .Include(r => r.Listing)
            .Include(r => r.Buyer)
            .AsQueryable();
        if (listingId.HasValue)
        {
            requestsQuery = requestsQuery.Where(r => r.ListingId == listingId.Value);
        }
        if (status == "replied")
        {
            requestsQuery = requestsQuery.Where(r => r.IsReplied);
        }
        else if (status == "unreplied")
        {
            requestsQuery = requestsQuery.Where(r => !r.IsReplied);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            requestsQuery = requestsQuery.Where(r =>
                (r.Listing != null && (r.Listing.Title.Contains(term) || r.Listing.AdNumber.Contains(term)))
                || r.BuyerName.Contains(term)
                || (r.BuyerPhone != null && r.BuyerPhone.Contains(term))
                || (r.Buyer != null && r.Buyer.FullName != null && r.Buyer.FullName.Contains(term)));
        }
        var requests = await requestsQuery
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        ViewBag.Search = search;
        ViewBag.Status = status;
        ViewBag.ListingId = listingId;
        ViewBag.RequestListings = await _context.Listings
            .Where(l => _context.ContactRequests.Any(r => r.ListingId == l.Id))
            .OrderByDescending(l => l.CreatedAt).Select(l => new { l.Id, l.AdNumber, l.Title }).ToListAsync();
        return View(requests);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkReplied(int id)
    {
        var request = await _context.ContactRequests
            .Include(r => r.Listing).FirstOrDefaultAsync(r => r.Id == id);
        if (request == null)
        {
            return NotFound();
        }
        request.IsReplied = true;
        request.RepliedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        if (!string.IsNullOrEmpty(request.BuyerId))
        {
            _context.Notifications.Add(new Notification
            {
                Title = "تم الرد على طلب تواصلك",
                Message = $"إدارة الموقع ردّت على طلب تواصلك بخصوص «{request.Listing?.Title ?? request.ListingId.ToString()}».",
                TargetUserId = request.BuyerId,
                Url = $"/Listings/Details/{request.ListingId}",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await _context.SaveChangesAsync();
            await NotifyBellAsync();
        }
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
        listing.RejectionReason = null;
        await _context.SaveChangesAsync();
        _context.Notifications.Add(new Notification
        {
            Title = "تمت الموافقة على إعلانك",
            Message = $"إعلانك «{listing.Title}» اتنشر وبقى ظاهر للكل.",
            TargetUserId = listing.OwnerId,
            Url = $"/Listings/Details/{listing.Id}",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId, "موافقة", "تمت الموافقة على الإعلان.");
        await SavedSearchAlerts.NotifyMatchingAsync(_context, listing, _hub);
        await PropertyRequestAlerts.NotifyMatchingAsync(_context, listing, _hub);
        try
        {
            await _facebook.PostListingAsync(listing, $"{Request.Scheme}://{Request.Host}");
        }
        catch
        {
            // النشر على فيسبوك اختياري — فشله لا يكسر الموافقة.
        }
        await NotifyAdminsAsync();
        await NotifyBellAsync();
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction(nameof(Listings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? reason = null)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (cleanReason != null && cleanReason.Length > 500)
        {
            cleanReason = cleanReason[..500];
        }
        listing.Approval = ListingApproval.Rejected;
        listing.RejectionReason = cleanReason;
        await _context.SaveChangesAsync();
        _context.Notifications.Add(new Notification
        {
            Title = "تم رفض إعلانك",
            Message = $"إعلانك «{listing.Title}» اترفض{(cleanReason != null ? $" — السبب: {cleanReason}" : "")}. راجع بياناته وعدّله وحاول تاني.",
            TargetUserId = listing.OwnerId,
            Url = "/Listings/MyListings",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await AuditLogHelper.AddAsync(_context, listing.Id, CurrentUserId, "رفض", cleanReason != null ? $"تم رفض الإعلان. السبب: {cleanReason}" : "تم رفض الإعلان.");
        await NotifyAdminsAsync();
        await NotifyBellAsync();
        await _hub.Clients.All.SendAsync("listingsChanged");
        return RedirectToAction(nameof(Listings));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetFeatured(int id, int? days = null)
    {
        var listing = await _context.Listings.FindAsync(id);
        if (listing == null)
        {
            return NotFound();
        }
        if (days.HasValue)
        {
            if (!listing.IsAvailable)
            {
                TempData["Error"] = "الإعلان غير متاح — شغّله الأول قبل التمييز.";
                return RedirectToAction(nameof(Listings));
            }
            listing.IsFeatured = true;
            listing.FeaturedUntil = DateTime.UtcNow.AddDays(days.Value);
            TempData["Info"] = $"تم تمييز الإعلان لمدة {days.Value} أيام.";
        }
        else
        {
            listing.IsFeatured = false;
            listing.FeaturedUntil = null;
            TempData["Info"] = "تم إلغاء تمييز الإعلان.";
        }
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Listings));
    }

    [HttpGet]
    public async Task<IActionResult> Listings(string? search = null, string? approval = null, string? propertyType = null)
    {
        var query = _context.Listings.Include(l => l.Owner).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(l => l.Title.Contains(term) || l.AdNumber.Contains(term));
        }
        if (Enum.TryParse<ListingApproval>(approval, out var approvalValue))
        {
            query = query.Where(l => l.Approval == approvalValue);
        }
        if (Enum.TryParse<PropertyType>(propertyType, out var propertyTypeValue))
        {
            query = query.Where(l => l.PropertyType == propertyTypeValue);
        }
        var listings = await query.OrderByDescending(l => l.CreatedAt).ToListAsync();
        ViewBag.Search = search;
        ViewBag.Approval = approval;
        ViewBag.PropertyType = propertyType;
        return View(listings);
    }

    [HttpGet]
    public async Task<IActionResult> Reports(string? status = null)
    {
        var query = _context.ListingReports
            .Include(r => r.Listing).Include(r => r.Reporter)
            .AsQueryable();
        if (status == "reviewed")
        {
            query = query.Where(r => r.Status == ReportStatus.Reviewed);
        }
        else if (status == "pending")
        {
            query = query.Where(r => r.Status == ReportStatus.Pending);
        }
        var reports = await query
            .OrderBy(r => r.Status).ThenByDescending(r => r.CreatedAt)
            .ToListAsync();
        ViewBag.Status = status;
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
        var reportedListing = await _context.Listings.FindAsync(report.ListingId);
        _context.Notifications.Add(new Notification
        {
            Title = "تمت مراجعة بلاغك",
            Message = $"إدارة الموقع راجعت بلاغك على «{reportedListing?.Title ?? report.ListingId.ToString()}».",
            TargetUserId = report.ReporterId,
            Url = $"/Listings/Details/{report.ListingId}",
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await NotifyBellAsync();
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

    /// <summary>تصدير كل الإعلانات في ملف CSV (يفتح في Excel) — للأرشفة والمتابعة.</summary>
    [HttpGet]
    public async Task<IActionResult> AuditLog(int listingId)
    {
        var listing = await _context.Listings.FindAsync(listingId);
        if (listing == null) return NotFound();
        ViewBag.Listing = listing;
        var audits = await _context.ListingAudits
            .Where(a => a.ListingId == listingId)
            .OrderByDescending(a => a.CreatedAt).ToListAsync();
        return View(audits);
    }

    [HttpGet]
    public async Task<IActionResult> ContactMessages()
    {
        var messages = await _context.ContactMessages
            .OrderBy(m => m.IsRead).ThenByDescending(m => m.CreatedAt).ToListAsync();
        return View(messages);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkMessageRead(int id)
    {
        var message = await _context.ContactMessages.FindAsync(id);
        if (message == null)
        {
            return NotFound();
        }
        message.IsRead = true;
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(ContactMessages));
    }

    [HttpGet]
    public async Task<IActionResult> ExportListings()
    {
        var listings = await _context.Listings.Include(l => l.Owner)
            .OrderByDescending(l => l.CreatedAt).ToListAsync();
        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // BOM حتى يفتح Excel العربي صح.
        sb.AppendLine("رقم الإعلان,العنوان,النوع,نوع العقار,المنطقة,المساحة (م²),السعر,المعلن,الهاتف,متاح,الموافقة,المشاهدات,تاريخ النشر");
        string Cell(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        foreach (var listing in listings)
        {
            var propertyType = listing.PropertyType switch
            {
                PropertyType.Apartment => "شقة",
                PropertyType.House => "بيت",
                PropertyType.Building => "عمارة",
                PropertyType.Shop => "محل",
                _ => "فيلا"
            };
            var approval = listing.Approval switch
            {
                ListingApproval.Approved => "مقبول",
                ListingApproval.Rejected => "مرفوض",
                _ => "معلّق"
            };
            sb.AppendLine(string.Join(",", new[]
            {
                Cell(listing.AdNumber),
                Cell(listing.Title),
                Cell(listing.ListingType == ListingType.Sale ? "بيع" : "إيجار"),
                Cell(propertyType),
                Cell(listing.Area),
                listing.AreaSqm.ToString(),
                listing.Price.ToString("0.##"),
                Cell(listing.Owner?.FullName ?? listing.Owner?.UserName),
                Cell(listing.Owner?.PhoneNumber),
                Cell(listing.IsAvailable ? "متاح" : "غير متاح"),
                Cell(approval),
                listing.ViewCount.ToString(),
                listing.CreatedAt.ToString("yyyy-MM-dd")
            }));
        }
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"aqarak-listings-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    /// <summary>
    /// نسخة احتياطية: ملف ZIP فيه CSV للإعلانات والمستخدمين وطلبات التواصل
    /// ورسائل «تواصل معنا» — للأدمن فقط.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ExportBackup()
    {
        string Cell(string? value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        var memory = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var listings = await _context.Listings.Include(l => l.Owner)
                .OrderByDescending(l => l.CreatedAt).ToListAsync();
            var listingsCsv = new StringBuilder();
            listingsCsv.Append('\uFEFF');
            listingsCsv.AppendLine("رقم الإعلان,العنوان,النوع,نوع العقار,المنطقة,المساحة (م²),السعر,المعلن,الهاتف,متاح,الموافقة,المشاهدات,تاريخ النشر");
            foreach (var listing in listings)
            {
                listingsCsv.AppendLine(string.Join(",", new[]
                {
                    Cell(listing.AdNumber), Cell(listing.Title),
                    Cell(listing.ListingType == ListingType.Sale ? "بيع" : "إيجار"),
                    Cell(listing.PropertyType.ToString()), Cell(listing.Area),
                    listing.AreaSqm.ToString(), listing.Price.ToString("0.##"),
                    Cell(listing.Owner?.FullName ?? listing.Owner?.UserName),
                    Cell(listing.Owner?.PhoneNumber),
                    Cell(listing.IsAvailable ? "متاح" : "غير متاح"),
                    Cell(listing.Approval.ToString()), listing.ViewCount.ToString(),
                    listing.CreatedAt.ToString("yyyy-MM-dd")
                }));
            }
            var listingsEntry = zip.CreateEntry("Listings.csv");
            using (var writer = new StreamWriter(listingsEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(listingsCsv.ToString());
            }

            var users = await _context.Users.OrderBy(u => u.FullName).ToListAsync();
            var usersCsv = new StringBuilder();
            usersCsv.Append('\uFEFF');
            usersCsv.AppendLine("الاسم,الهاتف,الإيميل,الحالة");
            foreach (var user in users)
            {
                usersCsv.AppendLine(string.Join(",", new[]
                {
                    Cell(user.FullName), Cell(user.PhoneNumber), Cell(user.Email),
                    Cell(user.AccountStatus.ToString())
                }));
            }
            var usersEntry = zip.CreateEntry("Users.csv");
            using (var writer = new StreamWriter(usersEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(usersCsv.ToString());
            }

            var requests = await _context.ContactRequests.Include(r => r.Listing)
                .OrderByDescending(r => r.CreatedAt).ToListAsync();
            var requestsCsv = new StringBuilder();
            requestsCsv.Append('\uFEFF');
            requestsCsv.AppendLine("الإعلان,الاسم,الهاتف,النوع,تم الرد,التاريخ");
            foreach (var request in requests)
            {
                requestsCsv.AppendLine(string.Join(",", new[]
                {
                    Cell(request.Listing?.AdNumber), Cell(request.BuyerName), Cell(request.BuyerPhone),
                    Cell(request.Intent.ToString()), Cell(request.IsReplied ? "نعم" : "لا"),
                    request.CreatedAt.ToString("yyyy-MM-dd")
                }));
            }
            var requestsEntry = zip.CreateEntry("ContactRequests.csv");
            using (var writer = new StreamWriter(requestsEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(requestsCsv.ToString());
            }

            var messages = await _context.ContactMessages
                .OrderByDescending(m => m.CreatedAt).ToListAsync();
            var messagesCsv = new StringBuilder();
            messagesCsv.Append('\uFEFF');
            messagesCsv.AppendLine("الاسم,الهاتف,الرسالة,تمت القراءة,التاريخ");
            foreach (var message in messages)
            {
                messagesCsv.AppendLine(string.Join(",", new[]
                {
                    Cell(message.Name), Cell(message.Phone), Cell(message.Message),
                    Cell(message.IsRead ? "نعم" : "لا"), message.CreatedAt.ToString("yyyy-MM-dd")
                }));
            }
            var messagesEntry = zip.CreateEntry("ContactMessages.csv");
            using (var writer = new StreamWriter(messagesEntry.Open(), Encoding.UTF8))
            {
                await writer.WriteAsync(messagesCsv.ToString());
            }
        }
        return File(memory.ToArray(), "application/zip", $"aqarak-backup-{DateTime.UtcNow:yyyy-MM-dd}.zip");
    }

    /// <summary>
    /// إرسال تنبيه تجديد لأصحاب الإعلانات المقبولة المتاحة الأقدم من 60 يومًا —
    /// تنبيه واحد لكل إعلان كل 60 يومًا كحد أقصى (لا تكرار).
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendRenewalReminders()
    {
        var cutoff = DateTime.UtcNow.AddDays(-60);
        var listings = await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.IsAvailable && l.CreatedAt <= cutoff)
            .ToListAsync();
        var sent = 0;
        foreach (var listing in listings)
        {
            var url = $"/Listings/Details/{listing.Id}";
            var already = await _context.Notifications.AnyAsync(n =>
                n.TargetUserId == listing.OwnerId && n.Title == "جدّد إعلانك" && n.Url == url
                && n.CreatedAt >= DateTime.UtcNow.AddDays(-60));
            if (already)
            {
                continue;
            }
            _context.Notifications.Add(new Notification
            {
                Title = "جدّد إعلانك",
                Message = $"إعلانك «{listing.Title}» بقاله أكتر من 60 يوم — جدّده من «إعلاناتي» عشان يرجع أول النتائج.",
                TargetUserId = listing.OwnerId,
                Url = url,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            sent++;
        }
        await _context.SaveChangesAsync();
        if (sent > 0)
        {
            await NotifyBellAsync();
        }
        TempData["Info"] = sent > 0
            ? $"تم إرسال تنبيه التجديد لـ {sent} معلن."
            : "كل المعلنين دول اتبعت لهم تنبيه تجديد في آخر 60 يوم.";
        return RedirectToAction(nameof(Dashboard));
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
    // مهم: من غير [Authorize] عمدًا — الـ redirect لصفحة الدخول كان بيرجّع
    // صفحة كاملة للـ fetch فيتحقن نسخة تانية من الموقع كله جوه #adminSidePanel.
    // لغير الأدمن نرجّع 401 فيتجاهلها الجافاسكريبت.
    [HttpGet]
    public IActionResult NavBadges(bool panelOnly = false)
    {
        if (!User.IsInRole("Admin"))
        {
            return Unauthorized();
        }
        return ViewComponent("AdminNav", new { panelOnly });
    }

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

    // ===== Batch 41 — التميّز: طلبات عقار، عروض، مجموعات، واتساب، فيسبوك. =====

    /// <summary>طلبات العقار (الأدمن): كل الطلبات مع بيانات الطالب (للأدمن فقط).</summary>
    public async Task<IActionResult> PropertyRequests()
    {
        var requests = await _context.PropertyRequests.Include(r => r.User)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return View(requests);
    }

    /// <summary>مطابقة طلب: الإعلانات المعتمدة المتاحة المطابقة لمعايير الطلب.</summary>
    public async Task<IActionResult> MatchPropertyRequest(int id)
    {
        var request = await _context.PropertyRequests.Include(r => r.User).FirstOrDefaultAsync(r => r.Id == id);
        if (request == null)
        {
            return NotFound();
        }
        var listings = await _context.Listings.Include(l => l.Attachments)
            .Where(l => l.IsAvailable && l.Approval == ListingApproval.Approved)
            .Where(l => l.ListingType == request.ListingType)
            .Where(l => !request.PropertyType.HasValue || l.PropertyType == request.PropertyType.Value)
            .Where(l => string.IsNullOrWhiteSpace(request.Area) || l.Area.Contains(request.Area.Trim()))
            .Where(l => !request.MinPrice.HasValue || l.Price >= request.MinPrice.Value)
            .Where(l => !request.MaxPrice.HasValue || l.Price <= request.MaxPrice.Value)
            .Where(l => !request.MinAreaSqm.HasValue || l.AreaSqm >= request.MinAreaSqm.Value)
            .Where(l => !request.MinRooms.HasValue || (l.Rooms ?? 0) >= request.MinRooms.Value)
            .OrderByDescending(l => l.CreatedAt)
            .Take(20)
            .ToListAsync();
        ViewBag.Request = request;
        return View(listings);
    }

    /// <summary>إبلاغ طالب العقار بإعلان مطابق (من الأدمن).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NotifyPropertyRequestMatch(int requestId, int listingId)
    {
        var request = await _context.PropertyRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
        if (request == null || listing == null)
        {
            return NotFound();
        }
        var url = $"/Listings/Details/{listing.Id}";
        var exists = await _context.Notifications.AnyAsync(n => n.TargetUserId == request.UserId && n.Url == url);
        if (!exists)
        {
            _context.Notifications.Add(new Notification
            {
                Title = "إعلان مطابق لطلبك (من الإدارة)",
                Message = $"{listing.Title} — {listing.Area}، {listing.Price:N0} جنيه. طابق طلبك.",
                TargetUserId = request.UserId,
                Url = url,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await _context.SaveChangesAsync();
            await NotifyBellAsync();
        }
        await WhatsAppAlerts.QueueAsync(_context, request.UserId,
            $"إعلان مطابق لطلبك على عقارك أونلاين: {listing.Title} — {listing.Area}، {listing.Price:N0} جنيه.", url);
        await _context.SaveChangesAsync();
        request.Status = PropertyRequestStatus.Matched;
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم إبلاغ الطالب بالإعلان المطابق.";
        return RedirectToAction(nameof(MatchPropertyRequest), new { id = requestId });
    }

    /// <summary>كل عروض الأسعار (للأدمن): مع بيانات المشتري والمالك.</summary>
    public async Task<IActionResult> Offers()
    {
        var offers = await _context.ListingOffers.Include(o => o.Listing).Include(o => o.Buyer)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View(offers);
    }

    /// <summary>المجموعات المختارة: قائمة + إنشاء.</summary>
    public async Task<IActionResult> Collections()
    {
        var collections = await _context.Collections.Include(c => c.Items)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();
        return View(collections);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCollection(string title, string? description)
    {
        title = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title) || title.Length > 100)
        {
            TempData["Error"] = "عنوان المجموعة مطلوب (بحد أقصى 100 حرف).";
            return RedirectToAction(nameof(Collections));
        }
        _context.Collections.Add(new Collection
        {
            Title = title,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم إنشاء المجموعة.";
        return RedirectToAction(nameof(Collections));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleCollection(int id)
    {
        var collection = await _context.Collections.FindAsync(id);
        if (collection == null)
        {
            return NotFound();
        }
        collection.IsActive = !collection.IsActive;
        await _context.SaveChangesAsync();
        TempData["Info"] = collection.IsActive ? "تم تفعيل المجموعة." : "تم إخفاء المجموعة من الرئيسية.";
        return RedirectToAction(nameof(Collections));
    }

    /// <summary>إدارة مجموعة: إعلاناتها + إضافة إعلان برقم الإعلان.</summary>
    public async Task<IActionResult> ManageCollection(int id)
    {
        var collection = await _context.Collections.Include(c => c.Items).ThenInclude(i => i.Listing)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (collection == null)
        {
            return NotFound();
        }
        return View(collection);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCollection(int id, string adNumber)
    {
        var collection = await _context.Collections.FindAsync(id);
        if (collection == null)
        {
            return NotFound();
        }
        adNumber = (adNumber ?? string.Empty).Trim();
        var listing = await _context.Listings.FirstOrDefaultAsync(l => l.AdNumber == adNumber);
        if (listing == null)
        {
            TempData["Error"] = "لا يوجد إعلان بهذا الرقم.";
            return RedirectToAction(nameof(ManageCollection), new { id });
        }
        var exists = await _context.CollectionListings.AnyAsync(i => i.CollectionId == id && i.ListingId == listing.Id);
        if (exists)
        {
            TempData["Error"] = "الإعلان موجود في المجموعة بالفعل.";
            return RedirectToAction(nameof(ManageCollection), new { id });
        }
        _context.CollectionListings.Add(new CollectionListing
        {
            CollectionId = id,
            ListingId = listing.Id,
            SortOrder = await _context.CollectionListings.Where(i => i.CollectionId == id).CountAsync()
        });
        await _context.SaveChangesAsync();
        TempData["Info"] = $"تمت إضافة «{listing.Title}» إلى المجموعة.";
        return RedirectToAction(nameof(ManageCollection), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveFromCollection(int id, int itemId)
    {
        var item = await _context.CollectionListings.FirstOrDefaultAsync(i => i.Id == itemId && i.CollectionId == id);
        if (item == null)
        {
            return NotFound();
        }
        _context.CollectionListings.Remove(item);
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم حذف الإعلان من المجموعة.";
        return RedirectToAction(nameof(ManageCollection), new { id });
    }

    /// <summary>قائمة انتظار واتساب: رسائل بانتظار الإرسال + رابط wa.me جاهز لكل رسالة.</summary>
    public async Task<IActionResult> WhatsAppOutbox()
    {
        var items = await _context.WhatsAppOutbox.Include(w => w.User)
            .OrderByDescending(w => w.CreatedAt)
            .Take(100)
            .ToListAsync();
        return View(items);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkWhatsAppSent(int id)
    {
        var item = await _context.WhatsAppOutbox.FindAsync(id);
        if (item == null)
        {
            return NotFound();
        }
        item.Status = WhatsAppStatus.Sent;
        item.SentAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم تعليم الرسالة كمُرسلة.";
        return RedirectToAction(nameof(WhatsAppOutbox));
    }

    /// <summary>إعدادات النشر على فيسبوك: معرف الصفحة + توكن الصفحة + تفعيل النشر التلقائي.</summary>
    public async Task<IActionResult> Facebook()
    {
        var settings = await _context.SiteSettings
            .Where(s => s.Key.StartsWith("Facebook:"))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        ViewBag.PageId = settings.TryGetValue("Facebook:PageId", out var pageId) ? pageId : string.Empty;
        ViewBag.PageAccessToken = settings.TryGetValue("Facebook:PageAccessToken", out var token) ? token : string.Empty;
        ViewBag.AutoPost = settings.TryGetValue("Facebook:AutoPost", out var auto) && auto == "true";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Facebook(string pageId, string pageAccessToken, bool autoPost)
    {
        await SaveSiteSettingAsync("Facebook:PageId", (pageId ?? string.Empty).Trim());
        await SaveSiteSettingAsync("Facebook:PageAccessToken", (pageAccessToken ?? string.Empty).Trim());
        await SaveSiteSettingAsync("Facebook:AutoPost", autoPost ? "true" : "false");
        TempData["Info"] = "تم حفظ إعدادات فيسبوك.";
        return RedirectToAction(nameof(Facebook));
    }

    private async Task SaveSiteSettingAsync(string key, string value)
    {
        var setting = await _context.SiteSettings.FindAsync(key);
        if (setting == null)
        {
            _context.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// إعدادات الأمان (Batch 43): مفاتيح Cloudflare Turnstile (كابتشا التسجيل
    /// والدخول والتواصل). بدونها يبقى التحقق متوقفًا والموقع شغالًا عاديًا.
    /// </summary>
    public async Task<IActionResult> Security()
    {
        var settings = await _context.SiteSettings
            .Where(s => s.Key.StartsWith("Turnstile:"))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        ViewBag.SiteKey = settings.TryGetValue("Turnstile:SiteKey", out var siteKey) ? siteKey : string.Empty;
        ViewBag.SecretKey = settings.TryGetValue("Turnstile:SecretKey", out var secret) ? secret : string.Empty;
        ViewBag.Enabled = settings.TryGetValue("Turnstile:Enabled", out var enabled) && enabled == "true";
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Security(string siteKey, string secretKey, bool enabled)
    {
        await SaveSiteSettingAsync("Turnstile:SiteKey", (siteKey ?? string.Empty).Trim());
        await SaveSiteSettingAsync("Turnstile:SecretKey", (secretKey ?? string.Empty).Trim());
        await SaveSiteSettingAsync("Turnstile:Enabled", enabled ? "true" : "false");
        TempData["Info"] = "تم حفظ إعدادات الأمان.";
        return RedirectToAction(nameof(Security));
    }

    /// <summary>عناوين IP المحظورة + الموثوقة (Batch 43).</summary>
    [HttpGet]
    public async Task<IActionResult> BlockedIps()
    {
        var now = DateTime.UtcNow;
        var blocked = await _context.BlockedIps.OrderByDescending(b => b.BlockedAt).ToListAsync();
        ViewBag.Blocked = blocked;
        ViewBag.Trusted = await _context.TrustedIpAddresses.OrderByDescending(t => t.LastSeenAt).ToListAsync();
        ViewBag.MyIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        ViewBag.Now = now;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBlockedIp(string ipAddress, string? reason = null)
    {
        if (!System.Net.IPAddress.TryParse((ipAddress ?? string.Empty).Trim(), out var parsed))
        {
            TempData["Error"] = "عنوان IP غير صحيح.";
            return RedirectToAction(nameof(BlockedIps));
        }
        var ip = parsed.ToString();
        var now = DateTime.UtcNow;
        if (await _context.BlockedIps.AnyAsync(b => b.IpAddress == ip
            && (b.BlockedUntil == null || b.BlockedUntil > now)))
        {
            TempData["Info"] = "العنوان محظور بالفعل.";
            return RedirectToAction(nameof(BlockedIps));
        }
        _context.BlockedIps.Add(new BlockedIp
        {
            IpAddress = ip,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            IsAuto = false,
            BlockedAt = now,
            BlockedUntil = null
        });
        await _context.SaveChangesAsync();
        IpBlockHelper.InvalidateCache(HttpContext.RequestServices);
        TempData["Info"] = "تم حظر العنوان.";
        return RedirectToAction(nameof(BlockedIps));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveBlockedIp(int id)
    {
        var blocked = await _context.BlockedIps.FindAsync(id);
        if (blocked != null)
        {
            _context.BlockedIps.Remove(blocked);
            await _context.SaveChangesAsync();
            IpBlockHelper.InvalidateCache(HttpContext.RequestServices);
        }
        return RedirectToAction(nameof(BlockedIps));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveTrustedIp(int id)
    {
        var trusted = await _context.TrustedIpAddresses.FindAsync(id);
        if (trusted != null)
        {
            _context.TrustedIpAddresses.Remove(trusted);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(BlockedIps));
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
    public async Task<IActionResult> Index(string? filter = null)
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
        if (filter == "unread")
        {
            rows = rows.Where(r => !r.IsRead).ToList();
        }
        else if (filter == "read")
        {
            rows = rows.Where(r => r.IsRead).ToList();
        }
        ViewBag.Filter = filter;
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
        // طلب AJAX من الجرس: يرجع العدد الجديد بدل إعادة التوجيه لصفحة الإشعارات.
        if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            var activeIds = await _context.Notifications
                .Where(n => n.IsActive && (n.TargetUserId == null || n.TargetUserId == CurrentUserId))
                .Select(n => n.Id)
                .ToListAsync();
            var readIds = await _context.NotificationReads
                .Where(r => r.UserId == CurrentUserId)
                .Select(r => r.NotificationId)
                .ToListAsync();
            return Json(new { ok = true, unreadCount = activeIds.Count(x => !readIds.Contains(x)) });
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

    /// <summary>حذف كل تنبيهاتي: التنبيهات الموجَّهة لي تُحذف،
    /// والتنبيهات العامة تُعلَّم كمقروءة حتى لا تختفي من عند باقي المستخدمين.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteAll()
    {
        var mine = await _context.Notifications
            .Where(n => n.TargetUserId == CurrentUserId)
            .ToListAsync();
        _context.Notifications.RemoveRange(mine);
        var broadcastIds = await _context.Notifications
            .Where(n => n.IsActive && n.TargetUserId == null)
            .Select(n => n.Id)
            .ToListAsync();
        var readIds = await _context.NotificationReads
            .Where(r => r.UserId == CurrentUserId)
            .Select(r => r.NotificationId)
            .ToListAsync();
        foreach (var id in broadcastIds.Where(id => !readIds.Contains(id)))
        {
            _context.NotificationReads.Add(new NotificationRead
            {
                NotificationId = id,
                UserId = CurrentUserId,
                ReadAt = DateTime.UtcNow
            });
        }
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRead()
    {
        var readIds = await _context.NotificationReads
            .Where(r => r.UserId == CurrentUserId)
            .Select(r => r.NotificationId)
            .ToListAsync();
        var mine = await _context.Notifications
            .Where(n => n.TargetUserId == CurrentUserId && readIds.Contains(n.Id))
            .ToListAsync();
        _context.Notifications.RemoveRange(mine);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}

[Authorize]
/// <summary>
/// <summary>تجميع المميزات من الاختيارات الجاهزة + الميزات المخصصة المكتوبة، مفصولة بسطر لكل ميزة.</summary>
public static class ListingFeatureComposer
{
    public static string? ComposeFeatures(string[]? selectedFeatures, string[]? customFeatures)
    {
        var features = new List<string>();
        if (selectedFeatures != null)
        {
            features.AddRange(selectedFeatures.Where(f => ListingFeatures.All.Contains(f)));
        }
        if (customFeatures != null)
        {
            foreach (var custom in customFeatures)
            {
                if (string.IsNullOrWhiteSpace(custom)) { continue; }
                features.AddRange(custom
                    .Split(new[] { '\n', '\r', ',', '،' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(f => f.Trim())
                    .Where(f => f.Length > 0 && !ListingFeatures.All.Contains(f)));
            }
        }
        var joined = string.Join("\n", features.Distinct());
        if (string.IsNullOrEmpty(joined))
        {
            return null;
        }
        return joined.Length > 2000 ? joined[..2000] : joined;
    }
}

/// <summary>سجل تعديلات الإعلانات (للأدمن): يسجّل من قام بالإجراء واسمه وقتها.</summary>
public static class AuditLogHelper
{
    public static async Task AddAsync(ApplicationDbContext context, int listingId, string userId, string action, string? details)
    {
        var user = await context.Users.FindAsync(userId);
        var cleanDetails = string.IsNullOrWhiteSpace(details) ? null : details.Trim();
        if (cleanDetails != null && cleanDetails.Length > 1000)
        {
            cleanDetails = cleanDetails[..1000];
        }
        context.ListingAudits.Add(new ListingAudit
        {
            ListingId = listingId,
            UserId = userId,
            UserName = user?.FullName ?? user?.UserName ?? "مستخدم",
            Action = action,
            Details = cleanDetails,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }
}

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
        if (!string.IsNullOrWhiteSpace(s.Furnishing))
        {
            if (s.Furnishing == "Full" && l.Furnishing != FurnishingLevel.Full)
            {
                return false;
            }
            if (s.Furnishing == "Partial" && l.Furnishing != FurnishingLevel.Partial)
            {
                return false;
            }
            if (s.Furnishing == "Any" && l.Furnishing == FurnishingLevel.None)
            {
                return false;
            }
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
            await WhatsAppAlerts.QueueAsync(context, userId,
                $"إعلان جديد مطابق لبحثك على عقارك أونلاين: {listing.Title} — {listing.Area}، {listing.Price:N0} جنيه.", url);
        }
        if (created)
        {
            await context.SaveChangesAsync();
            await hub.Clients.All.SendAsync("refreshBell");
        }
    }
}

/// <summary>تنبيهات واتساب: قيد في قائمة الانتظار يرسله الأدمن (wa.me) لحين ربط WhatsApp Business API.</summary>
public static class WhatsAppAlerts
{
    public static async Task QueueAsync(ApplicationDbContext context, string userId, string message, string? url)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null || !user.ReceiveWhatsAppAlerts || string.IsNullOrWhiteSpace(user.PhoneNumber))
        {
            return;
        }
        var exists = await context.WhatsAppOutbox.AnyAsync(w =>
            w.UserId == userId && w.Status == WhatsAppStatus.Pending && w.Message == message);
        if (exists)
        {
            return;
        }
        context.WhatsAppOutbox.Add(new WhatsAppOutbox
        {
            UserId = userId,
            Phone = user.PhoneNumber,
            Message = message,
            Url = url,
            Status = WhatsAppStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
    }
}

/// <summary>مطابقة الإعلان المعتمد مع طلبات العقار المفتوحة وتنبيه أصحابها.</summary>
public static class PropertyRequestAlerts
{
    public static bool Matches(PropertyRequest r, Listing l)
    {
        if (r.ListingType != l.ListingType)
        {
            return false;
        }
        if (r.PropertyType.HasValue && r.PropertyType.Value != l.PropertyType)
        {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(r.Area) && !l.Area.Contains(r.Area.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (r.MinPrice.HasValue && l.Price < r.MinPrice.Value)
        {
            return false;
        }
        if (r.MaxPrice.HasValue && l.Price > r.MaxPrice.Value)
        {
            return false;
        }
        if (r.MinAreaSqm.HasValue && l.AreaSqm < r.MinAreaSqm.Value)
        {
            return false;
        }
        if (r.MinRooms.HasValue && (l.Rooms ?? 0) < r.MinRooms.Value)
        {
            return false;
        }
        return true;
    }

    public static async Task NotifyMatchingAsync(ApplicationDbContext context, Listing listing,
        Microsoft.AspNetCore.SignalR.IHubContext<Services.RealtimeHub> hub)
    {
        var url = $"/Listings/Details/{listing.Id}";
        var requests = await context.PropertyRequests
            .Where(r => r.Status == PropertyRequestStatus.Open).ToListAsync();
        var created = false;
        foreach (var request in requests.Where(r => Matches(r, listing)))
        {
            if (request.UserId == listing.OwnerId)
            {
                continue;
            }
            var exists = await context.Notifications.AnyAsync(n => n.TargetUserId == request.UserId && n.Url == url);
            if (exists)
            {
                continue;
            }
            context.Notifications.Add(new Notification
            {
                Title = "إعلان جديد مطابق لطلبك",
                Message = $"{listing.Title} — {listing.Area}، {listing.Price:N0} جنيه. طابق طلبك ({request.Area}).",
                TargetUserId = request.UserId,
                Url = url,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            created = true;
            await WhatsAppAlerts.QueueAsync(context, request.UserId,
                $"إعلان جديد مطابق لطلبك على عقارك أونلاين: {listing.Title} — {listing.Area}، {listing.Price:N0} جنيه.", url);
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
        int? minRooms, string? furnishing = null, string? returnUrl = null)
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
            Furnishing = string.IsNullOrWhiteSpace(furnishing) ? null : furnishing.Trim(),
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
            furnishing = saved.Furnishing
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
        && a.MinRooms == b.MinRooms && a.Furnishing == b.Furnishing;
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
    public async Task<IActionResult> Index(string? status = null)
    {
        // طلباتي أنا فقط — طلبات الآخرين لا تظهر لي.
        var query = _context.ViewingAppointments
            .Include(v => v.Listing)
            .Where(v => v.RequesterId == CurrentUserId);
        if (Enum.TryParse<ViewingStatus>(status, out var statusValue))
        {
            query = query.Where(v => v.Status == statusValue);
        }
        var mine = await query.OrderByDescending(v => v.CreatedAt).ToListAsync();
        ViewBag.Status = status;
        return View(mine);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int listingId, DateTime? requestedAt, string? message, string? duration = null, int? slotId = null)
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
        // Batch 41 (7) — الحجز من ميعاد متاح حدده صاحب الإعلان.
        ViewingSlot? slot = null;
        if (slotId.HasValue)
        {
            slot = await _context.ViewingSlots.FirstOrDefaultAsync(s => s.Id == slotId.Value && s.ListingId == listingId && !s.IsBooked);
            if (slot == null)
            {
                TempData["Error"] = "المياد ده مش متاح.";
                return RedirectToAction("Details", "Listings", new { id = listingId });
            }
            requestedAt = slot.SlotDate.Date + slot.StartTime;
        }
        if (!requestedAt.HasValue || requestedAt.Value <= DateTime.Now)
        {
            TempData["Error"] = "ميعاد المعاينة لازم يكون في المستقبل.";
            return RedirectToAction("Details", "Listings", new { id = listingId });
        }
        var cleanMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (!string.IsNullOrWhiteSpace(duration))
        {
            var durationNote = $"المدة المتوقعة: {duration.Trim()} دقيقة";
            cleanMessage = string.IsNullOrWhiteSpace(cleanMessage) ? durationNote : $"{cleanMessage} — {durationNote}";
        }
        if (cleanMessage != null && cleanMessage.Length > 500)
        {
            cleanMessage = cleanMessage[..500];
        }
        _context.ViewingAppointments.Add(new ViewingAppointment
        {
            ListingId = listingId,
            RequesterId = CurrentUserId,
            RequestedAt = requestedAt.Value,
            Status = ViewingStatus.Pending,
            Message = cleanMessage,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        if (slot != null)
        {
            slot.IsBooked = true;
            await _context.SaveChangesAsync();
        }
        var requester = await _context.Users.FindAsync(CurrentUserId);
        var requesterName = (requester?.FullName ?? requester?.UserName ?? "مستخدم").Split(' ')[0];
        await NotifyAdminAsync("طلب معاينة جديد",
            $"{requesterName} طلب معاينة «{listing.Title}» يوم {requestedAt:dd/MM/yyyy HH:mm}.");
        TempData["Info"] = "تم إرسال طلب المعاينة — إدارة الموقع هتنسّق معاك الميعاد.";
        return RedirectToAction("Details", "Listings", new { id = listingId });
    }

    /// <summary>أوقات المعاينة المتاحة لإعلان (Batch 41 — تميّز 7): صاحب الإعلان أو الأدمن.</summary>
    public async Task<IActionResult> Slots(int listingId)
    {
        var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        ViewBag.Listing = listing;
        var slots = await _context.ViewingSlots
            .Where(s => s.ListingId == listingId)
            .OrderBy(s => s.SlotDate).ThenBy(s => s.StartTime)
            .ToListAsync();
        return View(slots);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddSlot(int listingId, DateTime slotDate, TimeSpan startTime, TimeSpan endTime)
    {
        var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
        if (listing == null)
        {
            return NotFound();
        }
        if (listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        if (slotDate.Date < DateTime.Now.Date || endTime <= startTime)
        {
            TempData["Error"] = "تأكد من اليوم والوقت: اليوم لازم يكون اليوم أو بعده، ووقت النهاية بعد البداية.";
            return RedirectToAction(nameof(Slots), new { listingId });
        }
        _context.ViewingSlots.Add(new ViewingSlot
        {
            ListingId = listingId,
            SlotDate = slotDate.Date,
            StartTime = startTime,
            EndTime = endTime,
            IsBooked = false,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        TempData["Info"] = "تمت إضافة الميعاد المتاح.";
        return RedirectToAction(nameof(Slots), new { listingId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteSlot(int id)
    {
        var slot = await _context.ViewingSlots.Include(s => s.Listing).FirstOrDefaultAsync(s => s.Id == id);
        if (slot?.Listing == null)
        {
            return NotFound();
        }
        if (slot.Listing.OwnerId != CurrentUserId && !User.IsInRole("Admin"))
        {
            return Forbid();
        }
        if (slot.IsBooked)
        {
            TempData["Error"] = "الميعاد ده محجوز بالفعل — لا يمكن حذفه.";
            return RedirectToAction(nameof(Slots), new { listingId = slot.ListingId });
        }
        _context.ViewingSlots.Remove(slot);
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم حذف الميعاد.";
        return RedirectToAction(nameof(Slots), new { listingId = slot.ListingId });
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
    public async Task<IActionResult> Index(string? sort = null, string? propertyType = null, string? search = null)
    {
        var sortKey = (sort ?? "newest").ToLowerInvariant() switch
        {
            "priceasc" => "priceAsc",
            "pricedesc" => "priceDesc",
            "viewsdesc" => "viewsDesc",
            "areaasc" => "areaAsc",
            "areadesc" => "areaDesc",
            _ => "newest"
        };
        ViewBag.Sort = sortKey;
        ViewBag.PropertyType = propertyType;
        ViewBag.Search = search;
        if (User.Identity?.IsAuthenticated == true)
        {
            var query = _context.Favorites
                .Include(f => f.Listing).ThenInclude(l => l!.Owner)
                .Include(f => f.Listing).ThenInclude(l => l!.Attachments)
                .Where(f => f.UserId == CurrentUserId);
            if (!string.IsNullOrWhiteSpace(propertyType) && Enum.TryParse<PropertyType>(propertyType, out var pt))
            {
                query = query.Where(f => f.Listing!.PropertyType == pt);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(f => f.Listing!.Title.Contains(term) || f.Listing!.Area.Contains(term));
            }
            var listings = sortKey switch
            {
                "priceAsc" => await query.OrderBy(f => f.Listing!.Price).ThenByDescending(f => f.CreatedAt).Select(f => f.Listing!).ToListAsync(),
                "priceDesc" => await query.OrderByDescending(f => f.Listing!.Price).ThenByDescending(f => f.CreatedAt).Select(f => f.Listing!).ToListAsync(),
                "viewsDesc" => await query.OrderByDescending(f => f.Listing!.ViewCount).ThenByDescending(f => f.CreatedAt).Select(f => f.Listing!).ToListAsync(),
                "areaAsc" => await query.OrderBy(f => f.Listing!.Area).ThenByDescending(f => f.CreatedAt).Select(f => f.Listing!).ToListAsync(),
                "areaDesc" => await query.OrderByDescending(f => f.Listing!.Area).ThenByDescending(f => f.CreatedAt).Select(f => f.Listing!).ToListAsync(),
                _ => await query.OrderByDescending(f => f.CreatedAt).Select(f => f.Listing!).ToListAsync()
            };
            ViewBag.FavoriteListingIds = listings.Select(l => l.Id).ToHashSet();
            return View(listings);
        }
        // زائر بدون حساب: مفضلته محفوظة في جهازه (كوكي aqarak-guest-favs يزامنه الجافاسكربت).
        var guestIds = (Request.Cookies["aqarak-guest-favs"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => int.TryParse(v, out var n) ? n : -1)
            .Where(n => n > 0).Distinct().ToList();
        var guestListings = await _context.Listings.Include(l => l.Owner).Include(l => l.Attachments)
            .Where(l => l.Approval == ListingApproval.Approved && guestIds.Contains(l.Id))
            .ToListAsync();
        var ordered = guestIds.Select(id => guestListings.FirstOrDefault(l => l.Id == id))
            .Where(l => l != null).Select(l => l!).ToList();
        if (!string.IsNullOrWhiteSpace(propertyType) && Enum.TryParse<PropertyType>(propertyType, out var gpt))
        {
            ordered = ordered.Where(l => l.PropertyType == gpt).ToList();
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            ordered = ordered.Where(l => l.Title.Contains(term) || l.Area.Contains(term)).ToList();
        }
        if (sortKey == "priceAsc")
        {
            ordered = ordered.OrderBy(l => l.Price).ToList();
        }
        else if (sortKey == "priceDesc")
        {
            ordered = ordered.OrderByDescending(l => l.Price).ToList();
        }
        else if (sortKey == "viewsDesc")
        {
            ordered = ordered.OrderByDescending(l => l.ViewCount).ToList();
        }
        else if (sortKey == "areaAsc")
        {
            ordered = ordered.OrderBy(l => l.Area).ToList();
        }
        else if (sortKey == "areaDesc")
        {
            ordered = ordered.OrderByDescending(l => l.Area).ToList();
        }
        ViewBag.FavoriteListingIds = ordered.Select(l => l.Id).ToHashSet();
        return View(ordered);
    }

    [Authorize]
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

    /// <summary>حذف كل مفضلتي دفعة واحدة — للحساب المسجَّل (الزائر يمسحها من جهازه).</summary>
    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearAll()
    {
        var mine = await _context.Favorites.Where(f => f.UserId == CurrentUserId).ToListAsync();
        _context.Favorites.RemoveRange(mine);
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم حذف كل المفضلة.";
        return RedirectToAction(nameof(Index));
    }
}

public class FooterStatsViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _context;

    public FooterStatsViewComponent(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var count = await _context.Listings.CountAsync(l => l.IsAvailable && l.Approval == ListingApproval.Approved);
        return View(count);
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
    public int UnreadContactMessages { get; set; }
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
            PendingViewings = await _context.ViewingAppointments.CountAsync(v => v.Status == ViewingStatus.Pending),
            UnreadContactMessages = await _context.ContactMessages.CountAsync(m => !m.IsRead)
        };
        return View(model);
    }
}

/// <summary>طلبات عقار (Batch 41 — تميّز 1): المشتري ينشر طلبه، والأدمن يطابقه بالإعلانات.</summary>
[Authorize]
public class PropertyRequestsController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<RealtimeHub> _hub;

    public PropertyRequestsController(ApplicationDbContext context, IConfiguration configuration, IHubContext<RealtimeHub> hub)
    {
        _context = context;
        _configuration = configuration;
        _hub = hub;
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

    private async Task NotifyAdminAsync(string title, string message, string url)
    {
        var adminId = await AdminUserIdAsync();
        if (adminId != null)
        {
            _context.Notifications.Add(new Notification
            {
                Title = title,
                Message = message,
                TargetUserId = adminId,
                Url = url,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            await _context.SaveChangesAsync();
            await _hub.Clients.Group(RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
            await _hub.Clients.All.SendAsync("refreshBell");
        }
    }

    /// <summary>قائمة الطلبات المفتوحة — المعايير فقط، بدون أي بيانات عن الطالب.</summary>
    public async Task<IActionResult> Index()
    {
        var requests = await _context.PropertyRequests
            .Where(r => r.Status == PropertyRequestStatus.Open)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return View(requests);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new PropertyRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PropertyRequest request)
    {
        request.UserId = CurrentUserId;
        request.Status = PropertyRequestStatus.Open;
        request.CreatedAt = DateTime.UtcNow;
        if (request.MaxPrice.HasValue && request.MinPrice.HasValue && request.MaxPrice < request.MinPrice)
        {
            ModelState.AddModelError(nameof(request.MaxPrice), "الحد الأقصى للسعر لازم يكون أكبر من الحد الأدنى.");
        }
        if (!ModelState.IsValid)
        {
            return View(request);
        }
        _context.PropertyRequests.Add(request);
        await _context.SaveChangesAsync();
        await NotifyAdminAsync("طلب عقار جديد",
            $"طلب جديد: {(request.ListingType == ListingType.Sale ? "شراء" : "إيجار")} في {request.Area}" +
            (request.MaxPrice.HasValue ? $" بحد أقصى {request.MaxPrice:N0} جنيه" : "") + ".", "/Admin/PropertyRequests");
        TempData["Info"] = "تم نشر طلبك — إدارة الموقع هتطابقه بالإعلانات المناسبة وتبلغك.";
        return RedirectToAction(nameof(MyRequests));
    }

    /// <summary>طلباتي — مع زر إغلاق لكل طلب مفتوح.</summary>
    public async Task<IActionResult> MyRequests()
    {
        var requests = await _context.PropertyRequests
            .Where(r => r.UserId == CurrentUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
        return View(requests);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Close(int id)
    {
        var request = await _context.PropertyRequests.FirstOrDefaultAsync(r => r.Id == id && r.UserId == CurrentUserId);
        if (request == null)
        {
            return NotFound();
        }
        request.Status = PropertyRequestStatus.Closed;
        await _context.SaveChangesAsync();
        TempData["Info"] = "تم إغلاق الطلب.";
        return RedirectToAction(nameof(MyRequests));
    }
}

/// <summary>عروض الأسعار (Batch 41 — تميّز 2): كلها تمر عبر الأدمن، بلا تواصل مباشر.</summary>
[Authorize]
public class OffersController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<RealtimeHub> _hub;

    public OffersController(ApplicationDbContext context, IConfiguration configuration, IHubContext<RealtimeHub> hub)
    {
        _context = context;
        _configuration = configuration;
        _hub = hub;
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

    private async Task NotifyUserAsync(string userId, string title, string message, string url)
    {
        _context.Notifications.Add(new Notification
        {
            Title = title,
            Message = message,
            TargetUserId = userId,
            Url = url,
            CreatedAt = DateTime.UtcNow,
            IsActive = true
        });
        await _context.SaveChangesAsync();
        await _hub.Clients.All.SendAsync("refreshBell");
    }

    private async Task NotifyAdminAsync(string title, string message, string url)
    {
        var adminId = await AdminUserIdAsync();
        if (adminId != null)
        {
            await NotifyUserAsync(adminId, title, message, url);
            await _hub.Clients.Group(RealtimeHub.AdminsGroup).SendAsync("refreshAdminNav");
        }
    }

    /// <summary>تقديم عرض سعر على إعلان (ليس لصاحب الإعلان).</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(int listingId, decimal amount, string? message)
    {
        var listing = await _context.Listings.FirstOrDefaultAsync(l => l.Id == listingId);
        if (listing == null || listing.Approval != ListingApproval.Approved || !listing.IsAvailable)
        {
            return NotFound();
        }
        if (listing.OwnerId == CurrentUserId)
        {
            return Forbid();
        }
        if (amount <= 0)
        {
            TempData["Error"] = "مبلغ العرض لازم يكون أكبر من صفر.";
            return RedirectToAction("Details", "Listings", new { id = listingId });
        }
        var cleanMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        if (cleanMessage != null && cleanMessage.Length > 500)
        {
            cleanMessage = cleanMessage[..500];
        }
        _context.ListingOffers.Add(new ListingOffer
        {
            ListingId = listingId,
            BuyerId = CurrentUserId,
            Amount = amount,
            Message = cleanMessage,
            Status = OfferStatus.Pending,
            CreatedAt = DateTime.UtcNow
        });
        await _context.SaveChangesAsync();
        await NotifyAdminAsync("عرض سعر جديد",
            $"عرض سعر {amount:N0} جنيه على «{listing.Title}» ({listing.AdNumber}).", "/Admin/Offers");
        await NotifyUserAsync(listing.OwnerId, "وصل عرض سعر على إعلانك",
            $"وصل عرض سعر {amount:N0} جنيه على «{listing.Title}». إدارة الموقع هتنسّق معاك — بدون تواصل مباشر مع المشتري.",
            "/Offers/ReceivedOffers");
        TempData["Info"] = "تم إرسال عرضك — إدارة الموقع هتوصله لصاحب الإعلان وتبلغك بالرد.";
        return RedirectToAction("Details", "Listings", new { id = listingId });
    }

    /// <summary>عروضي (المشتري): كل عروضي وحالتها، مع قبول العرض المضاد إن وُجد.</summary>
    public async Task<IActionResult> MyOffers()
    {
        var offers = await _context.ListingOffers.Include(o => o.Listing)
            .Where(o => o.BuyerId == CurrentUserId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View(offers);
    }

    /// <summary>العروض الواردة (المالك): المبلغ والرسالة فقط — بدون اسم المشتري أو رقمه.</summary>
    public async Task<IActionResult> ReceivedOffers()
    {
        var myListingIds = await _context.Listings.Where(l => l.OwnerId == CurrentUserId).Select(l => l.Id).ToListAsync();
        var offers = await _context.ListingOffers.Include(o => o.Listing)
            .Where(o => myListingIds.Contains(o.ListingId))
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
        return View(offers);
    }

    /// <summary>رد المالك على العرض: قبول / رفض / عرض مضاد بمبلغ.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Respond(int id, string decision, decimal? counterAmount = null)
    {
        var offer = await _context.ListingOffers.Include(o => o.Listing).FirstOrDefaultAsync(o => o.Id == id);
        if (offer?.Listing == null || offer.Listing.OwnerId != CurrentUserId || offer.Status != OfferStatus.Pending)
        {
            return NotFound();
        }
        if (decision == "accept")
        {
            offer.Status = OfferStatus.Accepted;
        }
        else if (decision == "decline")
        {
            offer.Status = OfferStatus.Declined;
        }
        else if (decision == "counter")
        {
            if (!counterAmount.HasValue || counterAmount.Value <= 0)
            {
                TempData["Error"] = "حدد مبلغ العرض المضاد.";
                return RedirectToAction(nameof(ReceivedOffers));
            }
            offer.Status = OfferStatus.Countered;
            offer.CounterAmount = counterAmount.Value;
        }
        else
        {
            return BadRequest();
        }
        offer.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        var buyerMsg = offer.Status switch
        {
            OfferStatus.Accepted => $"صاحب الإعلان قبل عرضك {offer.Amount:N0} جنيه على «{offer.Listing.Title}». إدارة الموقع هتنسّق معاك الخطوة الجاية.",
            OfferStatus.Declined => $"صاحب الإعلان رفض عرضك {offer.Amount:N0} جنيه على «{offer.Listing.Title}».",
            _ => $"صاحب الإعلان اقترح سعرًا مضادًا {offer.CounterAmount:N0} جنيه بدل عرضك {offer.Amount:N0} جنيه على «{offer.Listing.Title}» — تقدر تقبله من عروضك."
        };
        await NotifyUserAsync(offer.BuyerId, "رد على عرض سعرك", buyerMsg, "/Offers/MyOffers");
        await NotifyAdminAsync("رد مالك على عرض سعر",
            $"رد على عرض {offer.Amount:N0} جنيه لـ «{offer.Listing.Title}»: {offer.Status}.", "/Admin/Offers");
        TempData["Info"] = "تم إرسال ردك — إدارة الموقع هتبلغ المشتري.";
        return RedirectToAction(nameof(ReceivedOffers));
    }

    /// <summary>قبول المشتري للعرض المضاد من المالك.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcceptCounter(int id)
    {
        var offer = await _context.ListingOffers.Include(o => o.Listing).FirstOrDefaultAsync(o => o.Id == id);
        if (offer?.Listing == null || offer.BuyerId != CurrentUserId || offer.Status != OfferStatus.Countered)
        {
            return NotFound();
        }
        offer.Status = OfferStatus.Accepted;
        offer.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await NotifyUserAsync(offer.Listing.OwnerId, "المشتري قبل العرض المضاد",
            $"المشتري قبل سعرك المضاد {offer.CounterAmount:N0} جنيه على «{offer.Listing.Title}». إدارة الموقع هتنسّق معاكم.",
            "/Offers/ReceivedOffers");
        await NotifyAdminAsync("قبول عرض مضاد",
            $"المشتري قبل السعر المضاد {offer.CounterAmount:N0} جنيه لـ «{offer.Listing.Title}».", "/Admin/Offers");
        TempData["Info"] = "تم قبول العرض المضاد — إدارة الموقع هتنسّق معاكم.";
        return RedirectToAction(nameof(MyOffers));
    }
}
