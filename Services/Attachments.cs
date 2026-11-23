using AqarakOnline.Models;
using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;

namespace AqarakOnline.Services;

/// <summary>
/// حفظ وحذف ملفات مرفقات الإعلانات على القرص (wwwroot/uploads).
/// القواعد: حتى 5 صور للإعلان، وفيديو واحد (mp4/webm/mov) بأي حجم.
/// الصور تُضغط تلقائيًا عند الحفظ: تصغير لأقصى 1600px + تحويل WebP بجودة 80
/// + تصحيح اتجاه صور الموبايل، فالملف المحفوظ أصغر بكتير من الأصل والصفحة تفتح أسرع.
/// الفيديو يُضغط تلقائيًا عند الحفظ: تحويل MP4 (H.264) بأقصى عرض 1280px
/// وجودة CRF 28، فالملف المحفوظ أصغر بكتير من الأصل ويناسب التخزين والرفع.
/// الفيديو يُحفظ عبر ملفات مؤقتة (ستريمنج) مش في الذاكرة، عشان الفيديوهات
/// الكبيرة متستهلكش الرام. الضغط يحتاج ffmpeg مثبّت على الجهاز (PATH)؛
/// لو مش موجود أو فشل التحويل، الفيديو يُحفظ زي ما هو من غير ما يفشل الرفع.
/// </summary>
public static class AttachmentStorage
{
    public const int MaxPhotosPerListing = 5;
    public const long MaxPhotoBytes = 10L * 1024 * 1024; // 10 ميجا للصورة الواحدة عند الرفع
    public const int MaxPhotoDimension = 1600; // أقصى عرض/ارتفاع للصورة المحفوظة
    public const int PhotoQuality = 80; // جودة WebP

    private static readonly string[] PhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov" };

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
        // لا حد أقصى لحجم الفيديو — يُقبل بأي حجم ويُضغط عند الحفظ.
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
        if (kind == AttachmentKind.Video)
        {
            return await SaveVideoAsync(file, listingId, webRootPath, order);
        }
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = file.ContentType;
        // ضغط الصورة قبل الحفظ.
        var compressedPhoto = await CompressPhotoAsync(file);
        var bytes = compressedPhoto.Bytes;
        contentType = compressedPhoto.ContentType;
        ext = compressedPhoto.Extension;

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
    /// حفظ فيديو بأي حجم: الرفع يُنسخ كملف مؤقت (ستريمنج، من غير تحميل الملف
    /// كله في الذاكرة)، ثم يُضغط عبر ffmpeg، ثم يُنقل الناتج (أو الأصل لو
    /// الضغط فشل) لمجلد الإعلان. حجم الملف المحفوظ يُقاس من القرص.
    /// </summary>
    private static async Task<ListingAttachment> SaveVideoAsync(
        IFormFile file, int listingId, string webRootPath, int order)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = file.ContentType;
        var tempInput = Path.Combine(Path.GetTempPath(), $"aqarak-in-{Guid.NewGuid():N}{ext}");
        try
        {
            await using (var tempStream = File.Create(tempInput))
            {
                await file.CopyToAsync(tempStream);
            }
            var compressedPath = await CompressVideoFileAsync(tempInput);
            var isCompressed = compressedPath != null;
            var fileName = Guid.NewGuid().ToString("N") + (isCompressed ? ".mp4" : ext);
            var folder = Path.Combine(webRootPath, "uploads", "listings", listingId.ToString());
            Directory.CreateDirectory(folder);
            var fullPath = Path.Combine(folder, fileName);
            MoveFile(isCompressed ? compressedPath! : tempInput, fullPath);
            if (isCompressed)
            {
                contentType = "video/mp4";
            }
            return new ListingAttachment
            {
                ListingId = listingId,
                Kind = AttachmentKind.Video,
                FilePath = $"uploads/listings/{listingId}/{fileName}",
                ThumbPath = null,
                ContentType = contentType,
                OriginalFileName = Path.GetFileName(file.FileName),
                SizeBytes = new FileInfo(fullPath).Length,
                Order = order
            };
        }
        finally
        {
            DeleteQuiet(tempInput);
        }
    }

    /// <summary>
    /// ضغط فيديو عبر ffmpeg: تحويل MP4 (H.264) بأقصى عرض 1280px وجودة CRF 28
    /// + صوت AAC + faststart (يشتغل أثناء التحميل في المتصفح).
    /// يرجع مسار ملف مؤقت مضغوط، أو null لو ffmpeg مش مثبّت أو التحويل فشل
    /// أو تجاوز 20 دقيقة أو الناتج طلع أكبر من الأصل — والمتصل يحفظ الأصل.
    /// </summary>
    private static async Task<string?> CompressVideoFileAsync(string inputPath)
    {
        var outputPath = Path.Combine(Path.GetTempPath(), $"aqarak-out-{Guid.NewGuid():N}.mp4");
        try
        {
            var inputSize = new FileInfo(inputPath).Length;
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
                return null;
            }
            var stderrTask = process.StandardError.ReadToEndAsync();
            var finished = await Task.Run(() => process.WaitForExit(1200000));
            if (!finished)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                DeleteQuiet(outputPath);
                return null;
            }
            await stderrTask;
            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                DeleteQuiet(outputPath);
                return null;
            }
            var outputSize = new FileInfo(outputPath).Length;
            if (outputSize == 0 || outputSize >= inputSize)
            {
                DeleteQuiet(outputPath);
                return null;
            }
            return outputPath;
        }
        catch
        {
            DeleteQuiet(outputPath);
            return null;
        }
    }

    /// <summary>
    /// نقل ملف؛ النقل المباشر يفشل بين أقراص مختلفة (مثلاً Temp على C
    /// والمشروع على E) فالبديل نسخ ثم حذف المصدر.
    /// </summary>
    private static void MoveFile(string source, string destination)
    {
        try
        {
            File.Move(source, destination);
        }
        catch (IOException)
        {
            File.Copy(source, destination, overwrite: true);
            File.Delete(source);
        }
    }

    private static void DeleteQuiet(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
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
