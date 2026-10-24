using AqarakOnline.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AqarakOnline.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ContactRequest> ContactRequests => Set<ContactRequest>();
    public DbSet<ListingAttachment> ListingAttachments => Set<ListingAttachment>();
    public DbSet<PhoneVerificationCode> PhoneVerificationCodes => Set<PhoneVerificationCode>();
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
}
