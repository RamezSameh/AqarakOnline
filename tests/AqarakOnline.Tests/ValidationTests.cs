using AqarakOnline.Services;
using Microsoft.AspNetCore.Http;

namespace AqarakOnline.Tests;

public class PhoneValidationTests
{
    [Theory]
    [InlineData("01000000001")]
    [InlineData("01123456789")]
    [InlineData("01598765432")]
    public void ValidPhone_IsAccepted(string phone)
    {
        Assert.True(PhoneHelper.IsValidEgyptianMobile(phone));
    }

    [Theory]
    [InlineData("0100000000")]      // 10 أرقام: أقل من المطلوب
    [InlineData("010000000001")]    // 12 رقمًا: أكثر من المطلوب
    [InlineData("02000000001")]     // لا يبدأ بـ 01
    [InlineData("+201000000001")]   // صيغة دولية: مرفوضة، الإدخال يجب أن يكون 11 رقمًا
    [InlineData("00201000000001")]  // صيغة دولية: مرفوضة
    [InlineData("0100000000a")]     // حرف غير رقمي
    [InlineData("")]
    [InlineData(null)]
    public void InvalidPhone_IsRejected(string? phone)
    {
        Assert.False(PhoneHelper.IsValidEgyptianMobile(phone));
    }
}

public class NameValidationTests
{
    [Theory]
    [InlineData("رامز سامح")]
    [InlineData("محمد علي")]
    [InlineData("Ramez Sameh")]
    [InlineData("John Doe")]
    [InlineData("رامز Sameh")] // خليط عربي/إنجليزي مقبول
    public void ValidName_IsAccepted(string name)
    {
        Assert.True(NameHelper.IsValidName(name));
    }

    [Theory]
    [InlineData("Ramez123")]   // أرقام
    [InlineData("رامز@سامح")]  // رمز
    [InlineData("A")]          // حرف واحد فقط
    [InlineData("  ")]
    [InlineData("")]
    [InlineData(null)]
    public void InvalidName_IsRejected(string? name)
    {
        Assert.False(NameHelper.IsValidName(name));
    }
}

public class AttachmentValidationTests
{
    private static IFormFile FakeFile(string fileName, string contentType, long length)
    {
        var stream = new MemoryStream(new byte[length > 0 ? 1 : 0]);
        return new FormFile(stream, 0, length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }

    [Fact]
    public void ValidPhoto_IsAccepted()
    {
        Assert.Null(AttachmentStorage.ValidatePhoto(FakeFile("pic.jpg", "image/jpeg", 100)));
    }

    [Fact]
    public void ExecutableDisguisedAsPhoto_IsRejected()
    {
        Assert.NotNull(AttachmentStorage.ValidatePhoto(FakeFile("malware.exe", "image/jpeg", 100)));
        Assert.NotNull(AttachmentStorage.ValidatePhoto(FakeFile("pic.jpg", "application/octet-stream", 100)));
    }

    [Fact]
    public void OversizedVideo_IsRejected()
    {
        var big = FakeFile("vid.mp4", "video/mp4", AttachmentStorage.MaxVideoBytes + 1);
        Assert.NotNull(AttachmentStorage.ValidateVideo(big));
    }

    [Fact]
    public void ValidVideo_IsAccepted()
    {
        Assert.Null(AttachmentStorage.ValidateVideo(FakeFile("vid.mp4", "video/mp4", 1024)));
    }

    [Fact]
    public void MaxPhotosConstant_IsFive()
    {
        Assert.Equal(5, AttachmentStorage.MaxPhotosPerListing);
    }
}
