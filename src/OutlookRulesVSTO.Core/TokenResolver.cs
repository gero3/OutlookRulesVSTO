using System.Globalization;
using System.Text.RegularExpressions;

namespace OutlookRulesVSTO.Core;

public static class TokenResolver
{
    private static readonly Regex TokenPattern = new(@"\{(?<name>[A-Za-z][A-Za-z0-9_]*)(:(?<format>[^}]+))?\}", RegexOptions.Compiled);

    private static readonly HashSet<string> BuiltInTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReceivedDate",
        "ReceivedYear",
        "ReceivedMonth",
        "SenderEmail",
        "SenderName",
        "Subject",
        "RuleName",
        "AccountName",
        "OriginalFileName",
        "OriginalFileBaseName",
        "OriginalFileExtension"
    };

    public static string Resolve(
        string template,
        Rule rule,
        MessageContext message,
        AttachmentContext? attachment,
        IReadOnlyDictionary<string, string> extractions,
        bool sanitizeSegments)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var resolved = TokenPattern.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            var format = match.Groups["format"].Success ? match.Groups["format"].Value : string.Empty;
            return ResolveToken(name, format, rule, message, attachment, extractions);
        });

        return sanitizeSegments ? PathSanitizer.SanitizeRelativePath(resolved) : resolved;
    }

    public static IReadOnlyCollection<string> FindTokenNames(string template) =>
        TokenPattern.Matches(template ?? string.Empty)
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static bool IsBuiltInToken(string tokenName) => BuiltInTokens.Contains(tokenName);

    private static string ResolveToken(
        string name,
        string format,
        Rule rule,
        MessageContext message,
        AttachmentContext? attachment,
        IReadOnlyDictionary<string, string> extractions)
    {
        if (extractions.TryGetValue(name, out var extracted))
        {
            return extracted;
        }

        return name.ToLowerInvariant() switch
        {
            "receiveddate" => message.ReceivedTime.ToString(string.IsNullOrWhiteSpace(format) ? "yyyy-MM-dd" : format, CultureInfo.InvariantCulture),
            "receivedyear" => message.ReceivedTime.ToString("yyyy", CultureInfo.InvariantCulture),
            "receivedmonth" => message.ReceivedTime.ToString("MM", CultureInfo.InvariantCulture),
            "senderemail" => message.SenderEmail,
            "sendername" => message.SenderName,
            "subject" => message.Subject,
            "rulename" => rule.Name,
            "accountname" => message.AccountName,
            "originalfilename" => attachment?.Name ?? string.Empty,
            "originalfilebasename" => attachment?.BaseName ?? string.Empty,
            "originalfileextension" => attachment?.Extension ?? string.Empty,
            _ => string.Empty
        };
    }
}
