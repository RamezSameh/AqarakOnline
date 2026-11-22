using AqarakOnline.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AqarakOnline.Services;

/// <summary>
/// Cloudflare Turnstile (كابتشا بلا ألغاز): ودجت في نماذج التسجيل والدخول
/// والتواصل، والتحقق يتم على السيرفر عبر siteverify. يعمل فقط عند ضبط
/// SiteSetting: Turnstile:Enabled=true + Turnstile:SiteKey + Turnstile:SecretKey
/// (مفاتيح مجانية من dash.cloudflare.com ← Turnstile ← Add Site).
/// بدونها يتخطى التحقق بهدوء (الموقع يفضل شغالًا) — وصفحة الأدمن «الأمان»
/// توضح أنها ناقصة.
/// </summary>
public class TurnstileService
{
    private readonly ApplicationDbContext _context;
    private readonly HttpClient _http;

    public TurnstileService(ApplicationDbContext context, HttpClient http)
    {
        _context = context;
        _http = http;
    }

    public record Config(bool Enabled, string? SiteKey);

    public async Task<Config> GetConfigAsync()
    {
        var settings = await _context.SiteSettings
            .Where(s => s.Key.StartsWith("Turnstile:"))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        var enabled = settings.TryGetValue("Turnstile:Enabled", out var e) && e == "true";
        settings.TryGetValue("Turnstile:SiteKey", out var siteKey);
        var hasSecret = settings.TryGetValue("Turnstile:SecretKey", out var secret)
            && !string.IsNullOrWhiteSpace(secret);
        return new Config(enabled && !string.IsNullOrWhiteSpace(siteKey) && hasSecret, siteKey);
    }

    public async Task<bool> IsEnabledAsync() => (await GetConfigAsync()).Enabled;

    public async Task<bool> VerifyAsync(string? token, string? remoteIp)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }
        var settings = await _context.SiteSettings
            .Where(s => s.Key.StartsWith("Turnstile:"))
            .ToDictionaryAsync(s => s.Key, s => s.Value);
        if (!settings.TryGetValue("Turnstile:SecretKey", out var secret)
            || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }
        try
        {
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["secret"] = secret,
                ["response"] = token,
                ["remoteip"] = remoteIp ?? string.Empty
            });
            var response = await _http.PostAsync(
                "https://challenges.cloudflare.com/turnstile/v0/siteverify", form);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("success", out var ok) && ok.GetBoolean();
        }
        catch
        {
            return false;
        }
    }
}
