#!/usr/bin/env bash
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo"

dotnet restore Pinder.Core.sln
dotnet build Pinder.Core.sln --no-restore

if rg -n 'InMemoryPromptTraceService|IPromptTraceService' src; then
  echo "Retired prompt trace service reference found under src/." >&2
  exit 1
fi

echo "Running retirement and journal tests..."
dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj \
  --filter "FullyQualifiedName~AgentJournal|FullyQualifiedName~Issue1066|FullyQualifiedName~Issue1129|FullyQualifiedName~Issue1345|FullyQualifiedName~Issue1340|FullyQualifiedName~Issue1341|FullyQualifiedName~Issue1342|FullyQualifiedName~Issue1375" \
  --no-restore

dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj \
  --filter "FullyQualifiedName~AgentJournal|FullyQualifiedName~Issue1125|FullyQualifiedName~Issue1158|FullyQualifiedName~EmotionStemSelectorTests" \
  --no-restore

git diff --check
echo "Core prompt trace retirement verification passed."
