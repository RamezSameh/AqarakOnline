using AqarakOnline.Data;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AqarakOnline.Services;

/// <summary>
/// كاشف موجات التخمين: يعدّ محاولات الدخول الفاشلة لكل IP في نافذة 15 دقيقة
/// (في الذاكرة فقط — العدّاد للكشف، والحظر نفسه محفوظ في جدول BlockedIps).
/// </summary>
public class LoginAbuseTracker
{
    /// <summary>10 محاولات فاشلة خلال النافذة = موجة تخمين → حظر تلقائي.</summary>
    public const int MaxFailures = 10;

    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly object _gate = new();
    private readonly Dictionary<string, List<DateTime>> _fails = new(StringComparer.Ordinal);

    /// <summary>يسجّل محاولة فاشلة من IP ويرجع عدد محاولاته في النافذة.</summary>
    public int RecordFailure(string ip)
    {
        var now = DateTime.UtcNow;
        var cutoff = now - Window;
        lock (_gate)
        {
            if (!_fails.TryGetValue(ip, out var list))
            {
                list = new List<DateTime>();
                _fails[ip] = list;
            }
            list.RemoveAll(t => t < cutoff);
            list.Add(now);
            return list.Count;
        }
    }
}

/// <summary>
/// فحص حظر الـ IP: مجموعة المحظورين النشطين تُقرأ من قاعدة البيانات
/// وتُحفظ في الذاكرة 60 ثانية حتى لا يضرب كل طلب قاعدة البيانات.
/// </summary>
public static class IpBlockHelper
{
    private const string CacheKey = "blocked-ips-v1";

    public static async Task<bool> IsBlockedAsync(IServiceProvider services, string ip)
    {
        var cache = services.GetRequiredService<IMemoryCache>();
        if (cache.TryGetValue<HashSet<string>>(CacheKey, out var blocked) && blocked != null)
        {
            return blocked.Contains(ip);
        }
        var db = services.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var set = await db.BlockedIps
            .Where(b => b.BlockedUntil == null || b.BlockedUntil > now)
            .Select(b => b.IpAddress)
            .ToListAsync();
        var hash = new HashSet<string>(set, StringComparer.Ordinal);
        cache.Set(CacheKey, hash, TimeSpan.FromMinutes(1));
        return hash.Contains(ip);
    }

    public static void InvalidateCache(IServiceProvider services)
    {
        services.GetRequiredService<IMemoryCache>().Remove(CacheKey);
    }
}

/// <summary>
/// سياسة كاش تُخزّن الردود للزوار غير المسجّلين فقط: لو المستخدم مسجّل دخول
/// لا يُقرأ من الكاش ولا يُخزَّن فيه، حتى لا يتسرب محتوى يخص مستخدمًا لآخر.
/// </summary>
public sealed class AnonymousOnlyCachePolicy : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated == true)
        {
            context.AllowCacheLookup = false;
            context.AllowCacheStorage = false;
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;
}
