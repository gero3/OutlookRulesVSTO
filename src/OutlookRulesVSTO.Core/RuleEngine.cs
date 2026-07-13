namespace OutlookRulesVSTO.Core;

public sealed class RuleEngine
{
    private readonly Settings _settings;

    public RuleEngine(Settings settings)
    {
        _settings = settings;
    }

    public List<RuleEvaluationResult> Evaluate(RuleConfiguration configuration, MessageContext message, bool dryRun = true)
    {
        var results = new List<RuleEvaluationResult>();
        foreach (var rule in configuration.Rules.Where(rule => rule.Enabled).OrderBy(rule => rule.Priority))
        {
            var result = EvaluateRule(rule, message, dryRun);
            results.Add(result);

            if (result.IsMatch && rule.StopProcessingOnMatch)
            {
                break;
            }
        }

        return results;
    }

    public RuleEvaluationResult EvaluateRule(Rule rule, MessageContext message, bool dryRun = true)
    {
        foreach (var attachment in message.Attachments)
        {
            attachment.Normalize();
        }

        var result = new RuleEvaluationResult { Rule = rule };
        if (message.Attachments.Count > _settings.MaximumAttachmentCount)
        {
            result.Messages.Add("Attachment count exceeds configured maximum.");
            return result;
        }

        if (!ConditionEvaluator.RuleMatches(rule, message))
        {
            result.Messages.Add("Conditions did not match.");
            return result;
        }

        if (rule.SaveMode == SaveMode.MessageHtml)
        {
            result.IsMatch = true;
            result.Extractions = ExtractionEngine.ExtractAll(rule, message);
            if (!result.Extractions.Success && !rule.Destination.SaveFailedExtractionsToReviewFolder)
            {
                result.Messages.AddRange(result.Extractions.Errors);
                return result;
            }

            result.SavePlans.Add(BuildMessageHtmlPlan(rule, message, result.Extractions, dryRun));
            return result;
        }

        result.MatchingAttachments = AttachmentFilterEvaluator.MatchingAttachments(rule, message)
            .Where(attachment => attachment.SizeKb <= _settings.MaximumAttachmentSizeKb)
            .ToList();
        if (result.MatchingAttachments.Count == 0 || !AttachmentFilterEvaluator.MeetsRequiredCounts(rule, result.MatchingAttachments))
        {
            result.Messages.Add("No attachments matched the filter requirements.");
            return result;
        }

        result.IsMatch = true;
        result.Extractions = ExtractionEngine.ExtractAll(rule, message);
        if (!result.Extractions.Success && !rule.Destination.SaveFailedExtractionsToReviewFolder)
        {
            result.Messages.AddRange(result.Extractions.Errors);
            return result;
        }

        foreach (var attachment in result.MatchingAttachments)
        {
            var plan = BuildPlan(rule, message, attachment, result.Extractions, dryRun);
            plan.AttachmentIndex = attachment.Index;
            result.SavePlans.Add(plan);
        }

        return result;
    }

    private SavePlan BuildMessageHtmlPlan(Rule rule, MessageContext message, ExtractionResult extractions, bool dryRun)
    {
        var placeholder = new AttachmentContext { Name = "message.html", Extension = ".html", Index = 0 };
        var plan = BuildPlan(rule, message, placeholder, extractions, dryRun);
        plan.AttachmentIndex = 0;
        plan.AttachmentName = "message.html";
        plan.SaveMode = SaveMode.MessageHtml;
        return plan;
    }

    private SavePlan BuildPlan(Rule rule, MessageContext message, AttachmentContext attachment, ExtractionResult extractions, bool dryRun)
    {
        var usingReview = !extractions.Success && rule.Destination.SaveFailedExtractionsToReviewFolder;
        var root = usingReview
            ? FirstNonEmpty(rule.Destination.ReviewRoot, Path.Combine(rule.Destination.Root, rule.Destination.ReviewFolderTemplate))
            : rule.Destination.Root;

        var folderTemplate = usingReview ? rule.Destination.ReviewFolderTemplate : rule.Destination.FolderTemplate;
        var relativeFolder = TokenResolver.Resolve(folderTemplate, rule, message, attachment, extractions.Values, sanitizeSegments: true);
        var destinationFolder = Path.GetFullPath(Path.Combine(root, relativeFolder));

        if (_settings.RestrictDestinationsToAllowedRoots && !PathSanitizer.IsWithinAnyRoot(destinationFolder, _settings.AllowedDestinationRoots))
        {
            return new SavePlan
            {
                RuleId = rule.RuleId,
                RuleName = rule.Name,
                AttachmentName = attachment.Name,
                DestinationFolder = destinationFolder,
                DestinationPath = destinationFolder,
                Status = SavePlanStatus.Failed,
                Message = "Destination is outside allowed roots.",
                IsDryRun = dryRun
            };
        }

        var resolvedName = string.IsNullOrWhiteSpace(rule.Destination.FileNameTemplate)
            ? attachment.Name
            : TokenResolver.Resolve(rule.Destination.FileNameTemplate, rule, message, attachment, extractions.Values, sanitizeSegments: false);

        if (string.IsNullOrWhiteSpace(Path.GetExtension(resolvedName)))
        {
            resolvedName += attachment.Extension;
        }

        var plan = DuplicatePlanner.Plan(destinationFolder, resolvedName, rule.DuplicatePolicy, createDestinationFolder: !dryRun);
        plan.RuleId = rule.RuleId;
        plan.RuleName = rule.Name;
        plan.AttachmentName = attachment.Name;
        plan.SaveMode = rule.SaveMode;
        plan.IsDryRun = dryRun;

        if (usingReview)
        {
            plan.Message = string.Join(" ", extractions.Errors.Prepend("Review folder selected because extraction failed."));
        }

        return plan;
    }

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
