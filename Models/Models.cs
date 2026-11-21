using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace AqarakOnline.Models;

/// <summary>
/// حالة حساب المستخدم. التوثيق الآن عن طريق الأدمن (وليس برسالة موبايل)،
/// لحين ترتيب إجراءات خدمة SMS مدفوعة: الحساب الجديد يبدأ Pending ولا يدخل
/// حتى يوافق عليه الأدمن. Suspended إيقاف مؤقت، وBlocked حظر.
/// </summary>
public enum AccountStatus
{
    Pending,
    Active,
    Suspended,
    Blocked
}

public class ApplicationUser : IdentityUser
{
    [MaxLength(200)]
    public string? FullName { get; set; }

    /// <summary>عنوان المستخدم، مطلوب عند التسجيل. يظهر للأدمن فقط، أبدًا للعامة.</summary>
    [MaxLength(500)]
    public string? Address { get; set; }

    public AccountStatus AccountStatus { get; set; } = AccountStatus.Pending;

    /// <summary>تاريخ إنشاء الحساب — يُستخدم في رسم التسجيلات الجديدة بالداشبورد.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// كود تأكيد رقم الموبايل. يُحفظ هاش الكود فقط، وينتهي بعد 10 دقائق،
/// ويُستخدم مرة واحدة، ويُقفل بعد 5 محاولات خاطئة.
/// ملحوظة: التأكيد بالرسائل موقوف مؤقتًا — التوثيق الآن عن طريق الأدمن
/// لحين ترتيب خدمة SMS مدفوعة. الكيان والكود محفوظان لإعادة التفعيل لاحقًا.
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

/// <summary>مصدر الإعلان: من المالك مباشرة أم من سمسار. يظهر للعامة في الكارت والتفاصيل.</summary>
public enum AdvertiserType
{
    Owner,
    Broker
}

/// <summary>مستوى التشطيب (اختياري — يختاره المعلن عند إضافة الإعلان).</summary>
public enum FinishingLevel
{
    SuperLux,
    Lux,
    SemiFinished,
    RedBrick
}

/// <summary>قائمة جاهزة للمناطق (القاهرة والجيزة). تُستخدم في نماذج إضافة/تعديل الإعلان بدل الكتابة الحرة.</summary>
/// <summary>قائمة جاهزة للمميزات في نموذج الإضافة — اختيارات من متعدد بدل الكتابة الحرة.</summary>
public static class ListingFeatures
{
    public static readonly string[] All =
    {
        "مطبخ مجهز",
        "دولاب حائط",
        "شبابيك ألوميتال",
        "أبواب مصفحة",
        "أرضيات بورسلين",
        "أرضيات باركيه",
        "جبس بورد",
        "سخان",
        "كاميرات مراقبة",
        "حارس عقار",
        "مدخل رخام",
        "قريب من المواصلات",
        "قريب من المدارس",
        "هادئ"
    };
}

public static class ListingAreas
{
    public static readonly string[] All =
    {
        "مدينة نصر، القاهرة",
        "التجمع الخامس، القاهرة الجديدة",
        "التجمع الأول، القاهرة الجديدة",
        "التجمع الثالث، القاهرة الجديدة",
        "المعادي، القاهرة",
        "وسط البلد، القاهرة",
        "مصر الجديدة، القاهرة",
        "مدينة قباء، القاهرة",
        "جسر السويس، القاهرة",
        "الزمالك، القاهرة",
        "المقطم، القاهرة",
        "شبرا، القاهرة",
        "عين شمس، القاهرة",
        "حلوان، القاهرة",
        "الرحاب، القاهرة",
        "مدينتي، القاهرة",
        "العبور، القليوبية",
        "الشروق، القاهرة",
        "المهندسين، الجيزة",
        "الدقي، الجيزة",
        "فيصل، الجيزة",
        "الهرم، الجيزة",
        "أكتوبر، الجيزة",
        "الشيخ زايد، الجيزة",
        "حدائق الأهرام، الجيزة",
        "إمبابة، الجيزة"
    };
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

    /// <summary>
    /// العنوان التفصيلي للعقار (شارع/عمارة/دور...). يدخله المعلن، ويظهر
    /// للأدمن فقط (ولصاحب الإعلان في إعلاناته) — ولا يظهر أبدًا في الكارت
    /// العام ولا في صفحة التفاصيل العامة. العامة يرون المنطقة فقط.
    /// </summary>
    [MaxLength(500)]
    public string? DetailedAddress { get; set; }

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

    /// <summary>الإعلان من المالك أم من سمسار. الافتراضي مالك.</summary>
    public AdvertiserType AdvertiserType { get; set; } = AdvertiserType.Owner;

    /// <summary>عدد الغرف (اختياري).</summary>
    [Range(0, 50, ErrorMessage = "عدد الغرف يجب أن يكون بين 0 و 50")]
    public int? Rooms { get; set; }

    /// <summary>عدد الحمامات (اختياري).</summary>
    [Range(0, 20, ErrorMessage = "عدد الحمامات يجب أن يكون بين 0 و 20")]
    public int? Bathrooms { get; set; }

    /// <summary>الدور (مطلوب عند إضافة شقة). 0 = أرضي.</summary>
    [Range(0, 60, ErrorMessage = "الدور يجب أن يكون بين 0 و 60")]
    public int? Floor { get; set; }

    /// <summary>مفروش أم لا.</summary>
    public bool IsFurnished { get; set; }

    /// <summary>مستوى التشطيب (اختياري).</summary>
    public FinishingLevel? Finishing { get; set; }

    /// <summary>مزايا جاهزة (اختيارية) — تظهر في الكارت والتفاصيل ويُفلتر بها.</summary>
    public bool HasParking { get; set; }
    public bool HasElevator { get; set; }
    public bool HasBalcony { get; set; }
    public bool HasAirConditioning { get; set; }
    public bool HasSecurity { get; set; }

    /// <summary>يقبل التقسيط أم لا.</summary>
    public bool AcceptsInstallments { get; set; }

    /// <summary>آخر مرة عدّل فيها صاحب الإعلان (أو الأدمن) بيانات الإعلان.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>السعر قابل للتفاوض أم لا.</summary>
    public bool IsPriceNegotiable { get; set; }

    /// <summary>مدة الإيجار (للإيجارات فقط).</summary>
    public RentPeriod? RentPeriod { get; set; }

    /// <summary>عدد شهور التأمين (للإيجارات فقط).</summary>
    public int? DepositMonths { get; set; }

    /// <summary>عدادات (اختيارية) — تظهر في التفاصيل ويُفلتر بها.</summary>
    public bool HasElectricityMeter { get; set; }
    public bool HasGasMeter { get; set; }
    public bool HasWaterMeter { get; set; }

    /// <summary>مساحة الأرض بالمتر المربع (للفيلا والعمارة).</summary>
    public int? LandAreaSqm { get; set; }

    /// <summary>عدد أدوار العمارة أو الفيلا.</summary>
    public int? BuildingFloors { get; set; }

    /// <summary>عدد مرات مشاهدة صفحة تفاصيل الإعلان (من غير الأدمن وصاحب الإعلان).</summary>
    public int ViewCount { get; set; }

    /// <summary>إعلان مميز يثبّته الأدمن: يظهر أولًا في نتائج البحث وبشارة «مميز».</summary>
    public bool IsFeatured { get; set; }

    /// <summary>آخر مرة جدّد فيها صاحب الإعلان ظهوره أول النتائج (ترتيب «الأحدث» يستخدمها بدل تاريخ النشر).</summary>
    public DateTime? LastBumpedAt { get; set; }

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

/// <summary>
/// تنبيه ينشئه الأدمن ويظهر لكل المستخدمين على هيئة إشعار.
/// IsActive=false يخفيه من غير حذفه.
/// TargetUserId فارغ يعني التنبيه عام لكل المستخدمين؛ ولو متعبّي يظهر
/// لذلك المستخدم فقط (مثل تنبيه «إعلان جديد مطابق لبحثك المحفوظ»).
/// Url اختياري: رابط يفتحه المستخدم من التنبيه (مثل صفحة الإعلان).
/// </summary>
public class Notification
{
    public int Id { get; set; }

    [Required(ErrorMessage = "عنوان التنبيه مطلوب")]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "نص التنبيه مطلوب")]
    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    public string? TargetUserId { get; set; }

    [MaxLength(500)]
    public string? Url { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// بحث محفوظ لمستخدم: نفس شروط صفحة الإعلانات. عند نشر إعلان جديد مطابق
/// يوصله تنبيه (راجع SavedSearchesController.NotifyMatchingSearchesAsync).
/// الحقول الفارغة تعني «أي قيمة».
/// </summary>
public class SavedSearch
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [MaxLength(200)]
    public string? Search { get; set; }

    [MaxLength(200)]
    public string? Area { get; set; }

    public ListingType? ListingType { get; set; }
    public PropertyType? PropertyType { get; set; }

    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? MinAreaSqm { get; set; }
    public int? MaxAreaSqm { get; set; }
    public int? MinRooms { get; set; }
    public bool FurnishedOnly { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// علامة قراءة تنبيه لمستخدم معين. غياب الصف يعني أن التنبيه غير مقروء له.
/// </summary>
public class NotificationRead
{
    public int Id { get; set; }

    public int NotificationId { get; set; }
    public Notification? Notification { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}

/// <summary>سبب البلاغ عن إعلان مخالف.</summary>
public enum ReportReason
{
    Misleading,
    WrongInfo,
    Scam,
    Offensive,
    Other
}

/// <summary>حالة البلاغ: قيد المراجعة حتى يراجعه الأدمن.</summary>
public enum ReportStatus
{
    Pending,
    Reviewed
}

/// <summary>
/// بلاغ عن إعلان مخالف من مستخدم مسجَّل. يراجعه الأدمن من صفحة البلاغات.
/// </summary>
public class ListingReport
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public string ReporterId { get; set; } = string.Empty;
    public ApplicationUser? Reporter { get; set; }

    public ReportReason Reason { get; set; }

    [MaxLength(1000)]
    public string? Details { get; set; }

    public ReportStatus Status { get; set; } = ReportStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>حالة طلب معاينة عقار.</summary>
public enum ViewingStatus
{
    Pending,
    Confirmed,
    Declined,
    Cancelled
}

/// <summary>
/// طلب معاينة: مشتري يطلب ميعادًا لمعاينة عقار، والمعلن يؤكد أو يرفض.
/// </summary>
public class ViewingAppointment
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public string RequesterId { get; set; } = string.Empty;
    public ApplicationUser? Requester { get; set; }

    /// <summary>الميعاد المطلوب (بتوقيت المستخدم المحلي كما أدخله).</summary>
    public DateTime RequestedAt { get; set; }

    public ViewingStatus Status { get; set; } = ViewingStatus.Pending;

    [MaxLength(500)]
    public string? Message { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// عقار أضافه مستخدم إلى مفضلته. (المستخدم، العقار) فريد.
/// </summary>
public class Favorite
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>سجل تعديلات إعلان — للأدمن: مين عدّل إيه وإمتى.</summary>
public class ListingAudit
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public string UserId { get; set; } = string.Empty;

    /// <summary>اسم من قام بالإجراء وقتها (محفوظ نصًا حتى لو اتحذف الحساب).</summary>
    [MaxLength(200)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>نوع الإجراء: إنشاء / تعديل / موافقة / رفض / تجديد / نسخ.</summary>
    [MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Details { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>مدة الإيجار (للإيجارات فقط).</summary>
public enum RentPeriod
{
    Monthly = 0,
    Yearly = 1
}

/// <summary>رسالة من زائر عبر صفحة «تواصل معنا» — تصل للأدمن فقط.</summary>
public class ContactMessage
{
    public int Id { get; set; }

    [Required(ErrorMessage = "الاسم مطلوب")]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الموبايل مطلوب")]
    [MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "الرسالة مطلوبة")]
    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRead { get; set; }
}
