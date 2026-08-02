[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$provenancePath = Join-Path $repoRoot "packages/pi-csharp/provenance.json"
$provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json

if ($provenance.schemaVersion -ne 1) {
    throw "Unsupported Pi package provenance schema: $($provenance.schemaVersion)"
}

if ($provenance.repository -ne "https://github.com/decay256/pi-csharp") {
    throw "Pi package provenance points at an unexpected repository."
}

$expectedNames = @($provenance.packages.PSObject.Properties.Name | Sort-Object)
$actualNames = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "packages/pi-csharp") -Filter "*.nupkg" |
    Select-Object -ExpandProperty Name | Sort-Object)

if (($expectedNames -join "`n") -ne ($actualNames -join "`n")) {
    throw "Vendored Pi package set does not match provenance."
}

foreach ($property in $provenance.packages.PSObject.Properties) {
    $packagePath = Join-Path $repoRoot "packages/pi-csharp/$($property.Name)"
    $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    if ($actualHash -ne $property.Value) {
        throw "Hash mismatch for $($property.Name)."
    }
}

$adapterProject = Get-Content -LiteralPath (Join-Path $repoRoot "src/Pinder.LlmAdapters/Pinder.LlmAdapters.csproj") -Raw
$nugetConfig = Get-Content -LiteralPath (Join-Path $repoRoot "NuGet.Config") -Raw
if ($nugetConfig -notmatch '<packageSource key="pi-csharp-local">\s*<package pattern="Pi\.\*"') {
    throw "NuGet.Config must map Pi.* exclusively to the vendored package source."
}

foreach ($packageId in @("Pi.AI", "Pi.Agent.Core")) {
    $escapedId = [regex]::Escape($packageId)
    $escapedVersion = [regex]::Escape([string]$provenance.version)
    if ($adapterProject -notmatch "PackageReference\s+Include=`"$escapedId`"\s+Version=`"$escapedVersion`"") {
        throw "Pinder.LlmAdapters does not pin $packageId to provenance version $($provenance.version)."
    }
}

Write-Host "Pi package verification passed: $($expectedNames.Count) artifacts from $($provenance.commit)."
