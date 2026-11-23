using AqarakOnline.Models;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace AqarakOnline.Services;

/// <summary>
/// حفظ وحذف ملفات مرفقات الإعلانات على القرص (wwwroot/uploads).
/// القواعد: حتى 5 صور للإعلان، وفيديو واحد حتى 25 ميجا عند الرفع.
/// الصور تُضغط تلقائيًا عند الحفظ: تصغير لأقصى 1600px + تحويل WebP بجودة 80
/// + تصحيح اتجاه صور الموبايل، فالملف المحفوظ أصغر بكتير من الأصل والصفحة تفتح أسرع.
/// الفيديو يُقبل بأي صيغة شائعة ويُضغط تلقائيًا عند الحفظ: تحويل MP4 (H.264)
/// بأقصى عرض 1280px وجودة CRF 28، فالملف المحفوظ أصغر بكتير من الأصل.
/// الضغط يحتاج ffmpeg مثبّت على الجهاز (PATH)؛ لو مش موجود أو فشل التحويل،
/// الفيديو يُحفظ زي ما هو من غير ما يفشل الرفع.
/// </summary>
public static class AttachmentStorage
{
    public const int MaxPhotosPerListing = 5;
    public const long MaxVideoBytes = 25L * 1024 * 1024; // 25 ميجا
    public const long MaxPhotoBytes = 10L * 1024 * 1024; // 10 ميجا للصورة الواحدة عند الرفع
    public const int MaxPhotoDimension = 1600; // أقصى عرض/ارتفاع للصورة المحفوظة
    public const int PhotoQuality = 80; // جودة WebP

    private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".avi", ".mkv", ".3gp", ".m4v", ".wmv", ".flv", ".mpg", ".mpeg" };

    public static string? ValidatePhoto(IFormFile file)
    {
        if (file.Length == 0)
        {
            return "ملف الصورة فارغ.";
        }
        if (file.Length > MaxPhotoBytes)
        {
            return "حجم الصورة لا يتعدى 10 ميجا.";
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
            return "صيغة الفيديو غير مدعومة. الصيغ المقبولة: mp4، webm، mov، avi، mkv، 3gp، m4v، wmv، flv، mpg، mpeg.";
        }
        return null;
    }

    public static async Task<ListingAttachment> SaveAsync(
        IFormFile file, AttachmentKind kind, int listingId, string webRootPath, int order)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = file.ContentType;
        byte[] bytes;
        if (kind == AttachmentKind.Photo)
        {
            // ضغط الصورة قبل الحفظ؛ الفيديو يُحفظ زي ما هو.
            var compressed = await CompressPhotoAsync(file);
            bytes = compressed.Bytes;
            contentType = compressed.ContentType;
            ext = compressed.Extension;
        }
        else
        {
            await using var source = new MemoryStream();
            await file.CopyToAsync(source);
            var original = source.ToArray();
            // ضغط الفيديو قبل الحفظ؛ لو الضغط مش متاح أو فشل، يُحفظ الأصل زي ما هو.
            var compressed = await CompressVideoAsync(original, ext, contentType);
            bytes = compressed.Bytes;
            contentType = compressed.ContentType;
            ext = compressed.Extension;
        }

        var fileName = Guid.NewGuid().ToString("N") + ext;
        var folder = Path.Combine(webRootPath, "uploads", "listings", listingId.ToString());
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(fullPath, bytes);
        // نسخة مصغّرة 480px للكروت (srcset) — من الصورة المضغوطة بس.
        string? thumbPath = null;
        if (kind == AttachmentKind.Photo && ext == ".webp")
        {
            try
            {
                var thumbName = Path.GetFileNameWithoutExtension(fileName) + "-thumb.webp";
                await using var thumbInput = new MemoryStream(bytes);
                using var thumbImage = await Image.LoadAsync(thumbInput);
                thumbImage.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(480, 480)
                }));
                await using var thumbOutput = new MemoryStream();
                await thumbImage.SaveAsWebpAsync(thumbOutput, new WebpEncoder { Quality = PhotoQuality });
                await File.WriteAllBytesAsync(Path.Combine(folder, thumbName), thumbOutput.ToArray());
                thumbPath = $"uploads/listings/{listingId}/{thumbName}";
            }
            catch
            {
                // المصغّرة اختيارية — الكارت يرجع للصورة الكاملة.
            }
        }
        return new ListingAttachment
        {
            ListingId = listingId,
            Kind = kind,
            FilePath = $"uploads/listings/{listingId}/{fileName}",
            ThumbPath = thumbPath,
            ContentType = contentType,
            OriginalFileName = Path.GetFileName(file.FileName),
            SizeBytes = bytes.LongLength,
            Order = order
        };
    }

    /// <summary>
    /// ضغط صورة: تصحيح الاتجاه (صور الموبايل)، تصغير لأقصى 1600px من غير تكبير،
    /// وحفظ WebP بجودة 80. لو الملف تالف أو صيغته غريبة، يرجع الأصل زي ما هو
    /// بدل ما يفشل الرفع كله.
    /// </summary>
    private static async Task<(byte[] Bytes, string ContentType, string Extension)> CompressPhotoAsync(IFormFile file)
    {
        try
        {
            await using var input = file.OpenReadStream();
            using var image = await Image.LoadAsync(input);
            image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxPhotoDimension, MaxPhotoDimension)
            }));
            await using var output = new MemoryStream();
            await image.SaveAsWebpAsync(output, new WebpEncoder { Quality = PhotoQuality });
            return (output.ToArray(), "image/webp", ".webp");
        }
        catch
        {
            await using var fallback = new MemoryStream();
            await file.CopyToAsync(fallback);
            return (fallback.ToArray(), file.ContentType, Path.GetExtension(file.FileName).ToLowerInvariant());
        }
    }

    /// <summary>
    /// ضغط فيديو عبر ffmpeg: تحويل MP4 (H.264) بأقصى عرض 1280px وجودة CRF 28
    /// + صوت AAC + faststart (يشتغل أثناء التحميل في المتصفح).
    /// لو ffmpeg مش مثبّت أو التحويل فشل أو خلّص بوقت طويل، يرجع الأصل زي ما هو
    /// بدل ما يفشل الرفع كله. ولو الناتج طلع أكبر من الأصل، يُحفظ الأصل.
    /// </summary>
    private static async Task<(byte[] Bytes, string ContentType, string Extension)> CompressVideoAsync(byte[] inputBytes, string inputExt, string inputContentType)
    {
        var original = (inputBytes, inputContentType, inputExt);
        string? inputPath = null;
        string? outputPath = null;
        try
        {
            var tag = Guid.NewGuid().ToString("N");
            inputPath = Path.Combine(Path.GetTempPath(), $"aqarak-in-{tag}{inputExt}");
            outputPath = Path.Combine(Path.GetTempPath(), $"aqarak-out-{tag}.mp4");
            await File.WriteAllBytesAsync(inputPath, inputBytes);

            var start = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-y");
            start.ArgumentList.Add("-i");
            start.ArgumentList.Add(inputPath);
            start.ArgumentList.Add("-vf");
            start.ArgumentList.Add("scale='min(1280,iw)':-2");
            start.ArgumentList.Add("-c:v");
            start.ArgumentList.Add("libx264");
            start.ArgumentList.Add("-crf");
            start.ArgumentList.Add("28");
            start.ArgumentList.Add("-preset");
            start.ArgumentList.Add("veryfast");
            start.ArgumentList.Add("-c:a");
            start.ArgumentList.Add("aac");
            start.ArgumentList.Add("-b:a");
            start.ArgumentList.Add("128k");
            start.ArgumentList.Add("-movflags");
            start.ArgumentList.Add("+faststart");
            start.ArgumentList.Add(outputPath);

            using var process = Process.Start(start);
            if (process == null)
            {
                return original;
            }
            var stderrTask = process.StandardError.ReadToEndAsync();
            var finished = await Task.Run(() => process.WaitForExit(120000));
            if (!finished)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return original;
            }
            await stderrTask;
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                return original;
            }
            var outputBytes = await File.ReadAllBytesAsync(outputPath);
            if (outputBytes.Length == 0 || outputBytes.Length >= inputBytes.Length)
            {
                return original;
            }
            return (outputBytes, "video/mp4", ".mp4");
        }
        catch
        {
            return original;
        }
        finally
        {
            try { if (inputPath != null) File.Delete(inputPath); } catch { }
            try { if (outputPath != null) File.Delete(outputPath); } catch { }
        }
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
