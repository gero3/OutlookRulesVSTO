param(
    [string]$Artifacts = (Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'),
    [string]$Fragment = (Join-Path (Split-Path $PSScriptRoot -Parent) '.codex\visualizations\2026\07\12\019f56f3-b47f-7350-973b-35cd610cd056\ai-model-benchmark.html')
)
$ErrorActionPreference = 'Stop'
$small = Get-Content (Join-Path $Artifacts 'ai-benchmark-0.8B.json') -Raw | ConvertFrom-Json
$large = Get-Content (Join-Path $Artifacts 'ai-benchmark-2B.json') -Raw | ConvertFrom-Json
$smallBaseline = Get-Content (Join-Path $Artifacts 'ai-benchmark-baseline-0.8B.json') -Raw | ConvertFrom-Json
$largeBaseline = Get-Content (Join-Path $Artifacts 'ai-benchmark-baseline-2B.json') -Raw | ConvertFrom-Json
$smallPrevious = Get-Content (Join-Path $Artifacts 'ai-benchmark-pre-attachment-fallback-0.8B.json') -Raw | ConvertFrom-Json
$largePrevious = Get-Content (Join-Path $Artifacts 'ai-benchmark-pre-attachment-fallback-2B.json') -Raw | ConvertFrom-Json
$examples = Get-Content (Join-Path $Artifacts 'ai-benchmark-examples.json') -Raw | ConvertFrom-Json
function Html([object]$value) { [System.Net.WebUtility]::HtmlEncode([string]$value) }
function Scores($s) { "Metadata $(Html $s.Metadata) · Sender $(Html $s.Sender) · Subject $(Html $s.Subject) · Attachment $(Html $s.Attachment) · Extraction $(Html $s.Extraction) · Folder $(Html $s.Folder)" }
function ModelResult($r, [string]$label) {
    $conditions = if ($r.Conditions.Count) { ($r.Conditions | ForEach-Object { "<li><code>$(Html $_)</code></li>" }) -join '' } else { '<li class="text-destructive">No recognition conditions</li>' }
    @"
<section class="model-result" aria-label="$(Html $label) result">
  <h3>$(Html $label) — $([math]::Round($r.TotalScore,1))/10</h3>
  <dl><dt>Name</dt><dd>$(Html $r.Name)</dd><dt>Description</dt><dd>$(Html $r.Description)</dd><dt>Conditions</dt><dd><ul>$conditions</ul></dd><dt>Extraction</dt><dd>$(Html $r.ExtractionSource)</dd><dt>Folder</dt><dd><code>$(Html $r.Folder)</code></dd><dt>Prompt scores</dt><dd>$(Scores $r.Scores)</dd><dt>Runtime</dt><dd>$([math]::Round($r.Seconds,1)) seconds</dd></dl>
</section>
"@
}
$taskNames = @('Metadata','Sender','Subject','Attachment','Extraction','Folder')
$scoreRows = foreach ($task in $taskNames) {
    $a = [double]$small.Averages.$task; $b = [double]$large.Averages.$task
    $ap = [double]$smallPrevious.Averages.$task; $bp = [double]$largePrevious.Averages.$task
    "<tr><th>$(Html $task)</th><td class=`"num`">$([math]::Round($ap,1))</td><td class=`"num`"><strong>$([math]::Round($a,1))</strong></td><td class=`"num delta`">$('{0:+0.0;-0.0;0.0}' -f ($a-$ap))</td><td class=`"num`">$([math]::Round($bp,1))</td><td class=`"num`"><strong>$([math]::Round($b,1))</strong></td><td class=`"num delta`">$('{0:+0.0;-0.0;0.0}' -f ($b-$bp))</td></tr>"
}
$smallNoConditions = @($small.Results | Where-Object { @($_.Conditions).Count -eq 0 }).Count
$largeNoConditions = @($large.Results | Where-Object { @($_.Conditions).Count -eq 0 }).Count
$smallAttachmentConditions = @($small.Results | Where-Object { ($_.Conditions -join ' ') -match 'Attachment' }).Count
$largeAttachmentConditions = @($large.Results | Where-Object { ($_.Conditions -join ' ') -match 'Attachment' }).Count
$smallLow = @($small.Results | Where-Object TotalScore -lt 8).Count
$largeLow = @($large.Results | Where-Object TotalScore -lt 8).Count
$caseSections = foreach ($example in $examples) {
    $a = $small.Results | Where-Object Id -eq $example.Id
    $b = $large.Results | Where-Object Id -eq $example.Id
    $attachments = if ($example.Attachments.Count) { ($example.Attachments | ForEach-Object { "<code>$(Html $_)</code>" }) -join ', ' } else { 'None' }
    @"
<details class="case">
  <summary><strong>$(Html $example.Id). $(Html $example.Type)</strong><span>0.8B $([math]::Round($a.TotalScore,1)) · 2B $([math]::Round($b.TotalScore,1))</span></summary>
  <div class="input-block"><dl><dt>Sender</dt><dd>$(Html $example.Sender)</dd><dt>Subject</dt><dd>$(Html $example.Subject)</dd><dt>Body</dt><dd>$(Html $example.Body)</dd><dt>Attachments</dt><dd>$attachments</dd><dt>Expected sender condition</dt><dd>$(Html $example.ExpectSender)</dd><dt>Holding number occurs in</dt><dd>$(Html $example.Source) (the rule now searches all sources automatically)</dd><dt>Expected reusable terms</dt><dd>Subject: $(Html ($example.TypeKeywords -join ', ')); attachment: $(Html $example.AttachmentKeyword)</dd></dl></div>
  <div class="model-grid">$(ModelResult $a 'Qwen 0.8B')$(ModelResult $b 'Qwen 2B')</div>
</details>
"@
}
$html = @"
<div id="ai-model-benchmark">
<style>
#ai-model-benchmark{color:var(--foreground)}#ai-model-benchmark .viz-grid{margin:12px 0 22px}#ai-model-benchmark table{width:100%;border-collapse:collapse;margin:10px 0 24px}#ai-model-benchmark th,#ai-model-benchmark td{padding:9px;border-bottom:1px solid var(--border);text-align:left;vertical-align:top}#ai-model-benchmark .num{text-align:right;white-space:nowrap}#ai-model-benchmark .delta{color:var(--muted-foreground)}#ai-model-benchmark .analysis{display:grid;grid-template-columns:1fr 1fr;gap:20px;margin:14px 0 24px}#ai-model-benchmark .analysis section{min-width:0}#ai-model-benchmark .analysis ul{padding-left:20px}#ai-model-benchmark .case{border-bottom:1px solid var(--border);padding:10px 0}#ai-model-benchmark summary{cursor:pointer;display:flex;justify-content:space-between;gap:12px;align-items:center}#ai-model-benchmark summary span{color:var(--muted-foreground);white-space:nowrap}#ai-model-benchmark .input-block{margin:14px 0}#ai-model-benchmark dl{display:grid;grid-template-columns:minmax(120px,170px) 1fr;gap:7px 12px;margin:0}#ai-model-benchmark dt{color:var(--muted-foreground)}#ai-model-benchmark dd{margin:0;min-width:0;overflow-wrap:anywhere}#ai-model-benchmark dd ul{margin:0;padding-left:18px}#ai-model-benchmark .model-grid{display:grid;grid-template-columns:1fr 1fr;gap:24px;margin:16px 0 8px}#ai-model-benchmark .model-result{min-width:0}#ai-model-benchmark .model-result h3{margin-top:0}@media(max-width:620px){#ai-model-benchmark .analysis,#ai-model-benchmark .model-grid{grid-template-columns:1fr}#ai-model-benchmark dl{grid-template-columns:1fr}#ai-model-benchmark dt{margin-top:5px}#ai-model-benchmark summary{align-items:flex-start;flex-direction:column}}
</style>
<div class="viz-grid"><div class="card viz-stat"><span class="text-muted">0.8B current</span><div class="viz-stat-value">$([math]::Round($small.Overall,1))/10</div><span class="text-small">$([math]::Round($small.Seconds)) seconds · $smallLow cases below 8</span></div><div class="card viz-stat"><span class="text-muted">2B current</span><div class="viz-stat-value">$([math]::Round($large.Overall,1))/10</div><span class="text-small">$([math]::Round($large.Seconds)) seconds · $largeLow cases below 8</span></div><div class="card viz-stat"><span class="text-muted">Fresh evaluation</span><div class="viz-stat-value">200</div><span class="text-small">20 cases × 5 AI prompts × 2 models</span></div></div>
<h2>Executive analysis</h2>
<div class="analysis"><section><h3>What improved</h3><ul><li>A conservative deterministic fallback now creates a recurring condition from a literal document-type prefix when the model declines or invents a value.</li><li>Attachment coverage increased from 0/13 to $smallAttachmentConditions/13 for 0.8B and from 4/13 to $largeAttachmentConditions/13 for 2B.</li><li>Attachment quality increased from 3.5 to $([math]::Round($small.Averages.Attachment,1))/10 for 0.8B and from 5.5 to $([math]::Round($large.Averages.Attachment,1))/10 for 2B.</li><li>Overall quality increased from $([math]::Round($smallPrevious.Overall,1)) to $([math]::Round($small.Overall,1)) for 0.8B and from $([math]::Round($largePrevious.Overall,1)) to $([math]::Round($large.Overall,1)) for 2B.</li><li>All 70 normal tests pass, including five focused attachment-fallback tests.</li></ul></section><section><h3>How safety is preserved</h3><ul><li>A grounded model condition remains unchanged.</li><li>An invented or non-literal model value is rejected before the fallback runs.</li><li>The fallback uses only the first literal filename segment and excludes digits and generic prefixes such as rapport, document, bijlage, scan, and file.</li><li>Generic filenames are renamed descriptively but do not become recurring recognition conditions.</li><li>Some names, folders, and subject conditions can still contain variable details; 0.8B has $smallLow cases below 8/10 and 2B has $largeLow.</li></ul></section></div>
<h2>Current versus previous attachment logic</h2><table><thead><tr><th>Prompt</th><th class="num">0.8B previous</th><th class="num">0.8B current</th><th class="num">Δ</th><th class="num">2B previous</th><th class="num">2B current</th><th class="num">Δ</th></tr></thead><tbody>$($scoreRows -join '')<tr><th>Overall</th><td class="num">$([math]::Round($smallPrevious.Overall,1))</td><td class="num"><strong>$([math]::Round($small.Overall,1))</strong></td><td class="num delta">$('{0:+0.0;-0.0;0.0}' -f ($small.Overall-$smallPrevious.Overall))</td><td class="num">$([math]::Round($largePrevious.Overall,1))</td><td class="num"><strong>$([math]::Round($large.Overall,1))</strong></td><td class="num delta">$('{0:+0.0;-0.0;0.0}' -f ($large.Overall-$largePrevious.Overall))</td></tr></tbody></table>
<h2>Extraction safety matrix</h2><table><thead><tr><th>Situation</th><th>Current result</th><th>Verified behavior</th></tr></thead><tbody><tr><td>No holding number</td><td>Extraction failure</td><td>Routes to review when review storage is enabled</td></tr><tr><td>One number in attachment filename only</td><td>Extract that number</td><td>Passed focused core test</td></tr><tr><td>Same number repeated across sources</td><td>Deduplicate and use it</td><td>Passed focused core test</td></tr><tr><td>Different numbers across sources</td><td>Ambiguous extraction</td><td>Routes to review; never silently selects the first</td></tr><tr><td>Existing explicit-source rule</td><td>Keep configured source</td><td>XML round-trip and compatibility tests passed</td></tr></tbody></table>
<h2>Test execution</h2><table><thead><tr><th>Suite</th><th class="num">Passed</th><th>Scope</th></tr></thead><tbody><tr><td>Core</td><td class="num">44</td><td>Extraction, rule evaluation, review routing, XML, tokens, paths, duplicate behavior</td></tr><tr><td>Desktop UI</td><td class="num">3</td><td>Windows UI behavior</td></tr><tr><td>Web</td><td class="num">23</td><td>API, browser editor, EML parsing, filename renaming, attachment fallback</td></tr><tr><td>Real local AI</td><td class="num">6</td><td>Three emails × two models; all created Automatic extraction drafts</td></tr><tr><td>Adversarial benchmark</td><td class="num">2</td><td>Twenty veterinary examples per model; 200 fresh AI responses</td></tr></tbody></table>
<h2>Recommendation</h2><p><strong>Keep the deterministic fallback and use 2B with thinking disabled.</strong> Both models now handle attachment conditions well, but 2B remains slightly stronger overall at $([math]::Round($large.Overall,1))/10 versus $([math]::Round($small.Overall,1))/10. Automatic extraction and attachment fallback should remain deterministic; the model is best used for semantic naming and subject/sender judgments.</p>
<h2>All 20 examples and current model results</h2><p class="text-small text-muted">Expand a case to inspect the complete benchmark input, expected behavior, generated rule, six task scores, and runtime. Extraction now reads Automatic because source selection is deterministic rather than model-generated.</p>
$($caseSections -join "`n")
</div>
"@
New-Item -ItemType Directory -Force -Path (Split-Path $Fragment -Parent) | Out-Null
Set-Content -LiteralPath $Fragment -Value $html -Encoding utf8
Write-Output $Fragment

