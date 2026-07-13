using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.WebTests;

[TestClass, DoNotParallelize]
public sealed class WebFrontendTests
{
    private string _root = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    [TestInitialize]
    public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "outlookrulesvsto-web-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var rulesPath = Path.Combine(_root, "rules.xml");
        var alpha = CreateRule("alpha", "Alpha");
        alpha.Extractions = [new Extraction { Name = "Code", Source = ExtractionSource.Subject, Method = ExtractionMethod.RegexCapture, Pattern = @"(\d{4})", CaptureGroup = "1", Required = false }];
        RuleConfigurationXml.SaveRulesAtomic(rulesPath, new RuleConfiguration { Rules = [alpha] });
        Environment.SetEnvironmentVariable("OUTLOOKRULESVSTO_RULES_PATH", rulesPath);
        Environment.SetEnvironmentVariable("OUTLOOKRULESVSTO_SETTINGS_PATH", Path.Combine(_root, "settings.xml"));
        Environment.SetEnvironmentVariable("OUTLOOKRULESVSTO_OUTLOOK_TOKEN", "test-outlook-token-123456789");
        _factory = new WebApplicationFactory<Program>();
        _client = _factory.CreateClient();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _client.Dispose(); _factory.Dispose();
        Environment.SetEnvironmentVariable("OUTLOOKRULESVSTO_RULES_PATH", null);
        Environment.SetEnvironmentVariable("OUTLOOKRULESVSTO_SETTINGS_PATH", null);
        Environment.SetEnvironmentVariable("OUTLOOKRULESVSTO_OUTLOOK_TOKEN", null);
    }

    [TestMethod]
    public async Task AllUserFacingPagesAreAvailable()
    {
        foreach (var path in new[] { "/", "/editor.html", "/logs.html", "/settings.html" })
            Assert.IsTrue((await _client.GetAsync(path)).IsSuccessStatusCode, path);
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/rules");
        Assert.AreEqual("Saved", list.GetProperty("rules")[0].GetProperty("destination").GetString());
        Assert.AreEqual("Code: uit onderwerp, recognizes the extracted value using a configured pattern (optional)", list.GetProperty("rules")[0].GetProperty("extractionItems")[0].GetString());
        Assert.IsGreaterThanOrEqualTo(list.GetProperty("rules")[0].GetProperty("extractionDetails")[0].GetProperty("regexItems").GetArrayLength(), 3);
        Assert.IsFalse(string.Join(' ', list.GetProperty("rules")[0].GetProperty("extractionDetails")[0].GetProperty("regexItems").EnumerateArray().Select(item => item.GetString())).Contains(@"\d", StringComparison.Ordinal));
        StringAssert.Contains(await _client.GetStringAsync("/"), "aiRule");
        StringAssert.Contains(await _client.GetStringAsync("/settings.html"), "AI prompts");
    }

    [TestMethod]
    public async Task AiRuleEndpointIsOffByDefault()
    {
        const string eml = "From: lab@example.com\r\nSubject: test\r\n\r\nDossier: 123";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(eml), "file", "message.eml");
        var response = await _client.PostAsync("/api/ai/rule-from-eml", form);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "Enable the AI helper in Settings first");
    }

    [TestMethod]
    public async Task OutlookBridgeRequiresSessionToken()
    {
        var mail = new { senderEmail = "lab@example.com", subject = "Test", bodyText = "Beslagnummer BE12345678-1234", attachments = new[] { new { name = "rapport.pdf", sizeKb = 10 } } };
        Assert.AreEqual(System.Net.HttpStatusCode.Unauthorized, (await _client.PostAsJsonAsync("/api/outlook/ai-rule", mail)).StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/outlook/ai-rule") { Content = JsonContent.Create(mail) };
        request.Headers.Add("X-OutlookRulesVSTO-Outlook-Token", "test-outlook-token-123456789");
        var authorized = await _client.SendAsync(request);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, authorized.StatusCode);
        StringAssert.Contains(await authorized.Content.ReadAsStringAsync(), "Enable the AI helper in Settings first");
    }

    [TestMethod]
    public async Task RuleCrudCopyAndExportRoundTrip()
    {
        var created = await _client.PostAsJsonAsync("/api/rules", CreateRule("", "Website rule"), _json);
        created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var rule = await _client.GetFromJsonAsync<Rule>($"/api/rules/{id}", _json);
        Assert.AreEqual("Website rule", rule!.Name);
        rule.Description = "Updated";
        (await _client.PutAsJsonAsync($"/api/rules/{id}", rule, _json)).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/api/rules/{id}/copy", null)).EnsureSuccessStatusCode();
        (await _client.DeleteAsync($"/api/rules/{id}")).EnsureSuccessStatusCode();
        var export = await _client.GetAsync("/api/rules/export");
        export.EnsureSuccessStatusCode();
        StringAssert.Contains(await export.Content.ReadAsStringAsync(), "Copy of Website rule");
    }

    [TestMethod]
    public async Task SettingsCanBeSavedAndReloaded()
    {
        var defaults = await _client.GetFromJsonAsync<JsonElement>("/api/rule-defaults");
        Assert.AreEqual(_root, defaults.GetProperty("destinationRoot").GetString());
        Assert.AreEqual("ExistingRules", defaults.GetProperty("source").GetString());
        var settings = await _client.GetFromJsonAsync<Settings>("/api/settings", _json);
        settings!.LogRetentionDays = 123;
        settings.Language = "nl";
        settings.AiExtractionName = "Ordernummer";
        settings.AiExtractionPattern = @"Order:\s*([A-Z0-9-]+)";
        settings.AiSystemPrompt = "Aangepaste systeemprompt";
        (await _client.PutAsJsonAsync("/api/settings", settings, _json)).EnsureSuccessStatusCode();
        var reloaded = (await _client.GetFromJsonAsync<Settings>("/api/settings", _json))!;
        Assert.AreEqual(123, reloaded.LogRetentionDays);
        Assert.AreEqual("nl", reloaded.Language);
        Assert.AreEqual("Ordernummer", reloaded.AiExtractionName);
        Assert.AreEqual(@"Order:\s*([A-Z0-9-]+)", reloaded.AiExtractionPattern);
        Assert.AreEqual("Aangepaste systeemprompt", reloaded.AiSystemPrompt);
    }

    [TestMethod]
    public async Task PreviewAndImportUseTheCoreRuleContract()
    {
        var rule = CreateRule("", "Preview");
        var preview = await _client.PostAsJsonAsync("/api/rules/preview", new
        {
            rule, senderEmail = "sender@example.com", subject = "test subject", body = "Beslagnummer: 123", attachmentName = "rapport.pdf"
        }, _json);
        preview.EnsureSuccessStatusCode();
        Assert.IsTrue((await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("isMatch").GetBoolean());

        var xml = RuleConfigurationXml.ToRulesDocument(new RuleConfiguration { Rules = [CreateRule("imported", "Imported")] }).ToString();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(xml), "file", "rules.xml");
        (await _client.PostAsync("/api/rules/import", form)).EnsureSuccessStatusCode();
        StringAssert.Contains(await (await _client.GetAsync("/api/rules/export")).Content.ReadAsStringAsync(), "Imported");
    }

    [TestMethod]
    public async Task EmlPreviewParsesMessageAndAttachments()
    {
        const string eml = "From: Lab <lab@example.com>\r\nTo: receiver@example.com\r\nSubject: test laboratory result\r\nMIME-Version: 1.0\r\nContent-Type: multipart/mixed; boundary=sample\r\n\r\n--sample\r\nContent-Type: text/plain; charset=utf-8\r\n\r\nBeslagnummer: BE12345678-1234\r\n--sample\r\nContent-Type: application/pdf; name=rapport.pdf\r\nContent-Disposition: attachment; filename=rapport.pdf\r\nContent-Transfer-Encoding: base64\r\n\r\nJVBERi0xLjQ=\r\n--sample--\r\n";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(eml), "file", "message.eml");
        form.Add(new StringContent(JsonSerializer.Serialize(CreateRule("", "EML preview"), _json)), "rule");
        var response = await _client.PostAsync("/api/rules/preview-eml", form);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(result.GetProperty("isMatch").GetBoolean());
        Assert.AreEqual("rapport.pdf", result.GetProperty("email").GetProperty("attachments")[0].GetString());

        using var allRulesForm = new MultipartFormDataContent();
        allRulesForm.Add(new StringContent(eml), "file", "message.eml");
        var allRulesResponse = await _client.PostAsync("/api/rules/test-eml", allRulesForm);
        allRulesResponse.EnsureSuccessStatusCode();
        var allRulesResult = await allRulesResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.AreEqual(1, allRulesResult.GetProperty("results").GetArrayLength());
        Assert.AreEqual("Matched", allRulesResult.GetProperty("results")[0].GetProperty("status").GetString());
    }

    [TestMethod]
    public async Task RuleLifecycleToggleCopyMoveAndDeletePersistsOrderedState()
    {
        var betaResponse = await _client.PostAsJsonAsync("/api/rules", CreateRule("", "Beta"), _json);
        betaResponse.EnsureSuccessStatusCode();
        var betaId = (await betaResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        (await _client.PostAsync($"/api/rules/{betaId}/toggle", null)).EnsureSuccessStatusCode();
        Assert.IsFalse((await _client.GetFromJsonAsync<Rule>($"/api/rules/{betaId}", _json))!.Enabled);
        (await _client.PostAsync($"/api/rules/{betaId}/move/up", null)).EnsureSuccessStatusCode();
        var moved = await _client.GetFromJsonAsync<JsonElement>("/api/rules");
        Assert.AreEqual("Beta", moved.GetProperty("rules")[0].GetProperty("name").GetString());

        var copyResponse = await _client.PostAsync($"/api/rules/{betaId}/copy", null);
        copyResponse.EnsureSuccessStatusCode();
        var copyId = (await copyResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        Assert.AreEqual("Copy of Beta", (await _client.GetFromJsonAsync<Rule>($"/api/rules/{copyId}", _json))!.Name);
        (await _client.DeleteAsync($"/api/rules/{copyId}")).EnsureSuccessStatusCode();
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await _client.GetAsync($"/api/rules/{copyId}")).StatusCode);
    }

    [TestMethod]
    public async Task InvalidRuleMissingEntitiesAndMalformedUploadsReturnActionableErrors()
    {
        var invalid = CreateRule("", "");
        invalid.Destination.Root = string.Empty;
        var invalidResponse = await _client.PostAsJsonAsync("/api/rules", invalid, _json);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, invalidResponse.StatusCode);
        StringAssert.Contains(await invalidResponse.Content.ReadAsStringAsync(), "issues");
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await _client.GetAsync("/api/rules/missing")).StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.NotFound, (await _client.PostAsync("/api/rules/missing/copy", null)).StatusCode);

        using var badImport = new MultipartFormDataContent();
        badImport.Add(new StringContent("<not-rules>"), "file", "rules.xml");
        var importResponse = await _client.PostAsync("/api/rules/import", badImport);
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, importResponse.StatusCode);
        StringAssert.Contains(await importResponse.Content.ReadAsStringAsync(), "not a valid rules.xml");

        using var badEml = new MultipartFormDataContent();
        badEml.Add(new StringContent("not a MIME message"), "file", "message.eml");
        badEml.Add(new StringContent(JsonSerializer.Serialize(CreateRule("", "Bad EML"), _json)), "rule");
        Assert.AreEqual(System.Net.HttpStatusCode.BadRequest, (await _client.PostAsync("/api/rules/preview-eml", badEml)).StatusCode);
    }

    [TestMethod]
    public async Task FixedHoldingNumberFlowMatchesSubjectAndRejectsMissingNumber()
    {
        var rule = CreateRule("", "Beslagnummer");
        rule.Conditions = [new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Contains, Value = "Medicatiebestelling" }];
        rule.Extractions = [new Extraction { Name = "Beslagnummer", Source = ExtractionSource.Subject, Method = ExtractionMethod.RegexCapture, Pattern = @"(BE\d{8}-\d{4})", CaptureGroup = "1", Required = true, SanitizeForPath = true, TrimResult = true }];
        rule.Destination.FolderTemplate = @"{Beslagnummer}\Medicatie";

        var matching = await _client.PostAsJsonAsync("/api/rules/preview", new { rule, senderEmail = "bestelling@example.com", subject = "Medicatiebestelling - BE12345678-0101", body = "", attachmentName = "" }, _json);
        matching.EnsureSuccessStatusCode();
        var matchResult = await matching.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsTrue(matchResult.GetProperty("isMatch").GetBoolean());
        Assert.AreEqual("BE12345678-0101", matchResult.GetProperty("values").GetProperty("Beslagnummer").GetString());
        StringAssert.Contains(matchResult.GetProperty("plans")[0].GetProperty("destinationPath").GetString()!, "BE12345678-0101");

        var missing = await _client.PostAsJsonAsync("/api/rules/preview", new { rule, senderEmail = "bestelling@example.com", subject = "Medicatiebestelling zonder nummer", body = "", attachmentName = "" }, _json);
        missing.EnsureSuccessStatusCode();
        var missingResult = await missing.Content.ReadFromJsonAsync<JsonElement>();
        Assert.IsFalse(missingResult.GetProperty("extractionSuccess").GetBoolean());
        Assert.HasCount(1, missingResult.GetProperty("plans").EnumerateArray().ToArray());
    }

    [TestMethod]
    public async Task AllRulesEmlFlowReportsDisabledMatchedStoppedAndNotReached()
    {
        var disabled = CreateRule("disabled", "Disabled"); disabled.Priority = 10; disabled.Enabled = false;
        var matched = CreateRule("matched", "Matched"); matched.Priority = 20; matched.StopProcessingOnMatch = true; matched.SaveMode = SaveMode.MessageHtml;
        var unreached = CreateRule("unreached", "Unreached"); unreached.Priority = 30; unreached.SaveMode = SaveMode.MessageHtml;
        RuleConfigurationXml.SaveRulesAtomic(Path.Combine(_root, "rules.xml"), new RuleConfiguration { Rules = [disabled, matched, unreached] });

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(SimpleEml("test message", "Beslagnummer: BE12345678-1234")), "file", "message.eml");
        var response = await _client.PostAsync("/api/rules/test-eml", form);
        response.EnsureSuccessStatusCode();
        var statuses = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("results").EnumerateArray().Select(x => x.GetProperty("status").GetString()).ToArray();
        Assert.HasCount(3, statuses);
        Assert.AreEqual("Disabled", statuses[0]);
        Assert.AreEqual("Matched", statuses[1]);
        Assert.AreEqual("NotReached", statuses[2]);
    }

    [TestMethod]
    public async Task SettingsDefaultsLogsAndEditingLockFlowRoundTrip()
    {
        var settings = await _client.GetFromJsonAsync<Settings>("/api/settings", _json);
        settings!.DefaultDestinationRoot = Path.Combine(_root, "default");
        settings.LogsPath = Path.Combine(_root, "logs");
        (await _client.PutAsJsonAsync("/api/settings", settings, _json)).EnsureSuccessStatusCode();
        var defaults = await _client.GetFromJsonAsync<JsonElement>("/api/rule-defaults");
        Assert.AreEqual("Settings", defaults.GetProperty("source").GetString());
        Assert.AreEqual(settings.DefaultDestinationRoot, defaults.GetProperty("destinationRoot").GetString());

        CsvLogWriter.AppendProcessingLog(settings.LogsPath, new ProcessingLogRow(DateTime.Now, "Processed", "alpha", "Alpha", "m1", "lab@example.com", "test", "rapport.pdf", _root, "Success", "saved"));
        var logs = await _client.GetFromJsonAsync<JsonElement>("/api/logs?days=14");
        Assert.AreEqual(1, logs.GetProperty("rows").GetArrayLength());
        Assert.AreEqual("Success", logs.GetProperty("rows")[0].GetProperty("status").GetString());

        settings.RuleEditingEnabled = false;
        (await _client.PutAsJsonAsync("/api/settings", settings, _json)).EnsureSuccessStatusCode();
        Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, (await _client.PostAsJsonAsync("/api/rules", CreateRule("", "Blocked"), _json)).StatusCode);
        Assert.AreEqual(System.Net.HttpStatusCode.Forbidden, (await _client.PostAsync("/api/rules/alpha/toggle", null)).StatusCode);
    }

    private static string SimpleEml(string subject, string body) => $"From: Lab <lab@example.com>\r\nTo: receiver@example.com\r\nSubject: {subject}\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n{body}\r\n";

    private Rule CreateRule(string id, string name) => new()
    {
        RuleId = id, Name = name, Description = "Test rule", Conditions = [new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Contains, Value = "test" }],
        Destination = new Destination { Root = _root, FolderTemplate = "Saved", FileNameTemplate = "{OriginalFileName}" }
    };
}
