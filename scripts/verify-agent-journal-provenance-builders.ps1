$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
Set-Location $repoRoot

$python = Get-Command python3 -ErrorAction SilentlyContinue
if ($null -eq $python) {
    $python = Get-Command python -ErrorAction SilentlyContinue
}
if ($null -eq $python) {
    throw "python3 or python is required for the CORE-1378 static verifier."
}

& $python.Source (Join-Path $scriptDir "verify-agent-journal-provenance-builders.py")
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$trxDir = Join-Path $repoRoot "TestResults"
if (-not (Test-Path $trxDir)) {
    New-Item -ItemType Directory -Path $trxDir | Out-Null
}

dotnet test (Join-Path $repoRoot "tests/Pinder.LlmAdapters.Tests/Pinder.LlmAdapters.Tests.csproj") `
    --filter "FullyQualifiedName~PromptBuilderPropagationTests" `
    --logger "trx;LogFileName=CORE-1378.trx" `
    --results-directory $trxDir
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$trxPath = Join-Path $trxDir "CORE-1378.trx"
if (-not (Test-Path $trxPath)) {
    throw "Expected TRX was not written: $trxPath"
}

[xml]$trx = Get-Content -LiteralPath $trxPath
$unitResults = @($trx.TestRun.Results.UnitTestResult)
$executed = @($unitResults | Where-Object { $_.testName -like "*PromptBuilderPropagationTests*" })
$passed = @($executed | Where-Object { $_.outcome -eq "Passed" })
if ($executed.Count -eq 0) {
    throw "CORE-1378 TRX result count was zero."
}
if ($passed.Count -ne $executed.Count) {
    throw "CORE-1378 TRX has failing PromptBuilderPropagationTests results."
}

$acNames = @{}
foreach ($result in $executed) {
    if ($result.testName -match "AC([1-5])_") {
        $acNames[$Matches[1]] = $true
    }
}
if ($acNames.Count -ne 5) {
    throw "CORE-1378 TRX AC group count $($acNames.Count) != 5."
}
Write-Host "CORE-1378 trx_result_count=$($executed.Count)"
Write-Host "CORE-1378 trx_ac_group_count=$($acNames.Count)"
