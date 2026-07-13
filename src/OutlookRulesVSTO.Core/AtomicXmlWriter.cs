using System.Xml.Linq;

namespace OutlookRulesVSTO.Core;

public static class AtomicXmlWriter
{
    public static void Write(string path, XDocument document, Func<XDocument, ValidationResult> validate)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = $"{path}.tmp";
        var backupPath = $"{path}.{DateTime.Now:yyyyMMdd-HHmmss}.bak";

        document.Save(tempPath);
        var loadedTemp = XDocument.Load(tempPath);
        var validation = validate(loadedTemp);
        if (!validation.IsValid)
        {
            File.Delete(tempPath);
            var details = string.Join(Environment.NewLine, validation.Issues.Select(issue => $"{issue.Severity}: {issue.Message}"));
            throw new InvalidDataException($"XML validation failed before atomic replace.{Environment.NewLine}{details}");
        }

        if (File.Exists(path))
        {
            File.Copy(path, backupPath, overwrite: true);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}
