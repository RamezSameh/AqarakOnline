using AqarakOnline.Data;
using AqarakOnline.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AqarakOnline.Services;

public class ContactSettings
{
    /// <summary>رقم الهاتف المعروض في صفحة التواصل. غيّره في appsettings.json.</summary>
    public string Phone { get; set; } = "+200000000000";

    /// <summary>رقم واتساب الإدارة. غيّره في appsettings.json.</summary>
    public string WhatsApp { get; set; } = "+200000000000";

    /// <summary>إيميل التواصل المعروض في صفحة التواصل. غيّره في appsettings.json.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>رقم واتساب بدون + أو مسافات، جاهز لرابط wa.me.</summary>
    public string WhatsAppDigits =>
        new string((WhatsApp ?? string.Empty).Where(char.IsDigit).ToArray());
}

/// <summary>
/// ترجمة واجهة بسيطة قائمة على قاموس، من غير ملفات resx.
/// الثقافة الافتراضية عربي، والإنجليزي اختياري عبر كوكي aqarak-lang.
/// نصوص الإعلانات نفسها لا تُترجم، لأنها بيانات يدخلها المستخدم.
/// </summary>
public class LanguageService
{
    public string Culture { get; private set; } = "ar";
    public bool IsArabic => Culture == "ar";

    public void SetCulture(string? culture)
    {
        Culture = culture == "en" ? "en" : "ar";
    }

    private static readonly Dictionary<string, (string Ar, string En)> Texts = new()
    {
        ["Brand"] = ("عقارك أونلاين", "Aqarak Online"),
        ["Tagline"] = ("بيع واشتري عقارك أونلاين بسهولة", "Buy and sell your property online, easily"),
        ["Home"] = ("الرئيسية", "Home"),
        ["Listings"] = ("الإعلانات", "Listings"),
        ["MyListings"] = ("إعلاناتي", "My listings"),
        ["CreateListing"] = ("أضف إعلان", "Add listing"),
        ["Favorites"] = ("المفضلة", "Favorites"),
        ["NoFavorites"] = ("لا توجد عقارات في المفضلة بعد.", "No favorites yet."),
        ["Notifications"] = ("الإشعارات", "Notifications"),
        ["NoNotifications"] = ("لا توجد إشعارات بعد.", "No notifications yet."),
        ["MarkAllAsRead"] = ("تحديد الكل كمقروء", "Mark all as read"),
        ["MarkAsRead"] = ("تحديد كمقروء", "Mark as read"),
        ["AdminRequests"] = ("طلبات التواصل", "Contact requests"),
        ["AdminListings"] = ("كل الإعلانات", "All listings"),
        ["Login"] = ("تسجيل الدخول", "Log in"),
        ["Register"] = ("إنشاء حساب", "Register"),
        ["Logout"] = ("تسجيل الخروج", "Log out"),
        ["Search"] = ("بحث", "Search"),
        ["SearchPlaceholder"] = ("ابحث بالعنوان أو المنطقة...", "Search by title or area..."),
        ["All"] = ("الكل", "All"),
        ["ForSale"] = ("للبيع", "For sale"),
        ["ForRent"] = ("للإيجار", "For rent"),
        ["Available"] = ("متاح", "Available"),
        ["NotAvailable"] = ("غير متاح", "Not available"),
        ["Price"] = ("السعر المبدئي", "Starting price"),
        ["PerMonth"] = ("شهريًا", "per month"),
        ["Currency"] = ("جنيه", "EGP"),
        ["Area"] = ("المنطقة", "Area"),
        ["Details"] = ("التفاصيل", "Details"),
        ["AdditionalDetails"] = ("تفاصيل إضافية", "Additional details"),
        ["Features"] = ("المميزات", "Features"),
        ["AdNumber"] = ("رقم الإعلان", "Ad number"),
        ["PropertyType"] = ("النوع", "Type"),
        ["ListingType"] = ("للبيع أو للإيجار", "Sale or rent"),
        ["Title"] = ("عنوان الإعلان", "Listing title"),
        ["Photo"] = ("صورة (اختياري)", "Photo (optional)"),
        ["NoPhoto"] = ("لا توجد صورة", "No photo"),
        ["ContactUs"] = ("تواصل معنا", "Contact us"),
        ["Buy"] = ("اشتري", "Buy"),
        ["RentIt"] = ("استأجر", "Rent"),
        ["ViewDetails"] = ("عرض التفاصيل", "View details"),
        ["Edit"] = ("تعديل", "Edit"),
        ["Delete"] = ("حذف", "Delete"),
        ["Save"] = ("حفظ", "Save"),
        ["Cancel"] = ("إلغاء", "Cancel"),
        ["BackToList"] = ("العودة للقائمة", "Back to list"),
        ["BuyerName"] = ("اسم المشتري", "Buyer name"),
        ["BuyerPhone"] = ("رقم هاتف المشتري", "Buyer phone"),
        ["Message"] = ("رسالتك", "Your message"),
        ["SendRequest"] = ("إرسال الطلب", "Send request"),
        ["Phone"] = ("رقم الهاتف", "Phone number"),
        ["WhatsApp"] = ("واتساب", "WhatsApp"),
        ["OpenWhatsApp"] = ("فتح محادثة واتساب", "Open WhatsApp chat"),
        ["RequestSent"] = ("تم إرسال طلبك بنجاح", "Your request was sent successfully"),
        ["RequestSaved"] = ("تم حفظ طلبك وسيتابع الأدمن الرد عليك.", "Your request was saved and the admin will follow up with you."),
        ["MarkReplied"] = ("تم الرد", "Mark as replied"),
        ["Replied"] = ("تم الرد", "Replied"),
        ["New"] = ("جديد", "New"),
        ["Buyer"] = ("المشتري", "Buyer"),
        ["Date"] = ("التاريخ", "Date"),
        ["Intent"] = ("الغرض", "Intent"),
        ["Inquiry"] = ("استفسار", "Inquiry"),
        ["BuyIntent"] = ("شراء", "Buy"),
        ["RentIntent"] = ("استئجار", "Rent"),
        ["LoginRequired"] = ("يجب تسجيل الدخول أولًا للشراء أو الاستئجار أو إضافة إعلان.", "You must log in first to buy, rent, or add a listing."),
        ["Owner"] = ("المعلن", "Advertiser"),
        ["SiteAdmin"] = ("إدارة الموقع", "Site admin"),
        ["LatestListings"] = ("أحدث الإعلانات", "Latest listings"),
        ["SeeAll"] = ("عرض كل الإعلانات", "See all listings"),
        ["MinPrice"] = ("أقل سعر", "Min price"),
        ["MaxPrice"] = ("أعلى سعر", "Max price"),
        ["OnlyAvailable"] = ("المتاح فقط", "Available only"),
        ["Filter"] = ("فلترة", "Filter"),
        ["ClearFilter"] = ("مسح الفلترة", "Clear filters"),
        ["NoResults"] = ("لا توجد إعلانات مطابقة.", "No matching listings."),
        ["SortBy"] = ("ترتيب حسب", "Sort by"),
        ["SortNewest"] = ("الأحدث", "Newest"),
        ["SortPriceAsc"] = ("السعر: من الأقل", "Price: low to high"),
        ["SortPriceDesc"] = ("السعر: من الأعلى", "Price: high to low"),
        ["SortAreaDesc"] = ("المساحة: الأكبر", "Area: largest"),
        ["SortViewsDesc"] = ("الأكثر مشاهدة", "Most viewed"),
        ["SortAreaAsc"] = ("المنطقة (أ-ي)", "Area (A-Z)"),
        ["SortAreaDesc"] = ("المنطقة (ي-أ)", "Area (Z-A)"),
        ["SortPricePerSqmAsc"] = ("سعر المتر: من الأقل", "Price per sqm: low to high"),
        ["SortPricePerSqmDesc"] = ("سعر المتر: من الأعلى", "Price per sqm: high to low"),
        ["SearchByAdNumber"] = ("ابحث برقم الإعلان (مثال: AQ-0001)", "Search by ad number (e.g. AQ-0001)"),
        ["DirectOwnerOnly"] = ("مالك مباشر فقط", "Direct owner only"),
        ["Recency"] = ("الإعلانات الجديدة", "New listings"),
        ["RecencyAny"] = ("أي وقت", "Any time"),
        ["RecencyToday"] = ("اليوم", "Today"),
        ["RecencyWeek"] = ("آخر 7 أيام", "Last 7 days"),
        ["RecencyMonth"] = ("آخر 30 يوم", "Last 30 days"),
        ["Finishing"] = ("التشطيب", "Finishing"),
        ["FinishingSuperLux"] = ("سوبر لوكس", "Super lux"),
        ["FinishingLux"] = ("لوكس", "Lux"),
        ["FinishingSemiFinished"] = ("نص تشطيب", "Semi-finished"),
        ["FinishingRedBrick"] = ("على الطوب الأحمر", "Red brick"),
        ["CopyListing"] = ("انسخ الإعلان", "Duplicate listing"),
        ["CallUs"] = ("اتصل بينا", "Call us"),
        ["StatsAvailableListings"] = ("إعلانات متاحة", "Available listings"),
        ["StatsAreas"] = ("منطقة", "Areas"),
        ["ExportListings"] = ("تصدير Excel", "Export Excel"),
        ["MinFloor"] = ("أقل دور", "Min floor"),
        ["MaxFloor"] = ("أعلى دور", "Max floor"),
        ["Parking"] = ("جراج", "Parking"),
        ["Elevator"] = ("أسانسير", "Elevator"),
        ["Balcony"] = ("بلكونة", "Balcony"),
        ["AirConditioning"] = ("تكييف", "Air conditioning"),
        ["Security"] = ("أمن", "Security"),
        ["Amenities"] = ("مزايا", "Amenities"),
        ["OtherAmenities"] = ("مزايا أخرى", "Other amenities"),
        ["AcceptsInstallments"] = ("يقبل التقسيط", "Accepts installments"),
        ["InstallmentsOnly"] = ("يقبل التقسيط فقط", "Installments only"),
        ["PreviewListing"] = ("معاينة الإعلان", "Preview listing"),
        ["LastUpdated"] = ("آخر تحديث", "Last updated"),
        ["AuditLog"] = ("سجل التعديلات", "Change log"),
        ["Negotiable"] = ("قابل للتفاوض", "Negotiable"),
        ["PriceOptions"] = ("خيارات السعر", "Price options"),
        ["AmenitiesHint"] = ("دي المزايا الأساسية اللي الزوار يقدروا يفلتروا بيها في البحث.", "These are the core amenities visitors can filter by in search."),
        ["FeaturesHint"] = ("دي صفات وصفية للوحدة، بتظهر في صفحة التفاصيل بس ومش بتتفلتر في البحث.", "These are descriptive traits of the unit, shown on the Details page only, not filterable in search."),
        ["NegotiableOnly"] = ("قابل للتفاوض فقط", "Negotiable only"),
        ["RentPeriod"] = ("مدة الإيجار", "Rent period"),
        ["Monthly"] = ("شهري", "Monthly"),
        ["Yearly"] = ("سنوي", "Yearly"),
        ["DepositMonths"] = ("التأمين (شهور)", "Deposit (months)"),
        ["ElectricityMeter"] = ("عداد كهرباء", "Electricity meter"),
        ["GasMeter"] = ("عداد غاز", "Gas meter"),
        ["WaterMeter"] = ("عداد مياه", "Water meter"),
        ["Meters"] = ("عدادات", "Meters"),
        ["LandArea"] = ("مساحة الأرض", "Land area"),
        ["BuildingFloors"] = ("عدد الأدوار", "Building floors"),
        ["MinBuildingFloors"] = ("أقل عدد أدوار", "Min floors"),
        ["ContactUs"] = ("تواصل معنا", "Contact us"),
        ["ContactMessages"] = ("رسائل التواصل", "Contact messages"),
        ["YourName"] = ("اسمك", "Your name"),
        ["YourPhone"] = ("رقم موبايلك", "Your phone"),
        ["YourMessage"] = ("رسالتك", "Your message"),
        ["SendMessage"] = ("إرسال الرسالة", "Send message"),
        ["MessageSent"] = ("تم إرسال رسالتك، وسيتواصل معك فريق الموقع.", "Your message was sent, and the site team will contact you."),
        ["MarkAsRead"] = ("تمت القراءة", "Mark as read"),
        ["SellGuide"] = ("كيف تبيع/تأجر معنا", "How to sell/rent with us"),
        ["SortOldest"] = ("الأقدم", "Oldest"),
        ["DownloadBackup"] = ("تحميل نسخة احتياطية", "Download backup"),
        ["MostViewed"] = ("الأكثر مشاهدة", "Most viewed"),
        ["CustomFeature"] = ("ميزة مش موجودة في الاختيارات", "A feature not in the list"),
        ["Status"] = ("الحالة", "Status"),
        ["StatusAll"] = ("الكل", "All"),
        ["StatusAvailable"] = ("متاح", "Available"),
        ["StatusUnavailable"] = ("غير متاح", "Unavailable"),
        ["StatusPending"] = ("معلّق", "Pending"),
        ["Furnishing"] = ("مستوى الفرش", "Furnishing level"),
        ["FurnishingFull"] = ("مفروش بالكامل", "Fully furnished"),
        ["FurnishingPartial"] = ("مفروش جزئيًا", "Partly furnished"),
        ["FurnishingNone"] = ("غير مفروش", "Unfurnished"),
        ["FurnishingAny"] = ("مفروش (أي مستوى)", "Furnished (any level)"),
        ["SaleReason"] = ("سبب البيع", "Reason for selling"),
        ["BackToTop"] = ("العودة للأعلى", "Back to top"),
        ["PriceReduced"] = ("تم تخفيض السعر", "Price reduced"),
        ["OriginalPrice"] = ("السعر قبل التخفيض", "Price before reduction"),
        ["ApartmentsCount"] = ("عدد الشقق في العمارة", "Apartments in building"),
        ["ElevatorCount"] = ("عدد المصاعد", "Number of elevators"),
        ["FeaturedUntil"] = ("مميز حتى", "Featured until"),
        ["AdvertiserOwner"] = ("مالك", "Owner"),
        ["AdvertiserBroker"] = ("وسيط", "Broker"),
        ["MostReported"] = ("الأكثر بلاغًا", "Most reported"),
        ["ClearAllFavorites"] = ("حذف كل المفضلة", "Delete all favorites"),
        ["PopularAreas"] = ("مناطق شائعة", "Popular areas"),
        ["TrustedAdvertiser"] = ("معلن موثوق", "Trusted advertiser"),
        ["AvgPricePerSqmByArea"] = ("متوسط سعر المتر حسب المنطقة", "Average price per sqm by area"),
        ["AvgPricePerSqm"] = ("متوسط سعر المتر", "Avg price per sqm"),
        ["ListingsCount"] = ("عدد الإعلانات", "Listings count"),
        ["ListingsByPropertyType"] = ("الإعلانات حسب نوع العقار", "Listings by property type"),
        ["ListingsByListingType"] = ("الإعلانات حسب الغرض", "Listings by purpose"),
        ["ListingsByAdvertiserType"] = ("الإعلانات حسب نوع المعلن", "Listings by advertiser type"),
        ["UsersByStatus"] = ("المستخدمون حسب الحالة", "Users by status"),
        ["PriceGuide"] = ("كيف تقيّم سعر عقارك", "How to price your property"),
        ["BuyingMistakes"] = ("أخطاء شائعة عند الشراء", "Common buying mistakes"),
        ["AgreeToTerms"] = ("أوافق على", "I agree to the"),
        ["DeleteAllNotifications"] = ("حذف كل التنبيهات", "Delete all notifications"),
        ["SetAsCover"] = ("تعيين كغلاف", "Set as cover"),
        ["Cover"] = ("غلاف", "Cover"),
        ["MaintenanceMode"] = ("وضع الصيانة", "Maintenance mode"),
        ["MaintenanceMessage"] = ("رسالة الصيانة", "Maintenance message"),
        ["TopPosters"] = ("أكثر المستخدمين إضافة إعلانات", "Top posters"),
        ["ImportListings"] = ("استيراد إعلانات", "Import listings"),
        ["ImportTemplate"] = ("تحميل القالب", "Download template"),
        ["New"] = ("جديد", "New"),
        ["SortRoomsDesc"] = ("عدد الغرف (الأكثر أولًا)", "Most rooms first"),
        ["SortRoomsAsc"] = ("عدد الغرف (الأقل أولًا)", "Fewest rooms first"),
        ["TopSearchTerms"] = ("أكثر كلمات البحث — آخر 30 يومًا", "Top search terms — last 30 days"),
        ["TopContactedListings"] = ("أكثر الإعلانات طلب تواصل", "Most-contacted listings"),
        ["BlockedPhones"] = ("أرقام محظورة", "Blocked phones"),
        ["ResetPassword"] = ("إعادة تعيين كلمة المرور", "Reset password"),
        ["StopListing"] = ("إيقاف الإعلان", "Stop listing"),
        ["StartListing"] = ("تشغيل الإعلان", "Start listing"),
        ["Completeness"] = ("اكتمال بيانات الإعلان", "Listing completeness"),
        ["SuggestedPrice"] = ("سعر مقترح", "Suggested price"),
        ["TermsOfUse"] = ("شروط الاستخدام", "Terms of Use"),
        ["PrivacyPolicy"] = ("سياسة الخصوصية", "Privacy Policy"),
        ["Tips"] = ("نصائح عقارية", "Real estate tips"),
        ["MyAccount"] = ("حسابي", "My account"),
        ["Urgent"] = ("عاجل", "Urgent"),
        ["RejectionReason"] = ("سبب الرفض", "Rejection reason"),
        ["RequestReview"] = ("طلب مراجعة الحساب", "Request account review"),
        ["AccountReviewRequested"] = ("تم إرسال طلب المراجعة للإدارة.", "Your review request was sent to the admin."),
        ["MinArea"] = ("أقل مساحة (م²)", "Min area (sqm)"),
        ["MaxArea"] = ("أكبر مساحة (م²)", "Max area (sqm)"),
        ["ResultsCount"] = ("عدد النتائج", "Results"),
        ["Previous"] = ("السابق", "Previous"),
        ["Next"] = ("التالي", "Next"),
        ["Filters"] = ("الفلاتر", "Filters"),
        ["TryClearFilters"] = ("جرّب توسيع البحث أو مسح الفلاتر.", "Try widening your search or clearing the filters."),
        ["MarkUnavailable"] = ("إغلاق الإعلان (تم البيع / التأجير)", "Close listing (sold / rented)"),
        ["FullName"] = ("الاسم الكامل", "Full name"),
        ["PhoneNumber"] = ("رقم الموبايل", "Mobile number"),
        ["Address"] = ("العنوان", "Address"),
        ["VerifyPhone"] = ("تأكيد رقم الموبايل", "Verify mobile number"),
        ["VerificationCode"] = ("كود التأكيد", "Verification code"),
        ["Confirm"] = ("تأكيد", "Confirm"),
        ["ResendCode"] = ("إرسال كود جديد", "Send a new code"),
        ["PendingApproval"] = ("بانتظار موافقة الأدمن", "Pending admin approval"),
        ["Approved"] = ("مقبول ومنشور", "Approved and published"),
        ["Rejected"] = ("مرفوض", "Rejected"),
        ["Approve"] = ("موافقة", "Approve"),
        ["Reject"] = ("رفض", "Reject"),
        ["ApprovalStatus"] = ("حالة النشر", "Publication status"),
        ["PendingReviewNote"] = ("تم إرسال إعلانك للمراجعة، وسيظهر للعامة بعد موافقة الأدمن.", "Your listing was sent for review and will appear publicly after admin approval."),
        ["AdvertiserData"] = ("بيانات المعلن (للأدمن فقط)", "Advertiser data (admin only)"),
        ["AreaSqm"] = ("المساحة (م²)", "Area (sqm)"),
        ["Views"] = ("عدد المشاهدات", "Views"),
        ["MarkAvailable"] = ("إعادة إتاحة الإعلان", "Make listing available again"),
        ["AboutUs"] = ("من نحن", "About us"),
        ["AboutText"] = ("إحنا منصة إعلانات عقارية هدفها تبسيط البيع والإيجار: تنشر إعلانك في دقايق، والمشتري يلاقي اللي بيدور عليه بسهولة، وإدارة الموقع هي اللي بتنسق بينكم من أول طلب التواصل لحد المعاينة", "We're a real estate ads platform that makes buying, selling, and renting simple: publish your ad in minutes, buyers find what they're looking for easily, and the site's management coordinates everything between you — from the first contact request to the viewing."),
        ["WhyChooseUs"] = ("ليه تختارنا؟", "Why choose us?"),
        ["WhyFastTitle"] = ("أسرع طريقة للبيع والشراء والإيجار", "The fastest way to sell, buy, and rent"),
        ["WhyFastText"] = ("اعرض عقارك أو ابحث عن عقارك في دقائق، وتابع طلبات التواصل أولًا بأول.", "List your property or find your property in minutes, and follow up on contact requests right away."),
        ["WhyFollowTitle"] = ("تابع بنفسك واختر الأفضل", "Follow up yourself and choose the best"),
        ["WhyFollowText"] = ("أنت اللي بتتابع إعلانك وطلباتك وتختار العرض الأنسب لك، من غير ما حد يختار مكانك.", "You follow your own listing and requests and choose the offer that suits you best, without anyone choosing for you."),
        ["WhyCommissionTitle"] = ("عمولة على قد إيدك", "A commission you can afford"),
        ["WhyCommissionText"] = ("عمولة مناسبة من غير مبالغة، لأن هدفنا إن الصفقة تتم بسهولة للطرفين.", "A reasonable commission without exaggeration, because our goal is for the deal to close easily for both sides."),
        ["WhyNoExploitationTitle"] = ("كل حاجة واضحة من الأول", "Everything is clear from the start"),
        ["WhyNoExploitationText"] = ("السعر والمساحة والتفاصيل ظاهرة من الأول، من غير مفاجآت بعد كده.", "Price, area and details are clear from the start, with no surprises later."),
        ["AdvertiserType"] = ("نوع المعلن", "Advertiser type"),
        ["FromOwner"] = ("من المالك", "From owner"),
        ["FromBroker"] = ("من سمسار", "From broker"),
        ["Rooms"] = ("عدد الغرف", "Rooms"),
        ["Bathrooms"] = ("عدد الحمامات", "Bathrooms"),
        ["Floor"] = ("الدور", "Floor"),
        ["Furnished"] = ("مفروش", "Furnished"),
        ["NotFurnished"] = ("غير مفروش", "Unfurnished"),
        ["MinRooms"] = ("أقل عدد غرف", "Min rooms"),
        ["FurnishedOnly"] = ("مفروش فقط", "Furnished only"),
        ["FloorRequiredForApartment"] = ("الدور مطلوب عند إضافة شقة.", "Floor is required for an apartment."),
        ["UploadingListing"] = ("جاري رفع الإعلان...", "Uploading listing..."),
        ["OwnListingError"] = ("لا يمكنك شراء أو استئجار إعلانك الخاص.", "You cannot buy or rent your own listing."),
        ["OwnListingNote"] = ("هذا إعلانك، فلا يمكنك شراؤه أو استئجاره.", "This is your listing, so you cannot buy or rent it."),
        ["SearchUsersPlaceholder"] = ("ابحث بالاسم أو رقم الموبايل...", "Search by name or phone..."),
        ["Close"] = ("إغلاق", "Close"),
        ["SimilarListings"] = ("إعلانات مشابهة", "Similar listings"),
        ["Share"] = ("مشاركة", "Share"),
        ["CopyLink"] = ("نسخ الرابط", "Copy link"),
        ["Copied"] = ("تم النسخ ✓", "Copied ✓"),
        ["Calculator"] = ("حاسبة القسط", "Installment calculator"),
        ["DownPayment"] = ("المقدمة", "Down payment"),
        ["Years"] = ("عدد السنين", "Years"),
        ["InterestRate"] = ("الفائدة السنوية %", "Annual interest %"),
        ["MonthlyInstallment"] = ("القسط الشهري", "Monthly installment"),
        ["CalculatorNote"] = ("النتيجة تقريبية، واسأل البنك عن الشروط الفعلية.", "Result is approximate; ask the bank for the actual terms."),
        ["PublishedAt"] = ("تاريخ النشر", "Published"),
        ["Featured"] = ("مميز", "Featured"),
        ["MarkFeatured"] = ("تثبيت كمميز", "Mark as featured"),
        ["UnmarkFeatured"] = ("إلغاء التمييز", "Remove featured"),
        ["Compare"] = ("مقارنة", "Compare"),
        ["AddToCompare"] = ("أضف للمقارنة", "Add to compare"),
        ["InCompare"] = ("✓ في المقارنة", "✓ In compare"),
        ["CompareNow"] = ("قارن الآن", "Compare now"),
        ["ClearCompare"] = ("مسح", "Clear"),
        ["CompareEmpty"] = ("اختار إعلانين على الأقل للمقارنة من الكروت أو صفحة التفاصيل.", "Pick at least two listings to compare from the cards or the details page."),
        ["CompareMax"] = ("أقصى عدد للمقارنة 3 إعلانات", "Compare max is 3 listings"),
        ["CompareTitle"] = ("مقارنة الإعلانات", "Compare listings"),
        ["RemoveFromCompare"] = ("إزالة", "Remove"),
        ["SavedSearches"] = ("بحثي المحفوظ", "My saved searches"),
        ["SaveSearch"] = ("حفظ هذا البحث", "Save this search"),
        ["RunSearch"] = ("عرض النتائج", "Show results"),
        ["MatchingNow"] = ("مطابق الآن", "matching now"),
        ["NoSavedSearches"] = ("مفيش بحث محفوظ بعد — من صفحة الإعلانات دوس «حفظ هذا البحث».", "No saved searches yet — from the listings page, tap “Save this search”."),
        ["Comment"] = ("تعليق", "Comment"),
        ["RenewListing"] = ("جدّد الإعلان", "Renew listing"),
        ["Renewed"] = ("تم تجديد الإعلان — ظهر أول النتائج.", "Listing renewed — now at the top of results."),
        ["RenewCooldown"] = ("تقدر تجدّد الإعلان مرة كل 7 أيام.", "You can renew a listing once every 7 days."),
        ["ReportListing"] = ("الإبلاغ عن الإعلان", "Report this listing"),
        ["ReportReason"] = ("السبب", "Reason"),
        ["ReportDetails"] = ("تفاصيل إضافية", "Additional details"),
        ["ReportSubmitted"] = ("تم إرسال البلاغ — هنراجعه في أقرب وقت.", "Report submitted — we'll review it soon."),
        ["ReportAlreadySent"] = ("بلّغت عن الإعلان ده قبل كده، ولسه قيد المراجعة.", "You already reported this listing; it's still under review."),
        ["ReportReasonMisleading"] = ("إعلان مضلل", "Misleading listing"),
        ["ReportReasonWrongInfo"] = ("معلومات غير صحيحة", "Incorrect information"),
        ["ReportReasonScam"] = ("احتيال أو نصب", "Scam or fraud"),
        ["ReportReasonOffensive"] = ("محتوى مسيء", "Offensive content"),
        ["ReportReasonOther"] = ("سبب آخر", "Other reason"),
        ["Reports"] = ("البلاغات", "Reports"),
        ["MarkReviewed"] = ("تمت المراجعة", "Reviewed"),
        ["ReportPending"] = ("قيد المراجعة", "Under review"),
        ["ReportReviewed"] = ("تمت المراجعة", "Reviewed"),
        ["NoReports"] = ("لا توجد بلاغات.", "No reports."),
        ["BookViewing"] = ("احجز معاينة", "Book a viewing"),
        ["ViewingDate"] = ("ميعاد المعاينة", "Viewing date & time"),
        ["ViewingMessage"] = ("رسالة للمعلن (اختياري)", "Message for the advertiser (optional)"),
        ["ViewingRequested"] = ("تم إرسال طلب المعاينة — المعلن هيأكده.", "Viewing request sent — the advertiser will confirm it."),
        ["ViewingDatePast"] = ("ميعاد المعاينة لازم يكون في المستقبل.", "The viewing date must be in the future."),
        ["Viewings"] = ("مواعيد المعاينة", "Viewing appointments"),
        ["MyViewingRequests"] = ("طلباتي", "My requests"),
        ["ReceivedViewingRequests"] = ("طلبات وصلتني", "Requests received"),
        ["ViewingPending"] = ("بانتظار التأكيد", "Awaiting confirmation"),
        ["ViewingConfirmed"] = ("مؤكدة", "Confirmed"),
        ["ViewingDeclined"] = ("مرفوضة", "Declined"),
        ["ViewingCancelled"] = ("ملغاة", "Cancelled"),
        ["Confirm"] = ("تأكيد", "Confirm"),
        ["Decline"] = ("رفض", "Decline"),
        ["NoViewings"] = ("لا توجد مواعيد معاينة.", "No viewing appointments."),
        ["DropPhotosHere"] = ("اسحب الصور هنا أو دوس للاختيار", "Drag photos here or tap to choose"),
        ["CurrentPhotos"] = ("الصور الحالية — اسحب لإعادة ترتيبها", "Current photos — drag to reorder"),
        ["Email"] = ("البريد الإلكتروني", "Email"),
        ["Password"] = ("كلمة المرور", "Password"),
        ["ConfirmPassword"] = ("تأكيد كلمة المرور", "Confirm password"),
        ["RememberMe"] = ("تذكرني", "Remember me"),
        ["NoAccount"] = ("ليس لديك حساب؟ أنشئ حسابًا", "Don't have an account? Register"),
        ["HasAccount"] = ("لديك حساب؟ سجّل الدخول", "Already have an account? Log in"),
        ["DeleteConfirm"] = ("هل أنت متأكد من حذف هذا الإعلان؟", "Are you sure you want to delete this listing?"),
    };

    public string T(string key)
    {
        if (Texts.TryGetValue(key, out var value))
        {
            return IsArabic ? value.Ar : value.En;
        }
        return key;
    }

    public string ListingTypeText(Models.ListingType type) =>
        type == Models.ListingType.Sale ? T("ForSale") : T("ForRent");

    public string PropertyTypeText(Models.PropertyType type) => type switch
    {
        Models.PropertyType.Apartment => IsArabic ? "شقة" : "Apartment",
        Models.PropertyType.House => IsArabic ? "بيت" : "House",
        Models.PropertyType.Building => IsArabic ? "عمارة" : "Building",
        Models.PropertyType.Shop => IsArabic ? "محل" : "Shop",
        Models.PropertyType.Villa => IsArabic ? "فيلا" : "Villa",
        _ => type.ToString()
    };

    public string ApprovalText(Models.ListingApproval approval) => approval switch
    {
        Models.ListingApproval.Approved => T("Approved"),
        Models.ListingApproval.Rejected => T("Rejected"),
        _ => T("PendingApproval")
    };

    public string AdvertiserTypeText(Models.AdvertiserType type) =>
        type == Models.AdvertiserType.Broker ? T("FromBroker") : T("FromOwner");

    public string FinishingText(Models.FinishingLevel level) => level switch
    {
        Models.FinishingLevel.SuperLux => T("FinishingSuperLux"),
        Models.FinishingLevel.Lux => T("FinishingLux"),
        Models.FinishingLevel.SemiFinished => T("FinishingSemiFinished"),
        _ => T("FinishingRedBrick")
    };

    public string FurnishingText(Models.FurnishingLevel level) => level switch
    {
        Models.FurnishingLevel.Full => T("FurnishingFull"),
        Models.FurnishingLevel.Partial => T("FurnishingPartial"),
        _ => T("FurnishingNone")
    };

    public string ReportReasonText(Models.ReportReason reason) => reason switch
    {
        Models.ReportReason.Misleading => T("ReportReasonMisleading"),
        Models.ReportReason.WrongInfo => T("ReportReasonWrongInfo"),
        Models.ReportReason.Scam => T("ReportReasonScam"),
        Models.ReportReason.Offensive => T("ReportReasonOffensive"),
        _ => T("ReportReasonOther")
    };

    public string ViewingStatusText(Models.ViewingStatus status) => status switch
    {
        Models.ViewingStatus.Confirmed => T("ViewingConfirmed"),
        Models.ViewingStatus.Declined => T("ViewingDeclined"),
        Models.ViewingStatus.Cancelled => T("ViewingCancelled"),
        _ => T("ViewingPending")
    };

    /// <summary>تاريخ نسبي: «منذ يومين» / «منذ 3 ساعات». التاريخ المخزّن UTC.</summary>
    public string RelativeTime(DateTime utc)
    {
        var diff = DateTime.UtcNow - utc;
        if (diff.TotalMinutes < 1)
        {
            return IsArabic ? "الآن" : "Just now";
        }
        if (diff.TotalHours < 1)
        {
            return FormatUnit((int)diff.TotalMinutes,
                ("دقيقة", "دقيقتين", "دقائق", "دقيقة"),
                ("minute", "minutes"));
        }
        if (diff.TotalDays < 1)
        {
            return FormatUnit((int)diff.TotalHours,
                ("ساعة", "ساعتين", "ساعات", "ساعة"),
                ("hour", "hours"));
        }
        if (diff.TotalDays < 7)
        {
            return FormatUnit((int)diff.TotalDays,
                ("يوم", "يومين", "أيام", "يوم"),
                ("day", "days"));
        }
        if (diff.TotalDays < 30)
        {
            return FormatUnit((int)(diff.TotalDays / 7),
                ("أسبوع", "أسبوعين", "أسابيع", "أسبوع"),
                ("week", "weeks"));
        }
        if (diff.TotalDays < 365)
        {
            return FormatUnit((int)(diff.TotalDays / 30),
                ("شهر", "شهرين", "أشهر", "شهر"),
                ("month", "months"));
        }
        return FormatUnit((int)(diff.TotalDays / 365),
            ("سنة", "سنتين", "سنوات", "سنة"),
            ("year", "years"));
    }

    private string FormatUnit(int value, (string One, string Two, string Few, string Many) ar, (string One, string Many) en)
    {
        if (IsArabic)
        {
            var word = value switch
            {
                1 => ar.One,
                2 => ar.Two,
                >= 3 and <= 10 => ar.Few,
                _ => ar.Many
            };
            var number = value is 1 or 2 ? string.Empty : $"{value} ";
            return $"منذ {number}{word}";
        }
        return value == 1 ? $"1 {en.One} ago" : $"{value} {en.Many} ago";
    }

    public string IntentText(Models.ContactIntent intent) => intent switch
    {
        Models.ContactIntent.Buy => T("BuyIntent"),
        Models.ContactIntent.Rent => T("RentIntent"),
        _ => T("Inquiry")
    };

    public string PriceText(decimal price, Models.ListingType type)
    {
        var formatted = price.ToString("N0");
        var suffix = type == Models.ListingType.Rent ? $" {T("PerMonth")}" : string.Empty;
        return $"{formatted} {T("Currency")}{suffix}";
    }
}

/// <summary>إعدادات عامة للموقع مخزنة في قاعدة البيانات (مفتاح/قيمة) — مثل وضع الصيانة.</summary>
public static class SiteSettingsHelper
{
    public const string MaintenanceEnabledKey = "MaintenanceEnabled";
    public const string MaintenanceMessageKey = "MaintenanceMessage";
    public const string DefaultMaintenanceMessage = "الموقع تحت الصيانة حاليًا، ونعود قريبًا.";

    public static async Task<bool> IsMaintenanceEnabledAsync(ApplicationDbContext context)
    {
        var setting = await context.SiteSettings.FindAsync(MaintenanceEnabledKey);
        return setting?.Value == "true";
    }

    public static async Task<string> GetMaintenanceMessageAsync(ApplicationDbContext context)
    {
        var setting = await context.SiteSettings.FindAsync(MaintenanceMessageKey);
        return string.IsNullOrWhiteSpace(setting?.Value) ? DefaultMaintenanceMessage : setting.Value;
    }

    public static async Task SetAsync(ApplicationDbContext context, string key, string value)
    {
        var setting = await context.SiteSettings.FindAsync(key);
        if (setting == null)
        {
            context.SiteSettings.Add(new SiteSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
        await context.SaveChangesAsync();
    }
}

/// <summary>المعلن الموثوق: صاحب 3 إعلانات مقبولة أو أكثر. تُحسب مرة واحدة لكل طلب (scoped)
/// فلا تضيف استعلامًا مع كل كارت.</summary>
public class TrustedAdvertiserService
{
    /// <summary>عدد الإعلانات المقبولة اللازم لشارة «معلن موثوق».</summary>
    public const int TrustedThreshold = 3;

    private readonly ApplicationDbContext _context;
    private HashSet<string>? _trustedOwnerIds;

    public TrustedAdvertiserService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> IsTrustedAsync(string? ownerId)
    {
        if (string.IsNullOrEmpty(ownerId))
        {
            return false;
        }
        _trustedOwnerIds ??= (await _context.Listings
            .Where(l => l.Approval == ListingApproval.Approved && l.OwnerId != null)
            .GroupBy(l => l.OwnerId)
            .Where(g => g.Count() >= TrustedThreshold)
            .Select(g => g.Key!)
            .ToListAsync()).ToHashSet();
        return _trustedOwnerIds.Contains(ownerId);
    }
}

/// <summary>تذكير بموعد المعاينة قبلها بيوم: خدمة خلفية تفحص كل ساعة المعاينات المؤكدة
/// خلال الـ24 ساعة القادمة وترسل تنبيهًا لطالب المعاينة مرة واحدة (ReminderSent).
/// المعاينة بتوقيت المستخدم المحلي كما أدخله، فالمقارنة بـDateTime.Now زي ما بيعمل
/// ViewingsController عند إنشاء المعاينة.</summary>
public class ViewingReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ViewingReminderService> _logger;

    public ViewingReminderService(IServiceScopeFactory scopeFactory, ILogger<ViewingReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "فشل إرسال تذكيرات المعاينة.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task SendRemindersAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.Now;
        var upcoming = await context.ViewingAppointments
            .Include(v => v.Listing)
            .Where(v => v.Status == ViewingStatus.Confirmed
                && !v.ReminderSent
                && v.RequestedAt > now
                && v.RequestedAt <= now.AddHours(24))
            .ToListAsync(stoppingToken);
        foreach (var viewing in upcoming)
        {
            context.Notifications.Add(new Notification
            {
                Title = "تذكير بموعد المعاينة",
                Message = $"معاينة «{viewing.Listing?.Title}» غدًا {viewing.RequestedAt:dd/MM/yyyy HH:mm} — التنسيق كله بيتم عن طريق الأدمن.",
                TargetUserId = viewing.RequesterId,
                Url = "/Viewings",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });
            viewing.ReminderSent = true;
        }
        if (upcoming.Count > 0)
        {
            await context.SaveChangesAsync(stoppingToken);
        }
    }
}
