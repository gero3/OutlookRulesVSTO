namespace OutlookRulesVSTO.Core;

public static class SampleData
{
    public static RuleConfiguration CreateDefaultRules()
    {
        var labRoot = @"C:\MailArchive\Customers";
        var rule = new Rule
        {
            RuleId = "11111111-1111-1111-1111-111111111111",
            Name = "Laboresultaten",
            Description = "Saves lab-result PDFs under the extracted case number.",
            Enabled = true,
            Priority = 10,
            StopProcessingOnMatch = true,
            MatchMode = RuleMatchMode.AllConditions,
            DuplicatePolicy = DuplicatePolicy.RenameCounter,
            AuditLevel = AuditLevel.Summary,
            Conditions =
            [
                new Condition
                {
                    Target = ConditionTarget.Subject,
                    Operator = ConditionOperator.StartsWith,
                    Value = "Full test report"
                },
                new Condition
                {
                    Target = ConditionTarget.HasAttachments,
                    Operator = ConditionOperator.Equals,
                    Value = "true"
                }
            ],
            Extractions =
            [
                new Extraction
                {
                    Name = "CaseNumber",
                    Source = ExtractionSource.BodyText,
                    Method = ExtractionMethod.LineStartsWith,
                    Pattern = "Case number:",
                    Required = true,
                    SanitizeForPath = true
                }
            ],
            AttachmentFilters =
            [
                new AttachmentFilter
                {
                    Extensions = [".pdf"],
                    ExcludeInline = true,
                    RequiredCount = 1
                }
            ],
            Destination = new Destination
            {
                Root = labRoot,
                FolderTemplate = @"{CaseNumber}\Lab results",
                FileNameTemplate = "{OriginalFileName}",
                ReviewRoot = Path.Combine(labRoot, "_NeedsReview"),
                ReviewFolderTemplate = "Lab results",
                SaveFailedExtractionsToReviewFolder = true
            }
        };

        return new RuleConfiguration { ConfigVersion = 1, Rules = [rule] };
    }

    public static Settings CreateDefaultSettings()
    {
        var settings = new Settings();
        settings.AllowedDestinationRoots.Add(settings.DefaultDestinationRoot);
        return settings;
    }
}
