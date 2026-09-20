param(
    [Parameter(Mandatory = $true)]
    [string]$DestinationPath
)

$ErrorActionPreference = "Stop"

$archiveUrl = "https://freepats.zenvoid.org/Piano/UprightPianoKW/UprightPianoKW-small-SF2-20190703.7z"
$archiveFileName = "UprightPianoKW-small-SF2-20190703.7z"

$cacheRoot = if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
    Join-Path ([IO.Path]::GetTempPath()) "PianoPracticeTool\BuildCache"
}
else {
    Join-Path $env:LOCALAPPDATA "PianoPracticeTool\BuildCache"
}

if (Test-Path -LiteralPath $DestinationPath) {
    Write-Host "Default Upright Piano KW Small SoundFont is already present."
    exit 0
}

New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
$archivePath = Join-Path $cacheRoot $archiveFileName

if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Host "Downloading Upright Piano KW Small SoundFont archive from FreePats..."
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath -UseBasicParsing
}

$destinationDirectory = Split-Path -Parent $DestinationPath
New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null

$extractRoot = Join-Path $destinationDirectory ("UprightPianoExtract-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null

try {
    $tar = Get-Command tar.exe -ErrorAction Stop
    & $tar.Source -xf $archivePath -C $extractRoot
    if ($LASTEXITCODE -ne 0) {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
        throw "tar.exe failed to extract the Upright Piano KW Small archive (exit code $LASTEXITCODE). The cached archive was removed; rebuild to download it again."
    }

    $soundFonts = @(Get-ChildItem -LiteralPath $extractRoot -Recurse -File -Filter "*.sf2")
    if ($soundFonts.Count -ne 1) {
        throw "Expected exactly one SF2 file in the Upright Piano KW Small archive, but found $($soundFonts.Count)."
    }

    $soundFontPath = $soundFonts[0].FullName
    if ($soundFonts[0].Length -lt 12) {
        throw "The extracted Upright Piano KW Small SoundFont is unexpectedly small."
    }

    $stream = [IO.File]::OpenRead($soundFontPath)
    try {
        $header = New-Object byte[] 12
        if ($stream.Read($header, 0, $header.Length) -ne $header.Length) {
            throw "The extracted Upright Piano KW Small SoundFont header is incomplete."
        }

        $riff = [Text.Encoding]::ASCII.GetString($header, 0, 4)
        $soundFontType = [Text.Encoding]::ASCII.GetString($header, 8, 4)
        if ($riff -ne "RIFF" -or $soundFontType -ne "sfbk") {
            throw "The extracted file is not a valid SoundFont 2 RIFF container."
        }
    }
    finally {
        $stream.Dispose()
    }

    $tempDestination = "$DestinationPath.download"
    Remove-Item -LiteralPath $tempDestination -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath $soundFontPath -Destination $tempDestination -Force
    Move-Item -LiteralPath $tempDestination -Destination $DestinationPath -Force
}
finally {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $DestinationPath)) {
    throw "Default Upright Piano KW Small SoundFont was not created."
}

Write-Host "Default Upright Piano KW Small SoundFont is ready."
