using System.Globalization;
using AqarakOnline.Data;
using AqarakOnline.Models;
using AqarakOnline.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // إعدادات مناسبة لمشروع تجريبي؛ شدّدها قبل الإنتاج.
    options.Password.RequiredLength = 6;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.Configure<ContactSettings>(builder.Configuration.GetSection("Contact"));
builder.Services.Configure<SmsSettings>(builder.Configuration.GetSection("Sms"));
if (string.Equals(builder.Configuration["Sms:Provider"], "Twilio", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<ISmsSender, TwilioSmsSender>();
}
else
{
    // الافتراضي: لا يرسل SMS حقيقيًا، بل يكتب كود التأكيد في اللوج للتجربة.
    builder.Services.AddSingleton<ISmsSender, LogSmsSender>();
}
builder.Services.AddScoped<LanguageService>();
builder.Services.AddControllersWithViews();

var app = builder.Build();

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
if (app.Configuration.GetValue<bool>("UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// إنشاء قاعدة البيانات + الأدوار + بيانات تجريبية عند أول تشغيل.
// ملحوظة: للتجربة السريعة نستخدم EnsureCreated بدل Migrations.
// قبل الإنتاج: أنشئ Migration بـ dotnet ef ثم استبدل السطر التالي بـ Migrate().
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.EnsureCreated();
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
    // وسيُمنح دور Admin تلقائيًا. لا يوجد مستخدم أدمن مُنشأ بكلمة مرور داخل الكود.
    var adminEmail = builder.Configuration["AdminEmail"];
    if (!string.IsNullOrWhiteSpace(adminEmail))
    {
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin != null && !await userManager.IsInRoleAsync(admin, "Admin"))
        {
            await userManager.AddToRoleAsync(admin, "Admin");
        }
    }
}

app.Run();
