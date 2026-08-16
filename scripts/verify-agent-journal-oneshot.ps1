$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$script = Join-Path $repo "scripts/verify-agent-journal-oneshot.sh"
& bash $script
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
