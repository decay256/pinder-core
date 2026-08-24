$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
# The shell verifier owns the shared one-shot assertions for both platforms.
$script = Join-Path $repo "scripts/verify-agent-journal-oneshot.sh"
& bash $script
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
