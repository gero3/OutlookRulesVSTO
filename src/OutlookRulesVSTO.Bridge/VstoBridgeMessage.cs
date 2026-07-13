using System.Runtime.InteropServices;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Bridge;

[ComVisible(true)]
[Guid("A6889D34-7E0B-4C35-A8E4-95580EA32B45")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IVstoBridgeMessage
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

[ComVisible(true)]
[Guid("D6FEDE68-DD9A-4F25-A5BB-8B47893DD90C")]
[ClassInterface(ClassInterfaceType.None)]
[ComDefaultInterface(typeof(IVstoBridgeMessage))]
public sealed class VstoBridgeMessage : IVstoBridgeMessage
{
    private readonly List<AttachmentContext> _attachments = [];

    public string EntryId { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyText { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public DateTime ReceivedTime { get; set; } = DateTime.Now;

    public void AddAttachment(int index, string name, long sizeKb, bool isInline) =>
        _attachments.Add(new AttachmentContext { Index = index, Name = name, SizeKb = sizeKb, IsInline = isInline });

    internal MessageContext ToContext() => new()
    {
        EntryId = EntryId,
        SenderEmail = SenderEmail,
        SenderName = SenderName,
        Subject = Subject,
        BodyText = BodyText,
        BodyHtml = BodyHtml,
        ReceivedTime = ReceivedTime,
        Attachments = _attachments
    };
}
