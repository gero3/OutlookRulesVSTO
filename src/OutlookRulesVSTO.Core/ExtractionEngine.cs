using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace OutlookRulesVSTO.Core;

public static class ExtractionEngine
{
    public static ExtractionResult ExtractAll(Rule rule, MessageContext message)
    {
        var result = new ExtractionResult();
        foreach (var extraction in rule.Extractions)
        {
            if (extraction.Source == ExtractionSource.Automatic && extraction.Method == ExtractionMethod.RegexCapture)
            {
                var candidates = AutomaticRegexCandidates(extraction, message);
                if (candidates.Count > 1)
                {
                    result.Errors.Add($"Extraction '{extraction.Name}' is ambiguous: multiple different values were found ({string.Join(", ", candidates)}).");
                    continue;
                }

                var automaticValue = candidates.SingleOrDefault() ?? string.Empty;
                AddValue(result, extraction, automaticValue);
                continue;
            }

            var value = Extract(extraction, message);
            AddValue(result, extraction, value);
        }

        return result;
    }

    public static string Extract(Extraction extraction, MessageContext message)
    {
        if (extraction.Source == ExtractionSource.Automatic && extraction.Method == ExtractionMethod.RegexCapture)
        {
            var candidates = AutomaticRegexCandidates(extraction, message);
            return candidates.Count == 1 ? candidates[0] : string.Empty;
        }

        var source = SourceText(extraction.Source, message);
        return extraction.Method switch
        {
            ExtractionMethod.AfterLabelUntilLineEnd => AfterLabelUntilLineEnd(source, extraction.Pattern),
            ExtractionMethod.LineStartsWith => LineStartsWith(source, extraction.Pattern),
            ExtractionMethod.BetweenText => BetweenText(source, extraction.Pattern, extraction.Pattern2),
            ExtractionMethod.RegexCapture => RegexCapture(source, extraction.Pattern, extraction.CaptureGroup),
            _ => string.Empty
        };
    }

    private static string SourceText(ExtractionSource source, MessageContext message) =>
        source switch
        {
            ExtractionSource.Subject => message.Subject,
            ExtractionSource.BodyText => message.BodyText,
            ExtractionSource.BodyHtmlStripped => StripHtml(string.IsNullOrWhiteSpace(message.BodyHtml) ? message.BodyText : message.BodyHtml),
            ExtractionSource.AttachmentName => string.Join(Environment.NewLine, message.Attachments.Select(x => x.Name)),
            _ => string.Empty
        };

    private static List<string> AutomaticRegexCandidates(Extraction extraction, MessageContext message) =>
        new[]
        {
            message.Subject,
            string.Join(Environment.NewLine, message.Attachments.Select(x => x.Name)),
            message.BodyText,
            StripHtml(message.BodyHtml)
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .SelectMany(source => RegexCaptures(source, extraction.Pattern, extraction.CaptureGroup))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static IEnumerable<string> RegexCaptures(string source, string pattern, string captureGroup)
    {
        foreach (Match match in Regex.Matches(source, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline))
        {
            if (int.TryParse(captureGroup, out var index) && index >= 0 && index < match.Groups.Count)
                yield return match.Groups[index].Value;
            else if (match.Groups[captureGroup].Success)
                yield return match.Groups[captureGroup].Value;
        }
    }

    private static void AddValue(ExtractionResult result, Extraction extraction, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) value = extraction.DefaultValue;
        if (string.IsNullOrWhiteSpace(value) && extraction.Required)
        {
            result.Errors.Add($"Required extraction '{extraction.Name}' was not found.");
            return;
        }

        value ??= string.Empty;
        if (extraction.TrimResult) value = value.Trim();
        value = ApplyTransform(value, extraction);
        if (extraction.SanitizeForPath) value = PathSanitizer.SanitizeSegment(value);
        result.Values[extraction.Name] = value;
    }

    private static string AfterLabelUntilLineEnd(string source, string label)
    {
        foreach (var line in ReadLines(source))
        {
            var index = line.IndexOf(label, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                return line[(index + label.Length)..];
            }
        }

        return string.Empty;
    }

    private static string LineStartsWith(string source, string label)
    {
        foreach (var line in ReadLines(source))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[label.Length..];
            }
        }

        return string.Empty;
    }

    private static string BetweenText(string source, string start, string end)
    {
        var startIndex = source.IndexOf(start, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        startIndex += start.Length;
        var endIndex = string.IsNullOrEmpty(end)
            ? source.Length
            : source.IndexOf(end, startIndex, StringComparison.OrdinalIgnoreCase);

        if (endIndex < 0)
        {
            endIndex = source.Length;
        }

        return source[startIndex..endIndex];
    }

    private static string RegexCapture(string source, string pattern, string captureGroup)
    {
        var match = Regex.Match(source, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (!match.Success)
        {
            return string.Empty;
        }

        if (int.TryParse(captureGroup, out var index) && index >= 0 && index < match.Groups.Count)
        {
            return match.Groups[index].Value;
        }

        return match.Groups[captureGroup].Success ? match.Groups[captureGroup].Value : string.Empty;
    }

    private static IEnumerable<string> ReadLines(string source)
    {
        using var reader = new StringReader(source);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static string ApplyTransform(string value, Extraction extraction) =>
        extraction.Transform switch
        {
            ExtractionTransform.Uppercase => value.ToUpperInvariant(),
            ExtractionTransform.Lowercase => value.ToLowerInvariant(),
            ExtractionTransform.ReplaceSpaces => Regex.Replace(value, @"\s+", "_"),
            ExtractionTransform.DateFormat when DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date) =>
                date.ToString(string.IsNullOrWhiteSpace(extraction.TransformFormat) ? "yyyy-MM-dd" : extraction.TransformFormat, CultureInfo.InvariantCulture),
            _ => value
        };

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withBreaks = Regex.Replace(html, @"<(br|/p|/div|/tr)\b[^>]*>", Environment.NewLine, RegexOptions.IgnoreCase);
        var withoutTags = Regex.Replace(withBreaks, "<.*?>", string.Empty);
        return WebUtility.HtmlDecode(withoutTags);
    }
}
