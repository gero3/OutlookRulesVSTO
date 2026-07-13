using System.Xml;
using System.Xml.Linq;
using OutlookRulesVSTO.Core;

namespace OutlookRulesVSTO.Tests;

[TestClass]
public sealed class RuleEngineTests
{
    [TestMethod]
    public void AutomaticHoldingNumberExtractionFindsAttachmentOnlyValue()
    {
        var extraction = AutomaticHoldingNumberExtraction();
        var message = new MessageContext
        {
            Subject = "Longletselsrapport",
            BodyText = "Het rapport staat in bijlage.",
            Attachments = [new AttachmentContext { Name = "Longletsels_BE87654321-0202_20260710.pdf" }]
        };

        var result = ExtractionEngine.ExtractAll(new Rule { Extractions = [extraction] }, message);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("BE87654321-0202", result.Values["Beslagnummer"]);
    }

    [TestMethod]
    public void AutomaticHoldingNumberExtractionDeduplicatesSameValueAcrossSources()
    {
        var extraction = AutomaticHoldingNumberExtraction();
        var message = new MessageContext
        {
            Subject = "Rapport BE12345678-0101",
            BodyText = "Beslagnummer BE12345678-0101",
            BodyHtml = "<p>BE12345678-0101</p>",
            Attachments = [new AttachmentContext { Name = "Rapport_BE12345678-0101.pdf" }]
        };

        var result = ExtractionEngine.ExtractAll(new Rule { Extractions = [extraction] }, message);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("BE12345678-0101", result.Values["Beslagnummer"]);
    }

    [TestMethod]
    public void AutomaticHoldingNumberExtractionRejectsConflictingValues()
    {
        var extraction = AutomaticHoldingNumberExtraction();
        var message = new MessageContext
        {
            Subject = "Rapport BE12345678-0101",
            BodyText = "Oude locatie BE87654321-0202",
            Attachments = [new AttachmentContext { Name = "Rapport_BE12345678-0101.pdf" }]
        };

        var result = ExtractionEngine.ExtractAll(new Rule { Extractions = [extraction] }, message);

        Assert.IsFalse(result.Success);
        Assert.IsFalse(result.Values.ContainsKey("Beslagnummer"));
        StringAssert.Contains(result.Errors.Single(), "multiple different values");
    }

    [TestMethod]
    public void AutomaticHoldingNumberExtractionReportsMissingValue()
    {
        var result = ExtractionEngine.ExtractAll(new Rule { Extractions = [AutomaticHoldingNumberExtraction()] }, new MessageContext
        {
            Subject = "Rapport zonder nummer",
            Attachments = [new AttachmentContext { Name = "rapport.pdf" }]
        });

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Errors.Single(), "was not found");
    }

    [TestMethod]
    public void AttachmentNameCanBeSelectedAsExplicitExtractionSource()
    {
        var extraction = AutomaticHoldingNumberExtraction();
        extraction.Source = ExtractionSource.AttachmentName;
        var message = new MessageContext
        {
            Subject = "BE11111111-1111 is ignored for this explicit source",
            Attachments = [new AttachmentContext { Name = "Rapport_BE22222222-2222.pdf" }]
        };

        Assert.AreEqual("BE22222222-2222", ExtractionEngine.Extract(extraction, message));
    }

    [TestMethod]
    public void ConflictingAutomaticHoldingNumbersRouteAttachmentToReview()
    {
        var root = CreateTempDirectory();
        var rule = new Rule
        {
            Name = "Rapporten", SaveMode = SaveMode.Attachments,
            Conditions = [new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Contains, Value = "Rapport" }],
            Extractions = [AutomaticHoldingNumberExtraction()],
            Destination = new Destination
            {
                Root = root, FolderTemplate = @"{Beslagnummer}\Rapporten", FileNameTemplate = "{OriginalFileName}",
                ReviewRoot = root, ReviewFolderTemplate = "_NeedsReview", SaveFailedExtractionsToReviewFolder = true
            }
        };
        var message = new MessageContext
        {
            Subject = "Rapport BE12345678-0101", BodyText = "Vorige locatie BE87654321-0202",
            Attachments = [new AttachmentContext { Name = "rapport.pdf" }]
        };

        var result = new RuleEngine(new Settings()).EvaluateRule(rule, message, dryRun: true);

        Assert.IsTrue(result.IsMatch);
        Assert.IsFalse(result.Extractions.Success);
        Assert.HasCount(1, result.SavePlans);
        StringAssert.Contains(result.SavePlans[0].DestinationPath, "_NeedsReview");
        StringAssert.Contains(result.SavePlans[0].Message, "multiple different values");
    }

    [TestMethod]
    public void NewExtractionSourcesRoundTripThroughXml()
    {
        var path = Path.Combine(CreateTempDirectory(), "rules.xml");
        var configuration = new RuleConfiguration
        {
            Rules =
            [
                new Rule
                {
                    Name = "Automatic",
                    Conditions = [new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Contains, Value = "Rapport" }],
                    Extractions =
                    [
                        AutomaticHoldingNumberExtraction(),
                        new Extraction { Name = "Bijlagecode", Source = ExtractionSource.AttachmentName, Method = ExtractionMethod.RegexCapture, Pattern = "(code)", CaptureGroup = "1" }
                    ],
                    Destination = new Destination { Root = Path.GetDirectoryName(path)! }
                }
            ]
        };

        RuleConfigurationXml.SaveRulesAtomic(path, configuration);
        var restored = RuleConfigurationXml.LoadRules(path);

        Assert.AreEqual(ExtractionSource.Automatic, restored.Rules[0].Extractions[0].Source);
        Assert.AreEqual(ExtractionSource.AttachmentName, restored.Rules[0].Extractions[1].Source);
    }

    private static Extraction AutomaticHoldingNumberExtraction() => new()
    {
        Name = "Beslagnummer", Source = ExtractionSource.Automatic, Method = ExtractionMethod.RegexCapture,
        Pattern = @"(BE\d{8}-\d{4})", CaptureGroup = "1", Required = true, TrimResult = true, SanitizeForPath = true
    };
    [TestMethod]
    public void SampleIncludesExampleServicePdfAndDuurzaamheidsmonitorHtmlRules()
    {
        var configuration = RuleConfigurationXml.LoadRules(SamplePath("sample-config", "rules.xml"));
        var serviceRule = configuration.Rules.Single(rule => rule.Name == "Example Service bioveiligheidsaudit");
        Assert.AreEqual(SaveMode.Attachments, serviceRule.SaveMode);
        CollectionAssert.AreEqual(new[] { ".pdf" }, serviceRule.AttachmentFilters.Single().Extensions);

        var monitor = configuration.Rules.Single(rule => rule.Name == "Duurzaamheidsmonitor als HTML");
        var message = new MessageContext
        {
            SenderEmail = "forms@service.example",
            Subject = "Bedankt dat je dit formulier hebt ingevuld: Duurzaamheidsmonitor",
            BodyText = "Beslagnummer *\r\nBE40127478-0201",
            ReceivedTime = new DateTime(2026, 7, 7)
        };
        var result = new RuleEngine(new Settings()).EvaluateRule(monitor, message);
        Assert.IsTrue(result.IsMatch);
        Assert.AreEqual(SaveMode.MessageHtml, result.SavePlans.Single().SaveMode);
        StringAssert.EndsWith(result.SavePlans.Single().DestinationPath, Path.Combine("BE40127478-0201", "Duurzaamheidsmonitor", "Duurzaamheidsmonitor_20260707.html"));

        message.BodyText = string.Empty;
        message.BodyHtml = "<html><body><div>Beslagnummer <span>*</span></div><div>BE40127478-0201</div><div>Naam bedrijf</div></body></html>";
        var htmlResult = new RuleEngine(new Settings()).EvaluateRule(monitor, message);
        Assert.IsTrue(htmlResult.IsMatch);
        Assert.AreEqual("BE40127478-0201", htmlResult.Extractions.Values["Beslagnummer"]);
        StringAssert.EndsWith(htmlResult.SavePlans.Single().DestinationPath, Path.Combine("BE40127478-0201", "Duurzaamheidsmonitor", "Duurzaamheidsmonitor_20260707.html"));
    }

    [TestMethod]
    public void LoadsSampleRulesXml()
    {
        var configuration = RuleConfigurationXml.LoadRules(SamplePath("sample-config", "rules.xml"));

        Assert.AreEqual("1.0", configuration.SchemaVersion);
        Assert.AreEqual(3, configuration.Rules.Count);
        Assert.AreEqual("Laboresultaten", configuration.Rules[0].Name);
        Assert.AreEqual(SaveMode.Attachments, configuration.Rules[0].SaveMode);
        Assert.AreEqual(SaveMode.Attachments, configuration.Rules[1].SaveMode);
        Assert.AreEqual(SaveMode.MessageHtml, configuration.Rules[2].SaveMode);
        Assert.AreEqual(DuplicatePolicy.RenameCounter, configuration.Rules[0].DuplicatePolicy);
    }

    [TestMethod]
    public void MalformedXmlIsRejectedAndBackedUp()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "rules.xml");
        File.WriteAllText(path, "<Rules><Rule></Rules>");

        try
        {
            RuleConfigurationXml.LoadRules(path);
            Assert.Fail("Expected malformed XML to throw.");
        }
        catch (XmlException)
        {
        }
        Assert.IsTrue(Directory.GetFiles(directory, "rules.xml.corrupt-*.bak").Length == 1);
    }

    [TestMethod]
    public void ValidationReportsUnknownTemplateToken()
    {
        var configuration = SampleData.CreateDefaultRules();
        configuration.Rules[0].Destination.FolderTemplate = @"{DoesNotExist}\Files";

        var validation = RuleValidator.Validate(configuration);

        Assert.IsFalse(validation.IsValid);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Message.Contains("DoesNotExist")));
    }

    [TestMethod]
    public void SubjectStartsWithMatchingIsCaseInsensitive()
    {
        var condition = new Condition
        {
            Target = ConditionTarget.Subject,
            Operator = ConditionOperator.StartsWith,
            Value = "beproevingsrapport"
        };

        var message = new MessageContext { Subject = "Beproevingsrapport (volledig resultaat) 123" };

        Assert.IsTrue(ConditionEvaluator.Evaluate(condition, message));
    }

    [TestMethod]
    public void RuleSupportsMultipleConditionsOnSameField()
    {
        var rule = new Rule
        {
            Conditions =
            [
                new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Contains, Value = "factuur" },
                new Condition { Target = ConditionTarget.Subject, Operator = ConditionOperator.Contains, Value = "juli" }
            ]
        };
        var matchingMessage = new MessageContext { Subject = "Factuur voor juli 2026" };
        var nonMatchingMessage = new MessageContext { Subject = "Factuur voor augustus 2026" };

        Assert.IsTrue(ConditionEvaluator.RuleMatches(rule, matchingMessage));
        Assert.IsFalse(ConditionEvaluator.RuleMatches(rule, nonMatchingMessage));
    }

    [TestMethod]
    public void BodyLabelExtractionReturnsSanitizedValue()
    {
        var extraction = new Extraction { Name = "Beslagnummer", Method = ExtractionMethod.AfterLabelUntilLineEnd, Pattern = "Beslagnummer:" };
        var rule = new Rule { Extractions = [extraction] };
        var message = new MessageContext { BodyText = "Beslagnummer: 12/34:56\r\nResultaat: OK" };

        var result = ExtractionEngine.ExtractAll(rule, message);

        Assert.IsTrue(result.Success);
        Assert.AreEqual("12_34_56", result.Values["Beslagnummer"]);
    }

    [TestMethod]
    public void OptionalExtractionUsesDefaultValue()
    {
        var rule = new Rule
        {
            Extractions =
            [
                new Extraction
                {
                    Name = "Customer",
                    Pattern = "Customer:",
                    Required = false,
                    DefaultValue = "Unknown"
                }
            ]
        };

        var result = ExtractionEngine.ExtractAll(rule, new MessageContext { BodyText = "No label here" });

        Assert.IsTrue(result.Success);
        Assert.AreEqual("Unknown", result.Values["Customer"]);
    }

    [TestMethod]
    public void RequiredExtractionFailureIsReported()
    {
        var rule = SampleData.CreateDefaultRules().Rules[0];
        var message = MessageFixtureLoader.Load(SamplePath("tests", "fixtures", "messages", "missing-beslagnummer.xml"));

        var result = ExtractionEngine.ExtractAll(rule, message);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors[0].Contains("CaseNumber"));
    }

    [TestMethod]
    public void PdfAttachmentFilterSkipsInlineAndNonPdf()
    {
        var rule = SampleData.CreateDefaultRules().Rules[0];
        var message = MessageFixtureLoader.Load(SamplePath("tests", "fixtures", "messages", "laboresultaten.xml"));

        var attachments = AttachmentFilterEvaluator.MatchingAttachments(rule, message);

        Assert.AreEqual(1, attachments.Count);
        Assert.AreEqual("rapport.pdf", attachments[0].Name);
    }

    [TestMethod]
    public void AttachmentNameFilterSupportsStartsWithEndsWithAndEquals()
    {
        var message = new MessageContext
        {
            Attachments =
            [
                new AttachmentContext { Name = "factuur-123.pdf" },
                new AttachmentContext { Name = "rapport.pdf" },
                new AttachmentContext { Name = "factuur-123.txt" }
            ]
        };

        var startsWithRule = new Rule
        {
            AttachmentFilters =
            [
                new AttachmentFilter
                {
                    NamePattern = "factuur",
                    NamePatternOperator = ConditionOperator.StartsWith,
                    ExcludeInline = false
                }
            ]
        };
        var endsWithRule = new Rule
        {
            AttachmentFilters =
            [
                new AttachmentFilter
                {
                    NamePattern = ".pdf",
                    NamePatternOperator = ConditionOperator.EndsWith,
                    ExcludeInline = false
                }
            ]
        };
        var equalsRule = new Rule
        {
            AttachmentFilters =
            [
                new AttachmentFilter
                {
                    NamePattern = "rapport.pdf",
                    NamePatternOperator = ConditionOperator.Equals,
                    ExcludeInline = false
                }
            ]
        };

        Assert.AreEqual(2, AttachmentFilterEvaluator.MatchingAttachments(startsWithRule, message).Count);
        Assert.AreEqual(2, AttachmentFilterEvaluator.MatchingAttachments(endsWithRule, message).Count);
        Assert.AreEqual(1, AttachmentFilterEvaluator.MatchingAttachments(equalsRule, message).Count);
    }

    [TestMethod]
    public void AttachmentNameFiltersCanRequireMultipleNameConditions()
    {
        var rule = new Rule
        {
            AttachmentFilters =
            [
                new AttachmentFilter { NamePattern = "factuur", NamePatternOperator = ConditionOperator.StartsWith, ExcludeInline = false },
                new AttachmentFilter { NamePattern = "juli", NamePatternOperator = ConditionOperator.Contains, ExcludeInline = false },
                new AttachmentFilter { NamePattern = ".pdf", NamePatternOperator = ConditionOperator.EndsWith, ExcludeInline = false }
            ]
        };
        var message = new MessageContext
        {
            Attachments =
            [
                new AttachmentContext { Name = "factuur-juli.pdf" },
                new AttachmentContext { Name = "factuur-augustus.pdf" },
                new AttachmentContext { Name = "rapport-juli.pdf" }
            ]
        };

        var attachments = AttachmentFilterEvaluator.MatchingAttachments(rule, message);

        Assert.AreEqual(1, attachments.Count);
        Assert.AreEqual("factuur-juli.pdf", attachments[0].Name);
    }

    [TestMethod]
    public void AttachmentNamePatternOperatorRoundTripsThroughXml()
    {
        var configuration = SampleData.CreateDefaultRules();
        var filter = configuration.Rules[0].AttachmentFilters[0];
        filter.NamePattern = ".pdf";
        filter.NamePatternOperator = ConditionOperator.EndsWith;

        var document = RuleConfigurationXml.ToRulesDocument(configuration);
        var loaded = RuleConfigurationXml.FromRulesDocument(document);

        Assert.AreEqual(ConditionOperator.EndsWith, loaded.Rules[0].AttachmentFilters[0].NamePatternOperator);
        StringAssert.Contains(document.ToString(), "namePatternOperator=\"EndsWith\"");
    }

    [TestMethod]
    public void FolderTemplateResolvesBuiltInAndExtractedTokens()
    {
        var rule = SampleData.CreateDefaultRules().Rules[0];
        rule.Destination.FolderTemplate = @"{ReceivedYear}\{CaseNumber}";
        var message = MessageFixtureLoader.Load(SamplePath("tests", "fixtures", "messages", "laboresultaten.xml"));
        var values = ExtractionEngine.ExtractAll(rule, message).Values;

        var resolved = TokenResolver.Resolve(rule.Destination.FolderTemplate, rule, message, message.Attachments[0], values, sanitizeSegments: true);

        Assert.AreEqual(Path.Combine("2026", "123456"), resolved);
    }

    [TestMethod]
    public void PathSanitizationBlocksTraversalAndReservedCharacters()
    {
        Assert.IsTrue(PathSanitizer.IsPathTraversal(@"..\secret"));
        Assert.AreEqual("ACME_Beta_2026", PathSanitizer.SanitizeSegment("ACME/Beta:2026"));
    }

    [TestMethod]
    public void DuplicateCounterPolicyPlansNextAvailableName()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "rapport.pdf"), "one");
        File.WriteAllText(Path.Combine(directory, "rapport (2).pdf"), "two");

        var plan = DuplicatePlanner.Plan(directory, "rapport.pdf", DuplicatePolicy.RenameCounter);

        Assert.AreEqual(SavePlanStatus.Planned, plan.Status);
        Assert.AreEqual(Path.Combine(directory, "rapport (3).pdf"), plan.DestinationPath);
    }

    [TestMethod]
    public void DuplicateSkipPolicySkipsExistingFile()
    {
        var directory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(directory, "rapport.pdf"), "one");

        var plan = DuplicatePlanner.Plan(directory, "rapport.pdf", DuplicatePolicy.Skip);

        Assert.AreEqual(SavePlanStatus.Skipped, plan.Status);
    }

    [TestMethod]
    public void DryRunSavePlanIsGeneratedForSampleLaboFixture()
    {
        var settings = new Settings();
        var configuration = SampleData.CreateDefaultRules();
        var message = MessageFixtureLoader.Load(SamplePath("tests", "fixtures", "messages", "laboresultaten.xml"));
        var engine = new RuleEngine(settings);

        var results = engine.Evaluate(configuration, message);

        Assert.AreEqual(1, results.Count);
        Assert.IsTrue(results[0].IsMatch);
        Assert.AreEqual(1, results[0].SavePlans.Count);
        StringAssert.Contains(results[0].SavePlans[0].DestinationPath, Path.Combine("123456", "Lab results", "rapport.pdf"));
    }

    [TestMethod]
    public void ProcessingLogRowsAreWrittenAsCsv()
    {
        var directory = CreateTempDirectory();
        CsvLogWriter.AppendProcessingLog(directory, new ProcessingLogRow(
            DateTime.Parse("2026-07-08T10:15:00"),
            "DryRun",
            "rule-1",
            "Rule",
            "message-1",
            "sender@example.com",
            "Subject, with comma",
            "file.pdf",
            @"C:\Temp\file.pdf",
            "Planned",
            "ok"));

        var file = Directory.GetFiles(directory, "processing-*.csv").Single();
        var rows = CsvLogWriter.ReadCsv(file);

        Assert.AreEqual(2, rows.Count);
        Assert.AreEqual("Subject, with comma", rows[1][6]);
    }

    [TestMethod]
    public void VstoBridgeContractUsesTheCoreRuleEngineAndReturnsAttachmentIndex()
    {
        var rules = SampleData.CreateDefaultRules();
        var settings = new Settings();
        var rulesXml = RuleConfigurationXml.ToRulesDocument(rules).ToString();
        var settingsXml = RuleConfigurationXml.ToSettingsDocument(settings).ToString();
        var messageXml = """
            <Message entryId="message-1">
              <Sender email="lab@example.com" name="Lab" />
              <Subject>Full test report</Subject>
              <ReceivedTime>2026-07-08T10:15:00.0000000</ReceivedTime>
              <BodyText>Case number: 123456</BodyText>
              <Attachments><Attachment index="1" name="rapport.pdf" sizeKb="42" isInline="false" /></Attachments>
            </Message>
            """;

        var evaluationXml = VstoBridgeRuleEngineContract.EvaluateXml(rulesXml, settingsXml, messageXml, dryRun: true);
        var plan = XDocument.Parse(evaluationXml).Descendants("Plan").Single();

        Assert.AreEqual("1", (string?)plan.Attribute("attachmentIndex"));
        Assert.AreEqual("Planned", (string?)plan.Attribute("status"));
    }

    [TestMethod]
    public void RuleEngineRejectsMessagesAboveTheGlobalAttachmentLimit()
    {
        var settings = new Settings { MaximumAttachmentCount = 1 };
        var rule = SampleData.CreateDefaultRules().Rules[0];
        var message = new MessageContext
        {
            Subject = "Beproevingsrapport (volledig resultaat)",
            Attachments = [new AttachmentContext { Name = "one.pdf" }, new AttachmentContext { Name = "two.pdf" }]
        };

        var result = new RuleEngine(settings).EvaluateRule(rule, message);

        Assert.IsFalse(result.IsMatch);
        StringAssert.Contains(result.Messages.Single(), "Attachment count exceeds");
    }

    [TestMethod]
    public void ConfigVersionStampDetectsAtomicSaveChange()
    {
        var directory = CreateTempDirectory();
        var path = Path.Combine(directory, "rules.xml");
        var configuration = SampleData.CreateDefaultRules();

        RuleConfigurationXml.SaveRulesAtomic(path, configuration);
        var first = RuleConfigurationXml.ReadRulesStamp(path);
        RuleConfigurationXml.SaveRulesAtomic(path, configuration);
        var second = RuleConfigurationXml.ReadRulesStamp(path);

        Assert.IsTrue(second.ConfigVersion > first.ConfigVersion);
        Assert.IsTrue(second.LastWriteTimeUtc >= first.LastWriteTimeUtc);
    }

    private static string SamplePath(params string[] parts) =>
        Path.GetFullPath(Path.Combine([AppContext.BaseDirectory, "..", "..", "..", "..", "..", .. parts]));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "outlookrulesvsto-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
