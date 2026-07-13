namespace OutlookRulesVSTO.Core;

public static class DuplicatePlanner
{
    public static SavePlan Plan(string destinationFolder, string fileName, DuplicatePolicy policy, bool createDestinationFolder = true)
    {
        if (createDestinationFolder)
        {
            Directory.CreateDirectory(destinationFolder);
        }

        var sanitizedName = PathSanitizer.SanitizeFileName(fileName);
        var candidatePath = Path.Combine(destinationFolder, sanitizedName);
        if (!File.Exists(candidatePath))
        {
            return Planned(destinationFolder, candidatePath);
        }

        return policy switch
        {
            DuplicatePolicy.RenameCounter => Planned(destinationFolder, CounterPath(destinationFolder, sanitizedName)),
            DuplicatePolicy.RenameTimestamp => Planned(destinationFolder, TimestampPath(destinationFolder, sanitizedName)),
            DuplicatePolicy.Skip => Skipped(destinationFolder, candidatePath, "Duplicate exists; skip policy selected."),
            DuplicatePolicy.Overwrite => Planned(destinationFolder, candidatePath, "Duplicate exists; overwrite policy selected."),
            DuplicatePolicy.Fail => Failed(destinationFolder, candidatePath, "Duplicate exists; fail policy selected."),
            DuplicatePolicy.VersionFolder => Planned(Path.Combine(destinationFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss")), Path.Combine(destinationFolder, DateTime.Now.ToString("yyyyMMdd-HHmmss"), sanitizedName)),
            _ => Failed(destinationFolder, candidatePath, "Unsupported duplicate policy.")
        };
    }

    private static string CounterPath(string folder, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var counter = 2;
        string path;
        do
        {
            path = Path.Combine(folder, $"{baseName} ({counter}){extension}");
            counter++;
        }
        while (File.Exists(path));

        return path;
    }

    private static string TimestampPath(string folder, string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var path = Path.Combine(folder, $"{baseName}_{stamp}{extension}");
        var counter = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"{baseName}_{stamp}_{counter}{extension}");
            counter++;
        }

        return path;
    }

    private static SavePlan Planned(string folder, string path, string message = "Planned save.") => new()
    {
        DestinationFolder = folder,
        DestinationPath = path,
        Status = SavePlanStatus.Planned,
        Message = message
    };

    private static SavePlan Skipped(string folder, string path, string message) => new()
    {
        DestinationFolder = folder,
        DestinationPath = path,
        Status = SavePlanStatus.Skipped,
        Message = message
    };

    private static SavePlan Failed(string folder, string path, string message) => new()
    {
        DestinationFolder = folder,
        DestinationPath = path,
        Status = SavePlanStatus.Failed,
        Message = message
    };
}
