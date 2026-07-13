using System.Text.RegularExpressions;

namespace OutlookRulesVSTO.Core;

public static class TextNormalizer
{
    public static string NormalizeWhitespace(string value) =>
        Regex.Replace(value ?? string.Empty, @"\s+", " ").Trim();
}
