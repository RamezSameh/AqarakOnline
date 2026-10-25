using Microsoft.AspNetCore.SignalR;

namespace AqarakOnline.Services;

/// <summary>
/// قناة التحديث اللحظي. العميل لا يرسل شيئًا؛ السيرفر فقط يدفع إشارة
/// "حدّث" عند تغيّر يستحق ظهوره فورًا (تنبيه جديد، مستخدم/إعلان/طلب جديد
/// أو تغيّر حالته)، والعميل يعيد جلب HTML الجرس أو أرقام الأدمن ويستبدلها.
/// الأدمن وحده ينضم لمجموعة Admins.
/// </summary>
public class RealtimeHub : Hub
{
    public const string AdminsGroup = "Admins";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.IsInRole("Admin") == true)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup);
        }
        await base.OnConnectedAsync();
    }
}
