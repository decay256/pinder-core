$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$script = Join-Path $repo "scripts/verify-issue-1371.sh"
& bash $script
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
