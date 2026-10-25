namespace AqarakOnline.Services;

/// <summary>التحقق من الاسم: عربي أو إنجليزي فقط (مع مسافات وشرطة وفاصلة علوية).</summary>
public static class NameHelper
{
    public static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }
        var trimmed = name.Trim();
        if (trimmed.Length < 2 || trimmed.Length > 100)
        {
            return false;
        }
        var hasLetter = false;
        foreach (var ch in trimmed)
        {
            var isArabic = ch >= '\u0600' && ch <= '\u06FF';
            var isEnglish = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z');
            var isSeparator = ch == ' ' || ch == '-' || ch == '\'';
            if (isArabic || isEnglish)
            {
                hasLetter = true;
            }
            else if (!isSeparator)
            {
                return false;
            }
        }
        return hasLetter;
    }
}
