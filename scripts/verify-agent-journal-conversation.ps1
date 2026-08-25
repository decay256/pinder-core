param(
    [string]$ResultsDirectory = ""
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    if (-not [string]::IsNullOrWhiteSpace($env:EIGENTAKT_KEEP_DIR)) {
        $ResultsDirectory = Join-Path $env:EIGENTAKT_KEEP_DIR "CORE-1375"
    } else {
        $ResultsDirectory = Join-Path $repo "TestResults/agent-journal-conversation"
    }
}
$null = New-Item -ItemType Directory -Force -Path $ResultsDirectory
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ") + "-" + $PID
$trxName = "core-1375-conversation-$stamp.trx"
$trx = Join-Path $ResultsDirectory $trxName
$log = Join-Path $ResultsDirectory "core-1375-conversation-$stamp.log"

$inventoryPath = Join-Path $repo "src/Pinder.LlmAdapters/AgentJournals/GameRunConversationJournalInventory.cs"
$inventory = Get-Content -LiteralPath $inventoryPath -Raw
$expected = @(
    "game.datee.performance",
    "game.avatar.reply",
    "game.emotional-director",
    "game.avatar.emotional-director",
    "game.prefetch.option-branch",
    "game.speculation.option-branch",
    "game.emotional-director.branch-disposed"
)
$actual = [regex]::Matches($inventory, 'public const string \w+ = "([^"]+)";') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique
if ((Compare-Object ($expected | Sort-Object) $actual).Count -ne 0) {
    throw "Approved call-path inventory mismatch."
}

$testPath = Join-Path $repo "tests/Pinder.LlmAdapters.Tests/AgentJournals/Recording/Issue1375_ConversationJournalWiringTests.cs"
$testSource = Get-Content -LiteralPath $testPath -Raw
foreach ($forbidden in @("CompleteSyntheticAsync", "new AgentJournalRecorder")) {
    if ($testSource.Contains($forbidden)) {
        throw "CORE-1375 lifecycle matrix bypasses production adapter via $forbidden"
    }
}

$requiredFragments = @{
    "src/Pinder.Core/Conversation/GameSession.cs" = @(
        "config.AgentJournalContext ?? new GameRunAgentJournalContext",
        '"game-run-" + Guid.NewGuid()'
    )
    "src/Pinder.Core/Conversation/GameSession.Clone.cs" = @(
        "public GameSession Clone(",
        "GameRunConversationBranchKind branchKind",
        "branchKind != GameRunConversationBranchKind.Prefetch",
        "branchKind != GameRunConversationBranchKind.Speculative",
        "_agentJournalContext.ForBranch"
    )
    "tests/Pinder.Core.Tests/Phase5/Phase5_FastGameplayInvariantTests.cs" = @(
        "GameRunConversationBranchKind.Prefetch",
        "GameRunConversationBranchKind.Speculative",
        "AdapterReplacingClone_RejectsAmbiguousBranchPurpose"
    )
    "src/Pinder.LlmAdapters/PinderLlmAdapter.cs" = @(
        "ResolveConversationCallPath(context.AgentJournalContext",
        "GameRunConversationJournalInventory.DateePerformance",
        "GameRunConversationJournalInventory.AvatarReply",
        "StartBranchDisposalJournalAsync"
    )
    "src/Pinder.LlmAdapters/PinderLlmAdapter.EmotionalDirector.cs" = @(
        "GameRunConversationJournalInventory.EmotionalDirector"
    )
    "src/Pinder.LlmAdapters/PinderLlmAdapter.CharacterEmotionalDirector.cs" = @(
        "entryIds.AssistantEntryId",
        "CompleteAcceptedAsync(responseText, semanticEntryId)"
    )
    "src/Pinder.LlmAdapters/PinderLlmAdapter.AvatarEmotionalDirector.cs" = @(
        "GameRunConversationJournalInventory.AvatarEmotionalDirector",
        "avatar-private-analysis"
    )
    "src/Pinder.LlmAdapters/PinderLlmAdapter.AgentJournals.cs" = @(
        "GameRunConversationBranchKind.Prefetch",
        "GameRunConversationBranchKind.Speculative",
        "GameRunConversationJournalInventory.DirectorBranchDisposed",
        "AgentJournalCallScope.Disabled"
    )
}
foreach ($relative in $requiredFragments.Keys) {
    $source = Get-Content -LiteralPath (Join-Path $repo $relative) -Raw
    foreach ($fragment in $requiredFragments[$relative]) {
        if (-not $source.Contains($fragment)) {
            throw "Static no-bypass check failed for ${relative}: missing $fragment"
        }
    }
}

$cloneSource = Get-Content -LiteralPath (Join-Path $repo "src/Pinder.Core/Conversation/GameSession.Clone.cs") -Raw
if ($cloneSource -match 'public\s+GameSession\s+Clone\s*\(\s*ILlmAdapter\s+llm\s*\)') {
    throw "Ambiguous Clone(ILlmAdapter) can silently preserve main journal context"
}
foreach ($forbidden in @("CloneForPrefetch", "CloneForSpeculation")) {
    if ($cloneSource.Contains($forbidden)) {
        throw "Test-only clone boundary remains in production source: $forbidden"
    }
}
if (-not $testSource.Contains("parent.Clone(") -or
    -not $testSource.Contains("GameRunConversationBranchKind.Prefetch") -or
    -not $testSource.Contains("GameRunConversationBranchKind.Speculative")) {
    throw "Journal scenarios do not exercise the explicit production clone boundary"
}

$production = Get-ChildItem -LiteralPath (Join-Path $repo "src/Pinder.Core"), (Join-Path $repo "src/Pinder.LlmAdapters") -Recurse -Filter "*.cs" |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }
$productionText = $production -join "`n"
foreach ($forbidden in @("AgentJournalGameRunId", "_agentJournalDefaultGameRunId", "_agentJournalInvocationSequence")) {
    if ($productionText.Contains($forbidden)) {
        throw "Adapter-wide correlation state remains: $forbidden"
    }
}
Write-Host "CORE-1375 static no-bypass inventory passed: approved_paths=7 production_matrix=true"

$arguments = @(
    "test",
    (Join-Path $repo "tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj"),
    "--filter",
    "FullyQualifiedName~Issue1375_ConversationJournalWiringTests",
    "--results-directory",
    $ResultsDirectory,
    "--logger",
    "trx;LogFileName=$trxName"
)

& dotnet @arguments 2>&1 | Tee-Object -FilePath $log
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE"
}

[xml]$doc = Get-Content -LiteralPath $trx
$ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
$ns.AddNamespace("t", "http://microsoft.com/schemas/VisualStudio/TeamTest/2010")
$required = @(
    "accepted_datee",
    "accepted_avatar",
    "prefetch_branch_clone",
    "speculative_branch_clone",
    "identical_prompt_retry",
    "avatar_emotional_director",
    "validation_rejected",
    "cancelled",
    "provider_failed",
    "director_branch_disposed",
    "semantic_link_context_isolation"
)
$counts = @{}
foreach ($scenario in $required) {
    $matches = $doc.SelectNodes("//t:UnitTestResult[contains(@testName, '$scenario')]", $ns)
    $counts[$scenario] = $matches.Count
    if ($matches.Count -lt 1) {
        throw "CORE-1375 verifier saw zero tests for: $scenario"
    }
}

$counters = $doc.SelectSingleNode("//t:Counters", $ns)
if ($null -eq $counters -or $counters.failed -ne "0") {
    throw "CORE-1375 verifier TRX is missing counters or contains failures"
}
$scenarioCounts = ($required | ForEach-Object { "$_=$($counts[$_])" }) -join " "
Write-Host "CORE-1375 scenario counts: $scenarioCounts"
Write-Host "CORE-1375 conversation verifier passed: total=$($counters.total) passed=$($counters.passed) failed=$($counters.failed) trx=$trx"
