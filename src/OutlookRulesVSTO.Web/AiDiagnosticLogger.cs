using System.Text;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Web;

internal static class AiDiagnosticLogger
{
    private static readonly Lock Gate = new();

    public static void Failure(Settings settings, Exception exception)
    {
        Write(settings, $"FAILED | {exception}");
    }

    public static void InvalidRule(Settings settings, IEnumerable<ValidationIssue> issues)
    {
        var details = string.Join(" | ", issues.Select(issue => $"{issue.Severity}: {issue.Path}: {issue.Message}"));
        Write(settings, $"INVALID_RULE | {details}");
    }

    private static void Write(Settings settings, string message)
    {
        try
        {
            Directory.CreateDirectory(settings.LogsPath);
            var path = Path.Combine(settings.LogsPath, $"ai-{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.Now:O} | model={SafeModelName(settings.AiModel)} | {message}{Environment.NewLine}";
            lock (Gate) File.AppendAllText(path, line, new UTF8Encoding(false));
        }
        catch
        {
            // Diagnostic logging must never hide the original AI error.
        }
    }

    private static string SafeModelName(string model) => Path.GetFileName(model).Replace('|', '_');
}
