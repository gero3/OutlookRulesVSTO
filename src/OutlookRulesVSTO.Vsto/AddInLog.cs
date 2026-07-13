using System;
using System.IO;
using System.Text;
using OutlookRulesVSTO.Application;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace OutlookRulesVSTO.Vsto
{
    internal sealed class AddInApplicationLog : IApplicationLog
    {
        public void Error(string eventType, IMailMessage mail, Exception error) =>
            AddInLog.WriteRow(eventType, mail.EntryId, mail.Subject, "Error", "Manual processing failed", error);
    }

    internal static class AddInLog
    {
        internal static void Write(string eventType, string status, string message, Exception error)
        {
            WriteRow(eventType, "", "", status, message, error);
        }

        internal static void WriteMail(string eventType, Outlook.MailItem mail, string status, string message, Exception error)
        {
            string id = "";
            string subject = "";
            try { id = mail.EntryID ?? ""; } catch { }
            try { subject = mail.Subject ?? ""; } catch { }
            WriteRow(eventType, id, subject, status, message, error);
        }

        internal static void WriteRow(string eventType, string messageId, string subject, string status, string message, Exception error)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OutlookRulesVSTO", "logs");
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "processing-" + DateTime.Now.ToString("yyyyMMdd") + ".csv");
                if (!File.Exists(path)) File.AppendAllText(path, "timestamp,eventType,ruleId,ruleName,messageId,senderEmail,subject,attachmentName,destinationPath,status,message,errorNumber,errorDescription\r\n", Encoding.UTF8);
                string[] values = { DateTime.Now.ToString("s"), eventType, "", "", messageId, "", subject, "", "", status, message, error == null ? "" : error.HResult.ToString(), error == null ? "" : error.Message };
                for (int i = 0; i < values.Length; i++) values[i] = Csv(values[i]);
                File.AppendAllText(path, string.Join(",", values) + "\r\n", Encoding.UTF8);
            }
            catch { }
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }
    }
}
