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
    }

    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ContactRequest> ContactRequests => Set<ContactRequest>();
    public DbSet<ListingAttachment> ListingAttachments => Set<ListingAttachment>();
    public DbSet<PhoneVerificationCode> PhoneVerificationCodes => Set<PhoneVerificationCode>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationRead> NotificationReads => Set<NotificationRead>();
    public DbSet<Favorite> Favorites => Set<Favorite>();
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
                AreaSqm = 150,
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
                AreaSqm = 110,
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
                AreaSqm = 320,
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
                AreaSqm = 80,
                IsAvailable = true,
                Approval = ListingApproval.Approved,
                CreatedAt = seedDate
            });
        context.SaveChanges();
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
