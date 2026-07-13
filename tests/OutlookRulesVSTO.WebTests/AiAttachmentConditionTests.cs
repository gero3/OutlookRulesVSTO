using OutlookRulesVSTO.Web;

namespace OutlookRulesVSTO.WebTests;

[TestClass]
public sealed class AiAttachmentConditionTests
{
    [TestMethod]
    public void KeepsGroundedModelCondition()
    {
        var condition = AiRuleBuilder.SelectAttachmentCondition(
            Mail("Longletsels_BE12345678-0101_20260712.pdf"),
            new AiAttachmentCondition(true, "AttachmentName", "Contains", "Longletsels"));

        Assert.IsNotNull(condition);
        Assert.AreEqual("Longletsels", condition.Value);
    }

    [TestMethod]
    public void DerivesStablePrefixWhenModelDeclines()
    {
        var condition = AiRuleBuilder.SelectAttachmentCondition(
            Mail("Salmonella_S-2026-44.pdf"),
            new AiAttachmentCondition(false, "AttachmentName", "Contains", string.Empty));

        Assert.IsNotNull(condition);
        Assert.AreEqual("AttachmentName", condition.Target);
        Assert.AreEqual("Contains", condition.Operator);
        Assert.AreEqual("Salmonella", condition.Value);
    }

    [TestMethod]
    public void RejectsUngroundedModelValueAndUsesLiteralPrefix()
    {
        var condition = AiRuleBuilder.SelectAttachmentCondition(
            Mail("Echoresultaten_ronde_A.xlsx"),
            new AiAttachmentCondition(true, "AttachmentName", "Contains", "Drachtcontrole"));

        Assert.IsNotNull(condition);
        Assert.AreEqual("Echoresultaten", condition.Value);
    }

    [TestMethod]
    [DataRow("rapport.pdf")]
    [DataRow("Document1.pdf")]
    [DataRow("export_2.csv")]
    [DataRow("image001.png")]
    [DataRow("naamloos_bestand.xlsx")]
    [DataRow("Book1.xlsx")]
    [DataRow("Presentatie1.pptx")]
    [DataRow("IMG_001.jpg")]
    public void GenericFileNameDoesNotBecomeRecurringCondition(string fileName)
    {
        var condition = AiRuleBuilder.SelectAttachmentCondition(
            Mail(fileName),
            new AiAttachmentCondition(false, "AttachmentName", "Contains", string.Empty));

        Assert.IsNull(condition);
    }

    [TestMethod]
    public void MixedFilesSkipGenericNameAndUseMeaningfulPrefix()
    {
        var mail = new AiMail("sender@example.com", "Subject", "Body", ["bijlage.pdf", "Leveringsbon_LM-88210.pdf"]);
        var condition = AiRuleBuilder.SelectAttachmentCondition(mail, new AiAttachmentCondition(false, "AttachmentName", "Contains", string.Empty));

        Assert.IsNotNull(condition);
        Assert.AreEqual("Leveringsbon", condition.Value);
    }

    [TestMethod]
    public void GenericLeadingWordIsSkippedWhenFileContainsStableType()
    {
        var condition = AiRuleBuilder.SelectAttachmentCondition(
            Mail("rapport_Salmonella_20260712.pdf"),
            new AiAttachmentCondition(false, "AttachmentName", "Contains", string.Empty));

        Assert.IsNotNull(condition);
        Assert.AreEqual("Salmonella", condition.Value);
    }

    private static AiMail Mail(string fileName) => new("sender@example.com", "Subject", "Body", [fileName]);
}
