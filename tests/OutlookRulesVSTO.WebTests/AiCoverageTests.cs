using OutlookRulesVSTO.Core;
using OutlookRulesVSTO.Web;

namespace OutlookRulesVSTO.WebTests;

[TestClass]
public sealed class AiCoverageTests
{
    [TestMethod]
    public void DraftCleansUngroundedAndUnsafeConditions()
    {
        var mail = new AiMail("lab@example.com", "Uitslag salmonella", "Body marker", ["rapport_BE12345678-1234.pdf"]);
        var draft = new AiRuleDraft("Lab", "Beschrijving",
        [
            new("SenderEmail", "Contains", "wrong@example.com"),
            new("SenderName", "Contains", "Lab"),
            new("Subject", "Contains", "salmonella"),
            new("BodyText", "RegexMatch", "marker.*"),
            new("AttachmentName", "Contains", "BE12345678-1234"),
            new("Unknown", "Contains", "x"),
            new("Subject", "Contains", "missing")
        ], [new("Automatic")], "Resultaten");

        var rule = draft.ToRule(Settings(), mail);

        Assert.HasCount(3, rule.Conditions);
        Assert.AreEqual(ConditionTarget.SenderEmail, rule.Conditions[0].Target);
        Assert.AreEqual("lab@example.com", rule.Conditions[0].Value);
        Assert.AreEqual(ConditionTarget.Subject, rule.Conditions[1].Target);
        Assert.AreEqual(ConditionOperator.RegexMatch, rule.Conditions[2].Operator);
        Assert.AreEqual(@"{CaseNumber}\Resultaten", rule.Destination.FolderTemplate);
        Assert.AreEqual("{OriginalFileName}", rule.Destination.FileNameTemplate);
    }

    [TestMethod]
    public void DraftHandlesMessageOnlyAndExistingTokenFolder()
    {
        var draft = new AiRuleDraft("Mail", "Beschrijving",
            [new("AttachmentName", "Contains", "rapport"), new("SenderEmail", "Equals", "")],
            [new("Automatic")], @"{CaseNumber}\Mail");

        var rule = draft.ToRule(Settings(), new AiMail("", "", "", []));

        Assert.AreEqual(SaveMode.MessageHtml, rule.SaveMode);
        Assert.IsEmpty(rule.Conditions);
        Assert.IsEmpty(rule.AttachmentFilters);
        Assert.AreEqual(@"{CaseNumber}\Mail", rule.Destination.FolderTemplate);
        Assert.AreEqual("E-mail_{ReceivedDate:yyyyMMdd_HHmmss}.html", rule.Destination.FileNameTemplate);
    }

    [TestMethod]
    public void DraftUsesFallbackFolderAndRenamesOnlyGenericAttachments()
    {
        var generic = new AiRuleDraft("Generic", "", [], [new("Automatic")], " ")
            .ToRule(Settings(), new AiMail("a@b.test", "s", "b", ["rapport (2).pdf", "scan_3.pdf"]));
        var meaningful = new AiRuleDraft("Meaningful", "", [], [new("Automatic")], "Folder")
            .ToRule(Settings(), new AiMail("a@b.test", "s", "b", ["labresultaat.pdf"]));

        Assert.AreEqual("{CaseNumber}", generic.Destination.FolderTemplate);
        Assert.AreEqual("{RuleName}_{CaseNumber}_{ReceivedDate:yyyyMMdd}{OriginalFileExtension}", generic.Destination.FileNameTemplate);
        Assert.AreEqual("{OriginalFileName}", meaningful.Destination.FileNameTemplate);
    }

    [TestMethod]
    public void DiagnosticLoggerWritesSafeFailureAndValidationEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "outlookrulesvsto-ai-log-tests", Guid.NewGuid().ToString("N"));
        var settings = Settings();
        settings.LogsPath = root;
        settings.AiModel = @"C:\Models\unsafe|model.gguf";

        AiDiagnosticLogger.Failure(settings, new InvalidOperationException("boom"));
        AiDiagnosticLogger.InvalidRule(settings, [new ValidationIssue(ValidationSeverity.Error, "bad", "Rule.Name")]);

        var text = File.ReadAllText(Directory.GetFiles(root, "ai-*.log").Single());
        StringAssert.Contains(text, "model=unsafe_model.gguf");
        StringAssert.Contains(text, "FAILED");
        StringAssert.Contains(text, "INVALID_RULE | Error: Rule.Name: bad");
        Directory.Delete(root, true);
    }

    [TestMethod]
    public void DiagnosticLoggerNeverMasksAnUnwritableLogPath()
    {
        var settings = Settings();
        settings.LogsPath = Path.GetTempPath();
        var file = Path.GetTempFileName();
        try
        {
            settings.LogsPath = file;
            AiDiagnosticLogger.Failure(settings, new Exception("ignored"));
        }
        finally { File.Delete(file); }
    }

    [TestMethod]
    public void BuilderResolvesExplicitModelAndDisposesCleanly()
    {
        var explicitPath = Path.Combine(Path.GetTempPath(), "model.gguf");
        Assert.AreEqual(explicitPath, AiRuleBuilder.ResolveModelPath(explicitPath));
        using var builder = new AiRuleBuilder();
        Assert.AreEqual("Not loaded", builder.Status);
    }

    private static Settings Settings() => new()
    {
        DefaultDestinationRoot = @"C:\Archive",
        SaveFailedExtractionsToReviewFolder = true
    };
}
