using System.Text.RegularExpressions;

namespace OutlookRulesVSTO.Core;

public static class RuleValidator
{
    private static readonly Regex TokenNamePattern = new("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static ValidationResult Validate(RuleConfiguration configuration, Settings? settings = null)
    {
        var result = new ValidationResult();

        if (configuration.SchemaVersion != Schema.CurrentSchemaVersion)
        {
            result.Warning($"Rules schema version {configuration.SchemaVersion} will be handled by the migration placeholder.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < configuration.Rules.Count; i++)
        {
            ValidateRule(configuration.Rules[i], result, $"Rules[{i}]", settings);
            if (!ids.Add(configuration.Rules[i].RuleId))
            {
                result.Error($"Duplicate RuleId '{configuration.Rules[i].RuleId}'.", $"Rules[{i}].RuleId");
            }

            if (!string.IsNullOrWhiteSpace(configuration.Rules[i].Name) && !names.Add(configuration.Rules[i].Name))
            {
                result.Error($"Duplicate rule name '{configuration.Rules[i].Name}'.", $"Rules[{i}].Name");
            }
        }

        return result;
    }

    public static ValidationResult ValidateRule(Rule rule, Settings? settings = null)
    {
        var result = new ValidationResult();
        ValidateRule(rule, result, "Rule", settings);
        return result;
    }

    private static void ValidateRule(Rule rule, ValidationResult result, string path, Settings? settings)
    {
        if (string.IsNullOrWhiteSpace(rule.RuleId))
        {
            result.Error("RuleId is required.", $"{path}.RuleId");
        }

        if (string.IsNullOrWhiteSpace(rule.Name))
        {
            result.Error("Rule name is required.", $"{path}.Name");
        }

        if (rule.WatchFolderIds.Count == 0)
        {
            result.Error("At least one watch folder is required.", $"{path}.WatchFolderIds");
        }

        if (rule.Conditions.All(condition => !condition.Enabled))
        {
            result.Warning("Rule has no enabled conditions and may match every email.", $"{path}.Conditions");
        }

        ValidateDestination(rule, result, $"{path}.Destination", settings);

        var extractionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rule.Extractions.Count; i++)
        {
            var extraction = rule.Extractions[i];
            var extractionPath = $"{path}.Extractions[{i}]";

            if (string.IsNullOrWhiteSpace(extraction.Name) || !TokenNamePattern.IsMatch(extraction.Name))
            {
                result.Error("Extraction token name must start with a letter and contain only letters, numbers, and underscores.", $"{extractionPath}.Name");
            }
            else if (!extractionNames.Add(extraction.Name))
            {
                result.Error($"Duplicate extraction token '{extraction.Name}'.", $"{extractionPath}.Name");
            }

            if (string.IsNullOrWhiteSpace(extraction.Pattern))
            {
                result.Error("Extraction pattern is required.", $"{extractionPath}.Pattern");
            }

            if (extraction.Method == ExtractionMethod.RegexCapture)
            {
                ValidateRegex(extraction.Pattern, result, $"{extractionPath}.Pattern");
            }
        }

        for (var i = 0; i < rule.Conditions.Count; i++)
        {
            var condition = rule.Conditions[i];
            if (!condition.Enabled)
            {
                continue;
            }

            if (RequiresValue(condition.Operator) && string.IsNullOrWhiteSpace(condition.Value))
            {
                result.Error("Condition value is required for this operator.", $"{path}.Conditions[{i}].Value");
            }

            if (condition.Operator is ConditionOperator.RegexMatch or ConditionOperator.RegexNotMatch)
            {
                ValidateRegex(condition.Value, result, $"{path}.Conditions[{i}].Value");
            }
        }

        for (var i = 0; i < rule.AttachmentFilters.Count; i++)
        {
            var filter = rule.AttachmentFilters[i];
            for (var e = 0; e < filter.Extensions.Count; e++)
            {
                var normalized = PathSanitizer.NormalizeExtension(filter.Extensions[e]);
                if (normalized.Length == 0 || !normalized.StartsWith('.'))
                {
                    result.Error("Attachment extensions must have a leading dot.", $"{path}.AttachmentFilters[{i}].Extensions[{e}]");
                }
            }

            if (filter.NamePatternIsRegex && !string.IsNullOrWhiteSpace(filter.NamePattern))
            {
                ValidateRegex(filter.NamePattern, result, $"{path}.AttachmentFilters[{i}].NamePattern");
            }
        }

        foreach (var token in TokenResolver.FindTokenNames(rule.Destination.FolderTemplate).Concat(TokenResolver.FindTokenNames(rule.Destination.FileNameTemplate)))
        {
            if (!TokenResolver.IsBuiltInToken(token) && !extractionNames.Contains(token))
            {
                result.Error($"Template references unknown token '{token}'.", $"{path}.Destination");
            }
        }
    }

    private static void ValidateDestination(Rule rule, ValidationResult result, string path, Settings? settings)
    {
        if (string.IsNullOrWhiteSpace(rule.Destination.Root))
        {
            result.Error("Destination root is required.", $"{path}.Root");
        }
        else if (!Path.IsPathFullyQualified(rule.Destination.Root))
        {
            result.Error("Destination root must be a Windows absolute path.", $"{path}.Root");
        }

        if (settings?.RestrictDestinationsToAllowedRoots == true &&
            !PathSanitizer.IsWithinAnyRoot(rule.Destination.Root, settings.AllowedDestinationRoots))
        {
            result.Error("Destination root is outside the configured allowed roots.", $"{path}.Root");
        }
    }

    private static bool RequiresValue(ConditionOperator op) =>
        op is not ConditionOperator.IsEmpty and not ConditionOperator.IsNotEmpty;

    private static void ValidateRegex(string pattern, ValidationResult result, string path)
    {
        try
        {
            _ = new Regex(pattern);
        }
        catch (ArgumentException ex)
        {
            result.Error($"Invalid regex: {ex.Message}", path);
        }
    }
}
