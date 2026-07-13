using System.Xml.Linq;

namespace OutlookRulesVSTO.Core;

public static class MessageFixtureLoader
{
    public static MessageContext Load(string path)
    {
        var document = XDocument.Load(path);
        var root = document.Root ?? throw new InvalidDataException("Message fixture has no root element.");
        var sender = root.Element("Sender");

        var message = new MessageContext
        {
            MessageId = Attr(root, "id", Path.GetFileNameWithoutExtension(path)),
            EntryId = Attr(root, "entryId", Attr(root, "id", Path.GetFileNameWithoutExtension(path))),
            SenderEmail = Attr(sender, "email"),
            SenderName = Attr(sender, "name"),
            Subject = Element(root, "Subject"),
            ReceivedTime = Invariant.ParseDate(Element(root, "ReceivedTime"), DateTime.Now),
            BodyText = Element(root, "BodyText"),
            BodyHtml = Element(root, "BodyHtml"),
            AccountName = Element(root, "AccountName")
        };

        message.To = root.Element("Recipients")?.Elements("To").Select(node => node.Value.Trim()).ToList() ?? [];
        message.Cc = root.Element("Recipients")?.Elements("Cc").Select(node => node.Value.Trim()).ToList() ?? [];
        message.Categories = root.Element("Categories")?.Elements("Category").Select(node => node.Value.Trim()).ToList() ?? [];
        message.Attachments = root.Element("Attachments")?.Elements("Attachment").Select(ReadAttachment).ToList() ?? [];
        for (var index = 0; index < message.Attachments.Count; index++)
        {
            message.Attachments[index].Index = index + 1;
        }
        foreach (var attachment in message.Attachments)
        {
            attachment.Normalize();
        }

        return message;
    }

    private static AttachmentContext ReadAttachment(XElement element) => new()
    {
        Name = Attr(element, "name"),
        SizeKb = long.TryParse(Attr(element, "sizeKb"), out var size) ? size : 0,
        FixturePath = Attr(element, "fixturePath"),
        IsInline = bool.TryParse(Attr(element, "isInline"), out var inline) && inline
    };

    private static string Attr(XElement? element, string name, string fallback = "") =>
        element is null ? fallback : ((string?)element.Attribute(name) ?? fallback).Trim();

    private static string Element(XElement element, string name, string fallback = "") =>
        ((string?)element.Element(name) ?? fallback).Trim();
}
