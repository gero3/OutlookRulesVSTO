using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Playwright;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.WebTests;

[TestClass, DoNotParallelize]
public sealed class BrowserFlowTests
{
    public TestContext TestContext { get; set; } = null!;
    private string _root = null!;
    private string _baseUrl = null!;
    private Process _server = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "outlookrulesvsto-browser-e2e", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var settingsPath = Path.Combine(_root, "settings.xml");
        var rulesPath = Path.Combine(_root, "rules.xml");
        RuleConfigurationXml.SaveSettingsAtomic(settingsPath, CreateSettings(rulesPath));
        RuleConfigurationXml.SaveRulesAtomic(rulesPath, new RuleConfiguration());
        var port = ReservePort();
        _baseUrl = $"http://127.0.0.1:{port}";
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var start = new ProcessStartInfo("dotnet", typeof(Program).Assembly.Location)
        {
            UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = repoRoot
        };
        start.Environment["OUTLOOKRULESVSTO_URL"] = _baseUrl;
        start.Environment["OUTLOOKRULESVSTO_OUTLOOK_TOKEN"] = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        start.Environment["OUTLOOKRULESVSTO_SETTINGS_PATH"] = settingsPath;
        start.Environment["OUTLOOKRULESVSTO_RULES_PATH"] = rulesPath;
        start.Environment["ASPNETCORE_CONTENTROOT"] = Path.Combine(repoRoot, "src", "OutlookRulesVSTO.Web");
        _server = Process.Start(start)!;
        TestContext.WriteLine("Serverproces gestart");
        await WaitForServer();
        TestContext.WriteLine("Server bereikbaar");
        _playwright = await Playwright.CreateAsync();
        TestContext.WriteLine("Playwright gestart");
        var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true, ExecutablePath = edge });
        TestContext.WriteLine("Edge gestart");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_server is { HasExited: false }) _server.Kill(entireProcessTree: true);
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _server?.Dispose();
        try { Directory.Delete(_root, true); } catch { }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task BrowserCreatesEditsCopiesTogglesAndDeletesRule()
    {
        var page = await _browser.NewPageAsync();
        TestContext.WriteLine("Pagina gemaakt");
        page.SetDefaultTimeout(5_000);
        await page.GotoAsync($"{_baseUrl}/editor.html");
        await page.GetByLabel("Name required").FillAsync("Browserregel");
        await page.GetByLabel("Description").FillAsync("Volledige browserflow");
        await page.Locator("input[data-key=value]").FillAsync("Medicatiebestelling");
        await page.GetByLabel("Subfolder template").FillAsync(@"{CaseNumber}\Medicatie");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save rule" }).ClickAsync();
        await page.WaitForURLAsync($"{_baseUrl}/");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Browserregel" })).ToBeVisibleAsync();

        await page.Locator("button[onclick^='copyRule']").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/editor.html?id="));
        await Expect(page.GetByLabel("Name required")).ToHaveValueAsync("Copy of Browserregel");
        await page.GotoAsync($"{_baseUrl}/");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Copy of Browserregel" })).ToBeVisibleAsync();
        var switches = page.Locator("button[title='Disable rule']");
        Assert.IsGreaterThanOrEqualTo(2, await switches.CountAsync());
        await switches.First.ClickAsync();
        await Expect(page.GetByText("Disabled").First).ToBeVisibleAsync();

        page.Dialog += (_, dialog) => dialog.AcceptAsync();
        var deleteButtons = page.Locator("button[title='Delete rule']");
        Assert.IsGreaterThanOrEqualTo(2, await deleteButtons.CountAsync());
        await deleteButtons.Last.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Copy of Browserregel" })).Not.ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task EnglishIsDefaultAndCanBeSelectedFromSettings()
    {
        var settingsPath = Path.Combine(_root, "settings.xml");
        var settings = RuleConfigurationXml.LoadSettings(settingsPath);
        settings.Language = "en";
        RuleConfigurationXml.SaveSettingsAtomic(settingsPath, settings);
        try
        {
            var page = await _browser.NewPageAsync();
            await page.GotoAsync($"{_baseUrl}/");
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Manage attachment rules." })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "New rule" })).ToBeVisibleAsync();
            await page.GotoAsync($"{_baseUrl}/settings.html");
            await Expect(page.GetByLabel("Language")).ToHaveValueAsync("en");
        }
        finally
        {
            settings.Language = "en";
            RuleConfigurationXml.SaveSettingsAtomic(settingsPath, settings);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task BrowserPreviewsRuleAndUploadsRealEml()
    {
        var page = await _browser.NewPageAsync();
        page.SetDefaultTimeout(5_000);
        page.PageError += (_, error) => TestContext.WriteLine($"PAGE ERROR: {error}");
        page.Response += (_, response) => { if (response.Url.Contains("/api/rules/preview-eml")) TestContext.WriteLine($"EML HTTP {response.Status}"); };
        await page.GotoAsync($"{_baseUrl}/editor.html");
        await page.GetByLabel("Name required").FillAsync("EML test");
        await page.Locator("input[data-key=value]").FillAsync("Medicatiebestelling");
        await page.Locator("select[data-key=source]").SelectOptionAsync("Subject");
        await page.GetByLabel("Save as").SelectOptionAsync("MessageHtml");
        await page.GetByLabel("Example subject").FillAsync("Medicatiebestelling - Case number: ORD-0101");
        await page.GetByLabel("Subfolder template").FillAsync(@"{CaseNumber}\Medicatie");
        await page.GetByRole(AriaRole.Button, new() { Name = "Test example" }).ClickAsync();
        await Expect(page.GetByText("✓ This email matches")).ToBeVisibleAsync();
        await Expect(page.Locator("#previewResult").GetByText("ORD-0101", new() { Exact = false }).First).ToBeVisibleAsync();

        var emlPath = Path.Combine(_root, "message.eml");
        await File.WriteAllTextAsync(emlPath, SimpleEml());
        await page.Locator("#emlFile").SetInputFilesAsync(emlPath);
        TestContext.WriteLine($"Bestanden in input: {await page.Locator("#emlFile").EvaluateAsync<int>("input => input.files.length")}");
        await page.Locator("#emlFile").DispatchEventAsync("change");
        await Expect(page.Locator("#emlFileName")).ToContainTextAsync("message.eml");
        var emlResponse = await page.RunAndWaitForResponseAsync(
            () => page.GetByRole(AriaRole.Button, new() { Name = "Test email" }).ClickAsync(),
            response => response.Url.Contains("/api/rules/preview-eml"));
        Assert.AreEqual(200, emlResponse.Status);
        await page.Locator("#emlResult").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        TestContext.WriteLine($"EML-resultaat: {await page.Locator("#emlResult").InnerTextAsync()}");
        await Expect(page.Locator("#emlResult").GetByText("✓ This email matches")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(page.Locator("#emlResult").GetByText("ORD-0101", new() { Exact = false }).First).ToBeVisibleAsync();
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public async Task BrowserSavesSettingsAndShowsValidationError()
    {
        var page = await _browser.NewPageAsync();
        TestContext.WriteLine("Pagina gemaakt");
        page.SetDefaultTimeout(5_000);
        await page.GotoAsync($"{_baseUrl}/settings.html");
        TestContext.WriteLine("Instellingen geopend");
        await page.Locator("[name=logRetentionDays]").FillAsync("45");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save settings" }).ClickAsync();
        await Expect(page.GetByText("Settings saved")).ToBeVisibleAsync();
        await page.ReloadAsync();
        Assert.AreEqual("45", await page.Locator("[name=logRetentionDays]").InputValueAsync());

        await page.GotoAsync($"{_baseUrl}/editor.html");
        await page.GetByLabel("Name required").FillAsync("Invalid rule");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save rule" }).ClickAsync();
        await Expect(page.Locator("#errors")).ToBeVisibleAsync();
        await Expect(page.Locator("#errors")).ToContainTextAsync("rule cannot be saved yet");
    }

    [TestMethod]
    [TestCategory("LocalAi")]
    [DataRow("Qwen_Qwen3.5-0.8B-Q4_K_M.gguf", "ai-01-medicatiebestelling.eml", "Automatic", "MessageHtml")]
    [DataRow("Qwen_Qwen3.5-0.8B-Q4_K_M.gguf", "ai-02-slachthuisrapport.eml", "Automatic", "Attachments")]
    [DataRow("Qwen_Qwen3.5-0.8B-Q4_K_M.gguf", "ai-03-spoedmelding.eml", "Automatic", "MessageHtml")]
    [DataRow("Qwen_Qwen3.5-2B-Q4_K_M.gguf", "ai-01-medicatiebestelling.eml", "Automatic", "MessageHtml")]
    [DataRow("Qwen_Qwen3.5-2B-Q4_K_M.gguf", "ai-02-slachthuisrapport.eml", "Automatic", "Attachments")]
    [DataRow("Qwen_Qwen3.5-2B-Q4_K_M.gguf", "ai-03-spoedmelding.eml", "Automatic", "MessageHtml")]
    [Timeout(300_000, CooperativeCancellation = true)]
    public async Task LocalAiBrowserFlowCreatesGroundedDraftWhenExplicitlyEnabled(string model, string fileName, string expectedSource, string expectedSaveMode)
    {
        if (Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_RUN_LOCAL_AI_E2E") != "1") Assert.Inconclusive("Zet OUTLOOKRULESVSTO_RUN_LOCAL_AI_E2E=1 om de lokale 2B-modeltest uit te voeren.");
        var settingsPath = Path.Combine(_root, "settings.xml");
        var settings = RuleConfigurationXml.LoadSettings(settingsPath);
        settings.AiHelperEnabled = true;
        settings.AiModel = model;
        RuleConfigurationXml.SaveSettingsAtomic(settingsPath, settings);
        var eml = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "test-mails", "ai", fileName));

        var page = await _browser.NewPageAsync();
        page.SetDefaultTimeout(10_000);
        await page.GotoAsync(_baseUrl);
        await page.Locator("#aiRuleEml").SetInputFilesAsync(eml);
        await page.WaitForURLAsync($"{_baseUrl}/editor.html?draft=ai", new() { Timeout = 240_000 });
        Assert.AreEqual("CaseNumber", await page.Locator("input[data-key=name]").InputValueAsync());
        Assert.AreEqual(@"(BE\d{8}-\d{4})", await page.Locator("input[data-key=pattern]").InputValueAsync());
        Assert.AreEqual(expectedSource, await page.Locator("select[data-key=source]").InputValueAsync());
        Assert.AreEqual(expectedSaveMode, await page.GetByLabel("Save as").InputValueAsync());
    }

    private Settings CreateSettings(string rulesPath) => new()
    {
        StorageRoot = _root, RulesPath = rulesPath, LogsPath = Path.Combine(_root, "logs"),
        ProcessedHistoryPath = Path.Combine(_root, "history.csv"), DefaultDestinationRoot = _root,
        RuleEditingEnabled = true, AiHelperEnabled = false, Language = "en"
    };

    private static string SimpleEml() => "From: Orders <orders@example.test>\r\nTo: archive@example.test\r\nSubject: Medicatiebestelling - Case number: ORD-0101\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nDossier: ORD-0101.\r\n";

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }

    private async Task WaitForServer()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (_server.HasExited)
                throw new InvalidOperationException($"De E2E-testserver stopte met code {_server.ExitCode}.");
            try { if ((await client.GetAsync($"{_baseUrl}/api/ai/status")).IsSuccessStatusCode) return; } catch { }
            await Task.Delay(100);
        }
        throw new TimeoutException("De E2E-testserver startte niet.");
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
