param(
    [string]$EvidenceDir = $env:EIGENTAKT_KEEP_DIR
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$manifestPath = Join-Path $repo "contracts/agent-journal-invocation-ownership.v1.json"
$docsPath = Join-Path $repo "docs/agent-journal-invocation-ownership.md"
$testProject = Join-Path $repo "tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj"
$resultsDir = Join-Path $repo "TestResults/agent-journal-ownership"

$requiredIds = @(
    "game.datee.performance",
    "game.avatar.reply",
    "game.avatar.emotional-director",
    "game.emotional-director",
    "game.dialogue-options",
    "game.setup.dramatic-arc",
    "game.prefetch.option-branch",
    "game.speculation.option-branch",
    "character.synthesis",
    "admin.temporary-chat",
    "admin.prompt-speculation",
    "narrative.harness",
    "session.simulation",
    "game.delivery.success-improvement",
    "game.delivery.horniness-question",
    "game.delivery.steering-question",
    "game.datee.interest-change-beat"
)

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if (-not $Condition) {
        throw $Message
    }
}

function As-Array {
    param($Value)
    if ($null -eq $Value) {
        return @()
    }
    if ($Value -is [System.Array]) {
        return @($Value)
    }
    return @($Value)
}

function Normalize-PathText {
    param([string]$Path)
    return ($Path -replace "\\", "/")
}

function Read-Text {
    param([string]$Path)
    Assert-True (Test-Path -LiteralPath $Path) "Missing file: $Path"
    return Get-Content -LiteralPath $Path -Raw
}

function Get-SourceLines {
    param(
        [string]$RelativePath,
        [string]$Pattern
    )
    $path = Join-Path $repo $RelativePath
    $text = Read-Text $path
    $matches = New-Object System.Collections.Generic.List[string]
    $lines = $text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match $Pattern) {
            $matches.Add(("{0}:{1}" -f (Normalize-PathText $RelativePath), ($i + 1)))
        }
    }
    return @($matches)
}

function Test-Matcher {
    param($Row, $Matcher)

    $kind = [string]$Matcher.kind
    switch ($kind) {
        { $_ -in @("symbol", "production_call") } {
            $file = [string]$Matcher.file
            $pattern = [string]$Matcher.pattern
            Assert-True ($file.Length -gt 0) "Matcher for $($Row.id) is missing file"
            Assert-True ($pattern.Length -gt 0) "Matcher for $($Row.id) is missing pattern"
            Assert-True ($pattern -ne ".*") "Catch-all matcher is forbidden for $($Row.id)"
            return Get-SourceLines $file $pattern
        }
        "web_review_anchor" {
            $file = [string]$Matcher.file
            $anchor = [string]$Matcher.anchor
            Assert-True ($anchor.Length -gt 0) "Web review matcher for $($Row.id) is missing anchor"
            $text = Read-Text (Join-Path $repo $file)
            Assert-True ($text.Contains($anchor)) "Missing web review anchor '$anchor' for $($Row.id)"
            return @(("{0}:anchor:{1}" -f (Normalize-PathText $file), $anchor))
        }
        "no_production_caller" {
            $pattern = [string]$Matcher.pattern
            $allowed = As-Array $Matcher.allowed_files | ForEach-Object { Normalize-PathText ([string]$_) }
            $roots = As-Array $Matcher.search_roots | ForEach-Object { Join-Path $repo ([string]$_) }
            $violations = New-Object System.Collections.Generic.List[string]
            foreach ($rootPath in $roots) {
                if (-not (Test-Path -LiteralPath $rootPath)) {
                    continue
                }
                $files = Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter "*.cs" |
                    Where-Object {
                        $rel = Normalize-PathText ($_.FullName.Substring($repo.Path.Length + 1))
                        $rel -notmatch "/bin/" -and
                        $rel -notmatch "/obj/" -and
                        $rel -notmatch "^tests/" -and
                        $rel -notmatch "^docs/" -and
                        $rel -notmatch "^contracts/"
                    }
                foreach ($file in $files) {
                    $rel = Normalize-PathText ($file.FullName.Substring($repo.Path.Length + 1))
                    $lines = Get-Content -LiteralPath $file.FullName
                    for ($i = 0; $i -lt $lines.Count; $i++) {
                        if ($lines[$i] -match $pattern -and $allowed -notcontains $rel) {
                            $violations.Add(("{0}:{1}" -f $rel, ($i + 1)))
                        }
                    }
                }
            }
            Assert-True ($violations.Count -eq 0) ("Dormant caller guard failed for {0}: {1}" -f $Row.id, ($violations -join ", "))
            return @("dormant-no-caller-proof:$($Row.id)")
        }
        default {
            throw "Unknown matcher kind '$kind' for $($Row.id)"
        }
    }
}

function Add-Candidate {
    param(
        [System.Collections.Generic.List[object]]$Candidates,
        [string]$RelativePath,
        [int]$LineNumber,
        [string]$Text,
        [string]$Reason
    )
    $Candidates.Add([pscustomobject]@{
        Key = "{0}:{1}" -f (Normalize-PathText $RelativePath), $LineNumber
        File = Normalize-PathText $RelativePath
        Line = $LineNumber
        Text = $Text.Trim()
        Reason = $Reason
    })
}

function Get-StaticScanCandidates {
    $candidates = New-Object System.Collections.Generic.List[object]
    $roots = @("src", "session-runner", "tools")
    foreach ($root in $roots) {
        $rootPath = Join-Path $repo $root
        if (-not (Test-Path -LiteralPath $rootPath)) {
            continue
        }
        $files = Get-ChildItem -LiteralPath $rootPath -Recurse -File -Filter "*.cs" |
            Where-Object {
                $rel = Normalize-PathText ($_.FullName.Substring($repo.Path.Length + 1))
                $rel -notmatch "/bin/" -and
                $rel -notmatch "/obj/" -and
                $rel -notmatch "^src/Pinder.RemoteAssets/" -and
                $rel -notmatch "^src/Pinder.LlmAdapters/(PiLlmTransport|ThinkingStrippingLlmTransport|PunctuationNormalizingTransport|PiProviderTransportFactory)\.cs$" -and
                $rel -notmatch "^src/Pinder.Core/Interfaces/" -and
                $rel -notmatch "^src/Pinder.Core/Conversation/NullLlmAdapter\.cs$" -and
                $rel -notmatch "^src/Pinder.SessionSetup/(I|Synthesis/I)" -and
                $rel -notmatch "^src/Pinder.SessionSetup/LlmOptionalTextGeneration\.cs$"
            }
        foreach ($file in $files) {
            $rel = Normalize-PathText ($file.FullName.Substring($repo.Path.Length + 1))
            $lines = Get-Content -LiteralPath $file.FullName
            for ($i = 0; $i -lt $lines.Count; $i++) {
                $line = $lines[$i]
                if ($line.TrimStart().StartsWith("//")) {
                    continue
                }
                $lineNo = $i + 1
                if ($rel -match "^src/Pinder.Core/Conversation/" -and
                    $line -match "Get(DialogueOptions|DateeResponse|SuccessImprovement|SteeringQuestion|HorninessQuestion)Async\(") {
                    Add-Candidate $candidates $rel $lineNo $line "core-conversation-call"
                }
                elseif ($rel -match "^src/Pinder.LlmAdapters/PinderLlmAdapter(\.(AvatarEmotionalDirector|EmotionalDirector))?\.cs$" -and
                    $line -match "Get(DialogueOptions|DateeResponse|InterestChangeBeat|SuccessImprovement|SteeringQuestion|HorninessQuestion)Async\(|Generate(Avatar)?EmotionalDirectionAsync\(") {
                    Add-Candidate $candidates $rel $lineNo $line "adapter-provider-path"
                }
                elseif ($rel -match "^src/Pinder.SessionSetup/" -and
                    $line -match "public async Task<.*GenerateAsync\(|SynthesizeAsync\(|LlmOptionalTextGeneration\.RunAsync\(") {
                    Add-Candidate $candidates $rel $lineNo $line "setup-synthesis-provider-path"
                }
                elseif ($rel -match "^src/Pinder.NarrativeHarness/" -and
                    $line -match "public async Task<HarnessRunResult> RunAsync\(|CharacterPursuerActor|GenericLlmPursuerActor|_transport\.SendAsync\(") {
                    Add-Candidate $candidates $rel $lineNo $line "harness-provider-path"
                }
                elseif ($rel -eq "session-runner/LlmPlayerAgent.cs" -and
                    $line -match "public sealed class LlmPlayerAgent|PiProviderTransportFactory\.Create|SendStructuredAsync\(") {
                    Add-Candidate $candidates $rel $lineNo $line "simulation-provider-path"
                }
            }
        }
    }
    return @($candidates)
}

function Get-TrxTestCount {
    param([string]$TrxPath)
    [xml]$trx = Get-Content -LiteralPath $TrxPath -Raw
    $ns = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
    $ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
    $counters = $trx.SelectSingleNode("//t:Counters", $ns)
    Assert-True ($null -ne $counters) "TRX counters missing: $TrxPath"
    return [int]$counters.total
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
Assert-True ($manifest.schema_version -eq "agent-journal-invocation-ownership.v1") "Unexpected manifest schema_version"
Assert-True ($manifest.closed_inventory -eq $true) "Manifest must be closed_inventory=true"
Assert-True ([int]$manifest.inventory_size -eq 17) "Manifest inventory_size must be 17"

$rows = As-Array $manifest.rows
Assert-True ($rows.Count -eq 17) "Manifest must contain exactly 17 rows; found $($rows.Count)"
$ids = @($rows | ForEach-Object { [string]$_.id })
$missing = @($requiredIds | Where-Object { $ids -notcontains $_ })
$extra = @($ids | Where-Object { $requiredIds -notcontains $_ })
Assert-True ($missing.Count -eq 0) "Missing manifest IDs: $($missing -join ', ')"
Assert-True ($extra.Count -eq 0) "Unexpected manifest IDs: $($extra -join ', ')"
Assert-True (($ids | Select-Object -Unique).Count -eq $ids.Count) "Duplicate manifest IDs are forbidden"

$requiredFields = @(
    "id",
    "status",
    "status_evidence",
    "activation_rule",
    "owner",
    "owner_description",
    "pi_agent_session",
    "journal_destination",
    "context_membership",
    "player_delivery",
    "visibility",
    "retention_policy_key",
    "required_owner_ids",
    "required_correlation_ids",
    "forbidden_owner_ids",
    "provenance_builder_ids",
    "implementation_matchers",
    "verifier_group"
)

$symbolMatchMap = @{}
$allMatcherResults = New-Object System.Collections.Generic.List[string]
$webReviewMatches = 0
$dormantProofs = 0

foreach ($row in $rows) {
    foreach ($field in $requiredFields) {
        Assert-True ($row.PSObject.Properties.Name -contains $field) "Row $($row.id) is missing $field"
    }
    foreach ($arrayField in @("status_evidence", "required_owner_ids", "required_correlation_ids", "forbidden_owner_ids", "provenance_builder_ids", "implementation_matchers")) {
        Assert-True ((As-Array $row.$arrayField).Count -gt 0) "Row $($row.id) has empty $arrayField"
    }
    Assert-True (([string]$row.activation_rule).Trim().Length -gt 0) "Row $($row.id) has empty activation_rule"
    Assert-True (([string]$row.retention_policy_key).Trim().Length -gt 0) "Row $($row.id) has empty retention_policy_key"

    $ownerIds = As-Array $row.required_owner_ids | ForEach-Object { [string]$_ }
    $correlationIds = As-Array $row.required_correlation_ids | ForEach-Object { [string]$_ }
    if ([string]$row.id -like "game.*") {
        Assert-True ($ownerIds -contains "game_run_id") "Game row $($row.id) must require game_run_id"
        Assert-True ($correlationIds -contains "game_run_id") "Game row $($row.id) must correlate game_run_id"
    }
    else {
        Assert-True ($ownerIds -notcontains "game_run_id") "Non-Game row $($row.id) must not require game_run_id"
        Assert-True ($correlationIds -notcontains "game_run_id") "Non-Game row $($row.id) must not correlate game_run_id"
    }

    foreach ($matcher in (As-Array $row.implementation_matchers)) {
        $matches = @(Test-Matcher $row $matcher)
        Assert-True ($matches.Count -gt 0) "Matcher for $($row.id) produced zero matches"
        foreach ($match in $matches) {
            $allMatcherResults.Add("$($row.id) -> $match")
            if ([string]$matcher.kind -in @("symbol", "production_call")) {
                if (-not $symbolMatchMap.ContainsKey($match)) {
                    $symbolMatchMap[$match] = New-Object System.Collections.Generic.HashSet[string]
                }
                [void]$symbolMatchMap[$match].Add([string]$row.id)
            }
            elseif ([string]$matcher.kind -eq "web_review_anchor") {
                $webReviewMatches++
            }
            elseif ([string]$matcher.kind -eq "no_production_caller") {
                $dormantProofs++
            }
        }
    }
}

$interestRow = $rows | Where-Object { $_.id -eq "game.datee.interest-change-beat" } | Select-Object -First 1
Assert-True ($interestRow.status -eq "provider_capable_dormant") "Interest-change beat must be provider_capable_dormant"
Assert-True ([string]$interestRow.activation_rule -match "fails" -and [string]$interestRow.activation_rule -match "planning") "Interest-change activation rule must fail back to planning"
Assert-True ($dormantProofs -gt 0) "Dormant no-caller proof did not run"

$liveCount = @($rows | Where-Object { $_.status -eq "live_production" }).Count
$dormantCount = @($rows | Where-Object { $_.status -eq "provider_capable_dormant" }).Count
$deadCount = @($rows | Where-Object { $_.status -eq "dead_with_proof" }).Count
Assert-True ($liveCount -eq 16) "Expected 16 live rows; found $liveCount"
Assert-True ($dormantCount -eq 1) "Expected 1 dormant row; found $dormantCount"
Assert-True ($deadCount -eq 0) "No dead rows are approved; found $deadCount"

$candidates = @(Get-StaticScanCandidates)
Assert-True ($candidates.Count -gt 0) "Static production scan produced zero candidates"
$unmatched = New-Object System.Collections.Generic.List[object]
$duplicates = New-Object System.Collections.Generic.List[string]
foreach ($candidate in $candidates) {
    if (-not $symbolMatchMap.ContainsKey($candidate.Key)) {
        $unmatched.Add($candidate)
        continue
    }
    if ($symbolMatchMap[$candidate.Key].Count -ne 1) {
        $duplicates.Add(("{0} -> {1}" -f $candidate.Key, ([string]::Join(",", $symbolMatchMap[$candidate.Key]))))
    }
}
Assert-True ($unmatched.Count -eq 0) ("Unclassified production LLM paths: " + (($unmatched | ForEach-Object { "$($_.Key) [$($_.Reason)] $($_.Text)" }) -join "; "))
Assert-True ($duplicates.Count -eq 0) ("Duplicate production LLM path ownership: " + ($duplicates -join "; "))

New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null
$trxName = "agent-journal-ownership.trx"
dotnet test $testProject `
    --filter "FullyQualifiedName~OwnershipManifestTests" `
    --results-directory $resultsDir `
    --logger "trx;LogFileName=$trxName"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
$trxPath = Join-Path $resultsDir $trxName
Assert-True (Test-Path -LiteralPath $trxPath) "Test TRX was not produced: $trxPath"
$testCount = Get-TrxTestCount $trxPath
Assert-True ($testCount -gt 0) "OwnershipManifestTests matched zero tests"

git -C $repo diff --check
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$symbolMatchCount = $symbolMatchMap.Keys.Count
Assert-True ($symbolMatchCount -gt 0) "Production symbol-match count is zero"
Assert-True ($webReviewMatches -gt 0) "Web review match count is zero"

$summary = @(
    "agent-journal ownership verifier completed",
    "manifest_count=16",
    "live_count=$liveCount",
    "dormant_count=$dormantCount",
    "dead_count=$deadCount",
    "production_symbol_match_count=$symbolMatchCount",
    "static_scan_candidate_count=$($candidates.Count)",
    "web_review_match_count=$webReviewMatches",
    "dormant_interest_change_no_caller_proof=passed",
    "ownership_test_count=$testCount"
)

$summary | ForEach-Object { Write-Host $_ }

if (-not [string]::IsNullOrWhiteSpace($EvidenceDir)) {
    New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
    $summaryPath = Join-Path $EvidenceDir "CORE-1373-agent-journal-ownership-verifier.txt"
    $matchesPath = Join-Path $EvidenceDir "CORE-1373-agent-journal-ownership-matches.txt"
    $manifestCopy = Join-Path $EvidenceDir "CORE-1373-agent-journal-invocation-ownership.v1.json"
    Set-Content -LiteralPath $summaryPath -Value ($summary -join [Environment]::NewLine)
    Set-Content -LiteralPath $matchesPath -Value ($allMatcherResults -join [Environment]::NewLine)
    Copy-Item -LiteralPath $manifestPath -Destination $manifestCopy -Force
}
