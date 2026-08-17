$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = "/tmp/eigentakt-dotnet-home-core-1387" }
if (-not $env:NUGET_PACKAGES) { $env:NUGET_PACKAGES = "/tmp/eigentakt-nuget-core-1387" }

Push-Location $repoRoot
try {
    dotnet restore Pinder.Core.sln
    dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj `
        --filter "FullyQualifiedName~AgentJournalRecorderLifecycleTests|FullyQualifiedName~GameRunOneShotJournalWiringTests|FullyQualifiedName~Issue1375_ConversationJournalWiringTests|FullyQualifiedName~PiAgentJournalProjectionSinkTests|FullyQualifiedName~AgentJournalPiCodecRoundTripTests|FullyQualifiedName~AgentJournals.Materialization|FullyQualifiedName~Issue1345_PrivatePhaseObservabilityTests|FullyQualifiedName~Issue1342_1343_EmotionalDirectorPerformanceTests|FullyQualifiedName~Issue54_PiConversationSessionTests" `
        --no-restore
    dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj `
        --filter "FullyQualifiedName~AgentJournalContractValidationTests|FullyQualifiedName~AgentJournalUsageCaptureTests|FullyQualifiedName~Issue1158_DramaticArcGenerationTests|FullyQualifiedName~Issue1223_SetupGeneratorObservableFailureTests|FullyQualifiedName~OutfitDescriberPromptTests|FullyQualifiedName~Issue1254_BackstoryGenerationTests|FullyQualifiedName~Issue843_PromptCatalogPhase1Tests" `
        --no-restore
    git diff --check
}
finally {
    Pop-Location
}
