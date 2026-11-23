using AqarakOnline.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AqarakOnline.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // السعر بعملة الجنيه: دقة صريحة حتى لا يُقتطع عند الحفظ في SQL Server.
        builder.Entity<Listing>().Property(l => l.Price).HasPrecision(18, 2);
        builder.Entity<Listing>().Property(l => l.OriginalPrice).HasPrecision(18, 2);
        builder.Entity<SavedSearch>().Property(s => s.MinPrice).HasPrecision(18, 2);
        builder.Entity<SavedSearch>().Property(s => s.MaxPrice).HasPrecision(18, 2);
        builder.Entity<PropertyRequest>().Property(s => s.MinPrice).HasPrecision(18, 2);
        builder.Entity<PropertyRequest>().Property(s => s.MaxPrice).HasPrecision(18, 2);
        builder.Entity<ListingOffer>().Property(o => o.Amount).HasPrecision(18, 2);
        builder.Entity<ListingOffer>().Property(o => o.CounterAmount).HasPrecision(18, 2);
        // حذف مستخدم يحذف بحثه المحفوظ.
        builder.Entity<SavedSearch>()
            .HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        // حذف تنبيه يحذف علامات قراءته، وحذف مستخدم يحذف قراءاته ومفضلته.
        builder.Entity<NotificationRead>()
            .HasOne(r => r.Notification).WithMany().HasForeignKey(r => r.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<NotificationRead>()
            .HasOne(r => r.User).WithMany().HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        // حذف عقار يحذف مفضلته، وحذف مستخدم يحذف مفضلته.
        builder.Entity<Favorite>()
            .HasOne(f => f.Listing).WithMany().HasForeignKey(f => f.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Favorite>()
            .HasOne(f => f.User).WithMany().HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<Favorite>()
            .HasIndex(f => new { f.UserId, f.ListingId }).IsUnique();
        // سجل التعديلات: حذف الإعلان يحذف سجله.
        builder.Entity<ListingAudit>()
            .HasOne(a => a.Listing).WithMany().HasForeignKey(a => a.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
        // حذف إعلان يحذف بلاغاته وطلبات معاينته؛ وحذف مستخدم يحذف ما أرسله منهما.
        builder.Entity<ListingReport>()
            .HasOne(r => r.Listing).WithMany().HasForeignKey(r => r.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ListingReport>()
            .HasOne(r => r.Reporter).WithMany().HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ViewingAppointment>()
            .HasOne(v => v.Listing).WithMany().HasForeignKey(v => v.ListingId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Entity<ViewingAppointment>()
            .HasOne(v => v.Requester).WithMany().HasForeignKey(v => v.RequesterId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ContactRequest> ContactRequests => Set<ContactRequest>();
    public DbSet<ListingAttachment> ListingAttachments => Set<ListingAttachment>();
    public DbSet<PhoneVerificationCode> PhoneVerificationCodes => Set<PhoneVerificationCode>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();
    public DbSet<ListingReport> ListingReports => Set<ListingReport>();
    public DbSet<ListingAudit> ListingAudits => Set<ListingAudit>();
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();
    public DbSet<ViewingAppointment> ViewingAppointments => Set<ViewingAppointment>();
    public DbSet<SearchLog> SearchLogs => Set<SearchLog>();
    public DbSet<BlockedPhone> BlockedPhones => Set<BlockedPhone>();
    public DbSet<SiteSetting> SiteSettings => Set<SiteSetting>();
    public DbSet<PropertyRequest> PropertyRequests => Set<PropertyRequest>();
    public DbSet<ListingOffer> ListingOffers => Set<ListingOffer>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<CollectionListing> CollectionListings => Set<CollectionListing>();
    public DbSet<ViewingSlot> ViewingSlots => Set<ViewingSlot>();
    public DbSet<WhatsAppOutbox> WhatsAppOutbox => Set<WhatsAppOutbox>();
    public DbSet<BlockedIp> BlockedIps => Set<BlockedIp>();
    public DbSet<TrustedIpAddress> TrustedIpAddresses => Set<TrustedIpAddress>();
}

public static class SeedData
{
    public static void SeedListings(ApplicationDbContext context)
    {
        if (context.Listings.Any())
        {
            return;
        }

        var seedDate = new DateTime(2026, 10, 24, 0, 0, 0, DateTimeKind.Utc);
        context.Listings.AddRange(
            new Listing
            {
                AdNumber = "AQ-0001",
                Title = "شقة للبيع في مدينة نصر",
                ListingType = ListingType.Sale,
                PropertyType = PropertyType.Apartment,
                Area = "مدينة نصر، القاهرة",
                Description = "شقة واسعة بإطلالة مميزة، قريبة من الخدمات والمواصلات.",
                Features = "3 غرف\n2 حمام\nتشطيب سوبر لوكس\nمصعد",
                Price = 2500000,
                OriginalPrice = 2500000,
                AreaSqm = 150,
                Rooms = 3,
                Bathrooms = 2,
                IsFeatured = true,
                IsAvailable = true,
                Approval = ListingApproval.Approved,
                CreatedAt = seedDate
            },
            new Listing
            {
                AdNumber = "AQ-0002",
                Title = "شقة للإيجار في التجمع الخامس",
                ListingType = ListingType.Rent,
                PropertyType = PropertyType.Apartment,
                Area = "التجمع الخامس، القاهرة الجديدة",
                Description = "شقة مفروشة للإيجار، مناسبة للعائلات، بمنطقة هادئة وآمنة.",
                Features = "2 غرفة\nحمام\nمفروشة\nأمن وحراسة",
                Price = 12000,
                OriginalPrice = 12000,
                AreaSqm = 110,
                Rooms = 2,
                Bathrooms = 1,
                Furnishing = FurnishingLevel.Full,
                IsAvailable = true,
                Approval = ListingApproval.Approved,
                CreatedAt = seedDate
            },
            new Listing
            {
                AdNumber = "AQ-0003",
                Title = "فيلا للبيع في المعادي",
                ListingType = ListingType.Sale,
                PropertyType = PropertyType.Villa,
                Area = "المعادي، القاهرة",
                Description = "فيلا مستقلة بحديقة خاصة وموقف سيارات، تشطيب راقٍ.",
                Features = "4 غرف\n3 حمامات\nحديقة خاصة\nموقف سيارات",
                Price = 8500000,
                OriginalPrice = 8500000,
                AreaSqm = 320,
                Rooms = 4,
                Bathrooms = 3,
                IsAvailable = true,
                Approval = ListingApproval.Approved,
                CreatedAt = seedDate
            },
            new Listing
            {
                AdNumber = "AQ-0004",
                Title = "محل للإيجار في وسط البلد",
                ListingType = ListingType.Rent,
                PropertyType = PropertyType.Shop,
                Area = "وسط البلد، القاهرة",
                Description = "محل تجاري بموقع حيوي وحركة مرور عالية، مناسب لكل الأنشطة.",
                Features = "واجهة على الشارع\nمساحة مفتوحة\nكهرباء 3 فاز",
                Price = 15000,
                OriginalPrice = 15000,
                AreaSqm = 80,
                IsAvailable = true,
                Approval = ListingApproval.Approved,
                CreatedAt = seedDate
            });
        context.SaveChanges();
    }

    /// <summary>
    /// مجموعات مختارة افتراضية (فارغة — يملؤها الأدمن بالإعلانات من لوحة الإدارة).
    /// تُنشأ مرة واحدة فقط عند أول تشغيل بعد الترقية.
    /// </summary>
    public static void SeedCollections(ApplicationDbContext context)
    {
        if (context.Collections.Any())
        {
            return;
        }
        context.Collections.AddRange(
            new Collection { Title = "شقق للعرسان", Description = "شقق مناسبة للبداية: مساحات عملية وأسعار في المتناول.", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Collection { Title = "استثمار", Description = "عقارات بعائد استثماري جيد في مناطق واعدة.", IsActive = true, CreatedAt = DateTime.UtcNow },
            new Collection { Title = "أقل من مليون", Description = "عقارات بسعر أقل من مليون جنيه.", IsActive = true, CreatedAt = DateTime.UtcNow });
        context.SaveChanges();
    }

    /// <summary>
    /// Batch 74 — تنظيف بيانات قديمة: أي إعلان غير متاح كان مميزًا قبل Batch 69
    /// يفقد التمييز (القاعدة: غير المتاح مينفعش يبقى مميز). يعمل عند كل تشغيل،
    /// ولا يفعل شيئًا لو لم توجد إعلانات مخالفة.
    /// </summary>
    public static void ClearFeaturedForUnavailable(ApplicationDbContext context)
    {
        context.Listings
            .Where(l => !l.IsAvailable && l.IsFeatured)
            .ExecuteUpdate(s => s
                .SetProperty(l => l.IsFeatured, false)
                .SetProperty(l => l.FeaturedUntil, (DateTime?)null));
    }

    /// <summary>
    /// مستخدمون تجريبيون للتجربة المحلية فقط.
    /// تحذير أمني: كلمات المرور هنا معروفة ومنشورة مع الكود، فلا تنشر التطبيق
    /// بهذه الإعدادات أبدًا. قبل أي نشر عام، اضبط SeedTestUsers على false
    /// واحذف هؤلاء المستخدمين من قاعدة البيانات، وغيّر كلمات المرور.
    /// المستخدم الثالث قيد المراجعة (Pending) لاختبار موافقة الأدمن عليه.
    /// </summary>
    public static async Task SeedTestUsersAsync(UserManager<ApplicationUser> userManager)
    {
        var testUsers = new[]
        {
            new { Phone = "01000000001", FullName = "أدمن تجريبي", Address = "القاهرة", Email = "Admin@Aqarak.com", Password = "Admin123456", Role = "Admin", Status = AccountStatus.Active },
            new { Phone = "01000000002", FullName = "مستخدم تجريبي", Address = "الجيزة", Email = "user@aqarak.test", Password = "User123456", Role = "User", Status = AccountStatus.Active },
            new { Phone = "01000000003", FullName = "مستخدم قيد المراجعة (للتجربة)", Address = "القاهرة", Email = "pending@aqarak.test", Password = "User123456", Role = "User", Status = AccountStatus.Pending },
        };

        foreach (var test in testUsers)
        {
            var existing = await userManager.FindByNameAsync(test.Phone);
            if (existing != null)
            {
                continue;
            }
            var user = new ApplicationUser
            {
                UserName = test.Phone,
                PhoneNumber = test.Phone,
                PhoneNumberConfirmed = false,
                FullName = test.FullName,
                Address = test.Address,
                Email = test.Email,
                AccountStatus = test.Status
            };
            var result = await userManager.CreateAsync(user, test.Password);
            if (!result.Succeeded)
            {
                continue;
            }
            await userManager.AddToRoleAsync(user, test.Role);
        }
    }
}
