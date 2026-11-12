#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'The pinned SDK bootstrap supports Windows x64 only.'
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$pins = Get-Content -LiteralPath (Join-Path $projectRoot 'docs/toolchain/dotnet-release-pins.json') -Raw | ConvertFrom-Json
$archivePin = @($pins.sdk_windows_x64_files | Where-Object name -eq 'dotnet-sdk-win-x64.zip')
if ($archivePin.Count -ne 1 -or $pins.sdk_version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Expected one Windows x64 ZIP pin and an exact SDK version.'
}

$commonDirectory = & git -C $projectRoot rev-parse --path-format=absolute --git-common-dir
if ($LASTEXITCODE -ne 0) { throw 'Cannot locate the repository-local SDK cache.' }
$checkoutRoot = Split-Path -Parent ([IO.Path]::GetFullPath($commonDirectory.Trim()))
$cacheRoot = Join-Path $checkoutRoot '.local'
$sdkParent = Join-Path $cacheRoot 'dotnet'
$sdkDirectory = Join-Path $sdkParent $pins.sdk_version
$dotnetPath = Join-Path $sdkDirectory 'dotnet.exe'
if ((Test-Path -LiteralPath $sdkDirectory) -and -not (Test-Path -LiteralPath $dotnetPath -PathType Leaf)) {
    throw "Incomplete SDK directory retained for inspection: $sdkDirectory"
}

$downloadRoot = Join-Path $cacheRoot 'sdk-downloads'
[IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
[IO.Directory]::CreateDirectory($sdkParent) | Out-Null
$archive = Join-Path $downloadRoot "dotnet-sdk-$($pins.sdk_version)-win-x64.zip"
if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
    $download = "$archive.download-$([guid]::NewGuid().ToString('N'))"
    Invoke-WebRequest -Uri $archivePin[0].url -OutFile $download
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA512).Hash -ine $archivePin[0].hash) {
        throw "SDK download hash mismatch; retained $download"
    }
    Move-Item -LiteralPath $download -Destination $archive
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash -ine $archivePin[0].hash) {
    throw "SDK archive hash mismatch; retained $archive"
}
if (Test-Path -LiteralPath $dotnetPath -PathType Leaf) {
    return $dotnetPath
}

$stage = Join-Path $sdkParent ".install-$([guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($stage) | Out-Null
$stagePrefix = [IO.Path]::GetFullPath($stage) + [IO.Path]::DirectorySeparatorChar
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        $destination = [IO.Path]::GetFullPath((Join-Path $stage $entry.FullName))
        if (-not $destination.StartsWith($stagePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "SDK archive entry escapes staging: $($entry.FullName)"
        }
        if ((($entry.ExternalAttributes -shr 16) -band 0xF000) -eq 0xA000) {
            throw 'SDK archive symbolic links are unsupported.'
        }
    }
} finally {
    $zip.Dispose()
}
[IO.Compression.ZipFile]::ExtractToDirectory($archive, $stage)
if (-not (Test-Path -LiteralPath (Join-Path $stage 'dotnet.exe') -PathType Leaf)) {
    throw "SDK executable missing; retained staging directory $stage"
}
foreach ($target in @($stage, $sdkDirectory)) {
    if ((Split-Path -Parent ([IO.Path]::GetFullPath($target))) -ine [IO.Path]::GetFullPath($sdkParent)) {
        throw 'SDK publish target is outside the repository-local cache.'
    }
}
[IO.Directory]::Move($stage, $sdkDirectory)
@{
    sdk_version = $pins.sdk_version
    archive_sha512 = $archivePin[0].hash
    installed_utc = [DateTimeOffset]::UtcNow.ToString('O')
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $sdkDirectory 'workbench-install.json') -Encoding utf8NoBOM
$dotnetPath
