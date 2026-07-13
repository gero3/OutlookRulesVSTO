using System.Runtime.InteropServices;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Bridge;

[ComVisible(true)]
[Guid("F464925A-8222-4831-A845-DAC5FB1BB856")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IVstoBridgeRuleEngine
{
    IVstoBridgeMessage CreateMessage();
    IVstoBridgeEvaluation EvaluateFromFiles(string rulesPath, string settingsPath, IVstoBridgeMessage message, bool dryRun);
    void RecordSaveResultFromFile(string settingsPath, IVstoBridgeMessage message, IVstoBridgeSavePlan plan, string status, string messageText, string errorNumber, string errorDescription);
    void RecordActionSummaryFromFile(string settingsPath, IVstoBridgeMessage message, bool dryRun, int planCount, int succeededCount, int failedCount);
    string EvaluateXml(string rulesXml, string settingsXml, string messageXml, bool dryRun);
    void RecordSaveResult(string settingsXml, string messageXml, string ruleId, string ruleName, int attachmentIndex, string destinationPath, string status, string message, string errorNumber, string errorDescription);
}

[ComVisible(true)]
[Guid("9DBDF8BA-6F3A-4C7C-9304-46B6CB852075")]
[ProgId("OutlookRulesVSTO.RuleEngine")]
[ClassInterface(ClassInterfaceType.None)]
public sealed class VstoBridgeRuleEngine : IVstoBridgeRuleEngine
{
    public IVstoBridgeMessage CreateMessage() => new VstoBridgeMessage();

    public IVstoBridgeEvaluation EvaluateFromFiles(string rulesPath, string settingsPath, IVstoBridgeMessage message, bool dryRun)
    {
        if (message is not VstoBridgeMessage request) throw new ArgumentException("Expected a VstoBridgeMessage request.", nameof(message));
        var settings = RuleConfigurationXml.LoadSettings(settingsPath);
        var rules = RuleConfigurationXml.LoadRules(rulesPath);
        var results = new RuleEngine(settings).Evaluate(rules, request.ToContext(), dryRun);
        return new VstoBridgeEvaluation(results);
    }

    public void RecordSaveResultFromFile(string settingsPath, IVstoBridgeMessage message, IVstoBridgeSavePlan plan, string status, string messageText, string errorNumber, string errorDescription)
    {
        if (message is not VstoBridgeMessage request || plan is not VstoBridgeSavePlan savePlan) throw new ArgumentException("Invalid COM request or save plan.");
        var settings = RuleConfigurationXml.LoadSettings(settingsPath);
        var attachment = request.ToContext().Attachments.SingleOrDefault(item => item.Index == savePlan.AttachmentIndex);
        CsvLogWriter.AppendProcessingLog(settings.LogsPath, new ProcessingLogRow(DateTime.Now, "AttachmentSave", savePlan.RuleId, savePlan.RuleName, request.EntryId, request.SenderEmail, request.Subject, attachment?.Name ?? "", savePlan.DestinationPath, status, messageText, errorNumber, errorDescription));
        if (string.Equals(status, "Saved", StringComparison.OrdinalIgnoreCase)) CsvLogWriter.AppendProcessedHistory(settings.ProcessedHistoryPath, new ProcessedHistoryRow(DateTime.Now, request.EntryId, savePlan.RuleId, attachment?.Name ?? "", savePlan.DestinationPath, "Saved"));
    }

    public void RecordActionSummaryFromFile(string settingsPath, IVstoBridgeMessage message, bool dryRun, int planCount, int succeededCount, int failedCount)
    {
        if (message is not VstoBridgeMessage request) throw new ArgumentException("Invalid COM request.", nameof(message));
        var settings = RuleConfigurationXml.LoadSettings(settingsPath);
        string status;
        string summary;
        if (planCount == 0)
        {
            status = "NoMatch";
            summary = "No active rule matched; nothing was saved.";
        }
        else if (failedCount > 0)
        {
            status = "Error";
            summary = $"{(dryRun ? "Test" : "Verwerking")} voltooid: {succeededCount} geslaagd, {failedCount} mislukt.";
        }
        else
        {
            status = "Success";
            summary = dryRun
                ? $"Test voltooid: {planCount} opslagactie(s) gepland."
                : $"Verwerking voltooid: {succeededCount} opslagactie(s) saved.";
        }
        CsvLogWriter.AppendProcessingLog(settings.LogsPath, new ProcessingLogRow(
            DateTime.Now, dryRun ? "OutlookSelectionTest" : "OutlookSelectionProcess", "", "",
            request.EntryId, request.SenderEmail, request.Subject, "", "", status, summary));
    }
    public string EvaluateXml(string rulesXml, string settingsXml, string messageXml, bool dryRun) =>
        VstoBridgeRuleEngineContract.EvaluateXml(rulesXml, settingsXml, messageXml, dryRun);

    public void RecordSaveResult(string settingsXml, string messageXml, string ruleId, string ruleName, int attachmentIndex, string destinationPath, string status, string message, string errorNumber, string errorDescription) =>
        VstoBridgeRuleEngineContract.RecordSaveResult(settingsXml, messageXml, ruleId, ruleName, attachmentIndex, destinationPath, status, message, errorNumber, errorDescription);
}
