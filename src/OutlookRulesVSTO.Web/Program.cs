using OutlookRulesVSTO.Core;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json;
using MimeKit;
using System.Xml.Linq;

var outlookHostToken = Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_OUTLOOK_TOKEN") ?? string.Empty;
if (outlookHostToken.Length < 16)
{
    Console.Error.WriteLine("OutlookRulesVSTO web host may only be started by the Outlook VSTO add-in.");
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<OutlookRulesVSTO.Web.AiRuleBuilder>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_URL") ?? "http://127.0.0.1:5187");
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

var gate = new object();
var outlookBridgeToken = outlookHostToken;
Rule? pendingOutlookDraft = null;

bool OutlookBridgeAuthorized(HttpRequest request) =>
    outlookBridgeToken.Length >= 16 &&
    request.Headers.TryGetValue("X-OutlookRulesVSTO-Outlook-Token", out var supplied) &&
    System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(outlookBridgeToken), Encoding.UTF8.GetBytes(supplied.ToString()));

(Settings Settings, RuleConfiguration Rules) Load()
{
    var settingsPath = Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_SETTINGS_PATH") ?? AppPaths.DefaultSettingsPath;
    var settings = File.Exists(settingsPath)
        ? RuleConfigurationXml.LoadSettings(settingsPath)
        : SampleData.CreateDefaultSettings();
    settings.RulesPath = Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_RULES_PATH") ?? settings.RulesPath;
    AppPaths.EnsureStorageFolders(settings);
    var rules = File.Exists(settings.RulesPath)
        ? RuleMigrator.Migrate(RuleConfigurationXml.LoadRules(settings.RulesPath))
        : SampleData.CreateDefaultRules();
    return (settings, rules);
}

IResult Mutate(string id, Action<RuleConfiguration, Rule> action)
{
    lock (gate)
    {
        var (settings, rules) = Load();
        if (!settings.RuleEditingEnabled) return Results.Json(new { message = "Rule editing is disabled in Settings." }, statusCode: 403);
        var rule = rules.Rules.FirstOrDefault(item => item.RuleId == id);
        if (rule is null) return Results.NotFound(new { message = "Rule not found." });
        action(rules, rule);
        foreach (var pair in rules.Rules.OrderBy(item => item.Priority).Select((item, index) => (item, index)))
            pair.item.Priority = (pair.index + 1) * 10;
        var validation = RuleValidator.Validate(rules, settings);
        if (!validation.IsValid) return Results.BadRequest(new { message = "The change is invalid.", issues = validation.Issues });
        RuleConfigurationXml.SaveRulesAtomic(settings.RulesPath, rules);
        return Results.Ok(new { saved = true, rules.ConfigVersion });
    }
}

app.MapGet("/api/rules", () =>
{
    lock (gate)
    {
        var (settings, config) = Load();
        var defaultDestinationRoot = SuggestedDestinationRoot(settings, config);
        return Results.Ok(new
        {
            config.ConfigVersion, editingEnabled = settings.RuleEditingEnabled,
            path = settings.RulesPath,
            rules = config.Rules.OrderBy(rule => rule.Priority).Select(rule => new
            {
                id = rule.RuleId, rule.Name, rule.Description, rule.Enabled, rule.Priority,
                conditions = rule.Conditions.Count == 0 ? "No conditions" : string.Join(" · ", rule.Conditions.Where(c => c.Enabled).Select(ConditionSummary)),
                conditionItems = rule.Conditions.Where(condition => condition.Enabled).Select(ConditionSummary).DefaultIfEmpty("No conditions"),
                extractionItems = rule.Extractions.Select(ExtractionSummary).DefaultIfEmpty("No extracted value configured"),
                extractionDetails = rule.Extractions.Select(extraction => new
                {
                    summary = ExtractionSummary(extraction),
                    regexItems = RegexExplanation(extraction)
                }),
                destination = DestinationDisplay(defaultDestinationRoot, rule.Destination),
                destinationFull = Path.Combine(rule.Destination.Root, rule.Destination.FolderTemplate)
            })
        });
    }
});
app.MapGet("/api/rules/{id}", (string id) =>
{
    lock (gate)
    {
        var (_, config) = Load();
        var rule = config.Rules.FirstOrDefault(item => item.RuleId == id);
        return rule is null ? Results.NotFound() : Results.Ok(rule);
    }
});
app.MapGet("/api/templates/lab", () => Results.Ok(SampleData.CreateDefaultRules().Rules[0]));
app.MapPost("/api/rules", (Rule rule) =>
{
    lock (gate)
    {
        var (settings, config) = Load();
        if (!settings.RuleEditingEnabled) return Results.Json(new { message = "Rule editing is disabled in Settings." }, statusCode: 403);
        rule.RuleId = Guid.NewGuid().ToString("D");
        rule.CreatedAt = rule.UpdatedAt = DateTime.Now;
        rule.Priority = config.Rules.Count == 0 ? 10 : config.Rules.Max(item => item.Priority) + 10;
        PrepareRule(rule);
        config.Rules.Add(rule);
        var validation = RuleValidator.Validate(config, settings);
        if (!validation.IsValid) return Results.BadRequest(new { message = "Check the highlighted values.", issues = validation.Issues });
        RuleConfigurationXml.SaveRulesAtomic(settings.RulesPath, config);
        return Results.Created($"/api/rules/{rule.RuleId}", new { id = rule.RuleId });
    }
});
app.MapPut("/api/rules/{id}", (string id, Rule input) =>
{
    lock (gate)
    {
        var (settings, config) = Load();
        if (!settings.RuleEditingEnabled) return Results.Json(new { message = "Rule editing is disabled in Settings." }, statusCode: 403);
        var index = config.Rules.FindIndex(item => item.RuleId == id);
        if (index < 0) return Results.NotFound(new { message = "Rule not found." });
        var existing = config.Rules[index];
        input.RuleId = existing.RuleId;
        input.Priority = existing.Priority;
        input.CreatedAt = existing.CreatedAt;
        input.UpdatedAt = DateTime.Now;
        PrepareRule(input);
        config.Rules[index] = input;
        var validation = RuleValidator.Validate(config, settings);
        if (!validation.IsValid) return Results.BadRequest(new { message = "Check the highlighted values.", issues = validation.Issues });
        RuleConfigurationXml.SaveRulesAtomic(settings.RulesPath, config);
        return Results.Ok(new { saved = true, config.ConfigVersion });
    }
});
app.MapPost("/api/rules/preview", (RulePreviewRequest request) =>
{
    lock (gate)
    {
        var (settings, _) = Load();
        PrepareRule(request.Rule);
        var validation = RuleValidator.ValidateRule(request.Rule, settings);
        if (!validation.IsValid) return Results.BadRequest(new { message = "The rule is not valid yet.", issues = validation.Issues });
        var message = new MessageContext
        {
            MessageId = "web-preview", SenderEmail = request.SenderEmail, SenderName = "Voorbeeldafzender",
            Subject = request.Subject, BodyText = request.Body, ReceivedTime = DateTime.Now,
            Attachments = [new AttachmentContext { Name = string.IsNullOrWhiteSpace(request.AttachmentName) ? "example.pdf" : request.AttachmentName, SizeKb = 42 }]
        };
        var result = new RuleEngine(settings).EvaluateRule(request.Rule, message);
        return Results.Ok(new
        {
            result.IsMatch, extractionSuccess = result.Extractions.Success, values = result.Extractions.Values,
            errors = result.Extractions.Errors, plans = result.SavePlans.Select(plan => new { status = plan.Status.ToString(), plan.DestinationPath })
        });
    }
});
app.MapPost("/api/rules/preview-eml", async (HttpRequest request) =>
{
    if (!request.HasFormContentType) return Results.BadRequest(new { message = "Select an .eml file." });
    var form = await request.ReadFormAsync();
    var file = form.Files.GetFile("file");
    if (file is null || !file.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { message = "Select a file with the .eml extension." });
    if (file.Length == 0 || file.Length > 50_000_000)
        return Results.BadRequest(new { message = "The .eml file must be smaller than 50 MB." });
    try
    {
        var rule = JsonSerializer.Deserialize<Rule>(form["rule"].ToString(), new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } });
        if (rule is null) return Results.BadRequest(new { message = "The rule could not be read." });
        await using var stream = file.OpenReadStream();
        var email = await MimeMessage.LoadAsync(stream);
        lock (gate)
        {
            var (settings, _) = Load();
            PrepareRule(rule);
            var validation = RuleValidator.ValidateRule(rule, settings);
            if (!validation.IsValid) return Results.BadRequest(new { message = "The rule is not valid yet.", issues = validation.Issues });
            var sender = email.From.Mailboxes.FirstOrDefault();
            var message = new MessageContext
            {
                MessageId = email.MessageId ?? "eml-preview", SenderEmail = sender?.Address ?? string.Empty, SenderName = sender?.Name ?? string.Empty,
                To = email.To.Mailboxes.Select(item => item.Address).ToList(), Cc = email.Cc.Mailboxes.Select(item => item.Address).ToList(),
                Subject = email.Subject ?? string.Empty, BodyText = email.TextBody ?? string.Empty, BodyHtml = email.HtmlBody ?? string.Empty,
                ReceivedTime = email.Date == DateTimeOffset.MinValue ? DateTime.Now : email.Date.LocalDateTime,
                Attachments = email.Attachments.OfType<MimePart>().Select(part =>
                {
                    var contentStream = part.Content?.Stream;
                    return new AttachmentContext { Name = part.FileName ?? "attachment", SizeKb = contentStream?.CanSeek == true ? Math.Max(1, contentStream.Length / 1024) : 1 };
                }).ToList()
            };
            var result = new RuleEngine(settings).EvaluateRule(rule, message);
            return Results.Ok(new
            {
                email = new { sender = message.SenderEmail, message.Subject, attachments = message.Attachments.Select(item => item.Name) },
                result.IsMatch, extractionSuccess = result.Extractions.Success, values = result.Extractions.Values,
                errors = result.Extractions.Errors, plans = result.SavePlans.Select(plan => new { status = plan.Status.ToString(), plan.DestinationPath })
            });
        }
    }
    catch (Exception ex) when (ex is FormatException or ParseException)
    {
        return Results.BadRequest(new { message = "The .eml file could not be read.", detail = ex.Message });
    }
}).DisableAntiforgery();
app.MapPost("/api/rules/test-eml", async (IFormFile file) =>
{
    if (!file.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) || file.Length == 0 || file.Length > 50_000_000)
        return Results.BadRequest(new { message = "Select a valid .eml file smaller than 50 MB." });
    try
    {
        await using var stream = file.OpenReadStream();
        var email = await MimeMessage.LoadAsync(stream);
        lock (gate)
        {
            var (settings, configuration) = Load();
            var sender = email.From.Mailboxes.FirstOrDefault();
            var message = new MessageContext
            {
                MessageId = email.MessageId ?? "eml-all-rules", SenderEmail = sender?.Address ?? string.Empty, SenderName = sender?.Name ?? string.Empty,
                To = email.To.Mailboxes.Select(item => item.Address).ToList(), Cc = email.Cc.Mailboxes.Select(item => item.Address).ToList(),
                Subject = email.Subject ?? string.Empty, BodyText = email.TextBody ?? string.Empty, BodyHtml = email.HtmlBody ?? string.Empty,
                ReceivedTime = email.Date == DateTimeOffset.MinValue ? DateTime.Now : email.Date.LocalDateTime,
                Attachments = email.Attachments.OfType<MimePart>().Select(part => new AttachmentContext { Name = part.FileName ?? "attachment", SizeKb = 1 }).ToList()
            };
            var engine = new RuleEngine(settings);
            var results = new List<object>();
            var stopped = false;
            foreach (var rule in configuration.Rules.OrderBy(item => item.Priority))
            {
                if (!rule.Enabled) { results.Add(new { rule.RuleId, rule.Name, rule.Priority, status = "Disabled", isMatch = false, values = new Dictionary<string, string>(), errors = Array.Empty<string>(), plans = Array.Empty<object>() }); continue; }
                if (stopped) { results.Add(new { rule.RuleId, rule.Name, rule.Priority, status = "NotReached", isMatch = false, values = new Dictionary<string, string>(), errors = Array.Empty<string>(), plans = Array.Empty<object>() }); continue; }
                var evaluation = engine.EvaluateRule(rule, message);
                results.Add(new { rule.RuleId, rule.Name, rule.Priority, status = evaluation.IsMatch ? "Matched" : "NoMatch", evaluation.IsMatch, values = evaluation.Extractions.Values, errors = evaluation.Extractions.Errors, plans = evaluation.SavePlans.Select(plan => new { status = plan.Status.ToString(), plan.DestinationPath }) });
                if (evaluation.IsMatch && rule.StopProcessingOnMatch) stopped = true;
            }
            return Results.Ok(new { email = new { sender = message.SenderEmail, message.Subject, attachments = message.Attachments.Select(item => item.Name) }, stopped, results });
        }
    }
    catch (Exception ex) when (ex is FormatException or ParseException)
    {
        return Results.BadRequest(new { message = "The .eml file could not be read.", detail = ex.Message });
    }
}).DisableAntiforgery();
app.MapPost("/api/ai/rule-from-eml", async (IFormFile file, OutlookRulesVSTO.Web.AiRuleBuilder ai, CancellationToken cancellationToken) =>
{
    var (settings, configuration) = Load();
    if (!settings.AiHelperEnabled) return Results.BadRequest(new { message = "Enable the AI helper in Settings first." });
    if (!settings.RuleEditingEnabled) return Results.Json(new { message = "Rule editing is disabled." }, statusCode: 403);
    if (!file.FileName.EndsWith(".eml", StringComparison.OrdinalIgnoreCase) || file.Length is 0 or > 50_000_000)
        return Results.BadRequest(new { message = "Select a valid .eml file smaller than 50 MB." });
    settings.DefaultDestinationRoot = SuggestedDestinationRoot(settings, configuration);
    if (string.IsNullOrWhiteSpace(settings.DefaultDestinationRoot))
        return Results.BadRequest(new { message = "Configure a default destination folder in Settings first. The AI uses that folder for new draft rules." });
    try
    {
        await using var stream = file.OpenReadStream();
        var email = await MimeMessage.LoadAsync(stream, cancellationToken);
        var sender = email.From.Mailboxes.FirstOrDefault()?.Address ?? string.Empty;
        var mail = new OutlookRulesVSTO.Web.AiMail(sender, email.Subject ?? string.Empty, email.TextBody ?? string.Empty,
            email.Attachments.OfType<MimePart>().Select(x => x.FileName ?? "attachment").ToArray());
        var rule = await ai.BuildAsync(settings, mail, cancellationToken);
        PrepareRule(rule);
        var validation = RuleValidator.ValidateRule(rule, settings);
        if (!validation.IsValid) OutlookRulesVSTO.Web.AiDiagnosticLogger.InvalidRule(settings, validation.Issues);
        return validation.IsValid ? Results.Ok(rule) : Results.BadRequest(new { message = "AI created an invalid draft rule.", issues = validation.Issues });
    }
    catch (Exception ex) when (ex is FormatException or ParseException or InvalidDataException or InvalidOperationException or HttpRequestException or TimeoutException or FileNotFoundException or JsonException)
    {
        OutlookRulesVSTO.Web.AiDiagnosticLogger.Failure(settings, ex);
        return Results.BadRequest(new { message = ex.Message });
    }
}).DisableAntiforgery();
app.MapPost("/api/outlook/ai-rule", async (HttpRequest request, OutlookMailBridgeRequest mail, OutlookRulesVSTO.Web.AiRuleBuilder ai, CancellationToken cancellationToken) =>
{
    if (!OutlookBridgeAuthorized(request))
        return Results.Json(new { message = "The Outlook session could not be verified. Close Outlook and OutlookRulesVSTO completely and try again." }, statusCode: 401);
    var (settings, configuration) = Load();
    if (!settings.AiHelperEnabled) return Results.BadRequest(new { message = "Enable the AI helper in Settings first." });
    if (!settings.RuleEditingEnabled) return Results.Json(new { message = "Rule editing is disabled." }, statusCode: 403);
    settings.DefaultDestinationRoot = SuggestedDestinationRoot(settings, configuration);
    if (string.IsNullOrWhiteSpace(settings.DefaultDestinationRoot))
        return Results.BadRequest(new { message = "Configure a default destination folder in Settings first." });
    try
    {
        var aiMail = new OutlookRulesVSTO.Web.AiMail(mail.SenderEmail ?? string.Empty, mail.Subject ?? string.Empty,
            mail.BodyText ?? string.Empty, (mail.Attachments ?? []).Select(item => item.Name ?? "attachment").ToArray());
        var rule = await ai.BuildAsync(settings, aiMail, cancellationToken);
        PrepareRule(rule);
        var validation = RuleValidator.ValidateRule(rule, settings);
        if (!validation.IsValid) return Results.BadRequest(new { message = "AI created an invalid draft rule.", issues = validation.Issues });
        lock (gate) pendingOutlookDraft = rule;
        return Results.Accepted(value: new { accepted = true });
    }
    catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or HttpRequestException or TimeoutException or FileNotFoundException or JsonException)
    {
        OutlookRulesVSTO.Web.AiDiagnosticLogger.Failure(settings, ex);
        return Results.BadRequest(new { message = ex.Message });
    }
});
app.MapGet("/api/outlook/pending-draft", () =>
{
    lock (gate)
    {
        if (pendingOutlookDraft is null) return Results.Ok(new { available = false });
        var draft = pendingOutlookDraft;
        pendingOutlookDraft = null;
        return Results.Ok(new { available = true, draft });
    }
});
app.MapGet("/api/ai/status", (OutlookRulesVSTO.Web.AiRuleBuilder ai) => Results.Ok(new { status = ai.Status }));
app.MapPost("/api/rules/{id}/toggle", (string id) => Mutate(id, (_, rule) => rule.Enabled = !rule.Enabled));
app.MapPost("/api/rules/{id}/copy", (string id) =>
{
    lock (gate)
    {
        var (settings, config) = Load();
        if (!settings.RuleEditingEnabled) return Results.Json(new { message = "Rule editing is disabled in Settings." }, statusCode: 403);
        var source = config.Rules.FirstOrDefault(item => item.RuleId == id);
        if (source is null) return Results.NotFound(new { message = "Rule not found." });
        var copy = JsonSerializer.Deserialize<Rule>(JsonSerializer.Serialize(source))!;
        copy.RuleId = Guid.NewGuid().ToString("D");
        copy.Name = $"Copy of {source.Name}";
        copy.Priority = config.Rules.Max(item => item.Priority) + 10;
        copy.CreatedAt = copy.UpdatedAt = DateTime.Now;
        foreach (var condition in copy.Conditions) condition.ConditionId = Guid.NewGuid().ToString("D");
        foreach (var extraction in copy.Extractions) extraction.ExtractionId = Guid.NewGuid().ToString("D");
        foreach (var filter in copy.AttachmentFilters) filter.FilterId = Guid.NewGuid().ToString("D");
        config.Rules.Add(copy);
        var validation = RuleValidator.Validate(config, settings);
        if (!validation.IsValid) return Results.BadRequest(new { message = "The copy could not be created.", issues = validation.Issues });
        RuleConfigurationXml.SaveRulesAtomic(settings.RulesPath, config);
        return Results.Ok(new { id = copy.RuleId });
    }
});
app.MapPost("/api/rules/{id}/move/{direction}", (string id, string direction) => Mutate(id, (rules, rule) =>
{
    var ordered = rules.Rules.OrderBy(item => item.Priority).ToList();
    var index = ordered.IndexOf(rule);
    var target = direction == "up" ? index - 1 : index + 1;
    if (target >= 0 && target < ordered.Count)
        (ordered[index].Priority, ordered[target].Priority) = (ordered[target].Priority, ordered[index].Priority);
}));
app.MapDelete("/api/rules/{id}", (string id) => Mutate(id, (rules, rule) => rules.Rules.Remove(rule)));
app.MapGet("/api/rules/export", () =>
{
    lock (gate)
    {
        var (_, config) = Load();
        var xml = RuleConfigurationXml.ToRulesDocument(config).ToString();
        return Results.File(Encoding.UTF8.GetBytes(xml), "application/xml", "rules.xml");
    }
});
app.MapPost("/api/rules/import", async (IFormFile file) =>
{
    if (file.Length == 0 || file.Length > 5_000_000) return Results.BadRequest(new { message = "Choose a valid XML file smaller than 5 MB." });
    try
    {
        await using var stream = file.OpenReadStream();
        var imported = RuleMigrator.Migrate(RuleConfigurationXml.FromRulesDocument(await XDocument.LoadAsync(stream, LoadOptions.SetLineInfo, default)));
        lock (gate)
        {
            var (settings, _) = Load();
            if (!settings.RuleEditingEnabled) return Results.Json(new { message = "Rule editing is disabled in Settings." }, statusCode: 403);
            var validation = RuleValidator.Validate(imported, settings);
            if (!validation.IsValid) return Results.BadRequest(new { message = "The file contains invalid rules.", issues = validation.Issues });
            RuleConfigurationXml.SaveRulesAtomic(settings.RulesPath, imported);
            return Results.Ok(new { imported = imported.Rules.Count, imported.ConfigVersion });
        }
    }
    catch (Exception ex) when (ex is InvalidDataException or System.Xml.XmlException)
    {
        return Results.BadRequest(new { message = "This is not a valid rules.xml file.", detail = ex.Message });
    }
}).DisableAntiforgery();

app.MapGet("/api/settings", () =>
{
    lock (gate) return Results.Ok(Load().Settings);
});
app.MapGet("/api/rule-defaults", () =>
{
    lock (gate)
    {
        var (settings, configuration) = Load();
        var configured = settings.DefaultDestinationRoot?.Trim() ?? string.Empty;
        var suggested = SuggestedDestinationRoot(settings, configuration);
        return Results.Ok(new
        {
            destinationRoot = suggested,
            source = configured.Length > 0 ? "Settings" : suggested.Length > 0 ? "ExistingRules" : "None",
            duplicatePolicy = settings.DefaultDuplicatePolicy,
            settings.SaveFailedExtractionsToReviewFolder
        });
    }
});
app.MapPut("/api/settings", (Settings input) =>
{
    lock (gate)
    {
        input.SchemaVersion = Schema.CurrentSchemaVersion;
        AppPaths.EnsureStorageFolders(input);
        var settingsPath = Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_SETTINGS_PATH") ?? AppPaths.DefaultSettingsPath;
        RuleConfigurationXml.SaveSettingsAtomic(settingsPath, input);
        return Results.Ok(new { saved = true });
    }
});

app.MapGet("/api/logs", (int? days) =>
{
    lock (gate)
    {
        var (settings, _) = Load();
        var cutoff = DateTime.Today.AddDays(-Math.Clamp(days ?? 14, 1, 365));
        var rows = new List<object>();
        if (Directory.Exists(settings.LogsPath))
        {
            foreach (var file in Directory.GetFiles(settings.LogsPath, "processing-*.csv").OrderByDescending(Path.GetFileName))
            {
                foreach (var columns in CsvLogWriter.ReadCsv(file).Skip(1))
                {
                    if (columns.Length < 13 || (DateTime.TryParse(columns[0], out var timestamp) && timestamp < cutoff)) continue;
                    rows.Add(new { timestamp = columns[0], eventType = columns[1], ruleName = columns[3], sender = columns[5], subject = columns[6], attachment = columns[7], destination = columns[8], status = columns[9], message = columns[10] });
                    if (rows.Count >= 2000) break;
                }
                if (rows.Count >= 2000) break;
            }
        }
        return Results.Ok(new { settings.LogsPath, rows });
    }
});
app.MapFallbackToFile("index.html");
app.Run();

static void PrepareRule(Rule rule)
{
    if (string.IsNullOrWhiteSpace(rule.RuleId)) rule.RuleId = Guid.NewGuid().ToString("D");
    rule.SchemaVersion = Schema.CurrentSchemaVersion;
    rule.WatchFolderIds = rule.WatchFolderIds.Count == 0 ? ["Inbox"] : rule.WatchFolderIds;
    foreach (var condition in rule.Conditions)
        if (string.IsNullOrWhiteSpace(condition.ConditionId)) condition.ConditionId = Guid.NewGuid().ToString("D");
    foreach (var extraction in rule.Extractions)
        if (string.IsNullOrWhiteSpace(extraction.ExtractionId)) extraction.ExtractionId = Guid.NewGuid().ToString("D");
    foreach (var filter in rule.AttachmentFilters)
        if (string.IsNullOrWhiteSpace(filter.FilterId)) filter.FilterId = Guid.NewGuid().ToString("D");
}

static string ConditionSummary(Condition condition)
{
    var target = condition.Target switch
    {
        ConditionTarget.SenderEmail => "Sender", ConditionTarget.SenderName => "Sender name", ConditionTarget.To => "Aan", ConditionTarget.Cc => "Cc",
        ConditionTarget.Subject => "Subject", ConditionTarget.BodyText => "Message body", ConditionTarget.BodyHtml => "HTML-inhoud",
        ConditionTarget.ReceivedDate => "Received date", ConditionTarget.HasAttachments => "Has attachments", ConditionTarget.AttachmentName => "Attachment name",
        ConditionTarget.AttachmentExtension => "File type", ConditionTarget.AttachmentCount => "Attachment count", ConditionTarget.Account => "Account", ConditionTarget.Categories => "Categories",
        _ => condition.Target.ToString()
    };
    var comparison = condition.Operator switch
    {
        ConditionOperator.Contains => "bevat", ConditionOperator.DoesNotContain => "bevat niet", ConditionOperator.Equals => "is gelijk aan",
        ConditionOperator.NotEquals => "is niet gelijk aan", ConditionOperator.StartsWith => "begint met", ConditionOperator.EndsWith => "eindigt met",
        ConditionOperator.RegexMatch => "voldoet aan patroon", ConditionOperator.RegexNotMatch => "voldoet niet aan patroon",
        ConditionOperator.IsEmpty => "is leeg", ConditionOperator.IsNotEmpty => "is niet leeg", ConditionOperator.GreaterThan => "is groter dan",
        ConditionOperator.LessThan => "is kleiner dan", ConditionOperator.Between => "ligt tussen", _ => condition.Operator.ToString()
    };
    if (condition.Operator is ConditionOperator.IsEmpty or ConditionOperator.IsNotEmpty) return $"{target} {comparison}";
    if (condition.Operator == ConditionOperator.Between) return $"{target} {comparison} ‘{condition.Value}’ en ‘{condition.Value2}’";
    return $"{target} {comparison} ‘{condition.Value}’";
}

static string SuggestedDestinationRoot(Settings settings, RuleConfiguration configuration)
{
    var configured = settings.DefaultDestinationRoot?.Trim() ?? string.Empty;
    if (configured.Length > 0) return configured;
    return configuration.Rules
        .Select(rule => rule.Destination.Root?.Trim() ?? string.Empty)
        .Where(root => root.Length > 0)
        .GroupBy(root => root, StringComparer.OrdinalIgnoreCase)
        .OrderByDescending(group => group.Count())
        .Select(group => group.First())
        .FirstOrDefault() ?? string.Empty;
}

static string ExtractionSummary(Extraction extraction)
{
    var source = extraction.Source switch
    {
        ExtractionSource.Subject => "onderwerp",
        ExtractionSource.BodyText => "message body",
        ExtractionSource.BodyHtmlStripped => "HTML-inhoud zonder opmaak",
        ExtractionSource.AttachmentName => "attachment names",
        ExtractionSource.Automatic => "subject, attachment names, and message content",
        _ => extraction.Source.ToString()
    };
    var method = extraction.Method switch
    {
        ExtractionMethod.AfterLabelUntilLineEnd => $"tekst na label ‘{extraction.Pattern}’",
        ExtractionMethod.LineStartsWith => $"line starting with ‘{extraction.Pattern}’",
        ExtractionMethod.BetweenText => $"tekst tussen ‘{extraction.Pattern}’ en ‘{extraction.Pattern2}’",
        ExtractionMethod.RegexCapture => FriendlyPatternDescription(extraction.Pattern),
        _ => extraction.Method.ToString()
    };
    var requirement = extraction.Required ? "required" : "optional";
    var transform = extraction.Transform switch
    {
        ExtractionTransform.None => string.Empty,
        ExtractionTransform.Uppercase => "; naar hoofdletters",
        ExtractionTransform.Lowercase => "; naar kleine letters",
        ExtractionTransform.ReplaceSpaces => "; spaties vervangen",
        ExtractionTransform.DateFormat => $"; datumopmaak {extraction.TransformFormat}",
        _ => $"; {extraction.Transform}"
    };
    var fallback = string.IsNullOrWhiteSpace(extraction.DefaultValue) ? string.Empty : $"; standaard ‘{extraction.DefaultValue}’";
    return $"{extraction.Name}: uit {source}, {method} ({requirement}{transform}{fallback})";
}

static IReadOnlyList<string> RegexExplanation(Extraction extraction)
{
    if (extraction.Method != ExtractionMethod.RegexCapture) return [];
    var pattern = extraction.Pattern ?? string.Empty;
    var items = new List<string>
    {
        $"Searches in {extraction.Source switch { ExtractionSource.Subject => "the subject line", ExtractionSource.AttachmentName => "the attachment names", ExtractionSource.BodyText => "the email body", ExtractionSource.BodyHtmlStripped => "the HTML content", ExtractionSource.Automatic => "subject, attachment names, and message content; conflicting values are sent for review", _ => "the email" }}.",
        FriendlyPatternSentence(pattern)
    };
    if (pattern.Contains(@"\s", StringComparison.Ordinal)) items.Add("Extra spaces or a newline are allowed.");
    if (pattern.Contains(@"\*?", StringComparison.Ordinal)) items.Add("Een sterretje na het woord mag, maar hoeft niet.");
    items.Add("Use the extracted value in the folder or filename.");
    return items;
}

static string FriendlyPatternDescription(string pattern) =>
    pattern.Contains(@"BE\d{8}-\d{4}", StringComparison.Ordinal)
        ? "recognizes a Belgian holding number, for example BE12345678-1234"
        : "recognizes the extracted value using a configured pattern";

static string FriendlyPatternSentence(string pattern) =>
    pattern.Contains(@"BE\d{8}-\d{4}", StringComparison.Ordinal)
        ? "Recognizes a Belgian holding number: BE followed by 8 digits, a hyphen, and 4 more digits. For example BE12345678-1234."
        : "Recognizes the extracted value using a configured text pattern.";

static string DestinationDisplay(string defaultRoot, Destination destination)
{
    var fullPath = Path.Combine(destination.Root, destination.FolderTemplate);
    if (string.IsNullOrWhiteSpace(defaultRoot)) return fullPath;
    var relative = Path.GetRelativePath(defaultRoot, fullPath);
    if (relative == ".") return "Root folder";
    return relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        ? fullPath
        : relative;
}

public partial class Program { }

public sealed record RulePreviewRequest(Rule Rule, string SenderEmail, string Subject, string Body, string AttachmentName);
public sealed record OutlookMailBridgeRequest(string? MessageId, string? SenderEmail, string? SenderName, string? Subject,
    string? BodyText, string? BodyHtml, DateTime ReceivedTime, IReadOnlyList<OutlookMailBridgeAttachment>? Attachments);
public sealed record OutlookMailBridgeAttachment(string? Name, long SizeKb);
