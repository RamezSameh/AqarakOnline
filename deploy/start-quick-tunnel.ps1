# تشغيل Quick Tunnel لعقارك أونلاين — للتجربة السريعة فقط.
# الاستخدام: شغّل التطبيق أولًا على http://localhost:5000، ثم شغّل هذا السكربت
# من PowerShell (كليك يمين ← Run with PowerShell). سيطبع لينك trycloudflare.com
# مؤقتًا يتغير كل مرة ويتوقف عند إغلاق النافذة.
# يتطلب: cloudflared مثبّتًا وموجودًا في PATH (نزّله من موقع Cloudflare الرسمي).

$ErrorActionPreference = "Stop"

$cloudflared = Get-Command cloudflared -ErrorAction SilentlyContinue
if (-not $cloudflared) {
    Write-Host "cloudflared غير موجود في PATH. نزّله من موقع Cloudflare الرسمي ثم أعد تشغيل السكربت." -ForegroundColor Red
    exit 1
}

try {
    $response = Invoke-WebRequest -Uri "http://localhost:5000" -UseBasicParsing -TimeoutSec 5
    Write-Host "التطبيق يعمل على http://localhost:5000 (الحالة: $($response.StatusCode))." -ForegroundColor Green
} catch {
    Write-Host "التطبيق لا يرد على http://localhost:5000. شغّله أولًا ثم أعد تشغيل السكربت." -ForegroundColor Red
    exit 1
}

Write-Host "جارٍ تشغيل Quick Tunnel... انسخ لينك trycloudflare.com الظاهر أدناه." -ForegroundColor Cyan
cloudflared tunnel --url http://localhost:5000
