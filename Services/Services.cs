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
        ["NotAvailable"] = ("تم البيع / التأجير", "Sold / rented"),
        ["Price"] = ("السعر المبدئي", "Starting price"),
        ["PerMonth"] = ("شهريًا", "per month"),
        ["Currency"] = ("جنيه", "EGP"),
        ["Area"] = ("المنطقة", "Area"),
        ["Details"] = ("التفاصيل", "Details"),
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
        ["MarkUnavailable"] = ("إغلاق الإعلان (تم البيع / التأجير)", "Close listing (sold / rented)"),
        ["FullName"] = ("الاسم الكامل", "Full name"),
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
