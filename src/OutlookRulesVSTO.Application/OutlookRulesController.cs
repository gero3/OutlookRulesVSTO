using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OutlookRulesVSTO.Application
{
    public sealed class OutlookRulesController : IDisposable
    {
        private readonly MailProcessingService processor;
        private readonly IDesktopGateway desktop;
        private readonly IApplicationLog log;

        public OutlookRulesController(MailProcessingService processor, IDesktopGateway desktop, IApplicationLog log)
        {
            this.processor = processor ?? throw new ArgumentNullException(nameof(processor));
            this.desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
            this.log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void ProcessIncoming(IMailMessage mail) => processor.Process(mail, false);

        public IReadOnlyList<MailProcessResult> ProcessSelected(IEnumerable<IMailMessage> selected, bool dryRun)
        {
            var messages = (selected ?? Enumerable.Empty<IMailMessage>()).ToList();
            if (messages.Count == 0) throw new InvalidOperationException("Select one or more emails in the message list first.");
            return messages.Select(mail => ProcessSafely(mail, dryRun)).ToList();
        }

        public Task OpenAsync() => desktop.OpenAsync();

        public Task CreateAiRuleAsync(IMailMessage selected)
        {
            if (selected == null) throw new InvalidOperationException("Select an email in Outlook first.");
            return desktop.CreateAiRuleAsync(MailSnapshot.From(selected));
        }

        private MailProcessResult ProcessSafely(IMailMessage mail, bool dryRun)
        {
            try { return processor.Process(mail, dryRun); }
            catch (Exception ex)
            {
                log.Error("ManualProcessFailed", mail, ex);
                return new MailProcessResult { Subject = mail.Subject, Status = "Failed", Details = ex.Message, FailedCount = 1 };
            }
        }

        public void Dispose() => desktop.Dispose();
    }
}
