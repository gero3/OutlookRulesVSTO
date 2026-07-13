using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Web;

internal sealed class AiRuleBuilder : IDisposable
{
    private const string DefaultModelFile = "Qwen_Qwen3.5-0.8B-Q4_K_M.gguf";
    private const string DefaultModelUrl = "https://huggingface.co/bartowski/Qwen_Qwen3.5-0.8B-GGUF/resolve/main/Qwen_Qwen3.5-0.8B-Q4_K_M.gguf";
    private const string DefaultModelSha256 = "fb044e93939a70469c905781334f5de1e6c8b608ced6cbc8c9249bd4127d9526";
    private static readonly HashSet<string> GenericAttachmentWords = LoadGenericAttachmentWords();
    private static HashSet<string> LoadGenericAttachmentWords()
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "report", "document", "doc", "attachment", "scan", "file", "export", "download", "output",
            "result", "results", "data", "image", "photo", "picture", "screenshot", "summary", "new",
            "untitled", "unknown", "copy", "book", "workbook", "spreadsheet", "sheet", "presentation",
            "img", "dsc", "pict", "mail", "email", "message"
        };
        using var stream = typeof(AiRuleBuilder).Assembly.GetManifestResourceStream("OutlookRulesVSTO.Web.Localization.nl-generic-attachment-words.txt");
        if (stream is null) return words;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } word)
            if (!string.IsNullOrWhiteSpace(word)) words.Add(word.Trim());
        return words;
    }
    private static readonly TimeSpan IdleUnloadDelay = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _unloadTimer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly HttpClient _modelDownloadHttp = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly ChildProcessJob _childProcesses = new();
    private Process? _server;
    private Uri? _serverUrl;
    private string? _loadedModel;
    private string _status = "Not loaded";
    private string _systemPrompt = new Settings().AiSystemPrompt;

    public AiRuleBuilder() => _unloadTimer = new Timer(_ => StopIfIdle(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    public string Status => _status;

    [ExcludeFromCodeCoverage(Justification = "Requires the optional multi-gigabyte local model and llama.cpp process; covered by opt-in AI E2E tests.")]
    public async Task<Rule> BuildAsync(Settings settings, AiMail mail, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _systemPrompt = settings.AiSystemPrompt;
            var modelPath = ResolveModelPath(settings.AiModel);
            await EnsureModelAvailableAsync(settings.AiModel, modelPath, cancellationToken);
            await EnsureServerAsync(modelPath, cancellationToken);
            var body = mail.Body[..Math.Min(mail.Body.Length, 8_000)];
            _status = "Determining name and description (1/6)";
            var metadata = await InferAsync<AiMetadata>("metadata", MetadataSchema, $"""
                {settings.AiMetadataPrompt}
                Subject: {mail.Subject}
                Message body: {body}
                """, 220, cancellationToken);

            _status = "Evaluating sender (2/6)";
            var sender = await InferAsync<AiUseDecision>("sender", UseDecisionSchema, $"""
                {settings.AiSenderPrompt}
                Sender address: {mail.Sender}
                Subject for context: {mail.Subject}
                """, 100, cancellationToken);

            _status = "Evaluating subject (3/6)";
            var subject = await InferAsync<AiTextCondition>("subject", TextConditionSchema, $"""
                {settings.AiSubjectPrompt}
                Subject: {mail.Subject}
                """, 180, cancellationToken);

            _status = "Evaluating attachments (4/6)";
            var attachment = await InferAsync<AiAttachmentCondition>("attachment", AttachmentConditionSchema, $"""
                {settings.AiAttachmentPrompt}
                A meaningful original filename is preserved because variable data such as a date, case, customer, order, or reference may be valuable. A generic name such as report.pdf or attachment.pdf is automatically given a clearer name. Exclude variable data only from the recognition value. Set useCondition to false when there are no attachments or no stable distinguishing part.
                Attachments: {(mail.Attachments.Length == 0 ? "(none)" : string.Join(", ", mail.Attachments))}
                """, 180, cancellationToken);

            _status = $"Finding {settings.AiExtractionName} (5/6)";
            var extraction = new AiExtraction("Automatic");

            _status = "Determining destination folder (6/6)";
            var folder = await InferAsync<AiFolder>("folder", FolderSchema, $"""
                {settings.AiFolderPrompt}
                Data to extract: {settings.AiExtractionName}. {settings.AiExtractionInstructions}
                Rule name: {metadata.Name}
                Subject: {mail.Subject}
                """, 100, cancellationToken);

            var conditions = new List<AiCondition>();
            if (sender.UseCondition) conditions.Add(new("SenderEmail", "Equals", mail.Sender));
            if (subject.UseCondition) conditions.Add(new("Subject", subject.Operator, subject.Value));
            if (SelectAttachmentCondition(mail, attachment) is { } attachmentCondition) conditions.Add(attachmentCondition);
            var draft = new AiRuleDraft(metadata.Name, metadata.Description, conditions.ToArray(), [extraction], folder.Name);
            _status = "Ready";
            return draft.ToRule(settings, mail);
        }
        finally
        {
            _unloadTimer.Change(IdleUnloadDelay, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    [ExcludeFromCodeCoverage(Justification = "Exercises the external llama.cpp HTTP protocol and is covered by opt-in AI E2E tests.")]
    private async Task<T> InferAsync<T>(string name, string schemaText, string prompt, int maxTokens, CancellationToken cancellationToken)
    {
        var schema = JsonDocument.Parse(schemaText).RootElement.Clone();
        var requestBody = new
        {
            model = "local-qwen", temperature = 0.1, max_tokens = maxTokens,
            chat_template_kwargs = new { enable_thinking = false },
            messages = new[]
            {
                new { role = "system", content = _systemPrompt + " Respond only with valid JSON matching the supplied schema." },
                new { role = "user", content = prompt }
            },
            response_format = new { type = "json_schema", json_schema = new { name, strict = true, schema } }
        };
        using var content = new StringContent(JsonSerializer.Serialize(requestBody, Json), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync(new Uri(_serverUrl!, "/v1/chat/completions"), content, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AI step ‘{name}’ failed ({(int)response.StatusCode}).");
        using var document = JsonDocument.Parse(responseText);
        var choice = document.RootElement.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var reason) && string.Equals(reason.GetString(), "length", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"AI step ‘{name}’ stopped before the response was complete.");
        var output = choice.GetProperty("message").GetProperty("content").GetString();
        try { return JsonSerializer.Deserialize<T>(output!, Json) ?? throw new InvalidDataException($"AI step ‘{name}’ returned no response."); }
        catch (JsonException ex) { throw new InvalidDataException($"AI step ‘{name}’ returned invalid JSON.", ex); }
    }

    [ExcludeFromCodeCoverage(Justification = "Starts and supervises the external llama.cpp executable; covered by opt-in AI E2E tests.")]
    private async Task EnsureServerAsync(string modelPath, CancellationToken cancellationToken)
    {
        if (_server is { HasExited: false } && string.Equals(_loadedModel, modelPath, StringComparison.OrdinalIgnoreCase)) return;
        StopServer();
        if (!File.Exists(modelPath)) throw new FileNotFoundException($"The local AI model is missing: {modelPath}.");
        var executable = ResolveRuntimePath();
        if (!File.Exists(executable)) throw new FileNotFoundException($"The local llama.cpp runtime is missing: {executable}.");
        var port = ReservePort();
        _serverUrl = new Uri($"http://127.0.0.1:{port}");
        _status = "Loading local model";
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] { "--model", modelPath, "--host", "127.0.0.1", "--port", port.ToString(), "--ctx-size", "8192", "--threads", Math.Min(8, Math.Max(1, Environment.ProcessorCount - 1)).ToString(), "--jinja", "--no-webui" })
            start.ArgumentList.Add(argument);
        _server = Process.Start(start) ?? throw new InvalidOperationException("llama.cpp could not be started.");
        try { _childProcesses.Add(_server); }
        catch { _server.Kill(entireProcessTree: true); _server.Dispose(); _server = null; throw; }
        _server.BeginOutputReadLine();
        _server.BeginErrorReadLine();
        _loadedModel = modelPath;
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _server.Refresh();
            if (_server.HasExited) throw new InvalidOperationException($"llama.cpp stopped while loading (code {_server.ExitCode}).");
            try
            {
                using var health = await _http.GetAsync(new Uri(_serverUrl, "/health"), cancellationToken);
                if (health.StatusCode == HttpStatusCode.OK) { _status = "Ready"; return; }
            }
            catch (HttpRequestException) { }
            await Task.Delay(250, cancellationToken);
        }
        throw new TimeoutException("The local model was not ready within 30 seconds.");
    }

    private static int ReservePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string ResolveRuntimePath() => ResolveBundledFile(Path.Combine("Runtime", "llama", "llama-server.exe"));
    internal static string ResolveModelPath(string configured)
    {
        var name = string.IsNullOrWhiteSpace(configured) ? DefaultModelFile : configured.Trim();
        if (Path.IsPathFullyQualified(name)) return name;
        var bundled = TryResolveBundledFile(Path.Combine("Models", name), Path.Combine("models", name));
        if (bundled is not null) return bundled;
        if (string.Equals(name, DefaultModelFile, StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OutlookRulesVSTO", "Models", DefaultModelFile);
        return Path.Combine(AppContext.BaseDirectory, "Models", name);
    }

    [ExcludeFromCodeCoverage(Justification = "Downloads and verifies the optional model; it requires a multi-gigabyte network transfer.")]
    private async Task EnsureModelAvailableAsync(string configured, string modelPath, CancellationToken cancellationToken)
    {
        if (File.Exists(modelPath)) return;
        var name = string.IsNullOrWhiteSpace(configured) ? DefaultModelFile : configured.Trim();
        if (!string.Equals(name, DefaultModelFile, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException($"The configured local AI model is missing: {modelPath}.");

        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        var partialPath = modelPath + ".download";
        try
        {
            File.Delete(partialPath);
            _status = "AI-model downloaden (0%)";
            using var response = await _modelDownloadHttp.GetAsync(DefaultModelUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[1024 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    if (total > 0) _status = $"AI-model downloaden ({Math.Min(100, received * 100 / total.Value)}%)";
                }
            }

            _status = "Verifying AI model";
            await using var downloaded = File.OpenRead(partialPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(downloaded, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(actualHash, DefaultModelSha256, StringComparison.Ordinal))
                throw new InvalidDataException("The SHA-256 verification of the downloaded AI model failed.");
            File.Move(partialPath, modelPath, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("The local AI model could not be downloaded. Check the internet connection and try again.", ex);
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    private static string ResolveBundledFile(string publishedRelative, string? developmentRelative = null)
    {
        return TryResolveBundledFile(publishedRelative, developmentRelative) ?? Path.Combine(AppContext.BaseDirectory, publishedRelative);
    }

    private static string? TryResolveBundledFile(string publishedRelative, string? developmentRelative = null)
    {
        var published = Path.Combine(AppContext.BaseDirectory, publishedRelative);
        if (File.Exists(published)) return published;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, developmentRelative ?? publishedRelative);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return null;
    }

    internal static AiCondition? SelectAttachmentCondition(AiMail mail, AiAttachmentCondition proposed)
    {
        if (proposed.UseCondition && IsGroundedAttachmentCondition(mail, proposed))
            return new AiCondition(proposed.Target, proposed.Operator, proposed.Value);

        foreach (var fileName in mail.Attachments)
        {
            var stem = Path.GetFileNameWithoutExtension(fileName);
            var prefix = System.Text.RegularExpressions.Regex.Split(stem, @"[\s_-]+")
                .FirstOrDefault(part => !string.IsNullOrWhiteSpace(part) && part.Length >= 3 && !part.Any(char.IsDigit) && !IsGenericAttachmentPrefix(part));
            if (string.IsNullOrWhiteSpace(prefix)) continue;
            return new AiCondition("AttachmentName", "Contains", prefix);
        }

        return null;
    }

    private static bool IsGroundedAttachmentCondition(AiMail mail, AiAttachmentCondition proposed)
    {
        if (string.IsNullOrWhiteSpace(proposed.Value) ||
            System.Text.RegularExpressions.Regex.IsMatch(proposed.Value, @"BE\d{8}-\d{4}", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return false;
        var candidates = proposed.Target == "AttachmentExtension"
            ? mail.Attachments.Select(Path.GetExtension)
            : mail.Attachments.AsEnumerable();
        return candidates.Any(value => value?.Contains(proposed.Value, StringComparison.OrdinalIgnoreCase) == true);
    }

    private static bool IsGenericAttachmentPrefix(string value) => GenericAttachmentWords.Contains(value);

    internal static bool IsGenericAttachmentName(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName).Trim().ToLowerInvariant();
        baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"(?:\s*\(\d+\)|[ _-]*\d+)$", string.Empty).Trim();
        var words = System.Text.RegularExpressions.Regex.Split(baseName, @"[\s_-]+")
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToArray();
        return words.Length > 0 && words.All(GenericAttachmentWords.Contains);
    }

    private void StopIfIdle()
    {
        if (!_gate.Wait(0)) return;
        try { StopServer(); }
        finally { _gate.Release(); }
    }

    private void StopServer()
    {
        try { if (_server is { HasExited: false }) _server.Kill(entireProcessTree: true); } catch { }
        _server?.Dispose();
        _server = null; _serverUrl = null; _loadedModel = null; _status = "Not loaded";
    }

    public void Dispose()
    {
        _unloadTimer.Dispose(); _gate.Wait();
        try { StopServer(); _childProcesses.Dispose(); _http.Dispose(); _modelDownloadHttp.Dispose(); }
        finally { _gate.Release(); _gate.Dispose(); }
    }

    private const string MetadataSchema = """{"type":"object","additionalProperties":false,"required":["name","description"],"properties":{"name":{"type":"string","maxLength":60},"description":{"type":"string","maxLength":160}}}""";
    private const string UseDecisionSchema = """{"type":"object","additionalProperties":false,"required":["useCondition"],"properties":{"useCondition":{"type":"boolean"}}}""";
    private const string TextConditionSchema = """{"type":"object","additionalProperties":false,"required":["useCondition","operator","value"],"properties":{"useCondition":{"type":"boolean"},"operator":{"type":"string","enum":["Contains","Equals","StartsWith"]},"value":{"type":"string","maxLength":160}}}""";
    private const string AttachmentConditionSchema = """{"type":"object","additionalProperties":false,"required":["useCondition","target","operator","value"],"properties":{"useCondition":{"type":"boolean"},"target":{"type":"string","enum":["AttachmentName","AttachmentExtension"]},"operator":{"type":"string","enum":["Contains","Equals","StartsWith","EndsWith"]},"value":{"type":"string","maxLength":120}}}""";
    private const string FolderSchema = """{"type":"object","additionalProperties":false,"required":["name"],"properties":{"name":{"type":"string","maxLength":60}}}""";
}

internal sealed record AiMail(string Sender, string Subject, string Body, string[] Attachments);
internal sealed record AiMetadata(string Name, string Description);
internal sealed record AiUseDecision(bool UseCondition);
internal sealed record AiTextCondition(bool UseCondition, string Operator, string Value);
internal sealed record AiAttachmentCondition(bool UseCondition, string Target, string Operator, string Value);
internal sealed record AiFolder(string Name);
internal sealed record AiCondition(string Target, string Operator, string Value);
internal sealed record AiExtraction(string Source);
internal sealed record AiRuleDraft(string Name, string Description, AiCondition[] Conditions, AiExtraction[] Extractions, string FolderTemplate)
{
    public Rule ToRule(Settings settings, AiMail mail)
    {
        return new Rule
        {
        Name = Name, Description = Description, Enabled = false,
        Conditions = CleanConditions(Conditions, mail, settings),
        Extractions = Extractions.Select(value => CleanExtraction(value, settings)).ToList(),
        SaveMode = mail.Attachments.Length == 0 ? SaveMode.MessageHtml : SaveMode.Attachments,
        AttachmentFilters = mail.Attachments.Length == 0 ? [] : [new AttachmentFilter { Extensions = mail.Attachments.Select(Path.GetExtension).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToList() }],
        Destination = new Destination { Root = settings.DefaultDestinationRoot, FolderTemplate = CleanFolderTemplate(FolderTemplate, settings, Extractions.Length > 0), FileNameTemplate = BuildFileNameTemplate(mail, settings, Extractions.Length > 0), SaveFailedExtractionsToReviewFolder = settings.SaveFailedExtractionsToReviewFolder }
        };
    }

    private static List<Condition> CleanConditions(IEnumerable<AiCondition> conditions, AiMail mail, Settings settings) => conditions
        .Where(x => !string.IsNullOrWhiteSpace(x.Value))
        .Where(x => !ContainsExtractedValue(x.Value, mail, settings.AiExtractionPattern))
        .Where(x => mail.Attachments.Length > 0 || x.Target is not ("AttachmentName" or "AttachmentExtension"))
        .Where(x => x.Target != "SenderName" || !conditions.Any(c => c.Target == "SenderEmail"))
        .Select(x => GroundCondition(x, mail))
        .Where(x => x is not null)
        .Take(3)
        .Select(x => new Condition { Target = Enum.Parse<ConditionTarget>(x!.Target), Operator = Enum.Parse<ConditionOperator>(x.Operator), Value = x.Value })
        .ToList();

    private static AiCondition? GroundCondition(AiCondition condition, AiMail mail)
    {
        if (condition.Target == "SenderEmail")
            return string.IsNullOrWhiteSpace(mail.Sender) ? null : new AiCondition("SenderEmail", "Equals", mail.Sender);

        var candidates = condition.Target switch
        {
            "Subject" => new[] { mail.Subject },
            "BodyText" => new[] { mail.Body },
            "AttachmentName" => mail.Attachments,
            "AttachmentExtension" => mail.Attachments.Select(Path.GetExtension).ToArray(),
            _ => Array.Empty<string>()
        };
        if (condition.Operator == "RegexMatch") return condition;
        return candidates.Any(value => value?.Contains(condition.Value, StringComparison.OrdinalIgnoreCase) == true) ? condition : null;
    }

    private static Extraction CleanExtraction(AiExtraction value, Settings settings)
    {
        return new Extraction
        {
            Name = settings.AiExtractionName.Trim(), Source = Enum.Parse<ExtractionSource>(value.Source),
            Method = ExtractionMethod.RegexCapture, Pattern = settings.AiExtractionPattern.Trim(), Pattern2 = string.Empty,
            CaptureGroup = "1", Required = settings.AiExtractionRequired, SanitizeForPath = true, TrimResult = true
        };
    }

    private static bool ContainsExtractedValue(string value, AiMail mail, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var sources = new[] { mail.Subject, mail.Body }.Concat(mail.Attachments);
        foreach (var source in sources)
        {
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(source ?? string.Empty, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                if (match.Groups.Count > 1 && !string.IsNullOrWhiteSpace(match.Groups[1].Value) &&
                    value.Contains(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    private static string CleanFolderTemplate(string proposed, Settings settings, bool hasExtraction)
    {
        var token = hasExtraction ? $"{{{settings.AiExtractionName.Trim()}}}" : string.Empty;
        if (string.IsNullOrWhiteSpace(proposed)) return token;
        if (string.IsNullOrEmpty(token) || proposed.Contains(token, StringComparison.OrdinalIgnoreCase)) return proposed;
        return $"{token}\\{proposed.Trim().TrimStart('\\')}";
    }

    private static string BuildFileNameTemplate(AiMail mail, Settings settings, bool hasExtraction)
    {
        if (mail.Attachments.Length == 0) return "E-mail_{ReceivedDate:yyyyMMdd_HHmmss}.html";
        if (mail.Attachments.All(AiRuleBuilder.IsGenericAttachmentName))
            return hasExtraction
                ? $"{{RuleName}}_{{{settings.AiExtractionName.Trim()}}}_{{ReceivedDate:yyyyMMdd}}{{OriginalFileExtension}}"
                : "{RuleName}_{ReceivedDate:yyyyMMdd}{OriginalFileExtension}";
        return "{OriginalFileName}";
    }

}
