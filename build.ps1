param(
    [switch]$Package,
    [switch]$Install
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'DadsBepInExModManager.csproj'
$dll = Join-Path $root 'bin\Release\net48\DadsBepInExModManager.dll'
$packageRoot = Join-Path $root 'package'
$manifestPath = Join-Path $packageRoot 'manifest.json'

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { throw "DadsBepInExModManager build exited with code $LASTEXITCODE" }

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$assemblyVersion = [System.Reflection.AssemblyName]::GetAssemblyName($dll).Version
$assemblySemVer = "$($assemblyVersion.Major).$($assemblyVersion.Minor).$($assemblyVersion.Build)"
if ($assemblySemVer -ne $manifest.version_number) {
    throw "Assembly version $assemblySemVer does not match manifest version $($manifest.version_number)."
}

if ($Package) {
    $entries = [ordered]@{
        'DadsBepInExModManager.dll' = $dll
        'manifest.json' = $manifestPath
        'README.md' = (Join-Path $packageRoot 'README.md')
        'icon.png' = (Join-Path $packageRoot 'icon.png')
        'CHANGELOG.md' = (Join-Path $root 'CHANGELOG.md')
        'LICENSE' = (Join-Path $root 'LICENSE')
        'SOURCES.md' = (Join-Path $root 'SOURCES.md')
    }
    foreach ($source in $entries.Values) {
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required package file is missing: $source" }
    }

    Add-Type -AssemblyName System.Drawing
    $icon = [System.Drawing.Image]::FromFile($entries['icon.png'])
    try {
        if ($icon.Width -ne 256 -or $icon.Height -ne 256) { throw "icon.png must be 256x256; found $($icon.Width)x$($icon.Height)." }
        if ($icon.RawFormat.Guid -ne [System.Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'icon.png must be PNG.' }
    }
    finally { $icon.Dispose() }

    $dist = Join-Path $root 'dist'
    $archiveRoot = Join-Path $root 'Archive\package-builds'
    New-Item -ItemType Directory -Path $dist,$archiveRoot -Force | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    foreach ($artifact in Get-ChildItem -LiteralPath $dist -Force) {
        $archiveName = if ($artifact.PSIsContainer) { "$($artifact.Name)-$stamp" } else { "$($artifact.BaseName)-$stamp$($artifact.Extension)" }
        Move-Item -LiteralPath $artifact.FullName -Destination (Join-Path $archiveRoot $archiveName)
    }

    $folder = Join-Path $dist "DadsBepInExModManager-$($manifest.version_number)"
    $zipPath = Join-Path $dist "DadsBepInExModManager-$($manifest.version_number).zip"
    New-Item -ItemType Directory -Path $folder | Out-Null
    foreach ($entry in $entries.GetEnumerator()) {
        Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $folder $entry.Key)
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $entries.GetEnumerator()) {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value, $entry.Key, [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $zip.Dispose() }

    $zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $names = @($zip.Entries | ForEach-Object FullName)
        foreach ($required in $entries.Keys) {
            if ($required -notin $names) { throw "ZIP is missing root entry: $required" }
        }
        if ($names | Where-Object { $_ -match '[/\\]' }) { throw 'ZIP contains a nested directory.' }
        foreach ($forbidden in @('BepInEx.dll', '0Harmony.dll', 'assembly_valheim.dll')) {
            if ($forbidden -in $names) { throw "ZIP contains forbidden framework assembly: $forbidden" }
        }
    }
    finally { $zip.Dispose() }

    Write-Host "Thunderstore package: $zipPath"
    Write-Host "SHA256: $((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash)"
}

if ($Install) {
    $pluginRoot = 'C:\Users\DudeB\AppData\Roaming\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx\plugins'
    $installPath = Join-Path $pluginRoot 'Unknown-DadsBepInExModManager'
    $archiveRoot = Join-Path $root 'Archive\installed-builds'
    New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
    if (Test-Path -LiteralPath $installPath -PathType Container) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
        Move-Item -LiteralPath $installPath -Destination (Join-Path $archiveRoot "Unknown-DadsBepInExModManager-$stamp")
    }
    New-Item -ItemType Directory -Path $installPath | Out-Null
    if ($Package) {
        $builtPackage = Join-Path $root "dist\DadsBepInExModManager-$($manifest.version_number)"
        Get-ChildItem -LiteralPath $builtPackage -File | Copy-Item -Destination $installPath
    }
    else {
        Copy-Item -LiteralPath $dll -Destination (Join-Path $installPath 'DadsBepInExModManager.dll')
    }
    Write-Host "Installed: $installPath"
}
