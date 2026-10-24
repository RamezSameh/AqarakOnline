using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AqarakOnline.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(200)]
    public string? FullName { get; set; }

    /// <summary>عنوان المستخدم، مطلوب عند التسجيل.</summary>
    [MaxLength(500)]
    public string? Address { get; set; }
}

/// <summary>
/// كود تأكيد رقم الموبايل. يُحفظ هاش الكود فقط، وينتهي بعد 10 دقائق،
/// ويُستخدم مرة واحدة، ويُقفل بعد 5 محاولات خاطئة.
/// </summary>
public class PhoneVerificationCode
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public string CodeHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; }
    public int Attempts { get; set; }
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

/// <summary>
/// حالة نشر الإعلان: معلّق حتى موافقة الأدمن، ثم مقبول (منشور) أو مرفوض.
/// لا يظهر الإعلان للعامة إلا وهو مقبول.
/// </summary>
public enum ListingApproval
{
    Pending,
    Approved,
    Rejected
}

public enum AttachmentKind
{
    Photo,
    Video
}

/// <summary>
/// مرفق إعلان: صورة توضيحية (حتى 5 صور للإعلان) أو فيديو واحد (حتى 25 ميجا).
/// الملف نفسه يُحفظ على القرص تحت wwwroot/uploads، وقاعدة البيانات تحتفظ بمساره فقط.
/// </summary>
public class ListingAttachment
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public AttachmentKind Kind { get; set; }

    /// <summary>المسار النسبي داخل wwwroot، مثل uploads/listings/5/abc.jpg.</summary>
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    [MaxLength(200)]
    public string OriginalFileName { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public int Order { get; set; }
}

public class Listing
{
    public int Id { get; set; }

    // رقم الإعلان يتولد من السيرفر بعد الحفظ، فليس حقلًا يدخله المستخدم
    // ولا يجب أن يكون Required في التحقق من النموذج.
    [MaxLength(20)]
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

    /// <summary>مساحة العقار بالمتر المربع.</summary>
    [Range(1, 100000, ErrorMessage = "المساحة يجب أن تكون بالمتر المربع وأكبر من صفر")]
    public int AreaSqm { get; set; }

    /// <summary>عدد مرات مشاهدة صفحة تفاصيل الإعلان (من غير الأدمن وصاحب الإعلان).</summary>
    public int ViewCount { get; set; }

    public bool IsAvailable { get; set; } = true;

    /// <summary>موافقة الأدمن على النشر. الافتراضي معلّق.</summary>
    public ListingApproval Approval { get; set; } = ListingApproval.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    public List<ListingAttachment> Attachments { get; set; } = new();

    [NotMapped]
    public List<IFormFile> PhotoFiles { get; set; } = new();

    [NotMapped]
    public IFormFile? VideoFile { get; set; }

    [NotMapped]
    public int[] DeleteAttachmentIds { get; set; } = Array.Empty<int>();

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
