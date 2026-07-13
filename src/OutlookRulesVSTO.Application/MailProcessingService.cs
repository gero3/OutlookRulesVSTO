using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace OutlookRulesVSTO.Application
{
    public sealed class MailProcessingService
    {
        private readonly string settingsPath;
        private readonly IFileSystem files;
        private readonly IRuleEngine engine;

        public MailProcessingService(string settingsPath, IFileSystem files, IRuleEngine engine)
        {
            this.settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
            this.files = files ?? throw new ArgumentNullException(nameof(files));
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
        }

        public MailProcessResult Process(IMailMessage mail, bool dryRun)
        {
            if (mail == null) throw new ArgumentNullException(nameof(mail));
            if (!files.FileExists(settingsPath)) throw new FileNotFoundException("OutlookRulesVSTO settings.xml is missing. Open OutlookRulesVSTO from Outlook and save Settings.", settingsPath);
            string rulesPath = ResolveRulesPath();
            if (!files.FileExists(rulesPath)) throw new FileNotFoundException("The configured OutlookRulesVSTO rules file is missing.", rulesPath);

            MailSnapshot message = MailSnapshot.From(mail);
            RuleEvaluation evaluation = engine.Evaluate(rulesPath, settingsPath, message, dryRun);
            var plans = evaluation.Plans ?? Array.Empty<SavePlan>();
            int succeeded = 0, failed = 0;
            var details = new List<string>();
            foreach (SavePlan plan in plans)
            {
                bool completed = ExecutePlan(message, mail, plan, dryRun);
                if (completed) succeeded++; else failed++;
                string rule = string.IsNullOrWhiteSpace(plan.RuleName) ? "Unknown rule" : plan.RuleName;
                string destination = string.IsNullOrWhiteSpace(plan.DestinationPath) ? "no destination path" : plan.DestinationPath;
                details.Add(rule + ": " + (completed ? (dryRun ? "gepland" : "verwerkt") : "mislukt") + " → " + destination);
            }
            engine.RecordSummary(settingsPath, message, dryRun, plans.Count, succeeded, failed);
            return new MailProcessResult
            {
                Subject = message.Subject,
                Status = plans.Count == 0 ? "No match" : failed > 0 ? "Failed" : "Geslaagd",
                Details = plans.Count == 0 ? (string.IsNullOrWhiteSpace(evaluation.Summary) ? "No active rule matched; nothing was saved." : evaluation.Summary) : string.Join(Environment.NewLine, details),
                PlanCount = plans.Count, SucceededCount = succeeded, FailedCount = failed
            };
        }

        private string ResolveRulesPath()
        {
            XDocument settings = XDocument.Parse(files.ReadAllText(settingsPath));
            XElement value = settings.Root?.Element("RulesPath");
            string configured = value == null ? Path.Combine(Path.GetDirectoryName(settingsPath), "rules.xml") : value.Value.Trim();
            return Environment.ExpandEnvironmentVariables(configured);
        }

        private bool ExecutePlan(MailSnapshot message, IMailMessage mail, SavePlan plan, bool dryRun)
        {
            if (!string.Equals(plan.Status, "Planned", StringComparison.OrdinalIgnoreCase))
            {
                engine.RecordSaveResult(settingsPath, message, plan, plan.Status, plan.Message, "", "");
                return !string.Equals(plan.Status, "Failed", StringComparison.OrdinalIgnoreCase);
            }
            if (dryRun)
            {
                engine.RecordSaveResult(settingsPath, message, plan, "Planned", "Dry run save planned", "", "");
                return true;
            }
            try
            {
                files.CreateDirectory(Path.GetDirectoryName(plan.DestinationPath));
                if (string.Equals(plan.SaveMode, "MessageHtml", StringComparison.OrdinalIgnoreCase)) mail.SaveAsHtml(plan.DestinationPath);
                else mail.SaveAttachment(plan.AttachmentIndex, plan.DestinationPath);
                if (!files.FileExists(plan.DestinationPath)) throw new IOException("Outlook reported success, but the file was not found.");
                engine.RecordSaveResult(settingsPath, message, plan, "Saved", "File saved by VSTO add-in", "", "");
                return true;
            }
            catch (Exception ex)
            {
                engine.RecordSaveResult(settingsPath, message, plan, "Error", "Save mislukt", ex.HResult.ToString(), ex.Message);
                return false;
            }
        }
    }
}
