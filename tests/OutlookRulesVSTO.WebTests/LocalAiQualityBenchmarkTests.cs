using System.Diagnostics;
using System.Text.Json;
using OutlookRulesVSTO.Core;
using OutlookRulesVSTO.Web;

namespace OutlookRulesVSTO.WebTests;

[TestClass]
public sealed class LocalAiQualityBenchmarkTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [TestCategory("LocalAiBenchmarkData")]
    public void ExportsAdversarialBenchmarkExamples()
    {
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "ai-benchmark-examples.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(Examples, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.WriteLine(output);
    }

    [TestMethod]
    [TestCategory("LocalAiBenchmark")]
    [DataRow("Qwen_Qwen3.5-0.8B-Q4_K_M.gguf")]
    [DataRow("Qwen_Qwen3.5-2B-Q4_K_M.gguf")]
    [Timeout(900_000, CooperativeCancellation = true)]
    public async Task ScoresTwentyAdversarialVeterinaryEmails(string model)
    {
        if (Environment.GetEnvironmentVariable("OUTLOOKRULESVSTO_RUN_LOCAL_AI_BENCHMARK") != "1")
            Assert.Inconclusive("Zet OUTLOOKRULESVSTO_RUN_LOCAL_AI_BENCHMARK=1 om de 20-mail benchmark uit te voeren.");

        var settings = new Settings { AiModel = model, DefaultDestinationRoot = Path.GetTempPath() };
        using var builder = new AiRuleBuilder();
        var results = new List<BenchmarkResult>();
        foreach (var example in Examples)
        {
            var timer = Stopwatch.StartNew();
            var rule = await builder.BuildAsync(settings, new AiMail(example.Sender, example.Subject, example.Body, example.Attachments), TestContext.CancellationToken);
            timer.Stop();
            results.Add(Score(example, rule, timer.Elapsed.TotalSeconds));
        }

        var report = new BenchmarkReport(model, results, new PromptScores(
            results.Average(x => x.Scores.Metadata), results.Average(x => x.Scores.Sender),
            results.Average(x => x.Scores.Subject), results.Average(x => x.Scores.Attachment),
            results.Average(x => x.Scores.Extraction), results.Average(x => x.Scores.Folder)),
            results.Average(x => x.TotalScore), results.Sum(x => x.Seconds));
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", $"ai-benchmark-{(model.Contains("0.8B") ? "0.8B" : "2B")}.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        TestContext.WriteLine(JsonSerializer.Serialize(report.Averages));
        TestContext.WriteLine($"Overall={report.Overall:F2}/10, seconds={report.Seconds:F1}, report={output}");
    }

    private static BenchmarkResult Score(BenchmarkExample x, Rule rule, double seconds)
    {
        var sender = rule.Conditions.FirstOrDefault(c => c.Target == ConditionTarget.SenderEmail);
        var subject = rule.Conditions.FirstOrDefault(c => c.Target == ConditionTarget.Subject);
        var attachment = rule.Conditions.FirstOrDefault(c => c.Target is ConditionTarget.AttachmentName or ConditionTarget.AttachmentExtension);
        var extraction = rule.Extractions.Single();
        var folder = rule.Destination.FolderTemplate.Replace("{Beslagnummer}\\", "", StringComparison.OrdinalIgnoreCase);
        var scores = new PromptScores(
            ContainsAny(rule.Name + " " + rule.Description, x.TypeKeywords) ? 10 : string.IsNullOrWhiteSpace(rule.Name) ? 0 : 5,
            (sender is not null) == x.ExpectSender ? 10 : 0,
            subject is not null && ContainsAny(subject.Value, x.SubjectKeywords) ? 10 : subject is not null ? 5 : 0,
            ScoreAttachment(attachment, x.AttachmentKeyword),
            extraction.Source is ExtractionSource.Automatic || extraction.Source.ToString() == x.Source ? 10 : 0,
            ContainsAny(folder, x.TypeKeywords) ? 10 : string.IsNullOrWhiteSpace(folder) ? 0 : 5);
        return new BenchmarkResult(x.Id, scores, scores.Average, seconds, rule.Name, rule.Description,
            rule.Conditions.Select(c => $"{c.Target}:{c.Operator}:{c.Value}").ToArray(), extraction.Source.ToString(), rule.Destination.FolderTemplate);
    }

    private static double ScoreAttachment(Condition? condition, string? expected) => expected is null
        ? condition is null ? 10 : 0
        : condition is not null && condition.Value.Contains(expected, StringComparison.OrdinalIgnoreCase) ? 10 : condition is not null ? 5 : 0;
    private static bool ContainsAny(string value, string[] candidates) => candidates.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase));

    private static readonly BenchmarkExample[] Examples =
    [
        new("01", "Medicatiebestelling", ["medicatie", "bestelling"], "bestelling@dierenkliniek.example", true, "Medicatiebestelling zeugen / ref 99183 / BE12345678-0101", "Voor BE12345678-0101 graag vaccins. De datum 12-08-2026 en klant De Eik zijn variabel.", [], null, "Subject"),
        new("02", "Longletselsrapport", ["longletsel", "rapport"], "rapport@slachthuis.example", true, "Rapport 2026-8891 Zonnehoeve BE87654321-0202", "Veterinair longletselsrapport voor beslag BE87654321-0202.", ["Longletsels_BE87654321-0202_20260812.pdf"], "Longletsels", "Subject"),
        new("03", "Spoedmelding", ["spoed", "uitval"], "tom@bedrijf.example", false, "DRINGEND: 7 dode biggen stal 3", "Beslagnummer BE11223344-0303. Verhoogde uitval; bel onmiddellijk. Factuur BE99887766-9999 is slechts een oud referentienummer.", [], null, "BodyText"),
        new("04", "Laboratoriumuitslag", ["laboratorium", "uitslag"], "uitslagen@vetlab.example", true, "PCR resultaat PRRS – monster XQ-7781", "Uitslag voor beslag BE23456789-0404: PRRS positief. BE00000000-0000 is de controlecode en geen beslag.", ["PCR_PRRS_XQ-7781.pdf"], ".pdf", "BodyText"),
        new("05", "Vaccinatieherinnering", ["vaccin", "herinner"], "planning@praktijk.example", true, "Herinnering vaccinatie ronde 2026/09", "Plan vaccinatie voor BE34567890-0505. Gebruik niet het patiëntnummer 34567890.", [], null, "BodyText"),
        new("06", "Autopsieverslag", ["autops", "sectie"], "pathologie@univet.example", true, "Definitief sectieverslag dossier A-991", "Autopsie big voor beslag BE45678901-0606. In de footer staat voorbeeld BE11111111-1111.", ["Sectieverslag_A-991.docx"], "Sectie", "BodyText"),
        new("07", "Voederadvies", ["voeder", "advies"], "advisor@example.test", false, "Nieuw schema fase 2 – voorbeeldbedrijf", "Veterinair voederadvies voor BE56789012-0707; vorige versie voor BE22222222-2222 vervalt.", ["Voerschema_fase2.xlsx"], "Voerschema", "BodyText"),
        new("08", "Antibiogram", ["antibiogram", "resistent"], "microbiologie@vetlab.example", true, "Antibiogram isolaat 24-887", "Resistentieprofiel voor beslag BE67890123-0808. Onderwerp bevat bewust geen beslagnummer.", ["ABG_24-887.pdf"], "ABG", "BodyText"),
        new("09", "Geboortemelding", ["geboorte", "worp"], "zeugen@farm.example", false, "Worpresultaten week 32 – locatie West", "Geboortemelding: BE78901234-0909, 14 levend en 2 doodgeboren. Order BE33333333-3333 negeren.", [], null, "BodyText"),
        new("10", "Transportkeuring", ["transport", "keuring"], "keuring@favo.example", true, "Geschiktheidsverklaring transport BE89012345-1010", "Veterinaire transportkeuring. Document-ID BE44444444-4444 is niet het beslagnummer.", ["Transportverklaring_final_v7.pdf"], "Transport", "Subject"),
        new("11", "Mestonderzoek", ["mest", "onderzoek"], "parasito@lab.example", true, "Uitslag fecesonderzoek batch 771", "Parasietenonderzoek voor BE90123456-1111. Negatief op wormeieren.", ["Feces_batch771.csv"], "Feces", "BodyText"),
        new("12", "Kreupelheidsmelding", ["kreupel", "locomot"], "manager@farm.example", false, "Probleem afdeling 8 – graag visite", "Veel kreupele vleesvarkens op BE01234567-1212. Telefoon bevat 012345671212 maar is geen beslag.", [], null, "BodyText"),
        new("13", "Salmonellarapport", ["salmonella", "rapport"], "monitoring@dgz.example", true, "Monitoringrapport S-2026-44", "Salmonella categorie voor beslag BE13572468-1313. Sjabloontekst: BE########-####.", ["Salmonella_S-2026-44.pdf"], "Salmonella", "BodyText"),
        new("14", "Kadaverophaling", ["kadaver", "ophaling"], "planning@rendac.example", true, "Bevestiging ophaling nummer 88291", "Ophaling op beslag BE24681357-1414. De afzendernaam lijkt op een dierenarts maar is een vaste dienst.", [], null, "BodyText"),
        new("15", "Drinkwateranalyse", ["water", "analyse"], "chemie@agrolab.example", true, "Analysecertificaat water W-6612", "Drinkwateranalyse varkensbedrijf; beslag BE31415926-1515. Klantreferentie BE55555555-5555 niet gebruiken.", ["Water_W-6612_certificate.PDF"], ".PDF", "BodyText"),
        new("16", "Behandelplan", ["behandel", "therapie"], "clinician@example.test", false, "Therapievoorstel luchtwegproblemen", "Behandelplan voor BE27182818-1616. Medicatie en dosering staan in de bijlage.", ["Therapievoorstel_v3.docx"], "Therapie", "BodyText"),
        new("17", "Sterftemelding", ["sterfte", "mortaliteit"], "alerts@sensor.example", true, "ALERT mortaliteit boven drempel", "Automatische veterinaire waarschuwing voor BE16180339-1717. Sensor-ID 1717-BE is misleidend.", [], null, "BodyText"),
        new("18", "Leveringsbon medicatie", ["medicatie", "lever"], "apotheek@praktijk.example", true, "Leveringsbon LM-88210", "Medicatie geleverd voor beslag BE14142135-1818; factuur noemt BE66666666-6666 als bankkenmerk.", ["Leveringsbon_LM-88210.pdf", "Bijsluiter.pdf"], "Leveringsbon", "BodyText"),
        new("19", "Welzijnsaudit", ["welzijn", "audit"], "audit@cert.example", true, "Concept auditrapport – NIET DEFINITIEF", "Veterinaire welzijnsaudit voor BE17320508-1919. Voorbeeld uit handleiding: BE77777777-7777.", ["Audit_concept_v12.pdf"], "Audit", "BodyText"),
        new("20", "Drachtcontrole", ["dracht", "echo"], "sender@example.test", false, "Echoresultaten ronde A", "Drachtcontrole op beslag BE22360679-2020. 38 zeugen onderzocht; oude locatie BE88888888-8888 gesloten.", ["Echoresultaten_ronde_A.xlsx"], "Echo", "BodyText")
    ];

    private sealed record BenchmarkExample(string Id, string Type, string[] TypeKeywords, string Sender, bool ExpectSender, string Subject, string Body, string[] Attachments, string? AttachmentKeyword, string Source)
    {
        public string[] SubjectKeywords => TypeKeywords;
    }
    private sealed record PromptScores(double Metadata, double Sender, double Subject, double Attachment, double Extraction, double Folder)
    {
        public double Average => (Metadata + Sender + Subject + Attachment + Extraction + Folder) / 6;
    }
    private sealed record BenchmarkResult(string Id, PromptScores Scores, double TotalScore, double Seconds, string Name, string Description, string[] Conditions, string ExtractionSource, string Folder);
    private sealed record BenchmarkReport(string Model, List<BenchmarkResult> Results, PromptScores Averages, double Overall, double Seconds);
}
