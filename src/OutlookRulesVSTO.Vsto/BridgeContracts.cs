using System;
using System.Runtime.InteropServices;

namespace OutlookRulesVSTO.Vsto
{
    // These declarations mirror the explicit interfaces exposed by the .NET
    // COM host. Typed calls use QueryInterface/vtable dispatch and therefore do
    // not require a generated or registered COM type library.
    [ComImport]
    [Guid("A6889D34-7E0B-4C35-A8E4-95580EA32B45")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IVstoBridgeMessage
    {
        string EntryId { get; set; }
        string SenderEmail { get; set; }
        string SenderName { get; set; }
        string Subject { get; set; }
        string BodyText { get; set; }
        string BodyHtml { get; set; }
        DateTime ReceivedTime { get; set; }
        void AddAttachment(int index, string name, long sizeKb, bool isInline);
    }

    [ComImport]
    [Guid("797AE4B4-9439-4387-B64B-652209CA17A0")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IVstoBridgeSavePlan
    {
        string RuleId { get; }
        string RuleName { get; }
        int AttachmentIndex { get; }
        string AttachmentName { get; }
        string SaveMode { get; }
        string DestinationPath { get; }
        string Status { get; }
        string Message { get; }
    }

    [ComImport]
    [Guid("F0AA732D-425E-40FB-A7A9-5CAB5B698010")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IVstoBridgeEvaluation
    {
        int PlanCount { get; }
        string Summary { get; }
        IVstoBridgeSavePlan GetPlan(int oneBasedIndex);
    }

    [ComImport]
    [Guid("F464925A-8222-4831-A845-DAC5FB1BB856")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    internal interface IVstoBridgeRuleEngine
    {
        IVstoBridgeMessage CreateMessage();
        IVstoBridgeEvaluation EvaluateFromFiles(string rulesPath, string settingsPath, IVstoBridgeMessage message, bool dryRun);
        void RecordSaveResultFromFile(string settingsPath, IVstoBridgeMessage message, IVstoBridgeSavePlan plan, string status, string messageText, string errorNumber, string errorDescription);
        void RecordActionSummaryFromFile(string settingsPath, IVstoBridgeMessage message, bool dryRun, int planCount, int succeededCount, int failedCount);
        string EvaluateXml(string rulesXml, string settingsXml, string messageXml, bool dryRun);
        void RecordSaveResult(string settingsXml, string messageXml, string ruleId, string ruleName, int attachmentIndex, string destinationPath, string status, string message, string errorNumber, string errorDescription);
    }
}
