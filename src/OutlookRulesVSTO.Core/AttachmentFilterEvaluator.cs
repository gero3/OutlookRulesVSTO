using System.Text.RegularExpressions;

namespace OutlookRulesVSTO.Core;

public static class AttachmentFilterEvaluator
{
    public static List<AttachmentContext> MatchingAttachments(Rule rule, MessageContext message)
    {
        if (rule.AttachmentFilters.Count == 0)
        {
            return message.Attachments.ToList();
        }

        return message.Attachments
            .Where(attachment => rule.AttachmentFilters.All(filter => Matches(filter, attachment)))
            .ToList();
    }

    public static bool MeetsRequiredCounts(Rule rule, List<AttachmentContext> matchingAttachments)
    {
        if (rule.AttachmentFilters.Count == 0)
        {
            return true;
        }

        return rule.AttachmentFilters.All(filter => !filter.RequiredCount.HasValue || matchingAttachments.Count >= filter.RequiredCount.Value);
    }

    public static bool Matches(AttachmentFilter filter, AttachmentContext attachment)
    {
        attachment.Normalize();

        if (filter.ExcludeInline && attachment.IsInline)
        {
            return false;
        }

        if (filter.Extensions.Count > 0 &&
            !filter.Extensions.Select(PathSanitizer.NormalizeExtension).Contains(attachment.Extension, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.MinSizeKb.HasValue && attachment.SizeKb < filter.MinSizeKb.Value)
        {
            return false;
        }

        if (filter.MaxSizeKb.HasValue && attachment.SizeKb > filter.MaxSizeKb.Value)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.NamePattern))
        {
            var nameMatches = filter.NamePatternIsRegex
                ? Regex.IsMatch(attachment.Name, filter.NamePattern, RegexOptions.IgnoreCase)
                : MatchesNamePattern(attachment.Name, filter.NamePatternOperator, filter.NamePattern);

            if (!nameMatches)
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesNamePattern(string attachmentName, ConditionOperator op, string pattern) =>
        op switch
        {
            ConditionOperator.StartsWith => attachmentName.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.EndsWith => attachmentName.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
            ConditionOperator.Equals => string.Equals(attachmentName, pattern, StringComparison.OrdinalIgnoreCase),
            _ => attachmentName.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };
}
