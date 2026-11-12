#Requires -Version 7.0
[CmdletBinding()]
param([switch] $Live)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot

function Get-SettingsSnapshot {
    param([string] $Root)
    $snapshot = @{}
    foreach ($name in @('settings.json', 'settings.local.json')) {
        $path = Join-Path $Root $name
        $snapshot[$name] = if (Test-Path -LiteralPath $path -PathType Leaf) {
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        } else { $null }
    }
    return $snapshot
}

function Invoke-OwnedObservation {
    param([string] $Dotnet, [string] $Assembly, [bool] $UseLiveSource)
    $start = [Diagnostics.ProcessStartInfo]::new($Dotnet)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.WorkingDirectory = $projectRoot
    $start.ArgumentList.Add($Assembly)
    $start.ArgumentList.Add('observe-claude')
    $start.ArgumentList.Add('--json')
    $start.Environment['DOTNET_ROOT'] = Split-Path -Parent $Dotnet
    $start.Environment['DOTNET_ROOT_X64'] = Split-Path -Parent $Dotnet
    $start.Environment['DOTNET_GENERATE_ASPNET_CERTIFICATE'] = 'false'
    foreach ($name in @('DOTNET_ROLL_FORWARD', 'DOTNET_ROLL_FORWARD_TO_PRERELEASE')) {
        $start.Environment.Remove($name) | Out-Null
    }
    if (-not $UseLiveSource) {
        $start.Environment['PATH'] = ''
        $start.Environment.Remove('CLAUDE_CONFIG_DIR') | Out-Null
    } elseif ([string]::IsNullOrEmpty($start.Environment['CLAUDE_CONFIG_DIR'])) {
        $start.Environment.Remove('CLAUDE_CONFIG_DIR') | Out-Null
    }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $output = [IO.MemoryStream]::new()
    $started = $false
    try {
        if (-not $process.Start()) { throw 'OwnedHostStartFailed' }
        $started = $true
        $process.StandardInput.Close()
        $stdoutBuffer = [byte[]]::new(8192)
        $stderrBuffer = [byte[]]::new(8192)
        $stdoutTask = $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
        $stderrTask = $process.StandardError.BaseStream.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
        $stderrBytes = 0
        $timer = [Diagnostics.Stopwatch]::StartNew()
        while ($null -ne $stdoutTask -or $null -ne $stderrTask -or -not $process.HasExited) {
            if ($timer.Elapsed -gt [TimeSpan]::FromSeconds(30)) { throw 'OwnedHostTimeout' }
            if ($null -ne $stdoutTask -and $stdoutTask.IsCompleted) {
                $count = $stdoutTask.GetAwaiter().GetResult()
                if ($count -eq 0) { $stdoutTask = $null } else {
                    if ($output.Length + $count -gt 1048576) { throw 'OwnedHostOutputTooLarge' }
                    $output.Write($stdoutBuffer, 0, $count)
                    $stdoutTask = $process.StandardOutput.BaseStream.ReadAsync($stdoutBuffer, 0, $stdoutBuffer.Length)
                }
            }
            if ($null -ne $stderrTask -and $stderrTask.IsCompleted) {
                $count = $stderrTask.GetAwaiter().GetResult()
                if ($count -eq 0) { $stderrTask = $null } else {
                    $stderrBytes += $count
                    if ($stderrBytes -gt 1048576) { throw 'OwnedHostOutputTooLarge' }
                    $stderrTask = $process.StandardError.BaseStream.ReadAsync($stderrBuffer, 0, $stderrBuffer.Length)
                }
            }
            $pending = @()
            if ($null -ne $stdoutTask) { $pending += $stdoutTask }
            if ($null -ne $stderrTask) { $pending += $stderrTask }
            if ($pending.Count -gt 0) {
                [Threading.Tasks.Task]::WaitAny([Threading.Tasks.Task[]] $pending, 25) | Out-Null
            } elseif (-not $process.HasExited) {
                $process.WaitForExit(25) | Out-Null
            }
        }
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            StandardOutput = [Text.Encoding]::UTF8.GetString($output.ToArray())
            StandardErrorBytes = $stderrBytes
        }
    } finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill()
            if (-not $process.WaitForExit(5000)) { throw 'OwnedHostDidNotExit' }
        }
        $output.Dispose()
        $process.Dispose()
    }
}

try {
    $dotnet = & (Join-Path $PSScriptRoot 'bootstrap-dotnet.ps1')
    $assembly = Join-Path $projectRoot 'src/Workbench.Host/bin/Release/net11.0/Workbench.Host.dll'
    if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw 'HostBuildMissing' }

    $settingsRoot = $null
    $settingsBefore = $null
    if ($Live) {
        $configuredRoot = [Environment]::GetEnvironmentVariable('CLAUDE_CONFIG_DIR', 'Process')
        $settingsRoot = if ([string]::IsNullOrEmpty($configuredRoot)) {
            Join-Path ([Environment]::GetFolderPath('UserProfile')) '.claude'
        } else { $configuredRoot }
        if (-not [IO.Path]::IsPathFullyQualified($settingsRoot)) { throw 'ConfigurationRootMustBeAbsolute' }
        if (-not (Test-Path -LiteralPath $settingsRoot -PathType Container)) { throw 'ConfigurationRootMissing' }
        $settingsBefore = Get-SettingsSnapshot -Root $settingsRoot
    }

    $settingsUnchanged = $true
    try {
        $result = Invoke-OwnedObservation -Dotnet $dotnet -Assembly $assembly -UseLiveSource $Live.IsPresent
    } finally {
        if ($Live) {
            $settingsAfter = Get-SettingsSnapshot -Root $settingsRoot
            foreach ($name in $settingsBefore.Keys) {
                if ($settingsBefore[$name] -cne $settingsAfter[$name]) { $settingsUnchanged = $false }
            }
            if (-not $settingsUnchanged) { throw 'SettingsChangedDuringObservation' }
        }
    }
    try { $observation = $result.StandardOutput | ConvertFrom-Json -AsHashtable } catch { throw 'InvalidObservationJson' }
    if ($null -eq $observation -or $observation -isnot [Collections.IDictionary]) { throw 'InvalidObservationShape' }
    $states = @('Observed', 'Partial', 'Disconnected', 'Unavailable', 'UnsupportedVersion')
    if ($observation.state -notin $states) { throw 'InvalidObservationState' }
    if ($observation.controlsAvailable -isnot [bool] -or $observation.controlsAvailable -or
        $observation.supervisorHealth -ne 'Unknown') { throw 'InvalidReadOnlyContract' }
    if ($observation.configurationScope -notin @('DefaultRoot', 'ConfiguredRoot')) { throw 'InvalidObservationScope' }
    if ($null -ne $observation.sourceVersion -and $observation.sourceVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw 'InvalidObservationVersion'
    }
    $sourceObserved = $observation.state -in @('Observed', 'Partial')
    if ($sourceObserved -ne ($result.ExitCode -eq 0)) { throw 'InvalidObservationExit' }
    if ($sourceObserved -ne ($null -ne $observation.sessions)) { throw 'InvalidObservationSessions' }
    if ($sourceObserved -ne ($null -ne $observation.observedAt)) { throw 'InvalidObservationTime' }
    $parsedObservationTime = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($observation.attemptedAt, [ref] $parsedObservationTime) -or
        ($sourceObserved -and -not [DateTimeOffset]::TryParse($observation.observedAt, [ref] $parsedObservationTime))) {
        throw 'InvalidObservationTime'
    }
    if (-not $Live -and ($observation.state -ne 'Unavailable' -or $result.ExitCode -ne 1)) { throw 'SyntheticGuardFailed' }
    $counts = [ordered]@{ Desktop = 0; InteractiveCli = 0; Background = 0; Unknown = 0 }
    foreach ($session in $observation.sessions) {
        if ($session.entrypoint -notin $counts.Keys -or
            $session.state -notin @('Busy', 'Waiting', 'Idle', 'Starting', 'Stopped', 'Unknown') -or
            $session.processMatched -isnot [bool]) { throw 'InvalidObservationSession' }
        $counts[$session.entrypoint]++
    }
    $coverage = $observation.processCoverage
    if ($coverage.complete -isnot [bool] -or $coverage.knownUnmatchedCount -isnot [long] -or
        $coverage.knownUnmatchedCount -lt 0 -or
        ($null -ne $coverage.unmatchedCount -and ($coverage.unmatchedCount -isnot [long] -or $coverage.unmatchedCount -lt 0)) -or
        (-not $coverage.complete -and $null -ne $coverage.unmatchedCount) -or
        ($coverage.complete -and $null -eq $coverage.unmatchedCount) -or
        ($null -ne $coverage.unmatchedCount -and $coverage.knownUnmatchedCount -gt $coverage.unmatchedCount)) {
        throw 'InvalidProcessCoverage'
    }
    $receipt = [ordered]@{
        schemaVersion = 1
        recordedAt = [DateTimeOffset]::UtcNow.ToString('O')
        mode = $(if ($Live) { 'LiveReadOnly' } else { 'SyntheticMissingExecutable' })
        state = $observation.state
        sourceVersion = $observation.sourceVersion
        sourceObserved = $sourceObserved
        sessionsAvailable = ($null -ne $observation.sessions)
        sessionCounts = $counts
        processInventoryComplete = $coverage.complete
        knownUnmatchedProcessCount = $coverage.knownUnmatchedCount
        unmatchedProcessCount = $coverage.unmatchedCount
        supervisorHealth = 'Unknown'
        controlsAvailable = $false
        settingsRead = $Live.IsPresent
        settingsUnchanged = $settingsUnchanged
        parentEnvironmentMutated = $false
        sourceLifecycleCommandsSent = 0
        modelTurnsStarted = 0
        childExitCode = $result.ExitCode
        childStandardErrorEmpty = ($result.StandardErrorBytes -eq 0)
    }
    $verificationRoot = Join-Path $projectRoot '.local/verification'
    [IO.Directory]::CreateDirectory($verificationRoot) | Out-Null
    $receiptName = if ($Live) { 'claude-observation-live.json' } else { 'claude-observation-synthetic.json' }
    $json = $receipt | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText((Join-Path $verificationRoot $receiptName), $json + [Environment]::NewLine)
    Write-Output $json
    if ($Live -and -not $sourceObserved) { throw 'LiveObservationUnavailable' }
} catch {
    $knownCodes = @('OwnedHostStartFailed', 'OwnedHostTimeout', 'OwnedHostOutputTooLarge', 'OwnedHostDidNotExit',
        'HostBuildMissing', 'ConfigurationRootMustBeAbsolute', 'ConfigurationRootMissing', 'InvalidObservationJson',
        'InvalidObservationShape', 'InvalidObservationState', 'InvalidReadOnlyContract', 'InvalidObservationScope',
        'InvalidObservationVersion', 'InvalidObservationExit', 'InvalidObservationSessions', 'InvalidObservationTime', 'SyntheticGuardFailed',
        'InvalidObservationSession', 'InvalidProcessCoverage', 'SettingsChangedDuringObservation', 'LiveObservationUnavailable')
    if ($_.Exception.Message -in $knownCodes) { throw $_.Exception.Message }
    throw 'ObservationVerificationFailed'
}
