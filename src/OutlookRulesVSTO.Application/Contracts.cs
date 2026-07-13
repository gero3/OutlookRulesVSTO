using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OutlookRulesVSTO.Application
{
    public interface IMailMessage
    {
        string EntryId { get; }
        string SenderEmail { get; }
        string SenderName { get; }
        string Subject { get; }
        string BodyText { get; }
        string BodyHtml { get; }
        DateTime ReceivedTime { get; }
        IReadOnlyList<MailAttachment> Attachments { get; }
        void SaveAsHtml(string destinationPath);
        void SaveAttachment(int oneBasedIndex, string destinationPath);
    }

    public sealed class MailAttachment
    {
        public int Index { get; set; }
        public string Name { get; set; }
        public long SizeKb { get; set; }
        public bool IsInline { get; set; }
    }

    public sealed class MailSnapshot
    {
        public string EntryId { get; set; }
        public string SenderEmail { get; set; }
        public string SenderName { get; set; }
        public string Subject { get; set; }
        public string BodyText { get; set; }
        public string BodyHtml { get; set; }
        public DateTime ReceivedTime { get; set; }
        public IReadOnlyList<MailAttachment> Attachments { get; set; }

        public static MailSnapshot From(IMailMessage mail) => new MailSnapshot
        {
            EntryId = mail.EntryId, SenderEmail = mail.SenderEmail, SenderName = mail.SenderName,
            Subject = mail.Subject, BodyText = mail.BodyText, BodyHtml = mail.BodyHtml,
            ReceivedTime = mail.ReceivedTime, Attachments = mail.Attachments
        };
    }

    public interface IRuleEngine
    {
        RuleEvaluation Evaluate(string rulesPath, string settingsPath, MailSnapshot message, bool dryRun);
        void RecordSaveResult(string settingsPath, MailSnapshot message, SavePlan plan, string status, string text, string errorNumber, string errorDescription);
        void RecordSummary(string settingsPath, MailSnapshot message, bool dryRun, int planCount, int succeededCount, int failedCount);
    }

    public sealed class RuleEvaluation
    {
        public string Summary { get; set; }
        public IReadOnlyList<SavePlan> Plans { get; set; }
    }

    public sealed class SavePlan
    {
        public string RuleName { get; set; }
        public int AttachmentIndex { get; set; }
        public string SaveMode { get; set; }
        public string DestinationPath { get; set; }
        public string Status { get; set; }
        public string Message { get; set; }
        public object NativePlan { get; set; }
    }

    public interface IFileSystem
    {
        bool FileExists(string path);
        void CreateDirectory(string path);
        string ReadAllText(string path);
    }

    public interface IDesktopGateway : IDisposable
    {
        Task OpenAsync();
        Task CreateAiRuleAsync(MailSnapshot mail);
    }

    public interface IApplicationLog
    {
        void Error(string eventType, IMailMessage mail, Exception error);
    }

    public sealed class MailProcessResult
    {
        public string Subject { get; set; }
        public string Status { get; set; }
        public string Details { get; set; }
        public int PlanCount { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }
    }
}
