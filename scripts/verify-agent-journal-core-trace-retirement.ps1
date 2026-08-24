$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repo

try {
    dotnet restore Pinder.Core.sln
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet build Pinder.Core.sln --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $retiredReferences = Get-ChildItem -Path "src" -Recurse -File |
        Select-String -Pattern "InMemoryPromptTraceService|IPromptTraceService"
    if ($retiredReferences) {
        $retiredReferences | ForEach-Object { Write-Error $_.ToString() }
        throw "Retired prompt trace service reference found under src/."
    }

    dotnet test tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj `
      --filter "FullyQualifiedName~AgentJournal|FullyQualifiedName~Issue1066|FullyQualifiedName~Issue1129|FullyQualifiedName~Issue1345|FullyQualifiedName~Issue1340|FullyQualifiedName~Issue1341|FullyQualifiedName~Issue1342|FullyQualifiedName~Issue1375" `
      --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    dotnet test tests/Pinder.Core.Tests/Pinder.Core.Tests.csproj `
      --filter "FullyQualifiedName~AgentJournal|FullyQualifiedName~Issue1125|FullyQualifiedName~Issue1158|FullyQualifiedName~EmotionStemSelectorTests" `
      --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git diff --check
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
finally {
    Pop-Location
}
