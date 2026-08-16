$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo

$resultsDir = Join-Path $repo "artifacts/test-results/issue-1372"
New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

function Invoke-TestGroup {
    param(
        [string]$GroupName,
        [string]$Filter,
        [int]$MinimumCount
    )

    $groupResultsDir = Join-Path $resultsDir $GroupName
    New-Item -ItemType Directory -Force -Path $groupResultsDir | Out-Null
    $trxName = "issue-1372-$GroupName.trx"
    dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj `
        --filter $Filter `
        --logger "trx;LogFileName=$trxName" `
        --results-directory $groupResultsDir

    $trxPath = Join-Path $groupResultsDir $trxName
    if (-not (Test-Path $trxPath)) {
        throw "Expected TRX output was not written: $trxPath"
    }
    [xml]$trx = Get-Content -LiteralPath $trxPath
    $executed = [int]$trx.TestRun.ResultSummary.Counters.executed
    $failed = [int]$trx.TestRun.ResultSummary.Counters.failed
    if ($executed -lt $MinimumCount) {
        throw "$GroupName expected at least $MinimumCount tests, executed $executed."
    }
    if ($failed -ne 0) {
        throw "$GroupName reported $failed failures."
    }
    [PSCustomObject]@{ Name = $GroupName; Executed = $executed; Trx = $trxPath }
}

$testGroups = @(
    Invoke-TestGroup "materializer-branch" "FullyQualifiedName~Pinder.LlmAdapters.Tests.AgentJournals.Materialization.AgentJournalMaterializerBranchGoldenTests" 7
    Invoke-TestGroup "materializer-compatibility" "FullyQualifiedName~Pinder.LlmAdapters.Tests.AgentJournals.Materialization.AgentJournalMaterializerCompatibilityTests" 9
    Invoke-TestGroup "materializer-side-effects" "FullyQualifiedName~Pinder.LlmAdapters.Tests.AgentJournals.Materialization.AgentJournalMaterializerSideEffectTests" 7
    Invoke-TestGroup "pi-session" "FullyQualifiedName~Pinder.LlmAdapters.Tests.Issue54_PiConversationSessionTests" 2
)

$snapshotFixtureDir = Join-Path $repo "tests/Pinder.LlmAdapters.Tests/AgentJournals/Materialization/Fixtures/snapshots"
$requiredFixtures = @(
    "ambiguous-roots.snapshot.json",
    "child-before-parent.snapshot.json",
    "cycle.snapshot.json",
    "duplicate-ids.snapshot.json",
    "invalid-parentage.snapshot.json",
    "parent-first-equivalent.snapshot.json",
    "self-parent.snapshot.json"
)
foreach ($fixture in $requiredFixtures) {
    if (-not (Test-Path -LiteralPath (Join-Path $snapshotFixtureDir $fixture))) {
        throw "Missing required tree-validation fixture: $fixture"
    }
}

$fixtureDir = Join-Path $repo "tests/Pinder.LlmAdapters.Tests/AgentJournals/Materialization/Fixtures/normalized"
$first = Get-ChildItem -LiteralPath $fixtureDir -Filter "*.json" | Sort-Object Name | ForEach-Object {
    [PSCustomObject]@{
        Name = $_.Name
        Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
}
$second = Get-ChildItem -LiteralPath $fixtureDir -Filter "*.json" | Sort-Object Name | ForEach-Object {
    [PSCustomObject]@{
        Name = $_.Name
        Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash
    }
}
if (($first | ConvertTo-Json -Compress) -ne ($second | ConvertTo-Json -Compress)) {
    throw "Golden normalized fixture hash comparison was not deterministic."
}

$materializerFiles = Get-ChildItem -LiteralPath (Join-Path $repo "src/Pinder.LlmAdapters/AgentJournals") -Filter "AgentJournalMaterializer*.cs"
if ($materializerFiles.Count -eq 0) {
    throw "No AgentJournalMaterializer source files found."
}

$forbiddenPattern = "\bTransport\b|PiLlmTransport|PiProviderTransportFactory|BuildContextAsync|AgentHarness|PiConversationSession|Fixture"
foreach ($file in $materializerFiles) {
    $matches = Select-String -LiteralPath $file.FullName -Pattern $forbiddenPattern -AllMatches
    if ($matches) {
        $lines = $matches | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
        throw "Production materializer references forbidden provider/session-context/fixture symbols:`n$($lines -join [Environment]::NewLine)"
    }
}

dotnet build Pinder.Core.sln

git diff --check

$hashPath = Join-Path $resultsDir "normalized-fixture-hashes.sha256.txt"
$first | ForEach-Object { "$($_.Hash)  $($_.Name)" } | Set-Content -LiteralPath $hashPath

Write-Host "issue-1372 verifier passed"
foreach ($group in $testGroups) {
    Write-Host "$($group.Name): executed=$($group.Executed) trx=$($group.Trx)"
}
Write-Host "fixture_hashes=$hashPath"
