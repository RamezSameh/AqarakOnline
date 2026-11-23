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

    /// <summary>يستقبل تنبيهات واتساب عند مطابقة بحثه المحفوظ أو طلبه لإعلان جديد؟</summary>
    public bool ReceiveWhatsAppAlerts { get; set; }

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

/// <summary>مستوى الفرش: غير مفروش / جزئي / كامل.</summary>
public enum FurnishingLevel
{
    None,
    Partial,
    Full
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

    // Batch 105 — المميزات متقسمة 3 مجموعات في نموذج الإعلان بدل قائمة واحدة طويلة.
    public static readonly (string Title, string[] Items)[] Groups =
    {
        ("داخل الوحدة", new[] { "مطبخ مجهز", "دولاب حائط", "شبابيك ألوميتال", "أبواب مصفحة", "أرضيات بورسلين", "أرضيات باركيه", "جبس بورد", "سخان" }),
        ("المبنى والأمان", new[] { "كاميرات مراقبة", "حارس عقار", "مدخل رخام" }),
        ("الموقع", new[] { "قريب من المواصلات", "قريب من المدارس", "هادئ" })
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

    /// <summary>نسخة مصغّرة (480px) من الصورة للكروت وsrcset — null للصور القديمة والفيديو.</summary>
    [MaxLength(500)]
    public string? ThumbPath { get; set; }

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
    /// <summary>مستوى الفرش: غير مفروش / جزئي / كامل.</summary>
    public FurnishingLevel Furnishing { get; set; }

    /// <summary>سبب البيع — اختياري، للبيع فقط.</summary>
    [MaxLength(200)]
    public string? SaleReason { get; set; }

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

    /// <summary>إعلان عاجل — يحدده صاحب الإعلان، ويظهر بشارة مميزة.</summary>
    public bool IsUrgent { get; set; }

    /// <summary>سبب رفض الأدمن للإعلان (إن وُجد) — يظهر لصاحب الإعلان.</summary>
    [MaxLength(500)]
    public string? RejectionReason { get; set; }

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

    /// <summary>عدد الشقق في العمارة — للعمارة فقط.</summary>
    [Range(1, 1000)]
    public int? ApartmentsCount { get; set; }

    /// <summary>عدد المصاعد — عند وجود مصعد.</summary>
    [Range(1, 20)]
    public int? ElevatorCount { get; set; }

    /// <summary>السعر الأصلي عند الإنشاء — لشارة «تم تخفيض السعر» (السعر الحالي أقل منه).</summary>
    public decimal OriginalPrice { get; set; }

    /// <summary>سعره الحالي أقل من سعره الأصلي.</summary>
    [NotMapped]
    public bool IsPriceReduced => Price < OriginalPrice;

    /// <summary>عدد مرات مشاهدة صفحة تفاصيل الإعلان (من غير الأدمن وصاحب الإعلان).</summary>
    public int ViewCount { get; set; }

    /// <summary>إعلان مميز يثبّته الأدمن: يظهر أولًا في نتائج البحث وبشارة «مميز».</summary>
    public bool IsFeatured { get; set; }

    /// <summary>نهاية مدة التمييز — بلا قيمة = تمييز دائم.</summary>
    public DateTime? FeaturedUntil { get; set; }

    /// <summary>مميز فعليًا الآن: مميز ولم تنتهِ مدة تمييزه.</summary>
    [NotMapped]
    public bool IsEffectivelyFeatured => IsFeatured && (FeaturedUntil == null || FeaturedUntil > DateTime.UtcNow);

    /// <summary>آخر مرة جدّد فيها صاحب الإعلان ظهوره أول النتائج (ترتيب «الأحدث» يستخدمها بدل تاريخ النشر).</summary>
    public DateTime? LastBumpedAt { get; set; }

    public bool IsAvailable { get; set; } = true;

    /// <summary>موافقة الأدمن على النشر. الافتراضي معلّق.</summary>
    public ListingApproval Approval { get; set; } = ListingApproval.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? OwnerId { get; set; }
    public ApplicationUser? Owner { get; set; }

    /// <summary>بيانات صاحب الإعلان الضيف (بدون حساب): الاسم ورقم الهاتف/واتساب. فارغة للإعلانات المربوطة بحساب.</summary>
    [MaxLength(200)]
    public string? GuestName { get; set; }

    [MaxLength(50)]
    public string? GuestPhone { get; set; }

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
    /// <summary>مستوى الفرش المطلوب في البحث المحفوظ: Full أو Partial أو Any (أي مستوى مفروش).</summary>
    public string? Furnishing { get; set; }

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

/// <summary>كلمة بحث كتبها زائر — لإحصائية أكثر كلمات البحث في لوحة الأدمن.</summary>
public class SearchLog
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string Term { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>رقم موبايل محظور من التسجيل — يحدده الأدمن.</summary>
public class BlockedPhone
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>إعداد عام للموقع (مفتاح/قيمة) — مثل وضع الصيانة.</summary>
public class SiteSetting
{
    [Key]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Value { get; set; } = string.Empty;
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

    public string? RequesterId { get; set; }
    public ApplicationUser? Requester { get; set; }

    /// <summary>بيانات طالب المعاينة الضيف (بدون حساب): الاسم ورقم الهاتف/واتساب.</summary>
    [MaxLength(200)]
    public string? RequesterName { get; set; }

    [MaxLength(50)]
    public string? RequesterPhone { get; set; }

    /// <summary>الميعاد المطلوب (بتوقيت المستخدم المحلي كما أدخله).</summary>
    public DateTime RequestedAt { get; set; }

    public ViewingStatus Status { get; set; } = ViewingStatus.Pending;

    /// <summary>اترسل تذكير المعاينة لطالبها قبلها بيوم؟ (مرة واحدة بس).</summary>
    public bool ReminderSent { get; set; }

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

/// <summary>حالة طلب عقار: مفتوح حتى يغلقه صاحبه، أو مطابق (أُبلغ عنه بإعلان مناسب).</summary>
public enum PropertyRequestStatus
{
    Open,
    Matched,
    Closed
}

/// <summary>
/// طلب عقار من مشتري/مستأجر: نوع الإعلان والمنطقة والميزانية. الأدمن هو من
/// يطابقه بالإعلانات المناسبة ويُبلغ الطالب — بيانات الطالب لا تظهر أبدًا
/// لغير الأدمن (لا اسم ولا رقم) تماشيًا مع قاعدة «لا تواصل إلا عبر الأدمن».
/// </summary>
public class PropertyRequest
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    public ListingType ListingType { get; set; }

    public PropertyType? PropertyType { get; set; }

    [Required(ErrorMessage = "المنطقة مطلوبة")]
    [MaxLength(200)]
    public string Area { get; set; } = string.Empty;

    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? MinAreaSqm { get; set; }
    public int? MinRooms { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public PropertyRequestStatus Status { get; set; } = PropertyRequestStatus.Open;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>حالة عرض السعر: بانتظار رد المالك، أو مقبول، أو مرفوض، أو قابله المالك بعرض مضاد.</summary>
public enum OfferStatus
{
    Pending,
    Accepted,
    Declined,
    Countered
}

/// <summary>
/// عرض سعر على إعلان — يمر عبر الأدمن: المشتري يقدّم العرض، الأدمن يبلّغ
/// المالك بالمبلغ فقط (بدون اسم أو رقم المشتري)، والمالك يقبل أو يرفض أو
/// يقترح سعرًا مضادًا، والأدمن يبلّغ المشتري بالرد.
/// </summary>
public class ListingOffer
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public string? BuyerId { get; set; }
    public ApplicationUser? Buyer { get; set; }

    /// <summary>بيانات مقدّم العرض الضيف (بدون حساب): الاسم ورقم الهاتف/واتساب.</summary>
    [MaxLength(200)]
    public string? BuyerName { get; set; }

    [MaxLength(50)]
    public string? BuyerPhone { get; set; }

    [Range(1, 1000000000, ErrorMessage = "مبلغ العرض يجب أن يكون أكبر من صفر")]
    public decimal Amount { get; set; }

    [MaxLength(500)]
    public string? Message { get; set; }

    public OfferStatus Status { get; set; } = OfferStatus.Pending;

    /// <summary>السعر المضاد الذي اقترحه المالك (إن وُجد).</summary>
    public decimal? CounterAmount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RespondedAt { get; set; }
}

/// <summary>مجموعة مختارة من الأدمن (مثلاً: شقق للعرسان، استثمار، أقل من مليون).</summary>
public class Collection
{
    public int Id { get; set; }

    [Required(ErrorMessage = "عنوان المجموعة مطلوب")]
    [MaxLength(100)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<CollectionListing> Items { get; set; } = new();
}

/// <summary>إعلان داخل مجموعة مختارة (ترتيب اختياري).</summary>
public class CollectionListing
{
    public int Id { get; set; }

    public int CollectionId { get; set; }
    public Collection? Collection { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// ميعاد معاينة متاح يحدده صاحب الإعلان: يوم + من/إلى. المشتري يختار منه
/// بدل إدخال وقت حر. يُحجز (IsBooked) عند تأكيد طلب معاينة عليه.
/// </summary>
public class ViewingSlot
{
    public int Id { get; set; }

    public int ListingId { get; set; }
    public Listing? Listing { get; set; }

    /// <summary>اليوم (التاريخ فقط).</summary>
    public DateTime SlotDate { get; set; }

    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }

    public bool IsBooked { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>حالة رسالة واتساب في قائمة الانتظار: بانتظار الإرسال أو أُرسلت.</summary>
public enum WhatsAppStatus
{
    Pending,
    Sent
}

/// <summary>
/// رسالة واتساب في قائمة الانتظار: تُنشأ عند مطابقة إعلان جديد لبحث محفوظ
/// أو طلب عقار لمستخدم فعّل تنبيهات واتساب. الإرسال التلقائي يحتاج حساب
/// WhatsApp Business API (مثل Twilio/UltraMsg)؛ حتى ذلك الحين يرسلها
/// الأدمن يدويًا من لوحة الإدارة عبر رابط wa.me الجاهز بكل رسالة.
/// </summary>
public class WhatsAppOutbox
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser? User { get; set; }

    [MaxLength(20)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Url { get; set; }

    public WhatsAppStatus Status { get; set; } = WhatsAppStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

/// <summary>
/// عنوان IP محظور: يرفض الوسيط كل طلباته بـ 403. الحظر اليدوي بلا نهاية
/// (BlockedUntil = null)؛ الحظر التلقائي ينتهي بعد ساعة. العناوين الموثوقة
/// (دخول أدمن ناجح) لا تُحظر تلقائيًا أبدًا حتى لا يقفل الأدمن على نفسه.
/// </summary>
public class BlockedIp
{
    public int Id { get; set; }

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Reason { get; set; }

    /// <summary>حظر تلقائي من كاشف التخمين، أم يدوي من الأدمن.</summary>
    public bool IsAuto { get; set; }

    public DateTime BlockedAt { get; set; } = DateTime.UtcNow;

    /// <summary>نهاية الحظر؛ null تعني حظرًا دائمًا حتى يرفعه الأدمن.</summary>
    public DateTime? BlockedUntil { get; set; }
}

/// <summary>
/// عنوان IP موثوق: سجّل دخول أدمن ناجح منه. الحظر التلقائي يتخطاه،
/// حتى لو تكررت محاولات فاشلة من نفس العنوان (الأدمن نفسه يغلط أحيانًا).
/// </summary>
public class TrustedIpAddress
{
    public int Id { get; set; }

    [MaxLength(45)]
    public string IpAddress { get; set; } = string.Empty;

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
