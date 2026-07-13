using System.Xml.Linq;

namespace OutlookRulesVSTO.Core;

/// <summary>XML boundary used by the VSTO rule-engine bridge.</summary>
public static class MessageContextXml
{
    public static MessageContext FromXml(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new InvalidDataException("Message XML has no root element.");
        if (root.Name.LocalName != "Message")
        {
            throw new InvalidDataException("Message XML root element must be Message.");
        }

        var sender = root.Element("Sender");
        var message = new MessageContext
        {
            MessageId = Attr(root, "messageId"),
            EntryId = Attr(root, "entryId"),
            StoreId = Attr(root, "storeId"),
            ConversationId = Attr(root, "conversationId"),
            SenderEmail = Attr(sender, "email"),
            SenderName = Attr(sender, "name"),
            Subject = Element(root, "Subject"),
            ReceivedTime = Invariant.ParseDate(Element(root, "ReceivedTime"), DateTime.Now),
            BodyText = Element(root, "BodyText"),
            BodyHtml = Element(root, "BodyHtml"),
            AccountName = Element(root, "AccountName")
        };

        message.To = root.Element("Recipients")?.Elements("To").Select(value => value.Value.Trim()).ToList() ?? [];
        message.Cc = root.Element("Recipients")?.Elements("Cc").Select(value => value.Value.Trim()).ToList() ?? [];
        message.Categories = root.Element("Categories")?.Elements("Category").Select(value => value.Value.Trim()).ToList() ?? [];
        message.Attachments = root.Element("Attachments")?.Elements("Attachment").Select((element, index) => new AttachmentContext
        {
            Index = IntAttr(element, "index", index + 1),
            Name = Attr(element, "name"),
            Extension = Attr(element, "extension"),
            SizeKb = LongAttr(element, "sizeKb", 0),
            IsInline = BoolAttr(element, "isInline", false)
        }).ToList() ?? [];

        foreach (var attachment in message.Attachments)
        {
            attachment.Normalize();
        }

        return message;
    }

    private static string Attr(XElement? element, string name) => element is null ? string.Empty : ((string?)element.Attribute(name) ?? string.Empty).Trim();
    private static string Element(XElement element, string name) => ((string?)element.Element(name) ?? string.Empty).Trim();
    private static int IntAttr(XElement element, string name, int fallback) => int.TryParse(Attr(element, name), out var value) ? value : fallback;
    private static long LongAttr(XElement element, string name, long fallback) => long.TryParse(Attr(element, name), out var value) ? value : fallback;
    private static bool BoolAttr(XElement element, string name, bool fallback) => bool.TryParse(Attr(element, name), out var value) ? value : fallback;
}
