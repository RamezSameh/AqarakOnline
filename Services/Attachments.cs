using AqarakOnline.Models;
using Microsoft.AspNetCore.Http;

namespace AqarakOnline.Services;

/// <summary>
/// حفظ وحذف ملفات مرفقات الإعلانات على القرص (wwwroot/uploads).
/// القواعد: حتى 5 صور للإعلان، وفيديو واحد حتى 25 ميجا.
/// </summary>
public static class AttachmentStorage
{
    public const int MaxPhotosPerListing = 5;
    public const long MaxVideoBytes = 25L * 1024 * 1024; // 25 ميجا

    private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov" };

    public static string? ValidatePhoto(IFormFile file)
    {
        if (file.Length == 0)
        {
            return "ملف الصورة فارغ.";
        }
        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return "ملف الصورة يجب أن يكون صورة.";
        }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!PhotoExtensions.Contains(ext))
        {
            return "صيغة الصورة غير مدعومة. الصيغ المقبولة: jpg، jpeg، png، webp.";
        }
        return null;
    }

    public static string? ValidateVideo(IFormFile file)
    {
        if (file.Length == 0)
        {
            return "ملف الفيديو فارغ.";
        }
        if (file.Length > MaxVideoBytes)
        {
            return "حجم الفيديو لا يتعدى 25 ميجا.";
        }
        if (!file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return "ملف الفيديو يجب أن يكون فيديو.";
        }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!VideoExtensions.Contains(ext))
        {
            return "صيغة الفيديو غير مدعومة. الصيغ المقبولة: mp4، webm، mov.";
        }
        return null;
    }

    public static async Task<ListingAttachment> SaveAsync(
        IFormFile file, AttachmentKind kind, int listingId, string webRootPath, int order)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var fileName = Guid.NewGuid().ToString("N") + ext;
        var folder = Path.Combine(webRootPath, "uploads", "listings", listingId.ToString());
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, fileName);
        await using (var stream = new FileStream(fullPath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }
        return new ListingAttachment
        {
            ListingId = listingId,
            Kind = kind,
            FilePath = $"uploads/listings/{listingId}/{fileName}",
            ContentType = file.ContentType,
            OriginalFileName = Path.GetFileName(file.FileName),
            SizeBytes = file.Length,
            Order = order
        };
    }

    public static void DeleteFile(string webRootPath, string relativePath)
    {
        try
        {
            var fullPath = Path.Combine(webRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // حذف ملف غير موجود أو مقفول لا يجب أن يوقف حذف الإعلان نفسه.
        }
    }
}
