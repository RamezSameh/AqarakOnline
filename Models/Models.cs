using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AqarakOnline.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(200)]
    public string? FullName { get; set; }
}

public enum ListingType
{
    Rent,
    Sale
}

public enum PropertyType
{
    Apartment,
    House,
    Building,
    Shop,
    Villa
}

public enum ContactIntent
{
    Inquiry,
    Buy,
    Rent
}

public class Listing
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string AdNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "عنوان الإعلان مطلوب")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public ListingType ListingType { get; set; }

    public PropertyType PropertyType { get; set; }

    [Required(ErrorMessage = "المنطقة مطلوبة")]
    [MaxLength(200)]
    public string Area { get; set; } = string.Empty;

    [Required(ErrorMessage = "التفاصيل مطلوبة")]
    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    /// <summary>المميزات، سطر لكل ميزة (أو مفصولة بفاصلة).</summary>
    [MaxLength(2000)]
    public string? Features { get; set; }

    [Range(0, 1000000000, ErrorMessage = "السعر يجب أن يكون رقمًا موجبًا")]
    public decimal Price { get; set; }

    public bool IsAvailable { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    public byte[]? Photo { get; set; }
    public string? PhotoContentType { get; set; }

    [NotMapped]
    public IFormFile? PhotoFile { get; set; }

    public IEnumerable<string> FeaturesList =>
        (Features ?? string.Empty)
            .Split(new[] { '\n', '\r', ',', '،' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0);
}

public class ContactRequest
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public string? BuyerId { get; set; }
    public ApplicationUser? Buyer { get; set; }

    [Required(ErrorMessage = "اسم المشتري مطلوب")]
    [MaxLength(200)]
    public string BuyerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم هاتف المشتري مطلوب")]
    [MaxLength(50)]
    public string BuyerPhone { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Message { get; set; }

    public ContactIntent Intent { get; set; } = ContactIntent.Inquiry;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsReplied { get; set; }
    public DateTime? RepliedAt { get; set; }
}
