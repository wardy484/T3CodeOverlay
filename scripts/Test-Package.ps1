param(
    [string]$PackagePath = "out/T3Linkpearl/latest.zip"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PackagePath)) {
    throw "Package not found: $PackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $PackagePath))

try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $required = @(
        "T3Linkpearl.dll",
        "T3Linkpearl.json",
        "icon.png",
        "t3code.png",
        "dead.png",
        "LICENSE.txt",
        "NOTICE.md",
        "THIRD_PARTY_NOTICES.md",
        "renderer/Browsingway.Renderer.exe",
        "renderer/Browsingway.Renderer.runtimeconfig.json"
    )

    foreach ($entry in $required) {
        if ($entry -notin $entries) {
            throw "Package is missing $entry"
        }
    }

    $manifestEntry = $archive.GetEntry("T3Linkpearl.json")
    $reader = [System.IO.StreamReader]::new($manifestEntry.Open())
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    if ($manifest.InternalName -ne "T3Linkpearl") {
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
