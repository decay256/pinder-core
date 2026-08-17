#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/tmp/eigentakt-dotnet-home-core-1387}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-/tmp/eigentakt-nuget-core-1387}"

cd "$repo_root"

dotnet restore Pinder.Core.sln

dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj \
  --filter "FullyQualifiedName~AgentJournalRecorderLifecycleTests|FullyQualifiedName~GameRunOneShotJournalWiringTests|FullyQualifiedName~Issue1375_ConversationJournalWiringTests|FullyQualifiedName~PiAgentJournalProjectionSinkTests|FullyQualifiedName~AgentJournalPiCodecRoundTripTests|FullyQualifiedName~AgentJournals.Materialization|FullyQualifiedName~Issue1345_PrivatePhaseObservabilityTests|FullyQualifiedName~Issue1342_1343_EmotionalDirectorPerformanceTests|FullyQualifiedName~Issue54_PiConversationSessionTests" \
  --no-restore

dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj \
  --filter "FullyQualifiedName~AgentJournalContractValidationTests|FullyQualifiedName~AgentJournalUsageCaptureTests|FullyQualifiedName~Issue1158_DramaticArcGenerationTests|FullyQualifiedName~Issue1223_SetupGeneratorObservableFailureTests|FullyQualifiedName~OutfitDescriberPromptTests|FullyQualifiedName~Issue1254_BackstoryGenerationTests|FullyQualifiedName~Issue843_PromptCatalogPhase1Tests" \
  --no-restore

git diff --check
