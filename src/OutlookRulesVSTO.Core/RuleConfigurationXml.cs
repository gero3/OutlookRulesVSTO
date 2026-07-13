using System.Xml;
using System.Xml.Linq;

namespace OutlookRulesVSTO.Core;

public static class RuleConfigurationXml
{
    public static RuleConfiguration LoadRules(string path)
    {
        try
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            return FromRulesDocument(document);
        }
        catch (XmlException)
        {
            BackupCorruptFile(path);
            throw;
        }
    }

    public static Settings LoadSettings(string path)
    {
        try
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            return FromSettingsDocument(document);
        }
        catch (XmlException)
        {
            BackupCorruptFile(path);
            throw;
        }
    }

    public static RuleConfiguration FromRulesDocument(XDocument document)
    {
        var root = document.Root ?? throw new InvalidDataException("rules.xml has no root element.");
        if (root.Name.LocalName != "Rules")
        {
            throw new InvalidDataException("rules.xml root element must be Rules.");
        }

        var config = new RuleConfiguration
        {
            SchemaVersion = (string?)root.Attribute("schemaVersion") ?? Schema.CurrentSchemaVersion,
            ConfigVersion = (long?)root.Attribute("configVersion") ?? 1
        };

        foreach (var ruleElement in root.Elements("Rule"))
        {
            config.Rules.Add(ReadRule(ruleElement));
        }

        return config;
    }

    public static Settings FromSettingsDocument(XDocument document)
    {
        var root = document.Root ?? throw new InvalidDataException("settings.xml has no root element.");
        if (root.Name.LocalName != "Settings")
        {
            throw new InvalidDataException("settings.xml root element must be Settings.");
        }

        var settings = new Settings
        {
            SchemaVersion = Attr(root, "schemaVersion", Schema.CurrentSchemaVersion),
            Language = Element(root, "Language", "en"),
            StorageRoot = PathElement(root, "StorageRoot", AppPaths.DefaultStorageRoot),
            RulesPath = PathElement(root, "RulesPath", AppPaths.DefaultRulesPath),
            LogsPath = PathElement(root, "LogsPath", AppPaths.DefaultLogsPath),
            ProcessedHistoryPath = PathElement(root, "ProcessedHistoryPath", AppPaths.DefaultHistoryPath),
            DefaultDestinationRoot = PathElement(root, "DefaultDestinationRoot"),
            RestrictDestinationsToAllowedRoots = BoolElement(root, "RestrictDestinationsToAllowedRoots", false),
            SaveFailedExtractionsToReviewFolder = BoolElement(root, "SaveFailedExtractionsToReviewFolder", true),
            DefaultDuplicatePolicy = EnumElement(root, "DefaultDuplicatePolicy", DuplicatePolicy.RenameCounter),
            LogRetentionDays = IntElement(root, "LogRetentionDays", 90),
            StartupCatchUpEnabled = BoolElement(root, "StartupCatchUpEnabled", true),
            StartupCatchUpLookbackDays = IntElement(root, "StartupCatchUpLookbackDays", 7),
            MaximumAttachmentCount = IntElement(root, "MaximumAttachmentCount", 25),
            MaximumAttachmentSizeKb = LongElement(root, "MaximumAttachmentSizeKb", 51200),
            DebugLogging = BoolElement(root, "DebugLogging", false),
            RuleEditingEnabled = BoolElement(root, "RuleEditingEnabled", true),
            AiHelperEnabled = BoolElement(root, "AiHelperEnabled", false),
            AiModel = Element(root, "AiModel", "Qwen_Qwen3.5-0.8B-Q4_K_M.gguf"),
            AiIncludeBody = BoolElement(root, "AiIncludeBody", true),
            AiExtractionName = Element(root, "AiExtractionName", "CaseNumber"),
            AiExtractionPattern = Element(root, "AiExtractionPattern", @"(?:CaseNumber|Case number|Reference)\s*[:#]?\s*([A-Za-z0-9][A-Za-z0-9._/-]{2,})"),
            AiExtractionRequired = BoolElement(root, "AiExtractionRequired", true),
            AiExtractionInstructions = Element(root, "AiExtractionInstructions", "Choose the source in which the case number or reference appears most clearly."),
            AiSystemPrompt = Element(root, "AiSystemPrompt", new Settings().AiSystemPrompt),
            AiMetadataPrompt = Element(root, "AiMetadataPrompt", new Settings().AiMetadataPrompt),
            AiSenderPrompt = Element(root, "AiSenderPrompt", new Settings().AiSenderPrompt),
            AiSubjectPrompt = Element(root, "AiSubjectPrompt", new Settings().AiSubjectPrompt),
            AiAttachmentPrompt = Element(root, "AiAttachmentPrompt", new Settings().AiAttachmentPrompt),
            AiFolderPrompt = Element(root, "AiFolderPrompt", new Settings().AiFolderPrompt)
        };

        settings.AllowedDestinationRoots = root.Element("AllowedDestinationRoots")?
            .Elements("Root")
            .Select(node => Environment.ExpandEnvironmentVariables(node.Value.Trim()))
            .Where(value => value.Length > 0)
            .ToList() ?? [];

        return settings;
    }

    public static XDocument ToRulesDocument(RuleConfiguration configuration)
    {
        var root = new XElement("Rules",
            new XAttribute("schemaVersion", configuration.SchemaVersion),
            new XAttribute("configVersion", configuration.ConfigVersion));

        foreach (var rule in configuration.Rules.OrderBy(rule => rule.Priority).ThenBy(rule => rule.Name, StringComparer.OrdinalIgnoreCase))
        {
            root.Add(WriteRule(rule));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    public static XDocument ToSettingsDocument(Settings settings)
    {
        var root = new XElement("Settings",
            new XAttribute("schemaVersion", settings.SchemaVersion),
            new XElement("Language", settings.Language),
            new XElement("StorageRoot", settings.StorageRoot),
            new XElement("RulesPath", settings.RulesPath),
            new XElement("LogsPath", settings.LogsPath),
            new XElement("ProcessedHistoryPath", settings.ProcessedHistoryPath),
            new XElement("DefaultDestinationRoot", settings.DefaultDestinationRoot),
            new XElement("AllowedDestinationRoots", settings.AllowedDestinationRoots.Select(root => new XElement("Root", root))),
            new XElement("RestrictDestinationsToAllowedRoots", XmlBool(settings.RestrictDestinationsToAllowedRoots)),
            new XElement("SaveFailedExtractionsToReviewFolder", XmlBool(settings.SaveFailedExtractionsToReviewFolder)),
            new XElement("DefaultDuplicatePolicy", settings.DefaultDuplicatePolicy),
            new XElement("LogRetentionDays", settings.LogRetentionDays),
            new XElement("StartupCatchUpEnabled", XmlBool(settings.StartupCatchUpEnabled)),
            new XElement("StartupCatchUpLookbackDays", settings.StartupCatchUpLookbackDays),
            new XElement("MaximumAttachmentCount", settings.MaximumAttachmentCount),
            new XElement("MaximumAttachmentSizeKb", settings.MaximumAttachmentSizeKb),
            new XElement("DebugLogging", XmlBool(settings.DebugLogging)),
            new XElement("RuleEditingEnabled", XmlBool(settings.RuleEditingEnabled)),
            new XElement("AiHelperEnabled", XmlBool(settings.AiHelperEnabled)),
            new XElement("AiModel", settings.AiModel),
            new XElement("AiIncludeBody", XmlBool(settings.AiIncludeBody)),
            new XElement("AiExtractionName", settings.AiExtractionName),
            new XElement("AiExtractionPattern", settings.AiExtractionPattern),
            new XElement("AiExtractionRequired", XmlBool(settings.AiExtractionRequired)),
            new XElement("AiExtractionInstructions", settings.AiExtractionInstructions),
            new XElement("AiSystemPrompt", settings.AiSystemPrompt),
            new XElement("AiMetadataPrompt", settings.AiMetadataPrompt),
            new XElement("AiSenderPrompt", settings.AiSenderPrompt),
            new XElement("AiSubjectPrompt", settings.AiSubjectPrompt),
            new XElement("AiAttachmentPrompt", settings.AiAttachmentPrompt),
            new XElement("AiFolderPrompt", settings.AiFolderPrompt));

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    public static void SaveRulesAtomic(string path, RuleConfiguration configuration)
    {
        configuration.ConfigVersion = Math.Max(configuration.ConfigVersion + 1, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var document = ToRulesDocument(configuration);
        ValidateRulesDocument(document);
        AtomicXmlWriter.Write(path, document, ValidateRulesDocument);
    }

    public static void SaveSettingsAtomic(string path, Settings settings)
    {
        var document = ToSettingsDocument(settings);
        ValidateSettingsDocument(document);
        AtomicXmlWriter.Write(path, document, ValidateSettingsDocument);
    }

    public static ValidationResult ValidateRulesDocument(XDocument document)
    {
        var result = new ValidationResult();
        RuleConfiguration configuration;
        try
        {
            configuration = FromRulesDocument(document);
        }
        catch (Exception ex)
        {
            result.Error(ex.Message);
            return result;
        }

        return RuleValidator.Validate(configuration);
    }

    public static ValidationResult ValidateSettingsDocument(XDocument document)
    {
        var result = new ValidationResult();
        try
        {
            var settings = FromSettingsDocument(document);
            if (!System.Text.RegularExpressions.Regex.IsMatch(settings.Language, "^[a-z]{2,3}(?:-[A-Z]{2})?$"))
                result.Error("Language must be a valid locale code such as 'en', 'nl', or 'pt-BR'.", "Settings.Language");
            if (string.IsNullOrWhiteSpace(settings.StorageRoot))
            {
                result.Error("Storage root is required.", "Settings.StorageRoot");
            }

            foreach (var root in settings.AllowedDestinationRoots)
            {
                if (!Path.IsPathFullyQualified(root))
                {
                    result.Error($"Allowed destination root must be an absolute path: {root}", "Settings.AllowedDestinationRoots");
                }
            }

            if (settings.AiHelperEnabled && string.IsNullOrWhiteSpace(settings.AiModel))
                result.Error("AI model is required when the AI helper is enabled.", "Settings.AiModel");
            if (settings.AiHelperEnabled && string.IsNullOrWhiteSpace(settings.AiExtractionInstructions))
                result.Error("At least one AI extraction instruction is required.", "Settings.AiExtractionInstructions");
            if (settings.AiHelperEnabled && new[] { settings.AiSystemPrompt, settings.AiMetadataPrompt, settings.AiSenderPrompt, settings.AiSubjectPrompt, settings.AiAttachmentPrompt, settings.AiFolderPrompt }.Any(string.IsNullOrWhiteSpace))
                result.Error("All AI prompts are required when the AI helper is enabled.", "Settings.AiPrompts");
            if (settings.AiHelperEnabled && string.IsNullOrWhiteSpace(settings.AiExtractionName))
                result.Error("An AI extraction name is required.", "Settings.AiExtractionName");
            if (settings.AiHelperEnabled && string.IsNullOrWhiteSpace(settings.AiExtractionPattern))
                result.Error("An AI extraction pattern is required.", "Settings.AiExtractionPattern");
            if (settings.AiHelperEnabled && !string.IsNullOrWhiteSpace(settings.AiExtractionPattern))
            {
                try
                {
                    var regex = new System.Text.RegularExpressions.Regex(settings.AiExtractionPattern);
                    if (regex.GetGroupNumbers().Length < 2)
                        result.Error("The AI extraction pattern must contain a capture group.", "Settings.AiExtractionPattern");
                }
                catch (ArgumentException ex)
                {
                    result.Error($"Invalid AI extraction pattern: {ex.Message}", "Settings.AiExtractionPattern");
                }
            }
        }
        catch (Exception ex)
        {
            result.Error(ex.Message);
        }

        return result;
    }

    public static ConfigStamp ReadRulesStamp(string path)
    {
        if (!File.Exists(path))
        {
            return new ConfigStamp(DateTime.MinValue, 0);
        }

        var lastWrite = File.GetLastWriteTimeUtc(path);
        try
        {
            var document = XDocument.Load(path);
            var version = (long?)document.Root?.Attribute("configVersion") ?? 0;
            return new ConfigStamp(lastWrite, version);
        }
        catch
        {
            return new ConfigStamp(lastWrite, 0);
        }
    }

    private static Rule ReadRule(XElement element)
    {
        var rule = new Rule
        {
            RuleId = Attr(element, "id", Guid.NewGuid().ToString("D")),
            Name = Attr(element, "name"),
            Description = Element(element, "Description"),
            Enabled = BoolAttr(element, "enabled", true),
            Priority = IntAttr(element, "priority", 100),
            StopProcessingOnMatch = BoolAttr(element, "stopProcessingOnMatch", true),
            MatchMode = EnumAttr(element, "matchMode", RuleMatchMode.AllConditions),
            DuplicatePolicy = EnumAttr(element, "duplicatePolicy", DuplicatePolicy.RenameCounter),
            AuditLevel = EnumAttr(element, "auditLevel", AuditLevel.Summary),
            SaveMode = EnumAttr(element, "saveMode", SaveMode.Attachments),
            CreatedAt = Invariant.ParseDate(Attr(element, "createdAt"), DateTime.Now),
            UpdatedAt = Invariant.ParseDate(Attr(element, "updatedAt"), DateTime.Now),
            SchemaVersion = Attr(element, "schemaVersion", Schema.CurrentSchemaVersion)
        };

        rule.WatchFolderIds = element.Element("WatchFolders")?.Elements("Folder")
            .Select(node => Attr(node, "id", node.Value))
            .Where(value => value.Length > 0)
            .ToList() ?? ["Inbox"];

        rule.Conditions = element.Element("Conditions")?.Elements("Condition").Select(ReadCondition).ToList() ?? [];
        rule.Extractions = element.Element("Extractions")?.Elements("Extraction").Select(ReadExtraction).ToList() ?? [];
        rule.AttachmentFilters = element.Element("AttachmentFilters")?.Elements("AttachmentFilter").Select(ReadAttachmentFilter).ToList() ?? [];

        var destination = element.Element("Destination");
        if (destination is not null)
        {
            rule.Destination = new Destination
            {
                Root = Element(destination, "Root"),
                FolderTemplate = Element(destination, "FolderTemplate"),
                FileNameTemplate = Element(destination, "FileNameTemplate"),
                ReviewRoot = Element(destination, "ReviewRoot"),
                ReviewFolderTemplate = Element(destination, "ReviewFolderTemplate", "_NeedsReview"),
                SaveFailedExtractionsToReviewFolder = BoolElement(destination, "SaveFailedExtractionsToReviewFolder", true)
            };
        }

        var post = element.Element("PostProcessing");
        if (post is not null)
        {
            rule.PostProcessing = new PostProcessing
            {
                Action = EnumAttr(post, "action", PostProcessAction.None),
                Category = Attr(post, "category", "Saved by Attachment Rule Manager"),
                MoveFolderId = Attr(post, "moveFolderId"),
                OnlyOnSuccess = BoolAttr(post, "onlyOnSuccess", true)
            };
        }

        return rule;
    }

    private static Condition ReadCondition(XElement element) => new()
    {
        ConditionId = Attr(element, "id", Guid.NewGuid().ToString("D")),
        Target = EnumAttr(element, "target", ConditionTarget.Subject),
        Operator = EnumAttr(element, "operator", ConditionOperator.Contains),
        Value = Element(element, "Value"),
        Value2 = Element(element, "Value2"),
        CaseSensitive = BoolAttr(element, "caseSensitive", false),
        NormalizeWhitespace = BoolAttr(element, "normalizeWhitespace", true),
        Enabled = BoolAttr(element, "enabled", true)
    };

    private static Extraction ReadExtraction(XElement element) => new()
    {
        ExtractionId = Attr(element, "id", Guid.NewGuid().ToString("D")),
        Name = Attr(element, "name"),
        Source = EnumAttr(element, "source", ExtractionSource.BodyText),
        Method = EnumAttr(element, "method", ExtractionMethod.AfterLabelUntilLineEnd),
        Pattern = Element(element, "Pattern"),
        Pattern2 = Element(element, "Pattern2"),
        CaptureGroup = Attr(element, "captureGroup", "1"),
        Required = BoolAttr(element, "required", true),
        DefaultValue = Element(element, "DefaultValue"),
        SanitizeForPath = BoolAttr(element, "sanitizeForPath", true),
        TrimResult = BoolAttr(element, "trimResult", true),
        Transform = EnumAttr(element, "transform", ExtractionTransform.None),
        TransformFormat = Attr(element, "transformFormat")
    };

    private static AttachmentFilter ReadAttachmentFilter(XElement element) => new()
    {
        FilterId = Attr(element, "id", Guid.NewGuid().ToString("D")),
        NamePattern = Element(element, "NamePattern"),
        NamePatternOperator = EnumAttr(element, "namePatternOperator", ConditionOperator.Contains),
        NamePatternIsRegex = BoolAttr(element, "namePatternIsRegex", false),
        Extensions = element.Element("Extensions")?.Elements("Extension")
            .Select(node => PathSanitizer.NormalizeExtension(node.Value))
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [],
        MinSizeKb = NullableLongAttr(element, "minSizeKb"),
        MaxSizeKb = NullableLongAttr(element, "maxSizeKb"),
        ExcludeInline = BoolAttr(element, "excludeInline", true),
        RequiredCount = NullableIntAttr(element, "requiredCount")
    };

    private static XElement WriteRule(Rule rule) =>
        new("Rule",
            new XAttribute("id", rule.RuleId),
            new XAttribute("name", rule.Name),
            new XAttribute("enabled", XmlBool(rule.Enabled)),
            new XAttribute("priority", rule.Priority),
            new XAttribute("stopProcessingOnMatch", XmlBool(rule.StopProcessingOnMatch)),
            new XAttribute("matchMode", rule.MatchMode),
            new XAttribute("duplicatePolicy", rule.DuplicatePolicy),
            new XAttribute("auditLevel", rule.AuditLevel),
            new XAttribute("saveMode", rule.SaveMode),
            new XAttribute("createdAt", Invariant.Date(rule.CreatedAt)),
            new XAttribute("updatedAt", Invariant.Date(rule.UpdatedAt)),
            new XAttribute("schemaVersion", rule.SchemaVersion),
            new XElement("Description", rule.Description),
            new XElement("WatchFolders", rule.WatchFolderIds.Select(id => new XElement("Folder", new XAttribute("id", id)))),
            new XElement("Conditions", rule.Conditions.Select(WriteCondition)),
            new XElement("Extractions", rule.Extractions.Select(WriteExtraction)),
            new XElement("AttachmentFilters", rule.AttachmentFilters.Select(WriteAttachmentFilter)),
            new XElement("Destination",
                new XElement("Root", rule.Destination.Root),
                new XElement("FolderTemplate", rule.Destination.FolderTemplate),
                new XElement("FileNameTemplate", rule.Destination.FileNameTemplate),
                new XElement("ReviewRoot", rule.Destination.ReviewRoot),
                new XElement("ReviewFolderTemplate", rule.Destination.ReviewFolderTemplate),
                new XElement("SaveFailedExtractionsToReviewFolder", XmlBool(rule.Destination.SaveFailedExtractionsToReviewFolder))),
            new XElement("PostProcessing",
                new XAttribute("action", rule.PostProcessing.Action),
                new XAttribute("category", rule.PostProcessing.Category),
                new XAttribute("moveFolderId", rule.PostProcessing.MoveFolderId),
                new XAttribute("onlyOnSuccess", XmlBool(rule.PostProcessing.OnlyOnSuccess))));

    private static XElement WriteCondition(Condition condition) =>
        new("Condition",
            new XAttribute("id", condition.ConditionId),
            new XAttribute("target", condition.Target),
            new XAttribute("operator", condition.Operator),
            new XAttribute("caseSensitive", XmlBool(condition.CaseSensitive)),
            new XAttribute("normalizeWhitespace", XmlBool(condition.NormalizeWhitespace)),
            new XAttribute("enabled", XmlBool(condition.Enabled)),
            new XElement("Value", condition.Value),
            new XElement("Value2", condition.Value2));

    private static XElement WriteExtraction(Extraction extraction) =>
        new("Extraction",
            new XAttribute("id", extraction.ExtractionId),
            new XAttribute("name", extraction.Name),
            new XAttribute("source", extraction.Source),
            new XAttribute("method", extraction.Method),
            new XAttribute("captureGroup", extraction.CaptureGroup),
            new XAttribute("required", XmlBool(extraction.Required)),
            new XAttribute("sanitizeForPath", XmlBool(extraction.SanitizeForPath)),
            new XAttribute("trimResult", XmlBool(extraction.TrimResult)),
            new XAttribute("transform", extraction.Transform),
            new XAttribute("transformFormat", extraction.TransformFormat),
            new XElement("Pattern", extraction.Pattern),
            new XElement("Pattern2", extraction.Pattern2),
            new XElement("DefaultValue", extraction.DefaultValue));

    private static XElement WriteAttachmentFilter(AttachmentFilter filter) =>
        new("AttachmentFilter",
            new XAttribute("id", filter.FilterId),
            new XAttribute("namePatternOperator", filter.NamePatternOperator),
            new XAttribute("namePatternIsRegex", XmlBool(filter.NamePatternIsRegex)),
            new XAttribute("excludeInline", XmlBool(filter.ExcludeInline)),
            OptionalAttribute("minSizeKb", filter.MinSizeKb),
            OptionalAttribute("maxSizeKb", filter.MaxSizeKb),
            OptionalAttribute("requiredCount", filter.RequiredCount),
            new XElement("NamePattern", filter.NamePattern),
            new XElement("Extensions", filter.Extensions.Select(ext => new XElement("Extension", PathSanitizer.NormalizeExtension(ext)))));

    private static object[] OptionalAttribute(string name, long? value) =>
        value.HasValue ? [new XAttribute(name, value.Value)] : [];

    private static object[] OptionalAttribute(string name, int? value) =>
        value.HasValue ? [new XAttribute(name, value.Value)] : [];

    private static string Attr(XElement element, string name, string fallback = "") =>
        ((string?)element.Attribute(name) ?? fallback).Trim();

    private static string Element(XElement element, string name, string fallback = "") =>
        ((string?)element.Element(name) ?? fallback).Trim();

    private static string PathElement(XElement element, string name, string fallback = "") =>
        Environment.ExpandEnvironmentVariables(Element(element, name, fallback));

    private static bool BoolAttr(XElement element, string name, bool fallback) =>
        bool.TryParse(Attr(element, name), out var value) ? value : fallback;

    private static bool BoolElement(XElement element, string name, bool fallback) =>
        bool.TryParse(Element(element, name), out var value) ? value : fallback;

    private static int IntAttr(XElement element, string name, int fallback) =>
        int.TryParse(Attr(element, name), out var value) ? value : fallback;

    private static int IntElement(XElement element, string name, int fallback) =>
        int.TryParse(Element(element, name), out var value) ? value : fallback;

    private static long LongElement(XElement element, string name, long fallback) =>
        long.TryParse(Element(element, name), out var value) ? value : fallback;

    private static int? NullableIntAttr(XElement element, string name) =>
        int.TryParse(Attr(element, name), out var value) ? value : null;

    private static long? NullableLongAttr(XElement element, string name) =>
        long.TryParse(Attr(element, name), out var value) ? value : null;

    private static T EnumAttr<T>(XElement element, string name, T fallback) where T : struct =>
        Enum.TryParse<T>(Attr(element, name), ignoreCase: true, out var value) ? value : fallback;

    private static T EnumElement<T>(XElement element, string name, T fallback) where T : struct =>
        Enum.TryParse<T>(Element(element, name), ignoreCase: true, out var value) ? value : fallback;

    private static string XmlBool(bool value) => value ? "true" : "false";

    private static void BackupCorruptFile(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var backupPath = $"{path}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        File.Copy(path, backupPath, overwrite: true);
    }
}
