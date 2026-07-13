using System.Globalization;

namespace OutlookRulesVSTO.Core;

public static class Schema
{
    public const string CurrentSchemaVersion = "1.0";
}

public enum RuleMatchMode
{
    AllConditions,
    AnyCondition
}

public enum ConditionTarget
{
    SenderEmail,
    SenderName,
    To,
    Cc,
    Subject,
    BodyText,
    BodyHtml,
    ReceivedDate,
    HasAttachments,
    AttachmentName,
    AttachmentExtension,
    AttachmentCount,
    Account,
    Categories
}

public enum ConditionOperator
{
    Contains,
    DoesNotContain,
    Equals,
    NotEquals,
    StartsWith,
    EndsWith,
    RegexMatch,
    RegexNotMatch,
    IsEmpty,
    IsNotEmpty,
    GreaterThan,
    LessThan,
    Between
}

public enum ExtractionSource
{
    Subject,
    BodyText,
    BodyHtmlStripped,
    AttachmentName,
    Automatic
}

public enum ExtractionMethod
{
    RegexCapture,
    BetweenText,
    LineStartsWith,
    AfterLabelUntilLineEnd
}

public enum ExtractionTransform
{
    None,
    Uppercase,
    Lowercase,
    DateFormat,
    ReplaceSpaces
}

public enum DuplicatePolicy
{
    RenameCounter,
    RenameTimestamp,
    Skip,
    Overwrite,
    Fail,
    VersionFolder
}

public enum PostProcessAction
{
    None,
    MarkRead,
    AssignCategory,
    MoveEmail,
    FlagEmail
}

public enum AuditLevel
{
    ErrorsOnly,
    Summary,
    Verbose
}

public enum SavePlanStatus
{
    Planned,
    Skipped,
    Failed
}

public enum SaveMode
{
    Attachments,
    MessageHtml
}

public sealed class Rule
{
    public string RuleId { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public bool StopProcessingOnMatch { get; set; } = true;
    public List<string> WatchFolderIds { get; set; } = ["Inbox"];
    public RuleMatchMode MatchMode { get; set; } = RuleMatchMode.AllConditions;
    public List<Condition> Conditions { get; set; } = [];
    public List<Extraction> Extractions { get; set; } = [];
    public List<AttachmentFilter> AttachmentFilters { get; set; } = [];
    public SaveMode SaveMode { get; set; } = SaveMode.Attachments;
    public Destination Destination { get; set; } = new();
    public DuplicatePolicy DuplicatePolicy { get; set; } = DuplicatePolicy.RenameCounter;
    public PostProcessing PostProcessing { get; set; } = new();
    public AuditLevel AuditLevel { get; set; } = AuditLevel.Summary;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string SchemaVersion { get; set; } = Schema.CurrentSchemaVersion;
}

public sealed class Condition
{
    public string ConditionId { get; set; } = Guid.NewGuid().ToString("D");
    public ConditionTarget Target { get; set; }
    public ConditionOperator Operator { get; set; } = ConditionOperator.Contains;
    public string Value { get; set; } = string.Empty;
    public string Value2 { get; set; } = string.Empty;
    public bool CaseSensitive { get; set; }
    public bool NormalizeWhitespace { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

public sealed class Extraction
{
    public string ExtractionId { get; set; } = Guid.NewGuid().ToString("D");
    public string Name { get; set; } = string.Empty;
    public ExtractionSource Source { get; set; } = ExtractionSource.BodyText;
    public ExtractionMethod Method { get; set; } = ExtractionMethod.AfterLabelUntilLineEnd;
    public string Pattern { get; set; } = string.Empty;
    public string Pattern2 { get; set; } = string.Empty;
    public string CaptureGroup { get; set; } = "1";
    public bool Required { get; set; } = true;
    public string DefaultValue { get; set; } = string.Empty;
    public bool SanitizeForPath { get; set; } = true;
    public bool TrimResult { get; set; } = true;
    public ExtractionTransform Transform { get; set; } = ExtractionTransform.None;
    public string TransformFormat { get; set; } = string.Empty;
}

public sealed class AttachmentFilter
{
    public string FilterId { get; set; } = Guid.NewGuid().ToString("D");
    public string NamePattern { get; set; } = string.Empty;
    public ConditionOperator NamePatternOperator { get; set; } = ConditionOperator.Contains;
    public bool NamePatternIsRegex { get; set; }
    public List<string> Extensions { get; set; } = [];
    public long? MinSizeKb { get; set; }
    public long? MaxSizeKb { get; set; }
    public bool ExcludeInline { get; set; } = true;
    public int? RequiredCount { get; set; }
}

public sealed class Destination
{
    public string Root { get; set; } = string.Empty;
    public string FolderTemplate { get; set; } = string.Empty;
    public string FileNameTemplate { get; set; } = string.Empty;
    public string ReviewRoot { get; set; } = string.Empty;
    public string ReviewFolderTemplate { get; set; } = "_NeedsReview";
    public bool SaveFailedExtractionsToReviewFolder { get; set; } = true;
}

public sealed class PostProcessing
{
    public PostProcessAction Action { get; set; } = PostProcessAction.None;
    public string Category { get; set; } = "Saved by Attachment Rule Manager";
    public string MoveFolderId { get; set; } = string.Empty;
    public bool OnlyOnSuccess { get; set; } = true;
}

public sealed class RuleConfiguration
{
    public string SchemaVersion { get; set; } = Schema.CurrentSchemaVersion;
    public long ConfigVersion { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public List<Rule> Rules { get; set; } = [];
}

public sealed class Settings
{
    public string SchemaVersion { get; set; } = Schema.CurrentSchemaVersion;
    public string Language { get; set; } = "en";
    public string StorageRoot { get; set; } = AppPaths.DefaultStorageRoot;
    public string RulesPath { get; set; } = AppPaths.DefaultRulesPath;
    public string LogsPath { get; set; } = AppPaths.DefaultLogsPath;
    public string ProcessedHistoryPath { get; set; } = AppPaths.DefaultHistoryPath;
    public string DefaultDestinationRoot { get; set; } = string.Empty;
    public List<string> AllowedDestinationRoots { get; set; } = [];
    public bool RestrictDestinationsToAllowedRoots { get; set; }
    public bool SaveFailedExtractionsToReviewFolder { get; set; } = true;
    public DuplicatePolicy DefaultDuplicatePolicy { get; set; } = DuplicatePolicy.RenameCounter;
    public int LogRetentionDays { get; set; } = 90;
    public bool StartupCatchUpEnabled { get; set; } = true;
    public int StartupCatchUpLookbackDays { get; set; } = 7;
    public int MaximumAttachmentCount { get; set; } = 25;
    public long MaximumAttachmentSizeKb { get; set; } = 51200;
    public bool DebugLogging { get; set; }
    public bool RuleEditingEnabled { get; set; } = true;
    public bool AiHelperEnabled { get; set; }
    public string AiModel { get; set; } = "Qwen_Qwen3.5-0.8B-Q4_K_M.gguf";
    public bool AiIncludeBody { get; set; } = true;
    public string AiExtractionName { get; set; } = "CaseNumber";
    public string AiExtractionPattern { get; set; } = @"(?:CaseNumber|Case number|Reference)\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9._/-]{2,})";
    public bool AiExtractionRequired { get; set; } = true;
    public string AiExtractionInstructions { get; set; } = "Choose the source in which the case number or reference appears most clearly.";
    public string AiSystemPrompt { get; set; } = "Create a rule for a recurring stream of similar emails from one example email. Use only stable characteristics, base every choice on the example data, and do not invent information.";
    public string AiMetadataPrompt { get; set; } = "Provide a short general rule name and description. State the specific purpose and omit variable names, numbers, dates, locations, and references.";
    public string AiSenderPrompt { get; set; } = "Use a fixed functional sender address as a condition, but not a personal address that may send multiple types of email.";
    public string AiSubjectPrompt { get; set; } = "Choose the shortest literal text that identifies the recurring type or purpose and omit variable data.";
    public string AiAttachmentPrompt { get; set; } = "Use only a stable part of a filename or a distinctive extension; omit variable data from the recognition value.";
    public string AiFolderPrompt { get; set; } = "Provide a short English folder name for the specific email type, without variable data or path characters.";
}

public static class AppPaths
{
    public static string DefaultStorageRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OutlookRulesVSTO");

    public static string DefaultRulesPath => Path.Combine(DefaultStorageRoot, "rules.xml");
    public static string DefaultSettingsPath => Path.Combine(DefaultStorageRoot, "settings.xml");
    public static string DefaultLogsPath => Path.Combine(DefaultStorageRoot, "logs");
    public static string DefaultHistoryPath => Path.Combine(DefaultStorageRoot, "processed-history.csv");

    public static void EnsureStorageFolders(Settings settings)
    {
        Directory.CreateDirectory(settings.StorageRoot);
        Directory.CreateDirectory(settings.LogsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(settings.RulesPath) ?? settings.StorageRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(settings.ProcessedHistoryPath) ?? settings.StorageRoot);
    }
}

public sealed class MessageContext
{
    public string MessageId { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;
    public string StoreId { get; set; } = string.Empty;
    public string ConversationId { get; set; } = string.Empty;
    public string SenderEmail { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public List<string> To { get; set; } = [];
    public List<string> Cc { get; set; } = [];
    public string Subject { get; set; } = string.Empty;
    public DateTime ReceivedTime { get; set; } = DateTime.Now;
    public string BodyText { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
    public List<AttachmentContext> Attachments { get; set; } = [];
}

public sealed class AttachmentContext
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public long SizeKb { get; set; }
    public string FixturePath { get; set; } = string.Empty;
    public bool IsInline { get; set; }

    public string BaseName => Path.GetFileNameWithoutExtension(Name);

    public void Normalize()
    {
        Extension = PathSanitizer.NormalizeExtension(string.IsNullOrWhiteSpace(Extension)
            ? Path.GetExtension(Name)
            : Extension);
    }
}

public sealed class ExtractionResult
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Errors { get; } = [];
    public bool Success => Errors.Count == 0;
}

public sealed class RuleEvaluationResult
{
    public Rule Rule { get; init; } = new();
    public bool IsMatch { get; set; }
    public ExtractionResult Extractions { get; set; } = new();
    public List<AttachmentContext> MatchingAttachments { get; set; } = [];
    public List<SavePlan> SavePlans { get; set; } = [];
    public List<string> Messages { get; set; } = [];
}

public sealed class SavePlan
{
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string AttachmentName { get; set; } = string.Empty;
    public int AttachmentIndex { get; set; }
    public SaveMode SaveMode { get; set; } = SaveMode.Attachments;
    public string DestinationFolder { get; set; } = string.Empty;
    public string DestinationPath { get; set; } = string.Empty;
    public SavePlanStatus Status { get; set; } = SavePlanStatus.Planned;
    public string Message { get; set; } = string.Empty;
    public bool IsDryRun { get; set; } = true;
}

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];
    public bool IsValid => Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public void Error(string message, string path = "") => Issues.Add(new ValidationIssue(ValidationSeverity.Error, message, path));
    public void Warning(string message, string path = "") => Issues.Add(new ValidationIssue(ValidationSeverity.Warning, message, path));
    public void Info(string message, string path = "") => Issues.Add(new ValidationIssue(ValidationSeverity.Info, message, path));
}

public sealed record ValidationIssue(ValidationSeverity Severity, string Message, string Path);

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public sealed record ConfigStamp(DateTime LastWriteTimeUtc, long ConfigVersion);

public static class Invariant
{
    public static string Date(DateTime value) => value.ToString("O", CultureInfo.InvariantCulture);

    public static DateTime ParseDate(string value, DateTime fallback) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : fallback;
}
