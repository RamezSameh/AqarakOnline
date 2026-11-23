using System.Globalization;
using System.Net;
using System.Threading.RateLimiting;
using AqarakOnline.Data;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Server.IIS;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Batch 64 — الفيديو يُقبل بأي حجم: رفع حدود الطلب من 30/128 ميجا
// الافتراضية إلى 2 جيجا (Kestrel + IIS + نموذج الفورم)، عشان فيديو
// كبير يترفع ويتضغط عند الحفظ بدل ما يترفض.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);
builder.Services.Configure<IISServerOptions>(options => options.MaxRequestBodySize = null);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // سياسة كلمات مرور مشددة: 8 أحرف على الأقل، حرف كبير + حرف صغير + رقم.
    // قفل الحساب: 5 محاولات خاطئة → قفل 15 دقيقة (حماية من التخمين).
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.SignIn.RequireConfirmedAccount = false;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.AllowedForNewUsers = true;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    // SameAsRequest: آمنة على https في الإنتاج، وتعمل على http في تجربة الشبكة المحلية.
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.Configure<ContactSettings>(builder.Configuration.GetSection("Contact"));
builder.Services.Configure<SmsSettings>(builder.Configuration.GetSection("Sms"));

builder.Services.AddScoped<LanguageService>();
builder.Services.AddScoped<TrustedAdvertiserService>();
builder.Services.AddHttpClient<FacebookService>();
builder.Services.AddHttpClient<TurnstileService>();
builder.Services.AddSingleton<LoginAbuseTracker>();
builder.Services.AddMemoryCache();

// Batch 43 — ضغط الردود (HTML/CSS/JS) لتخفيف الحمل؛ EnableForHttps لازم
// لأن الموقع HTTPS وراء Cloudflare (الافتراضي لا يضغط على HTTPS).
builder.Services.AddResponseCompression(options => { options.EnableForHttps = true; });

// Batch 43 — كاش للصفحات العامة الثابتة (5 دقائق) + سياسة تمنع تخزين
// أي رد لمستخدم مسجّل دخول حتى لا يتسرب محتواه لمستخدم آخر.
builder.Services.AddOutputCache(options =>
{
    options.AddPolicy("Static300", policy =>
    {
        policy.AddPolicy<AnonymousOnlyCachePolicy>();
        policy.Expire(TimeSpan.FromMinutes(5));
    });
});

// Batch 43 — تحديد معدل الطلبات لكل IP (بعد ForwardedHeaders حتى يكون
// العنوان هو عنوان الزائر الحقيقي وراء Cloudflare Tunnel).
// auth: دخول/تسجيل 10/دقيقة • contact: تواصل 5/10 دقائق •
// search: بحث واقتراحات 60/دقيقة • upload: إنشاء/تعديل إعلان 10/10 دقائق.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "text/plain; charset=utf-8";
        await context.HttpContext.Response.WriteAsync("طلبات كثيرة جدًا من عنوانك — حاول بعد دقيقة.", token);
    };
    RateLimitPartition<string> PerIp(HttpContext ctx, int limit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
    options.AddPolicy("auth", ctx => PerIp(ctx, 10, TimeSpan.FromMinutes(1)));
    options.AddPolicy("contact", ctx => PerIp(ctx, 5, TimeSpan.FromMinutes(10)));
    options.AddPolicy("search", ctx => PerIp(ctx, 60, TimeSpan.FromMinutes(1)));
    options.AddPolicy("upload", ctx => PerIp(ctx, 10, TimeSpan.FromMinutes(10)));
});

builder.Services.AddHostedService<ViewingReminderService>();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

var app = builder.Build();

// Batch 43 — IP الحقيقي وراء Cloudflare Tunnel: cloudflared يعمل محليًا،
// فيرث X-Forwarded-For تلقائيًا (الـ loopback موثوق افتراضيًا). لازم يكون
// أول وسيط حتى يقرأ الـ rate limiting وحظر الـ IP العنوان الصحيح.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// رؤوس أمان أساسية على كل رد: منع sniffing لنوع المحتوى، منع التضمين
// في إطارات (حماية من clickjacking)، وسياسة referrer وصلاحيات محافظة.
// Batch 43 — CSP أساسي: يسمح فقط بمصادر الموقع نفسه + ما يستخدمه فعلًا
// (خطوط Google، SignalR من jsDelivr، Turnstile) ويمنع أي سكربت/إطار غريب.
// 'unsafe-inline' باقٍ عمدًا لأن الواجهة تستخدم أنماطًا وسكربتات مضمّنة؛
// CSP مشدد بلا unsafe-inline يحتاج فصلها في ملفات خارجية أولًا.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net https://challenges.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: https:; " +
        "media-src 'self'; " +
        "connect-src 'self' https://challenges.cloudflare.com; " +
        "frame-src https://challenges.cloudflare.com; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "frame-ancestors 'none';";
    await next();
});

// Batch 43 — حظر الـ IP: أي عنوان محظور (يدوي أو تلقائي) يأخذ 403 قبل أي شيء.
// الـ loopback مستثنى عمدًا (تجربة محلية)؛ الفحص رخيص (مجموعة في الذاكرة 60 ثانية).
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    if (remote != null && !IPAddress.IsLoopback(remote)
        && await IpBlockHelper.IsBlockedAsync(context.RequestServices, remote.ToString()))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("تم حظر هذا العنوان مؤقتًا لكثرة محاولات الدخول الفاشلة. تواصل مع إدارة الموقع.");
        return;
    }
    await next();
});

app.UseResponseCompression();

// اللغة: عربي افتراضيًا، وتُحفظ في كوكي aqarak-lang.
app.Use(async (context, next) =>
{
    var language = context.RequestServices.GetRequiredService<LanguageService>();
    language.SetCulture(context.Request.Cookies["aqarak-lang"]);
    var culture = new CultureInfo(language.IsArabic ? "ar-EG" : "en-US");
    CultureInfo.CurrentCulture = culture;
    CultureInfo.CurrentUICulture = culture;
    await next();
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// إعادة التوجيه إلى HTTPS قابلة للإيقاف من الإعدادات.
// ملحوظة دقيقة: عند التشغيل على http فقط، لن يحدث تحويل أصلًا لأن الخادم
// لا يستمع على https (الوسيط يحذّر ويمرّر الطلب)، لكن الإعداد يضمن عدم
// التحويل حتى لو كان https مفعّلًا في وضع تشغيل آخر. للتجربة على نفس الشبكة
// من الموبايل، اضبط UseHttpsRedirection على false زيادة أمان.
// للتشغيل وراء Cloudflare Tunnel، شغّل ببيئة Tunnel (ملف
// appsettings.Tunnel.json) حيث UseHttpsRedirection=false، لأن Cloudflare
// هي التي توفّر HTTPS للزوار والاتصال الداخلي http.
if (app.Configuration.GetValue<bool>("UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseStatusCodePagesWithReExecute("/Home/Error404");
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// وضع الصيانة: يحجب كل الصفحات عن غير الأدمن، ما عدا تسجيل الدخول
// ولوحة الأدمن (حتى يستطيع الأدمن إيقافه) والملفات الثابتة.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isExempt = path.StartsWithSegments("/Admin", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/Account/Login", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/Account/Logout", StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments("/realtimeHub", StringComparison.OrdinalIgnoreCase);
    if (!context.User.IsInRole("Admin") && !isExempt)
    {
        var db = context.RequestServices.GetRequiredService<ApplicationDbContext>();
        if (await SiteSettingsHelper.IsMaintenanceEnabledAsync(db))
        {
            var message = await SiteSettingsHelper.GetMaintenanceMessageAsync(db);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync($"<!DOCTYPE html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>تحت الصيانة</title></head><body style=\"font-family: 'Segoe UI', Tahoma, sans-serif; margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center; background: linear-gradient(135deg, #1e1b4b, #312e81 55%, #0f172a); color: #f1f5f9; text-align: center; padding: 1rem;\"><div style=\"max-width: 480px; width: 100%; background: #1e293b; border: 1px solid #334155; border-radius: 16px; padding: 2.5rem 2rem; box-shadow: 0 12px 32px rgba(0,0,0,.4);\"><div style=\"font-size: 48px;\">🔧</div><h1 style=\"margin: .5rem 0;\">الموقع تحت الصيانة</h1><p style=\"color: #cbd5e1;\">{System.Net.WebUtility.HtmlEncode(message)}</p></div></body></html>");
            return;
        }
    }
    await next();
});

// Batch 43 — كاش الصفحات العامة: بعد وسيط الصيانة عمدًا حتى لا تُقدَّم
// صفحة مخزَّنة أثناء الصيانة، وقبل الـ endpoints.
app.UseOutputCache();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapHub<AqarakOnline.Services.RealtimeHub>("/realtimeHub");

// إنشاء قاعدة البيانات + الأدوار + بيانات تجريبية عند أول تشغيل.
// Migrations بدل EnsureCreated: تعديلات الجداول تتم من غير مسح البيانات.
// عند أول تشغيل، Migrate() يطبّق أي Migrations لم تُطبَّق بعد (وينشئ قاعدة
// البيانات لو لم تكن موجودة)، ثم يعمل الـ seeding.
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate();
    SeedData.SeedListings(context);
    SeedData.SeedCollections(context);
    SeedData.ClearFeaturedForUnavailable(context);

    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "User" })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            await roleManager.CreateAsync(new IdentityRole(role));
        }
    }

    // مستخدمون تجريبيون: يُنشأون فقط إذا كان SeedTestUsers=true في الإعدادات.
    // لا تترك هذا مفعّلًا في أي نشر عام (راجع تحذير SeedTestUsersAsync).
    if (bool.TryParse(builder.Configuration["SeedTestUsers"], out var seedTestUsers) && seedTestUsers)
    {
        var testUserManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await SeedData.SeedTestUsersAsync(testUserManager);
    }

    // الأدمن الأول: سجّل حسابًا بالإيميل الموجود في AdminEmail ثم أعد تشغيل التطبيق،
    // وسيُمنح دور Admin ويُفعَّل تلقائيًا (حتى لا يُحبس خارج النظام بانتظار موافقة).
    // لا يوجد مستخدم أدمن مُنشأ بكلمة مرور داخل الكود.
    var adminEmail = builder.Configuration["AdminEmail"];
    if (!string.IsNullOrWhiteSpace(adminEmail))
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin != null)
        {
            if (!await userManager.IsInRoleAsync(admin, "Admin"))
            {
                await userManager.AddToRoleAsync(admin, "Admin");
            }
            if (admin.AccountStatus != AccountStatus.Active)
            {
                admin.AccountStatus = AccountStatus.Active;
                await userManager.UpdateAsync(admin);
            }
        }
    }
}

app.Run();
