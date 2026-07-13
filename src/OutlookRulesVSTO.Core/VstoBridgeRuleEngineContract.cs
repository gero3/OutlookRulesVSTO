using System.Xml.Linq;

namespace OutlookRulesVSTO.Core;

/// <summary>
/// Stable, COM-friendly boundary. It deliberately delegates all decisions to RuleEngine.
/// </summary>
public static class VstoBridgeRuleEngineContract
{
    public static string EvaluateXml(string rulesXml, string settingsXml, string messageXml, bool dryRun)
    {
        var rules = RuleConfigurationXml.FromRulesDocument(XDocument.Parse(rulesXml));
        var settings = RuleConfigurationXml.FromSettingsDocument(XDocument.Parse(settingsXml));
        var message = MessageContextXml.FromXml(messageXml);
        var results = new RuleEngine(settings).Evaluate(rules, message, dryRun);
        foreach (var plan in results.SelectMany(result => result.SavePlans))
        {
            if (!dryRun && plan.Status == SavePlanStatus.Planned && WasSaved(settings.ProcessedHistoryPath, message.EntryId, plan.RuleId, plan.AttachmentName))
            {
                plan.Status = SavePlanStatus.Skipped;
                plan.Message = "Already processed according to processed-history.csv.";
            }
        }

        var document = new XDocument(new XElement("Evaluation",
            new XAttribute("schemaVersion", Schema.CurrentSchemaVersion),
            new XAttribute("dryRun", XmlBool(dryRun)),
            results.Select(result => new XElement("Rule",
                new XAttribute("id", result.Rule.RuleId),
                new XAttribute("name", result.Rule.Name),
                new XAttribute("matched", XmlBool(result.IsMatch)),
                result.Messages.Select(messageText => new XElement("Message", messageText)),
                result.SavePlans.Select(plan => new XElement("Plan",
                    new XAttribute("ruleId", plan.RuleId),
                    new XAttribute("ruleName", plan.RuleName),
                    new XAttribute("attachmentName", plan.AttachmentName),
                    new XAttribute("attachmentIndex", plan.AttachmentIndex),
                    new XAttribute("saveMode", plan.SaveMode),
                    new XAttribute("destinationFolder", plan.DestinationFolder),
                    new XAttribute("destinationPath", plan.DestinationPath),
                    new XAttribute("status", plan.Status),
                    new XAttribute("message", plan.Message)))))));

        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static void RecordSaveResult(
        string settingsXml,
        string messageXml,
        string ruleId,
        string ruleName,
        int attachmentIndex,
        string destinationPath,
        string status,
        string message,
        string errorNumber,
        string errorDescription)
    {
        var settings = RuleConfigurationXml.FromSettingsDocument(XDocument.Parse(settingsXml));
        var mail = MessageContextXml.FromXml(messageXml);
        var attachment = mail.Attachments.SingleOrDefault(item => item.Index == attachmentIndex);
        var attachmentName = attachment?.Name ?? string.Empty;
        CsvLogWriter.AppendProcessingLog(settings.LogsPath, new ProcessingLogRow(
            DateTime.Now, "AttachmentSave", ruleId, ruleName, mail.EntryId, mail.SenderEmail,
            mail.Subject, attachmentName, destinationPath, status, message, errorNumber, errorDescription));

        if (string.Equals(status, "Saved", StringComparison.OrdinalIgnoreCase))
        {
            CsvLogWriter.AppendProcessedHistory(settings.ProcessedHistoryPath, new ProcessedHistoryRow(
                DateTime.Now, mail.EntryId, ruleId, attachmentName, destinationPath, "Saved"));
        }
    }

    private static bool WasSaved(string historyPath, string messageId, string ruleId, string attachmentName) =>
        CsvLogWriter.ReadCsv(historyPath).Skip(1).Any(row => row.Length >= 6 &&
            string.Equals(row[1], messageId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row[2], ruleId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row[3], attachmentName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(row[5], "Saved", StringComparison.OrdinalIgnoreCase));

    private static string XmlBool(bool value) => value ? "true" : "false";
}
