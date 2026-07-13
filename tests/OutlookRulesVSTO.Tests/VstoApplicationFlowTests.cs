using OutlookRulesVSTO.Application;

namespace OutlookRulesVSTO.Tests;

[TestClass]
public sealed class VstoApplicationFlowTests
{
    [TestMethod]
    public void FullFlow_EvaluatesAndSavesMessageAndAttachment()
    {
        var files = new FakeFiles();
        files.Text["settings.xml"] = "<Settings><RulesPath>rules.xml</RulesPath></Settings>";
        files.Existing.Add("settings.xml"); files.Existing.Add("rules.xml");
        var engine = new FakeEngine
        {
            Evaluation = new RuleEvaluation { Plans = new[]
            {
                Plan("Mail", "MessageHtml", 0, "out/mail.html"),
                Plan("Bijlage", "Attachment", 1, "out/file.pdf")
            }}
        };
        var mail = new FakeMail();
        mail.OnSave = path => files.Existing.Add(path);
        using var controller = Controller(files, engine);

        MailProcessResult result = controller.ProcessSelected(new[] { mail }, false).Single();

        Assert.AreEqual("Geslaagd", result.Status);
        Assert.AreEqual(2, result.SucceededCount);
        CollectionAssert.AreEquivalent(new[] { "html:out/mail.html", "attachment:1:out/file.pdf" }, mail.Saves);
        Assert.AreEqual(2, engine.SaveRecords.Count);
        Assert.AreEqual((2, 2, 0), engine.Summary);
    }

    [TestMethod]
    public void FullFlow_DryRunAndNoMatchDoNotTouchOutlook()
    {
        var files = ReadyFiles();
        var engine = new FakeEngine { Evaluation = new RuleEvaluation { Plans = new[] { Plan("R", "Attachment", 1, "x") } } };
        var mail = new FakeMail();
        using var controller = Controller(files, engine);
        Assert.AreEqual("Geslaagd", controller.ProcessSelected(new[] { mail }, true).Single().Status);
        Assert.HasCount(0, mail.Saves);
        Assert.AreEqual("Planned", engine.SaveRecords.Single());

        engine.Evaluation = new RuleEvaluation { Summary = "nothing", Plans = Array.Empty<SavePlan>() };
        MailProcessResult none = controller.ProcessSelected(new[] { mail }, false).Single();
        Assert.AreEqual("No match", none.Status);
        Assert.AreEqual("nothing", none.Details);
    }

    [TestMethod]
    public void FullFlow_ReportsPlanAndSaveFailuresWithoutStoppingBatch()
    {
        var files = ReadyFiles();
        var engine = new FakeEngine { Evaluation = new RuleEvaluation { Plans = new[]
        {
            new SavePlan { RuleName = "Rejected", Status = "Failed", Message = "bad" },
            Plan("Broken save", "Attachment", 1, "broken")
        } } };
        var first = new FakeMail { SaveError = new IOException("disk") };
        var log = new FakeLog();
        using var controller = Controller(files, engine, log);
        MailProcessResult result = controller.ProcessSelected(new[] { first }, false).Single();
        Assert.AreEqual("Failed", result.Status);
        Assert.AreEqual(2, result.FailedCount);
        CollectionAssert.Contains(engine.SaveRecords, "Error");

        engine.Throw = new InvalidOperationException("engine down");
        MailProcessResult caught = controller.ProcessSelected(new[] { first }, false).Single();
        Assert.AreEqual("engine down", caught.Details);
        Assert.AreEqual("ManualProcessFailed", log.Event);
    }

    [TestMethod]
    public async Task Controller_DelegatesDesktopAndValidatesSelection()
    {
        var desktop = new FakeDesktop();
        using var controller = Controller(ReadyFiles(), new FakeEngine(), desktop: desktop);
        await controller.OpenAsync();
        var mail = new FakeMail();
        await controller.CreateAiRuleAsync(mail);
        Assert.IsTrue(desktop.Opened);
        Assert.AreEqual(mail.Subject, desktop.AiMail.Subject);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.ProcessSelected(Array.Empty<IMailMessage>(), false));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => controller.CreateAiRuleAsync(null));
        controller.ProcessIncoming(mail);
    }

    [TestMethod]
    public void Processor_ValidatesFilesAndUsesDefaultRulesPath()
    {
        var files = new FakeFiles(); var engine = new FakeEngine(); var mail = new FakeMail();
        var processor = new MailProcessingService("folder/settings.xml", files, engine);
        Assert.ThrowsExactly<FileNotFoundException>(() => processor.Process(mail, false));
        files.Existing.Add("folder/settings.xml"); files.Text["folder/settings.xml"] = "<Settings />";
        Assert.ThrowsExactly<FileNotFoundException>(() => processor.Process(mail, false));
        files.Existing.Add("folder/rules.xml");
        Assert.AreEqual("No match", processor.Process(mail, false).Status);
    }

    private static SavePlan Plan(string name, string mode, int index, string path) => new() { RuleName = name, SaveMode = mode, AttachmentIndex = index, DestinationPath = path, Status = "Planned" };
    private static FakeFiles ReadyFiles() { var f = new FakeFiles(); f.Existing.UnionWith(new[] { "settings.xml", "rules.xml" }); f.Text["settings.xml"] = "<Settings><RulesPath>rules.xml</RulesPath></Settings>"; return f; }
    private static OutlookRulesController Controller(FakeFiles files, FakeEngine engine, FakeLog? log = null, FakeDesktop? desktop = null) => new(new MailProcessingService(files.Text.ContainsKey("settings.xml") ? "settings.xml" : "folder/settings.xml", files, engine), desktop ?? new FakeDesktop(), log ?? new FakeLog());

    private sealed class FakeMail : IMailMessage
    {
        public string EntryId => "id"; public string SenderEmail => "a@b.test"; public string SenderName => "A"; public string Subject => "Subject";
        public string BodyText => "text"; public string BodyHtml => "<p>text</p>"; public DateTime ReceivedTime => new(2026, 1, 1);
        public IReadOnlyList<MailAttachment> Attachments => new[] { new MailAttachment { Index = 1, Name = "file.pdf", SizeKb = 4 } };
        public List<string> Saves { get; } = new(); public Action<string>? OnSave { get; set; } public Exception? SaveError { get; set; }
        public void SaveAsHtml(string path) { if (SaveError != null) throw SaveError; Saves.Add("html:" + path); OnSave?.Invoke(path); }
        public void SaveAttachment(int index, string path) { if (SaveError != null) throw SaveError; Saves.Add($"attachment:{index}:{path}"); OnSave?.Invoke(path); }
    }
    private sealed class FakeFiles : IFileSystem
    {
        public HashSet<string> Existing { get; } = new(StringComparer.OrdinalIgnoreCase); public Dictionary<string, string> Text { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool FileExists(string path) => Existing.Contains(path.Replace('\\', '/')) || Existing.Contains(path);
        public void CreateDirectory(string path) { }
        public string ReadAllText(string path) => Text[path];
    }
    private sealed class FakeEngine : IRuleEngine
    {
        public RuleEvaluation Evaluation { get; set; } = new() { Plans = Array.Empty<SavePlan>() }; public Exception? Throw { get; set; }
        public List<string> SaveRecords { get; } = new(); public (int, int, int) Summary { get; private set; }
        public RuleEvaluation Evaluate(string r, string s, MailSnapshot m, bool d) { if (Throw != null) throw Throw; return Evaluation; }
        public void RecordSaveResult(string s, MailSnapshot m, SavePlan p, string status, string t, string n, string d) => SaveRecords.Add(status);
        public void RecordSummary(string s, MailSnapshot m, bool d, int plans, int ok, int failed) => Summary = (plans, ok, failed);
    }
    private sealed class FakeDesktop : IDesktopGateway
    {
        public bool Opened { get; private set; } public MailSnapshot? AiMail { get; private set; } public bool Disposed { get; private set; }
        public Task OpenAsync() { Opened = true; return Task.CompletedTask; }
        public Task CreateAiRuleAsync(MailSnapshot mail) { AiMail = mail; return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
    private sealed class FakeLog : IApplicationLog { public string? Event { get; private set; } public void Error(string eventType, IMailMessage mail, Exception error) => Event = eventType; }
}
