using OutlookRulesVSTO.Core;
using OutlookRulesVSTO.Web;

namespace OutlookRulesVSTO.WebTests;

[TestClass]
public sealed class AiRuleDraftTests
{
    private static readonly Settings Settings = new() { DefaultDestinationRoot = @"C:\Saved" };
    private static readonly AiRuleDraft Draft = new("Laboratoriumuitslag", "Uitslag", [], [new AiExtraction("BodyText")], "Laboratoriumuitslagen");

    [TestMethod]
    [DataRow("rapport.pdf")]
    [DataRow("Document1.pdf")]
    [DataRow("bijlage (2).docx")]
    [DataRow("scan_003.jpg")]
    [DataRow("export.csv")]
    [DataRow("nieuw_document.pdf")]
    [DataRow("image001.png")]
    [DataRow("naamloos bestand.xlsx")]
    [DataRow("Book1.xlsx")]
    [DataRow("Map1.xlsx")]
    [DataRow("Presentatie1.pptx")]
    [DataRow("IMG_001.jpg")]
    public void GenericAttachmentGetsDescriptiveRecurringNameTemplate(string fileName)
    {
        var rule = Draft.ToRule(Settings, new AiMail("lab@example.com", "Uitslag", "BE12345678-1234", [fileName]));

        Assert.AreEqual("{RuleName}_{CaseNumber}_{ReceivedDate:yyyyMMdd}{OriginalFileExtension}", rule.Destination.FileNameTemplate);
    }

    [TestMethod]
    public void MeaningfulAttachmentKeepsOriginalFileName()
    {
        var rule = Draft.ToRule(Settings, new AiMail("lab@example.com", "Uitslag", "BE12345678-1234", ["Salmonella_BE12345678-1234_20260712.pdf"]));

        Assert.AreEqual("{OriginalFileName}", rule.Destination.FileNameTemplate);
    }

    [TestMethod]
    public void MixedAttachmentsKeepAllOriginalFileNames()
    {
        var rule = Draft.ToRule(Settings, new AiMail("lab@example.com", "Uitslag", "BE12345678-1234", ["rapport.pdf", "Salmonella_BE12345678-1234.pdf"]));

        Assert.AreEqual("{OriginalFileName}", rule.Destination.FileNameTemplate);
    }
}
