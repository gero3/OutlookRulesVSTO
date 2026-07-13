using System.Xml.Linq;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Tests;

[TestClass]
public sealed class CoreCoverageTests
{
    [TestMethod]
    public void ConditionsSupportWhitespaceRegexNumericDateAndAttachmentTargets()
    {
        var message = new MessageContext
        {
            Subject = "  Invoice\t42 ",
            ReceivedTime = new DateTime(2026, 7, 9),
            Attachments = [new AttachmentContext { Name = "Report.PDF" }]
        };

        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Equals, Value = "Invoice 42" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.RegexMatch, Value = "invoice\\s+42" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.AttachmentExtension, Operator = ConditionOperator.Equals, Value = "pdf;docx" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.AttachmentCount, Operator = ConditionOperator.Between, Value = "1", Value2 = "2" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.ReceivedDate, Operator = ConditionOperator.GreaterThan, Value = "2026-01-01" }, message));
        Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.RegexNotMatch, Value = "Invoice" }, message));
    }

    [TestMethod]
    public void AttachmentFiltersCoverInlineSizePatternAndRequiredCount()
    {
        var rule = new Rule { AttachmentFilters = [new AttachmentFilter { Extensions = ["pdf"], MinSizeKb = 10, MaxSizeKb = 100, NamePattern = "^report", NamePatternIsRegex = true, ExcludeInline = true, RequiredCount = 2 }] };
        var message = new MessageContext { Attachments = [new AttachmentContext { Name = "report.pdf", SizeKb = 20 }, new AttachmentContext { Name = "report-2.pdf", SizeKb = 30 }, new AttachmentContext { Name = "report-inline.pdf", SizeKb = 20, IsInline = true }, new AttachmentContext { Name = "report-small.pdf", SizeKb = 1 }] };

        var matching = AttachmentFilterEvaluator.MatchingAttachments(rule, message);

        Assert.HasCount(2, matching);
        Assert.IsTrue(AttachmentFilterEvaluator.MeetsRequiredCounts(rule, matching));
        Assert.IsFalse(AttachmentFilterEvaluator.MeetsRequiredCounts(rule, [matching[0]]));
    }

    [TestMethod]
    public void DuplicatePlannerCoversEveryPolicy()
    {
        var folder = CreateTempDirectory();
        File.WriteAllText(Path.Combine(folder, "report.pdf"), "existing");

        Assert.AreEqual(SavePlanStatus.Planned, DuplicatePlanner.Plan(folder, "report.pdf", DuplicatePolicy.RenameCounter).Status);
        Assert.AreEqual(SavePlanStatus.Planned, DuplicatePlanner.Plan(folder, "report.pdf", DuplicatePolicy.RenameTimestamp).Status);
        Assert.AreEqual(SavePlanStatus.Skipped, DuplicatePlanner.Plan(folder, "report.pdf", DuplicatePolicy.Skip).Status);
        Assert.AreEqual(SavePlanStatus.Planned, DuplicatePlanner.Plan(folder, "report.pdf", DuplicatePolicy.Overwrite).Status);
        Assert.AreEqual(SavePlanStatus.Failed, DuplicatePlanner.Plan(folder, "report.pdf", DuplicatePolicy.Fail).Status);
        var version = DuplicatePlanner.Plan(folder, "report.pdf", DuplicatePolicy.VersionFolder);
        Assert.AreEqual(SavePlanStatus.Planned, version.Status);
        StringAssert.Contains(version.DestinationFolder, Path.GetFileName(folder));
    }

    [TestMethod]
    public void PathsAndTokensAreSanitizedAndRootBounded()
    {
        var root = CreateTempDirectory();
        var message = new MessageContext { SenderEmail = "a/b@example.com", ReceivedTime = new DateTime(2026, 7, 9) };
        var rule = new Rule { Name = "CON", Destination = new Destination() };
        var resolved = TokenResolver.Resolve(@"{ReceivedDate:yyyy}\{SenderEmail}\..\{RuleName}", rule, message, null, new Dictionary<string, string>(), true);

        Assert.AreEqual(Path.Combine("2026", "a", "b@example.com", "_CON"), resolved);
        Assert.IsTrue(PathSanitizer.IsWithinRoot(Path.Combine(root, "child"), root));
        Assert.IsFalse(PathSanitizer.IsWithinRoot(root + "-sibling", root));
        Assert.IsTrue(PathSanitizer.IsPathTraversal("child/../secret"));
    }

    [TestMethod]
    public void ExtractionMethodsAndTransformsCoverAllSupportedModes()
    {
        var message = new MessageContext { Subject = "ID: abc", BodyText = "Label: value\nStart middle End\nCode 42", BodyHtml = "<p>HTML: &amp; value</p>" };
        var rule = new Rule { Extractions = [
            new Extraction { Name = "after", Pattern = "Label:", Method = ExtractionMethod.AfterLabelUntilLineEnd },
            new Extraction { Name = "line", Pattern = "Code", Method = ExtractionMethod.LineStartsWith, Transform = ExtractionTransform.Uppercase },
            new Extraction { Name = "between", Pattern = "Start", Pattern2 = "End", Method = ExtractionMethod.BetweenText, Transform = ExtractionTransform.ReplaceSpaces },
            new Extraction { Name = "regex", Source = ExtractionSource.Subject, Pattern = "ID: (\\w+)", Method = ExtractionMethod.RegexCapture, CaptureGroup = "1", Transform = ExtractionTransform.Lowercase },
            new Extraction { Name = "html", Source = ExtractionSource.BodyHtmlStripped, Pattern = "HTML:", Method = ExtractionMethod.AfterLabelUntilLineEnd }
        ] };

        var result = ExtractionEngine.ExtractAll(rule, message);

        Assert.IsTrue(result.Success, string.Join("; ", result.Errors));
        Assert.AreEqual("value", result.Values["after"]);
        Assert.AreEqual("42", result.Values["line"]);
        Assert.AreEqual("middle", result.Values["between"]);
        Assert.AreEqual("abc", result.Values["regex"]);
        Assert.AreEqual("& value", result.Values["html"]);
    }

    [TestMethod]
    public void XmlRoundTripMigrationAndAtomicWriterPreserveRules()
    {
        var folder = CreateTempDirectory();
        var path = Path.Combine(folder, "rules.xml");
        var configuration = SampleData.CreateDefaultRules();
        AtomicXmlWriter.Write(path, RuleConfigurationXml.ToRulesDocument(configuration), document => RuleValidator.Validate(RuleConfigurationXml.FromRulesDocument(document)));

        var migrated = RuleMigrator.Migrate(RuleConfigurationXml.LoadRules(path));

        Assert.AreEqual(Schema.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.HasCount(1, migrated.Rules);
        Assert.IsTrue(File.Exists(path));
    }

    [TestMethod]
    public void AtomicWriterRejectsInvalidDocumentWithoutReplacingActiveFile()
    {
        var folder = CreateTempDirectory();
        var path = Path.Combine(folder, "rules.xml");
        File.WriteAllText(path, "original");

        Assert.ThrowsExactly<InvalidDataException>(() => AtomicXmlWriter.Write(path, new XDocument(new XElement("Rules")), _ => InvalidValidation()));
        Assert.AreEqual("original", File.ReadAllText(path));
    }

    [TestMethod]
    public void ValidatorReportsRuleDestinationExtractionConditionAndFilterFailures()
    {
        var rule = new Rule
        {
            RuleId = "",
            Name = "",
            WatchFolderIds = [],
            Destination = new Destination { Root = "relative" },
            Conditions = [new Condition { Operator = ConditionOperator.RegexMatch, Value = "[" }],
            Extractions = [new Extraction { Name = "1bad", Pattern = "[", Method = ExtractionMethod.RegexCapture }],
            AttachmentFilters = [new AttachmentFilter { Extensions = [""], NamePattern = "[", NamePatternIsRegex = true }]
        };
        var settings = new Settings { RestrictDestinationsToAllowedRoots = true, AllowedDestinationRoots = [CreateTempDirectory()] };

        var validation = RuleValidator.Validate(new RuleConfiguration { Rules = [rule, RuleClone(rule)] }, settings);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Count >= 8);
    }

    [TestMethod]
    public void RuleEngineCoversNoConditionNoAttachmentReviewAndRestrictedDestinationPaths()
    {
        var root = CreateTempDirectory();
        var settings = new Settings { RestrictDestinationsToAllowedRoots = true, AllowedDestinationRoots = [root], MaximumAttachmentSizeKb = 10 };
        var message = new MessageContext { Subject = "no-match", Attachments = [new AttachmentContext { Index = 1, Name = "a.pdf", SizeKb = 20 }] };
        var engine = new RuleEngine(settings);
        var noConditionRule = new Rule { Destination = new Destination { Root = root }, AttachmentFilters = [] };
        var sizeResult = engine.EvaluateRule(noConditionRule, message);
        Assert.IsFalse(sizeResult.IsMatch);

        message.Attachments[0].SizeKb = 1;
        var reviewRule = new Rule
        {
            Destination = new Destination { Root = root + "-outside", ReviewRoot = root, SaveFailedExtractionsToReviewFolder = true },
            Extractions = [new Extraction { Name = "required", Pattern = "missing" }]
        };
        var reviewResult = engine.EvaluateRule(reviewRule, message);
        Assert.IsTrue(reviewResult.IsMatch);
        Assert.AreEqual(SavePlanStatus.Planned, reviewResult.SavePlans.Single().Status);

        reviewRule.Destination.SaveFailedExtractionsToReviewFolder = false;
        var failedExtraction = engine.EvaluateRule(reviewRule, message);
        Assert.IsTrue(failedExtraction.IsMatch);
        Assert.HasCount(0, failedExtraction.SavePlans);
    }

    [TestMethod]
    public void ConfigurationAndMessageXmlRejectInvalidRootsAndApplyFallbacks()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => MessageContextXml.FromXml("<Other />"));
        Assert.ThrowsExactly<InvalidDataException>(() => RuleConfigurationXml.FromRulesDocument(new XDocument(new XElement("Other"))));
        Assert.ThrowsExactly<InvalidDataException>(() => RuleConfigurationXml.FromSettingsDocument(new XDocument(new XElement("Other"))));

        var message = MessageContextXml.FromXml("<Message><Attachments><Attachment name='a.txt' index='bad' sizeKb='bad' isInline='bad' /></Attachments></Message>");
        Assert.AreEqual(1, message.Attachments[0].Index);
        Assert.AreEqual(0L, message.Attachments[0].SizeKb);
        Assert.IsFalse(message.Attachments[0].IsInline);
    }

    private static Rule RuleClone(Rule source) => new()
    {
        RuleId = source.RuleId,
        Name = source.Name,
        WatchFolderIds = [.. source.WatchFolderIds],
        Destination = source.Destination,
        Conditions = [.. source.Conditions],
        Extractions = [.. source.Extractions],
        AttachmentFilters = [.. source.AttachmentFilters]
    };

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "outlookrulesvsto-coverage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ValidationResult InvalidValidation()
    {
        var result = new ValidationResult();
        result.Error("intentional test failure");
        return result;
    }
}
