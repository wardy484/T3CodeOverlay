param(
    [string]$ManifestPath = "out/T3Linkpearl/T3Linkpearl.json",
    [string]$OutputDirectory = "site"
)

$ErrorActionPreference = "Stop"
$repository = "https://github.com/wardy484/T3Linkpearl"
$download = "$repository/releases/latest/download/latest.zip"

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$manifest | Add-Member -NotePropertyName DownloadLinkInstall -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName DownloadLinkUpdate -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName DownloadLinkTesting -NotePropertyValue $download -Force
$manifest | Add-Member -NotePropertyName IsTestingExclusive -NotePropertyValue $true -Force
$manifest | Add-Member -NotePropertyName RepoUrl -NotePropertyValue $repository -Force
$manifest | Add-Member -NotePropertyName IconUrl -NotePropertyValue "https://raw.githubusercontent.com/wardy484/T3Linkpearl/main/Browsingway/images/icon.png" -Force

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
ConvertTo-Json @($manifest) -Depth 20 | Set-Content "$OutputDirectory/repo.json" -Encoding utf8NoBOM
Set-Content "$OutputDirectory/index.html" '<!doctype html><meta charset="utf-8"><title>T3 Linkpearl</title><p>Add <code>https://wardy484.github.io/T3Linkpearl/repo.json</code> to Dalamud custom plugin repositories.</p>' -Encoding utf8NoBOM

Write-Host "Generated $OutputDirectory/repo.json for version $($manifest.AssemblyVersion)."
