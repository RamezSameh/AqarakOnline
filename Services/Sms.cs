using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace AqarakOnline.Services;

/// <summary>تطبيع رقم موبايل مصري إلى الصيغة 01xxxxxxxxx (11 رقمًا).</summary>
public static class PhoneHelper
{
    public static string Normalize(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return string.Empty;
        }
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        // +20 أو 0020 أو 20 ثم 10 أرقام → 0 ثم 10 أرقام.
        if (digits.StartsWith("0020") && digits.Length == 14)
        {
            return "0" + digits[4..];
        }
        if (digits.StartsWith("20") && digits.Length == 12)
        {
            return "0" + digits[2..];
        }
        return digits;
    }

    public static bool IsValidEgyptianMobile(string? phone)
    {
        var normalized = Normalize(phone);
        return normalized.Length == 11
            && normalized.StartsWith("01")
            && normalized.All(char.IsDigit);
    }
}

public static class VerificationCodeHelper
{
    public static string GenerateCode()
    {
        // 6 أرقام عشوائية آمنة.
        return RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
    }

    public static string Hash(string code, string userId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(userId + ":" + code));
        return Convert.ToHexString(bytes);
    }
}

public class SmsSettings
{
    /// <summary>Log (الافتراضي: يكتب الكود في لوج التطبيق للتجربة) أو Twilio.</summary>
    public string Provider { get; set; } = "Log";
    public string TwilioAccountSid { get; set; } = string.Empty;
    public string TwilioAuthToken { get; set; } = string.Empty;
    public string TwilioFromNumber { get; set; } = string.Empty;
}

public interface ISmsSender
{
    Task SendVerificationCodeAsync(string phoneNumber, string code);
}

/// <summary>
/// مزود تجريبي: لا يرسل رسالة حقيقية، بل يكتب الكود في اللوج.
/// هذا هو الافتراضي حتى يضبط مالك المشروع مزود SMS حقيقيًا وبياناته.
/// لا تضع مفاتيح SMS حقيقية في appsettings.json داخل ريبو عام؛
/// استخدم متغيرات البيئة أو user-secrets (راجع README).
/// </summary>
public class LogSmsSender : ISmsSender
{
    private readonly ILogger<LogSmsSender> _logger;

    public LogSmsSender(ILogger<LogSmsSender> logger)
    {
        _logger = logger;
    }

    public Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        _logger.LogWarning(
            "SMS provider is Log (no real SMS sent). Verification code for {Phone} is {Code}. " +
            "Configure Sms:Provider=Twilio with real credentials to send real SMS.",
            phoneNumber, code);
        return Task.CompletedTask;
    }
}

/// <summary>
/// إرسال SMS حقيقي عبر Twilio REST API (من غير حزمة Twilio نفسها).
/// يتطلب: Sms:TwilioAccountSid و Sms:TwilioAuthToken و Sms:TwilioFromNumber.
/// الرقم المرسل إليه يجب أن يكون بالصيغة الدولية (+20...)، فيُحوَّل هنا من 01... إلى +20....
/// </summary>
public class TwilioSmsSender : ISmsSender
{
    private readonly HttpClient _http;
    private readonly SmsSettings _settings;
    private readonly ILogger<TwilioSmsSender> _logger;

    public TwilioSmsSender(HttpClient http, IOptions<SmsSettings> settings, ILogger<TwilioSmsSender> logger)
    {
        _http = http;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendVerificationCodeAsync(string phoneNumber, string code)
    {
        if (string.IsNullOrWhiteSpace(_settings.TwilioAccountSid)
            || string.IsNullOrWhiteSpace(_settings.TwilioAuthToken)
            || string.IsNullOrWhiteSpace(_settings.TwilioFromNumber))
        {
            throw new InvalidOperationException(
                "Twilio is configured but Sms:TwilioAccountSid, Sms:TwilioAuthToken, or Sms:TwilioFromNumber is missing.");
        }

        var normalized = PhoneHelper.Normalize(phoneNumber);
        var international = normalized.StartsWith("0") ? "+20" + normalized[1..] : normalized;
        var body = $"كود تأكيد عقارك أونلاين: {code} — صالح لمدة 10 دقائق.";

        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{_settings.TwilioAccountSid}/Messages.json");
        var credentials = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{_settings.TwilioAccountSid}:{_settings.TwilioAuthToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["From"] = _settings.TwilioFromNumber,
            ["To"] = international,
            ["Body"] = body
        });

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync();
            _logger.LogError("Twilio SMS failed ({Status}): {Detail}", response.StatusCode, detail);
            throw new InvalidOperationException($"Twilio SMS failed with status {response.StatusCode}.");
        }
    }
}
