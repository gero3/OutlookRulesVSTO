using System.Text.RegularExpressions;

namespace OutlookRulesVSTO.Core;

public static class ConditionEvaluator
{
    public static bool RuleMatches(Rule rule, MessageContext message)
    {
        var enabled = rule.Conditions.Where(condition => condition.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return true;
        }

        return rule.MatchMode == RuleMatchMode.AllConditions
            ? enabled.All(condition => Evaluate(condition, message))
            : enabled.Any(condition => Evaluate(condition, message));
    }

    public static bool Evaluate(Condition condition, MessageContext message)
    {
        if (condition.Target is ConditionTarget.AttachmentName or ConditionTarget.AttachmentExtension)
        {
            return message.Attachments.Any(attachment => EvaluateAttachment(condition, attachment));
        }

        var actual = GetTargetValue(condition.Target, message);
        if (condition.NormalizeWhitespace)
        {
            actual = TextNormalizer.NormalizeWhitespace(actual);
        }

        var expected = condition.NormalizeWhitespace ? TextNormalizer.NormalizeWhitespace(condition.Value) : condition.Value;
        var expected2 = condition.NormalizeWhitespace ? TextNormalizer.NormalizeWhitespace(condition.Value2) : condition.Value2;
        return EvaluateText(actual, condition.Operator, expected, expected2, condition.CaseSensitive);
    }

    private static bool EvaluateAttachment(Condition condition, AttachmentContext attachment)
    {
        attachment.Normalize();
        var actual = condition.Target == ConditionTarget.AttachmentExtension ? attachment.Extension : attachment.Name;
        var expected = condition.Target == ConditionTarget.AttachmentExtension
            ? string.Join(';', condition.Value.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(PathSanitizer.NormalizeExtension))
            : condition.Value;

        if (condition.Target == ConditionTarget.AttachmentExtension &&
            condition.Operator is ConditionOperator.Equals or ConditionOperator.Contains)
        {
            var allowed = expected.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return allowed.Contains(attachment.Extension, StringComparer.OrdinalIgnoreCase);
        }

        return EvaluateText(actual, condition.Operator, expected, condition.Value2, condition.CaseSensitive);
    }

    private static string GetTargetValue(ConditionTarget target, MessageContext message) =>
        target switch
        {
            ConditionTarget.SenderEmail => message.SenderEmail,
            ConditionTarget.SenderName => message.SenderName,
            ConditionTarget.To => string.Join(';', message.To),
            ConditionTarget.Cc => string.Join(';', message.Cc),
            ConditionTarget.Subject => message.Subject,
            ConditionTarget.BodyText => message.BodyText,
            ConditionTarget.BodyHtml => message.BodyHtml,
            ConditionTarget.ReceivedDate => message.ReceivedTime.ToString("O"),
            ConditionTarget.HasAttachments => message.Attachments.Count > 0 ? "true" : "false",
            ConditionTarget.AttachmentCount => message.Attachments.Count.ToString(),
            ConditionTarget.Account => message.AccountName,
            ConditionTarget.Categories => string.Join(';', message.Categories),
            _ => string.Empty
        };

    private static bool EvaluateText(string actual, ConditionOperator op, string expected, string expected2, bool caseSensitive)
    {
        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return op switch
        {
            ConditionOperator.Contains => actual.Contains(expected, comparison),
            ConditionOperator.DoesNotContain => !actual.Contains(expected, comparison),
            ConditionOperator.Equals => string.Equals(actual, expected, comparison),
            ConditionOperator.NotEquals => !string.Equals(actual, expected, comparison),
            ConditionOperator.StartsWith => actual.StartsWith(expected, comparison),
            ConditionOperator.EndsWith => actual.EndsWith(expected, comparison),
            ConditionOperator.RegexMatch => Regex.IsMatch(actual, expected, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
            ConditionOperator.RegexNotMatch => !Regex.IsMatch(actual, expected, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase),
            ConditionOperator.IsEmpty => string.IsNullOrWhiteSpace(actual),
            ConditionOperator.IsNotEmpty => !string.IsNullOrWhiteSpace(actual),
            ConditionOperator.GreaterThan => CompareNumbersOrDates(actual, expected) > 0,
            ConditionOperator.LessThan => CompareNumbersOrDates(actual, expected) < 0,
            ConditionOperator.Between => CompareNumbersOrDates(actual, expected) >= 0 && CompareNumbersOrDates(actual, expected2) <= 0,
            _ => false
        };
    }

    private static int CompareNumbersOrDates(string left, string right)
    {
        if (decimal.TryParse(left, out var leftNumber) && decimal.TryParse(right, out var rightNumber))
        {
            return leftNumber.CompareTo(rightNumber);
        }

        if (DateTime.TryParse(left, out var leftDate) && DateTime.TryParse(right, out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
