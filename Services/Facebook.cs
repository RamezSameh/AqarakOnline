using AqarakOnline.Data;
using AqarakOnline.Models;
using Microsoft.EntityFrameworkCore;

namespace AqarakOnline.Services;

/// <summary>
/// النشر التلقائي للإعلانات المعتمدة على صفحة فيسبوك الموقع عبر Graph API.
/// يعمل فقط عند ضبط مفاتيح SiteSetting: Facebook:AutoPost=true +
/// Facebook:PageId + Facebook:PageAccessToken (توكن صفحة طويل الأمد من
/// Meta Developers). بدونها يرجع false بهدوء ولا يكسر الموافقة على الإعلان.
/// </summary>
public class FacebookService
{
    private readonly ApplicationDbContext _context;
    private readonly HttpClient _http;

    public FacebookService(ApplicationDbContext context, HttpClient http)
    {
        _context = context;
        _http = http;
    }

    public async Task<bool> PostListingAsync(Listing listing, string baseUrl)
    {
        var settings = await _context.SiteSettings
            .Where(s => s.Key.StartsWith("Facebook:"))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        if (!settings.TryGetValue("Facebook:AutoPost", out var auto) || auto != "true")
        {
            return false;
        }
        if (!settings.TryGetValue("Facebook:PageId", out var pageId) || string.IsNullOrWhiteSpace(pageId)
            || !settings.TryGetValue("Facebook:PageAccessToken", out var token) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }
        try
        {
            var typeText = listing.ListingType == ListingType.Sale ? "للبيع" : "للإيجار";
            var message = $"🏠 {listing.Title} {typeText} — {listing.Area}\n💰 {listing.Price:N0} جنيه • {listing.AreaSqm} م²\nرقم الإعلان: {listing.AdNumber}\nللتواصل والمعاينة عن طريق إدارة الموقع.";
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["message"] = message,
                ["link"] = $"{baseUrl.TrimEnd('/')}/Listings/Details/{listing.Id}",
                ["access_token"] = token
            });
            var response = await _http.PostAsync($"https://graph.facebook.com/v21.0/{pageId}/feed", form);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
