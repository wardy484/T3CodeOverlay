param(
    [string]$PackagePath = "out/T3CodeOverlay/latest.zip"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PackagePath)) {
    throw "Package not found: $PackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $PackagePath))

try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    $required = @(
        "T3CodeOverlay.dll",
        "T3CodeOverlay.json",
        "icon.png",
        "t3code.png",
        "dead.png",
        "renderer/Browsingway.Renderer.exe",
        "renderer/Browsingway.Renderer.runtimeconfig.json"
    )

    foreach ($entry in $required) {
        if ($entry -notin $entries) {
            throw "Package is missing $entry"
        }
    }

    $manifestEntry = $archive.GetEntry("T3CodeOverlay.json")
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    if ($manifest.InternalName -ne "T3CodeOverlay") {
        throw "Unexpected InternalName: $($manifest.InternalName)"
    }

    if ($manifest.DalamudApiLevel -ne 15) {
        throw "Unexpected Dalamud API level: $($manifest.DalamudApiLevel)"
    }

    Write-Host "Validated $PackagePath ($($entries.Count) files, version $($manifest.AssemblyVersion))."
}
finally {
    $archive.Dispose()
}
