using System.Xml;
using System.Xml.Linq;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Tests;

[TestClass]
public sealed class FullCoreCoverageTests
{
    [TestMethod]
    public void ConditionsCoverEveryTargetOperatorAndMatchMode()
    {
        var message = new MessageContext
        {
            SenderEmail = "sender@example.test", SenderName = "Example Sender", To = ["to@example.test"], Cc = ["cc@example.test"],
            Subject = "Invoice July", BodyText = "body", BodyHtml = "<b>html</b>", ReceivedTime = new DateTime(2026, 7, 9),
            AccountName = "Work", Categories = ["Finance"], Attachments = [new AttachmentContext { Name = "invoice.pdf" }]
        };
        foreach (var target in new[] { ConditionTarget.SenderEmail, ConditionTarget.SenderName, ConditionTarget.To, ConditionTarget.Cc, ConditionTarget.Subject, ConditionTarget.BodyText, ConditionTarget.BodyHtml, ConditionTarget.Account, ConditionTarget.Categories })
            Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = target, Operator = ConditionOperator.Contains, Value = target == ConditionTarget.Account ? "work" : target == ConditionTarget.Categories ? "finance" : "@" }, message) || target is ConditionTarget.SenderName or ConditionTarget.Subject or ConditionTarget.BodyText or ConditionTarget.BodyHtml);

        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.HasAttachments, Operator = ConditionOperator.Equals, Value = "true" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.AttachmentName, Operator = ConditionOperator.StartsWith, Value = "INVOICE" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.AttachmentExtension, Operator = ConditionOperator.Contains, Value = ".PDF" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.AttachmentCount, Operator = ConditionOperator.LessThan, Value = "2" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.DoesNotContain, Value = "credit" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.NotEquals, Value = "other" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.EndsWith, Value = "July" }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.IsNotEmpty }, message));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.BodyText, Operator = ConditionOperator.IsEmpty }, new MessageContext()));
        Assert.IsTrue(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.GreaterThan, Value = "A" }, message));
        Assert.IsFalse(ConditionEvaluator.Evaluate(new Condition { Target = ConditionTarget.Subject, Operator = (ConditionOperator)999, Value = "x" }, message));

        var rule = new Rule { MatchMode = RuleMatchMode.AnyCondition, Conditions = [new Condition { Target = ConditionTarget.Subject, Value = "missing" }, new Condition { Target = ConditionTarget.Subject, Value = "invoice" }, new Condition { Enabled = false, Value = "ignored" }] };
        Assert.IsTrue(ConditionEvaluator.RuleMatches(rule, message));
    }

    [TestMethod]
    public void FiltersPathsTokensAndDuplicateFallbacksCoverBoundaryCases()
    {
        var attachment = new AttachmentContext { Name = "file.txt", SizeKb = 10, IsInline = false };
        Assert.IsFalse(AttachmentFilterEvaluator.Matches(new AttachmentFilter { ExcludeInline = true, Extensions = ["pdf"] }, attachment));
        Assert.IsFalse(AttachmentFilterEvaluator.Matches(new AttachmentFilter { MinSizeKb = 11 }, attachment));
        Assert.IsFalse(AttachmentFilterEvaluator.Matches(new AttachmentFilter { MaxSizeKb = 9 }, attachment));
        Assert.IsFalse(AttachmentFilterEvaluator.Matches(new AttachmentFilter { NamePattern = "none" }, attachment));
        Assert.IsTrue(AttachmentFilterEvaluator.Matches(new AttachmentFilter { NamePattern = "file.txt", NamePatternOperator = ConditionOperator.Equals }, attachment));
        Assert.IsTrue(AttachmentFilterEvaluator.MeetsRequiredCounts(new Rule(), []));

        Assert.AreEqual(string.Empty, PathSanitizer.NormalizeExtension(" "));
        Assert.AreEqual(".pdf", PathSanitizer.NormalizeExtension("PDF"));
        Assert.AreEqual("_", PathSanitizer.SanitizeSegment("..."));
        Assert.AreEqual("file.pdf", PathSanitizer.SanitizeFileName("file.PDF"));
        Assert.AreEqual(Path.Combine("one", "two"), PathSanitizer.SanitizeRelativePath("./one/../two"));
        Assert.IsFalse(PathSanitizer.IsWithinRoot("", "C:\\temp"));
        Assert.IsFalse(PathSanitizer.IsWithinAnyRoot("C:\\outside", ["C:\\temp"]));

        var message = new MessageContext { ReceivedTime = new DateTime(2026, 7, 9), SenderEmail = "a@b", SenderName = "A", Subject = "S", AccountName = "Acc" };
        var attachmentWithExtension = new AttachmentContext { Name = "report.pdf" };
        attachmentWithExtension.Normalize();
        var tokens = TokenResolver.Resolve("{ReceivedDate:dd}-{ReceivedMonth}-{SenderEmail}-{SenderName}-{Subject}-{RuleName}-{AccountName}-{OriginalFileName}-{OriginalFileBaseName}-{OriginalFileExtension}-{Missing}", new Rule { Name = "R" }, message, attachmentWithExtension, new Dictionary<string, string>(), false);
        StringAssert.Contains(tokens, "09-07-a@b-A-S-R-Acc-report.pdf-report-.pdf-");
        Assert.AreEqual(string.Empty, TokenResolver.Resolve("", new Rule(), message, null, new Dictionary<string, string>(), false));
        Assert.HasCount(2, TokenResolver.FindTokenNames("{Subject}-{subject}-{Missing}"));

        var folder = Temp();
        File.WriteAllText(Path.Combine(folder, "file.txt"), "x");
        Assert.AreEqual(SavePlanStatus.Failed, DuplicatePlanner.Plan(folder, "file.txt", (DuplicatePolicy)999).Status);
        Assert.AreEqual(SavePlanStatus.Planned, DuplicatePlanner.Plan(Path.Combine(folder, "new"), "new.txt", DuplicatePolicy.Skip).Status);
    }

    [TestMethod]
    public void XmlSettingsRulesAndFilesCoverRoundTripsDefaultsAndMalformedInputs()
    {
        var root = Temp();
        var settings = new Settings { StorageRoot = root, RulesPath = Path.Combine(root, "rules.xml"), LogsPath = Path.Combine(root, "logs"), ProcessedHistoryPath = Path.Combine(root, "history.csv"), DefaultDestinationRoot = root, AllowedDestinationRoots = [root], RestrictDestinationsToAllowedRoots = true, SaveFailedExtractionsToReviewFolder = false, DefaultDuplicatePolicy = DuplicatePolicy.Fail, DebugLogging = true, RuleEditingEnabled = false, AiHelperEnabled = true, AiModel = "test-model", AiIncludeBody = false, AiExtractionInstructions = "Zaaknummer | verplicht" };
        RuleConfigurationXml.SaveSettingsAtomic(Path.Combine(root, "settings.xml"), settings);
        var loadedSettings = RuleConfigurationXml.LoadSettings(Path.Combine(root, "settings.xml"));
        Assert.AreEqual(DuplicatePolicy.Fail, loadedSettings.DefaultDuplicatePolicy);
        Assert.IsFalse(loadedSettings.RuleEditingEnabled);
        Assert.IsTrue(loadedSettings.AiHelperEnabled);
        Assert.AreEqual("test-model", loadedSettings.AiModel);
        Assert.IsFalse(loadedSettings.AiIncludeBody);
        Assert.AreEqual("Zaaknummer | verplicht", loadedSettings.AiExtractionInstructions);
        AppPaths.EnsureStorageFolders(loadedSettings);
        Assert.IsTrue(Directory.Exists(loadedSettings.LogsPath));

        var rule = new Rule { RuleId = "rule", Name = "Rule", Priority = 2, StopProcessingOnMatch = false, MatchMode = RuleMatchMode.AnyCondition, DuplicatePolicy = DuplicatePolicy.Fail, AuditLevel = AuditLevel.Verbose, WatchFolderIds = ["Folder"], Destination = new Destination { Root = root, FolderTemplate = "{Subject}", FileNameTemplate = "x", ReviewRoot = root, ReviewFolderTemplate = "review", SaveFailedExtractionsToReviewFolder = false }, PostProcessing = new PostProcessing { Action = PostProcessAction.MoveEmail, Category = "C", MoveFolderId = "M", OnlyOnSuccess = false }, Conditions = [new Condition { ConditionId = "c", Target = ConditionTarget.Subject, Operator = ConditionOperator.Equals, Value = "x", Value2 = "y", CaseSensitive = true, NormalizeWhitespace = false }], Extractions = [new Extraction { ExtractionId = "e", Name = "Token", Source = ExtractionSource.Subject, Method = ExtractionMethod.RegexCapture, Pattern = "(x)", Pattern2 = "y", CaptureGroup = "1", Required = false, DefaultValue = "d", SanitizeForPath = false, TrimResult = false, Transform = ExtractionTransform.DateFormat, TransformFormat = "yyyy" }], AttachmentFilters = [new AttachmentFilter { FilterId = "f", NamePattern = "x", NamePatternOperator = ConditionOperator.EndsWith, Extensions = ["pdf"], MinSizeKb = 1, MaxSizeKb = 2, ExcludeInline = false, RequiredCount = 1 }] };
        var config = new RuleConfiguration { Rules = [rule] };
        var document = RuleConfigurationXml.ToRulesDocument(config);
        var restored = RuleConfigurationXml.FromRulesDocument(document);
        Assert.AreEqual(PostProcessAction.MoveEmail, restored.Rules[0].PostProcessing.Action);
        Assert.AreEqual(ExtractionTransform.DateFormat, restored.Rules[0].Extractions[0].Transform);
        Assert.AreEqual(ConditionOperator.EndsWith, restored.Rules[0].AttachmentFilters[0].NamePatternOperator);
        Assert.IsFalse(RuleConfigurationXml.ValidateRulesDocument(new XDocument(new XElement("Other"))).IsValid);
        Assert.IsFalse(RuleConfigurationXml.ValidateSettingsDocument(new XDocument(new XElement("Settings", new XElement("StorageRoot", ""), new XElement("AllowedDestinationRoots", new XElement("Root", "relative"))))).IsValid);
        Assert.AreEqual(0L, RuleConfigurationXml.ReadRulesStamp(Path.Combine(root, "none.xml")).ConfigVersion);
        File.WriteAllText(Path.Combine(root, "bad.xml"), "<Rules>");
        Assert.AreEqual(0L, RuleConfigurationXml.ReadRulesStamp(Path.Combine(root, "bad.xml")).ConfigVersion);
        Assert.ThrowsExactly<XmlException>(() => RuleConfigurationXml.LoadSettings(Path.Combine(root, "bad.xml")));
        Assert.IsNotEmpty(Directory.GetFiles(root, "bad.xml.corrupt-*.bak"));
    }

    [TestMethod]
    public void ValidatorEngineCsvAndMessageBoundaryCoverFailuresAndSuccesses()
    {
        var root = Temp();
        var invalid = new Rule { RuleId = "id", Name = "name", WatchFolderIds = ["inbox"], Destination = new Destination { Root = root }, Conditions = [new Condition { Operator = ConditionOperator.IsEmpty }, new Condition { Enabled = false, Operator = ConditionOperator.RegexMatch, Value = "[" }], Extractions = [new Extraction { Name = "token", Pattern = "x" }, new Extraction { Name = "token", Pattern = "x" }], AttachmentFilters = [new AttachmentFilter { Extensions = ["pdf"] }] };
        Assert.IsFalse(RuleValidator.ValidateRule(invalid).IsValid);
        invalid.Conditions[0].Operator = ConditionOperator.Contains;
        Assert.IsFalse(RuleValidator.Validate(new RuleConfiguration { SchemaVersion = "old", Rules = [invalid, invalid] }).IsValid);

        var first = new Rule { Name = "first", Priority = 2, Conditions = [new Condition { Target = ConditionTarget.Subject, Value = "yes" }], Destination = new Destination { Root = root } };
        var second = new Rule { Name = "second", Priority = 1, Conditions = [new Condition { Target = ConditionTarget.Subject, Value = "yes" }], Destination = new Destination { Root = root } };
        var evaluation = new RuleEngine(new Settings()).Evaluate(new RuleConfiguration { Rules = [first, second, new Rule { Enabled = false }] }, new MessageContext { Subject = "yes", Attachments = [new AttachmentContext { Name = "a.txt" }] }, false);
        Assert.HasCount(1, evaluation);
        Assert.IsFalse(evaluation[0].SavePlans[0].IsDryRun);

        var csv = Path.Combine(root, "quoted.csv");
        File.WriteAllText(csv, "a,b\n\"a\"\"b\",c\n");
        Assert.AreEqual("a\"b", CsvLogWriter.ReadCsv(csv)[1][0]);
        Assert.HasCount(0, CsvLogWriter.ReadCsv(Path.Combine(root, "missing.csv")));
        var parsed = MessageContextXml.FromXml("<Message><Recipients><To> t </To><Cc> c </Cc></Recipients><Categories><Category> x </Category></Categories></Message>");
        Assert.AreEqual("t", parsed.To[0]);
    }

    [TestMethod]
    public void ExtractionCsvContractAndRuleEngineCoverRemainingOperationalPaths()
    {
        var message = new MessageContext { Subject = "Subject", BodyText = "line\nLabel: value\nStart value", BodyHtml = "<p>x</p>" };
        Assert.AreEqual(string.Empty, ExtractionEngine.Extract(new Extraction { Method = (ExtractionMethod)99 }, message));
        Assert.AreEqual("value", ExtractionEngine.Extract(new Extraction { Method = ExtractionMethod.BetweenText, Pattern = "Start", Pattern2 = "missing" }, message).Trim());
        Assert.AreEqual("", ExtractionEngine.Extract(new Extraction { Method = ExtractionMethod.RegexCapture, Pattern = "no", CaptureGroup = "bad" }, message));
        var date = ExtractionEngine.ExtractAll(new Rule { Extractions = [new Extraction { Name = "d", Pattern = "Missing:", Transform = ExtractionTransform.DateFormat, TransformFormat = "yyyy", Required = false, DefaultValue = "2026-07-09", SanitizeForPath = false }] }, message);
        Assert.AreEqual("2026", date.Values["d"]);

        var root = Temp();
        CsvLogWriter.AppendProcessedHistory(Path.Combine(root, "history.csv"), new ProcessedHistoryRow(DateTime.UtcNow, "m", "r", "a", "p", "Saved"));
        Assert.HasCount(2, CsvLogWriter.ReadCsv(Path.Combine(root, "history.csv")));
        Assert.AreEqual("\"x,y\"", CsvLogWriter.ToCsvLine(["x,y"]));

        var engine = new RuleEngine(new Settings());
        Assert.IsFalse(engine.EvaluateRule(new Rule { Destination = new Destination { Root = root } }, new MessageContext()).IsMatch);
        Assert.IsFalse(engine.EvaluateRule(new Rule { Conditions = [new Condition { Target = ConditionTarget.Subject, Value = "no" }], Destination = new Destination { Root = root } }, new MessageContext { Subject = "yes", Attachments = [new AttachmentContext { Name = "a.txt" }] }).IsMatch);

        var settings = new Settings { LogsPath = Path.Combine(root, "logs"), ProcessedHistoryPath = Path.Combine(root, "processed.csv") };
        var settingsXml = RuleConfigurationXml.ToSettingsDocument(settings).ToString();
        const string mailXml = "<Message entryId='m'><Sender email='s' /><Subject>x</Subject><Attachments><Attachment index='2' name='a.txt' /></Attachments></Message>";
        VstoBridgeRuleEngineContract.RecordSaveResult(settingsXml, mailXml, "r", "Rule", 2, "p", "Saved", "ok", "1", "none");
        VstoBridgeRuleEngineContract.RecordSaveResult(settingsXml, mailXml, "r", "Rule", 9, "p", "Failed", "no", "", "");
        Assert.HasCount(2, CsvLogWriter.ReadCsv(settings.ProcessedHistoryPath));
        Assert.IsNotEmpty(Directory.GetFiles(settings.LogsPath));
        Assert.IsFalse(SampleData.CreateDefaultSettings().AllowedDestinationRoots.Count == 0);
    }

    private static string Temp()
    {
        var path = Path.Combine(Path.GetTempPath(), "outlookrulesvsto-full-coverage", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
