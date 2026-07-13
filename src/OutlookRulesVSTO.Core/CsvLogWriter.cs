using System.Globalization;
using System.Text;

namespace OutlookRulesVSTO.Core;

public sealed record ProcessingLogRow(
    DateTime Timestamp,
    string EventType,
    string RuleId,
    string RuleName,
    string MessageId,
    string SenderEmail,
    string Subject,
    string AttachmentName,
    string DestinationPath,
    string Status,
    string Message,
    string ErrorNumber = "",
    string ErrorDescription = "");

public sealed record ProcessedHistoryRow(
    DateTime Timestamp,
    string MessageId,
    string RuleId,
    string AttachmentName,
    string DestinationPath,
    string Status);

public static class CsvLogWriter
{
    public static readonly string[] ProcessingLogColumns =
    [
        "timestamp",
        "eventType",
        "ruleId",
        "ruleName",
        "messageId",
        "senderEmail",
        "subject",
        "attachmentName",
        "destinationPath",
        "status",
        "message",
        "errorNumber",
        "errorDescription"
    ];

    public static readonly string[] ProcessedHistoryColumns =
    [
        "timestamp",
        "messageId",
        "ruleId",
        "attachmentName",
        "destinationPath",
        "status"
    ];

    public static void AppendProcessingLog(string logsFolder, ProcessingLogRow row)
    {
        Directory.CreateDirectory(logsFolder);
        var path = Path.Combine(logsFolder, $"processing-{row.Timestamp:yyyyMMdd}.csv");
        AppendRow(path, ProcessingLogColumns, [
            row.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            row.EventType,
            row.RuleId,
            row.RuleName,
            row.MessageId,
            row.SenderEmail,
            row.Subject,
            row.AttachmentName,
            row.DestinationPath,
            row.Status,
            row.Message,
            row.ErrorNumber,
            row.ErrorDescription
        ]);
    }

    public static void AppendProcessedHistory(string path, ProcessedHistoryRow row)
    {
        AppendRow(path, ProcessedHistoryColumns, [
            row.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            row.MessageId,
            row.RuleId,
            row.AttachmentName,
            row.DestinationPath,
            row.Status
        ]);
    }

    public static string ToCsvLine(IEnumerable<string> cells) =>
        string.Join(',', cells.Select(Escape));

    public static List<string[]> ReadCsv(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        return File.ReadAllLines(path).Select(ParseCsvLine).ToList();
    }

    private static void AppendRow(string path, IReadOnlyList<string> header, IReadOnlyList<string> cells)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var writeHeader = !File.Exists(path) || new FileInfo(path).Length == 0;
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        if (writeHeader)
        {
            writer.WriteLine(ToCsvLine(header));
        }

        writer.WriteLine(ToCsvLine(cells));
    }

    private static string Escape(string value)
    {
        value ??= string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static string[] ParseCsvLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    builder.Append(ch);
                }
            }
            else if (ch == ',')
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else
            {
                builder.Append(ch);
            }
        }

        values.Add(builder.ToString());
        return values.ToArray();
    }
}
