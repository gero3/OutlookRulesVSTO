using System;
using System.Collections.Generic;
using OutlookRulesVSTO.Application;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace OutlookRulesVSTO.Vsto
{
    // The intentionally tiny, untestable boundary: property and save translation only.
    internal sealed class OutlookMailMessage : IMailMessage
    {
        private readonly Outlook.MailItem mail;
        internal OutlookMailMessage(Outlook.MailItem mail) { this.mail = mail ?? throw new ArgumentNullException(nameof(mail)); }
        public string EntryId => Safe(() => mail.EntryID);
        public string SenderEmail => Safe(() => mail.SenderEmailAddress);
        public string SenderName => Safe(() => mail.SenderName);
        public string Subject => Safe(() => mail.Subject);
        public string BodyText => Safe(() => mail.Body);
        public string BodyHtml => Safe(() => mail.HTMLBody);
        public DateTime ReceivedTime => mail.ReceivedTime;
        public IReadOnlyList<MailAttachment> Attachments
        {
            get
            {
                var result = new List<MailAttachment>();
                for (int i = 1; i <= mail.Attachments.Count; i++)
                {
                    Outlook.Attachment attachment = mail.Attachments[i];
                    result.Add(new MailAttachment { Index = i, Name = attachment.FileName, SizeKb = (long)attachment.Size / 1024L, IsInline = IsInline(attachment) });
                }
                return result;
            }
        }
        public void SaveAsHtml(string destinationPath) => mail.SaveAs(destinationPath, Outlook.OlSaveAsType.olHTML);
        public void SaveAttachment(int oneBasedIndex, string destinationPath) => mail.Attachments[oneBasedIndex].SaveAsFile(destinationPath);

        private bool IsInline(Outlook.Attachment attachment)
        {
            try { if (Convert.ToBoolean(attachment.PropertyAccessor.GetProperty("http://schemas.microsoft.com/mapi/proptag/0x7FFE000B"))) return true; } catch { }
            try { if ((Convert.ToInt32(attachment.PropertyAccessor.GetProperty("http://schemas.microsoft.com/mapi/proptag/0x37140003")) & 4) != 0) return true; } catch { }
            try { string id = Convert.ToString(attachment.PropertyAccessor.GetProperty("http://schemas.microsoft.com/mapi/proptag/0x3712001F")); return !string.IsNullOrWhiteSpace(id) && BodyHtml.IndexOf("cid:" + id.Trim('<', '>'), StringComparison.OrdinalIgnoreCase) >= 0; } catch { return false; }
        }
        private static string Safe(Func<string> getter) { try { return getter() ?? string.Empty; } catch { return string.Empty; } }
    }
}
