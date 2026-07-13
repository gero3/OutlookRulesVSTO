namespace OutlookRulesVSTO.Core;

public static class PathSanitizer
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string NormalizeExtension(string extension)
    {
        extension = extension.Trim().ToLowerInvariant();
        if (extension.Length == 0)
        {
            return string.Empty;
        }

        return extension.StartsWith('.') ? extension : $".{extension}";
    }

    public static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Concat([':', '\\', '/', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim().TrimEnd('.');
        sanitized = sanitized.Replace("..", "_", StringComparison.Ordinal);
        if (sanitized.Length == 0 || ReservedNames.Contains(sanitized))
        {
            return $"_{sanitized}".TrimEnd();
        }

        return sanitized;
    }

    public static string SanitizeRelativePath(string templateResult)
    {
        var parts = templateResult
            .Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part != "." && part != "..")
            .Select(SanitizeSegment);

        return Path.Combine(parts.ToArray());
    }

    public static string SanitizeFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        return $"{SanitizeSegment(baseName)}{NormalizeExtension(extension)}";
    }

    public static bool IsPathTraversal(string value)
    {
        var normalized = value.Replace('/', '\\');
        return normalized.Split('\\').Any(part => part == "..");
    }

    public static bool IsWithinRoot(string candidatePath, string root)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsWithinAnyRoot(string candidatePath, IEnumerable<string> roots) =>
        roots.Any(root => IsWithinRoot(candidatePath, root));
}
