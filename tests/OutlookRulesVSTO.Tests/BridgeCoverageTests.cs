using OutlookRulesVSTO.Bridge;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Tests;

[TestClass]
public sealed class BridgeCoverageTests
{
    [TestMethod]
    public void BridgeEvaluatesPlansAndRecordsAllSummaryOutcomes()
    {
        var root = CreateRoot();
        var (settingsPath, rulesPath) = WriteConfiguration(root);
        var engine = new VstoBridgeRuleEngine();
        var message = (VstoBridgeMessage)engine.CreateMessage();
        message.EntryId = "entry-1";
        message.SenderEmail = "lab@example.com";
        message.SenderName = "Lab";
        message.Subject = "Lab result";
        message.BodyText = "BE12345678-1234";
        message.BodyHtml = "<p>BE12345678-1234</p>";
        message.ReceivedTime = new DateTime(2026, 7, 13, 10, 0, 0);
        message.AddAttachment(1, "result.pdf", 42, false);

        var evaluation = engine.EvaluateFromFiles(rulesPath, settingsPath, message, true);
        Assert.AreEqual(1, evaluation.PlanCount);
        StringAssert.Contains(evaluation.Summary, "match");
        var plan = evaluation.GetPlan(1);
        Assert.AreEqual("rule-1", plan.RuleId);
        Assert.AreEqual("Lab rule", plan.RuleName);
        Assert.AreEqual(1, plan.AttachmentIndex);
        Assert.AreEqual("result.pdf", plan.AttachmentName);
        Assert.AreEqual("Attachments", plan.SaveMode);
        Assert.AreEqual("Planned", plan.Status);
        Assert.IsNotEmpty(plan.DestinationPath);

        engine.RecordSaveResultFromFile(settingsPath, message, plan, "Saved", "ok", "", "");
        engine.RecordActionSummaryFromFile(settingsPath, message, true, 0, 0, 0);
        engine.RecordActionSummaryFromFile(settingsPath, message, false, 2, 1, 1);
        engine.RecordActionSummaryFromFile(settingsPath, message, true, 2, 2, 0);
        engine.RecordActionSummaryFromFile(settingsPath, message, false, 2, 2, 0);

        Assert.HasCount(2, CsvLogWriter.ReadCsv(Path.Combine(root, "history.csv")));
        var processingLog = Directory.GetFiles(Path.Combine(root, "logs"), "processing-*.csv").Single();
        Assert.HasCount(6, CsvLogWriter.ReadCsv(processingLog));
        Directory.Delete(root, true);
    }

    [TestMethod]
    public void BridgeTranslatesNoMatchAndFilterMessages()
    {
        var rule = Rule();
        var noMatch = new VstoBridgeEvaluation([new RuleEvaluationResult
        {
            Rule = rule,
            IsMatch = false,
            Messages = ["Conditions did not match."]
        }]);
        var filter = new VstoBridgeEvaluation([new RuleEvaluationResult
        {
            Rule = rule,
            IsMatch = true,
            Messages = ["No attachments matched the filter requirements."]
        }]);
        var count = new VstoBridgeEvaluation([new RuleEvaluationResult
        {
            Rule = rule,
            Messages = ["Attachment count exceeds configured maximum."]
        }]);
        var fallbackMatch = new VstoBridgeEvaluation([new RuleEvaluationResult { Rule = rule, IsMatch = true }]);
        var fallbackNoMatch = new VstoBridgeEvaluation([new RuleEvaluationResult { Rule = rule, IsMatch = false }]);

        StringAssert.Contains(noMatch.Summary, "Conditions did not match");
        StringAssert.Contains(filter.Summary, "No attachments matched");
        StringAssert.Contains(count.Summary, "Attachment count exceeds");
        StringAssert.Contains(fallbackMatch.Summary, "No save action created");
        StringAssert.Contains(fallbackNoMatch.Summary, "No match");
    }

    [TestMethod]
    public void BridgeForwardsXmlContractAndRejectsForeignComObjects()
    {
        var root = CreateRoot();
        var (settingsPath, rulesPath) = WriteConfiguration(root);
        var engine = new VstoBridgeRuleEngine();
        var messageXml = "<Message entryId='m' receivedTime='2026-07-13T00:00:00Z'><Sender email='lab@example.com' name='Lab'/><Subject>Lab result</Subject><BodyText>BE12345678-1234</BodyText><BodyHtml/><Attachments><Attachment index='1' name='result.pdf' sizeKb='1' isInline='false'/></Attachments></Message>";
        var evaluated = engine.EvaluateXml(File.ReadAllText(rulesPath), File.ReadAllText(settingsPath), messageXml, true);
        StringAssert.Contains(evaluated, "Evaluation");
        engine.RecordSaveResult(File.ReadAllText(settingsPath), messageXml, "r", "Rule", 1, "p", "Saved", "ok", "", "");

        Assert.ThrowsExactly<ArgumentException>(() => engine.EvaluateFromFiles(rulesPath, settingsPath, new ForeignMessage(), true));
        Assert.ThrowsExactly<ArgumentException>(() => engine.RecordSaveResultFromFile(settingsPath, new ForeignMessage(), new ForeignPlan(), "x", "x", "", ""));
        Assert.ThrowsExactly<ArgumentException>(() => engine.RecordActionSummaryFromFile(settingsPath, new ForeignMessage(), false, 0, 0, 0));
        Directory.Delete(root, true);
    }

    private static (string Settings, string Rules) WriteConfiguration(string root)
    {
        var settingsPath = Path.Combine(root, "settings.xml");
        var rulesPath = Path.Combine(root, "rules.xml");
        var settings = new Settings
        {
            StorageRoot = root,
            RulesPath = rulesPath,
            LogsPath = Path.Combine(root, "logs"),
            ProcessedHistoryPath = Path.Combine(root, "history.csv"),
            DefaultDestinationRoot = root,
            AllowedDestinationRoots = [root],
            RestrictDestinationsToAllowedRoots = true
        };
        var rule = Rule();
        rule.Destination.Root = root;
        RuleConfigurationXml.SaveSettingsAtomic(settingsPath, settings);
        RuleConfigurationXml.SaveRulesAtomic(rulesPath, new RuleConfiguration { Rules = [rule] });
        return (settingsPath, rulesPath);
    }

    private static Rule Rule() => new()
    {
        RuleId = "rule-1",
        Name = "Lab rule",
        Enabled = true,
        Conditions = [new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Contains, Value = "Lab" }],
        Extractions = [new Extraction { Name = "Beslagnummer", Source = ExtractionSource.BodyText, Method = ExtractionMethod.RegexCapture, Pattern = @"(BE\d{8}-\d{4})", CaptureGroup = "1", Required = true }],
        SaveMode = SaveMode.Attachments,
        Destination = new Destination { Root = Path.GetTempPath(), FolderTemplate = "{Beslagnummer}", FileNameTemplate = "{OriginalFileName}" }
    };

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "outlookrulesvsto-bridge-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class ForeignMessage : IVstoBridgeMessage
    {
        public string EntryId { get; set; } = "";
        public string SenderEmail { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string Subject { get; set; } = "";
        public string BodyText { get; set; } = "";
        public string BodyHtml { get; set; } = "";
        public DateTime ReceivedTime { get; set; }
        public void AddAttachment(int index, string name, long sizeKb, bool isInline) { }
    }

    private sealed class ForeignPlan : IVstoBridgeSavePlan
    {
        public string RuleId => "";
        public string RuleName => "";
        public int AttachmentIndex => 0;
        public string AttachmentName => "";
        public string SaveMode => "";
        public string DestinationPath => "";
        public string Status => "";
        public string Message => "";
    }
}
