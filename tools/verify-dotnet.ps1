#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$localRoot = Join-Path $projectRoot '.local'
$environment = @{
    DOTNET_ROOT = ''
    DOTNET_ROOT_X64 = ''
    DOTNET_HOST_PATH = ''
    DOTNET_ROLL_FORWARD = ''
    DOTNET_ROLL_FORWARD_TO_PRERELEASE = ''
    MSBuildSDKsPath = ''
    MSBUILD_EXE_PATH = ''
    MSBUILDDISABLENODEREUSE = '1'
    DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    UseSharedCompilation = 'false'
    DOTNET_CLI_HOME = Join-Path $localRoot 'dotnet-cli'
    DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
    DOTNET_TEST_RUNNER = 'Microsoft.Testing.Platform'
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    DOTNET_NOLOGO = '1'
    DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    DOTNET_MULTILEVEL_LOOKUP = '0'
    NUGET_PACKAGES = Join-Path $localRoot 'nuget/packages'
    NUGET_HTTP_CACHE_PATH = Join-Path $localRoot 'nuget/http-cache'
}
$previous = @{}
foreach ($name in $environment.Keys) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

function Invoke-Dotnet {
    param([Parameter(Mandatory)][string[]] $Arguments)
    & $script:dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $projectRoot
try {
    foreach ($name in $environment.Keys) {
        if ([string]::IsNullOrEmpty($environment[$name])) {
            Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($name, $environment[$name], 'Process')
        }
    }
    $script:dotnetPath = & (Join-Path $PSScriptRoot 'bootstrap-dotnet.ps1')
    $env:DOTNET_ROOT = Split-Path -Parent $script:dotnetPath
    $env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
    $env:DOTNET_HOST_PATH = $script:dotnetPath
    $releasePins = Get-Content -LiteralPath 'docs/toolchain/dotnet-release-pins.json' -Raw | ConvertFrom-Json
    $globalJson = Get-Content -LiteralPath 'global.json' -Raw | ConvertFrom-Json
    $sdkVersion = & $script:dotnetPath --version
    if ($LASTEXITCODE -ne 0 -or $sdkVersion.Trim() -ne $releasePins.sdk_version -or
        $globalJson.sdk.version -ne $releasePins.sdk_version -or $globalJson.sdk.rollForward -ne 'disable' -or
        $globalJson.sdk.allowPrerelease -ne $false) {
        throw 'SDK pin, global.json or selected executable does not match the release manifest.'
    }

    $packagePins = Get-Content -LiteralPath 'docs/toolchain/nuget-direct-pins.json' -Raw | ConvertFrom-Json
    [xml] $central = Get-Content -LiteralPath 'Directory.Packages.props' -Raw
    $versions = @($central.SelectNodes('//PackageVersion'))
    if ($versions.Count -ne $packagePins.packages.Count) {
        throw 'Central package versions must match the complete direct package pin set.'
    }
    $artifactRoot = Join-Path $localRoot 'nuget/direct-artifacts'
    [IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
    foreach ($pin in $packagePins.packages) {
        $match = @($versions | Where-Object { $_.Include -ieq $pin.id })
        if ($match.Count -ne 1 -or $match[0].Version -ne $pin.version) {
            throw "Central package version differs from pin: $($pin.id)"
        }
        $artifact = Join-Path $artifactRoot "$($pin.id).$($pin.version).nupkg"
        if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
            $download = "$artifact.download-$([guid]::NewGuid().ToString('N'))"
            Invoke-WebRequest -Uri $pin.artifact_url -OutFile $download
            Move-Item -LiteralPath $download -Destination $artifact
        }
        $actualHash = [Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $artifact -Algorithm SHA512).Hash))
        if ($actualHash -cne $pin.nupkg_sha512_base64) {
            throw "Direct package SHA-512 mismatch: $($pin.id)"
        }
    }

    $solution = 'drone-workbench.slnx'
    Invoke-Dotnet -Arguments @('restore', $solution, '--locked-mode')
    foreach ($pin in $packagePins.packages) {
        $restoredArtifact = Join-Path $env:NUGET_PACKAGES "$($pin.id)/$($pin.version)/$($pin.id).$($pin.version).nupkg"
        if (-not (Test-Path -LiteralPath $restoredArtifact -PathType Leaf)) {
            throw "Pinned direct package was not restored: $($pin.id)"
        }
        $restoredHash = [Convert]::ToBase64String([Convert]::FromHexString((Get-FileHash -LiteralPath $restoredArtifact -Algorithm SHA512).Hash))
        if ($restoredHash -cne $pin.nupkg_sha512_base64) {
            throw "Restored direct package SHA-512 mismatch: $($pin.id)"
        }
    }
    Invoke-Dotnet -Arguments @('build', $solution, '-c', 'Release', '--no-restore', '-p:UseSharedCompilation=false')
    Invoke-Dotnet -Arguments @('format', $solution, '--verify-no-changes', '--no-restore', '--verbosity', 'minimal')
    Invoke-Dotnet -Arguments @('test', $solution, '-c', 'Release', '--no-build', '--no-restore')
    Invoke-Dotnet -Arguments @('vstest', 'tests/Workbench.Cli.Tests/bin/Release/net11.0/Workbench.Cli.Tests.dll')
    foreach ($component in @('Host', 'Cli', 'Supervisor')) {
        Invoke-Dotnet -Arguments @('publish', "src/Workbench.$component/Workbench.$component.csproj", '-c', 'Release',
            '-r', 'win-x64', '--self-contained', 'false', '--no-restore', '-p:UseAppHost=false', '-p:UseSharedCompilation=false',
            '-o', (Join-Path $localRoot "publish/$component"))
    }
    Write-Host "Verified SDK $sdkVersion, $($packagePins.packages.Count) direct package hashes, locked restore, build, format, tests and win-x64 publish."
} finally {
    foreach ($name in $environment.Keys) {
        if ($null -eq $previous[$name]) {
            Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue
        } else {
            [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
        }
    }
    Pop-Location
}
