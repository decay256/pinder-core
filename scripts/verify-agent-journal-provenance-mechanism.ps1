$ErrorActionPreference = "Stop"

$script = Join-Path $PSScriptRoot "verify-agent-journal-provenance-mechanism.sh"
if (!(Test-Path $script)) {
    throw "Missing verifier script: $script"
}

$bash = Get-Command bash -ErrorAction SilentlyContinue
if ($null -eq $bash) {
    throw "bash is required to run $script on this host."
}

& $bash.Source $script @args
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    exit $exitCode
}

exit 0
