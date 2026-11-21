using System.Globalization;
using AqarakOnline.Data;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<ViewingReminderService>();
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

var app = builder.Build();

// رؤوس أمان أساسية على كل رد: منع sniffing لنوع المحتوى، منع التضمين
// في إطارات (حماية من clickjacking)، وسياسة referrer وصلاحيات محافظة.
// ملحوظة: لا يوجد CSP صارم هنا عمدًا، لأن الواجهة تستخدم أنماطًا مضمّنة؛
// إضافة CSP صارم لاحقًا تتطلب فصل الأنماط والسكربتات في ملفات خارجية أولًا.
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

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
