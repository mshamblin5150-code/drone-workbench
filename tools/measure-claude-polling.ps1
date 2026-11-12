#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(5, 200)][int] $Iterations = 20,
    [int[]] $CandidateIntervalsSeconds = @(5, 10, 15, 30, 60),
    [string] $HostDll
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot

function Find-Claude {
    foreach ($directory in ($env:PATH -split [IO.Path]::PathSeparator)) {
        $candidate = $directory.Trim('"')
        if (-not [IO.Path]::IsPathFullyQualified($candidate)) { continue }
        $path = Join-Path $candidate 'claude.exe'
        if (Test-Path -LiteralPath $path -PathType Leaf) { return $path }
    }
    throw 'claude.exe was not found on PATH.'
}

function Invoke-Measured {
    param([string] $File, [string[]] $Arguments)
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $true
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($start)
    $process.StandardInput.Close()
    $errorTask = $process.StandardError.ReadToEndAsync()
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    $clock.Stop()
    $null = $errorTask.Result
    $result = [pscustomobject]@{
        WallMs = $clock.Elapsed.TotalMilliseconds
        CpuMs = $process.TotalProcessorTime.TotalMilliseconds
        ExitCode = $process.ExitCode
        Output = $output
    }
    $process.Dispose()
    $result
}

function Get-Stats([double[]] $Values) {
    $sorted = $Values | Sort-Object
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    [pscustomobject]@{
        median = [Math]::Round($median, 1)
        min = [Math]::Round($sorted[0], 1)
        max = [Math]::Round($sorted[-1], 1)
        runs = $sorted.Count
    }
}

$claude = Find-Claude
$dotnet = & (Join-Path $PSScriptRoot 'bootstrap-dotnet.ps1')
$hostDll = if ($HostDll) { $HostDll } else { Join-Path $projectRoot '.local/publish/Host/Workbench.Host.dll' }
if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    throw 'Run tools/verify-dotnet.ps1 first so the published Host exists.'
}

$version = [Collections.Generic.List[double]]::new()
$versionWall = [Collections.Generic.List[double]]::new()
$agents = [Collections.Generic.List[double]]::new()
$agentsWall = [Collections.Generic.List[double]]::new()
$observe = [Collections.Generic.List[double]]::new()
$observeWall = [Collections.Generic.List[double]]::new()
$startup = [Collections.Generic.List[double]]::new()
$errors = 0
$listedTotal = 0
$cpuSampler = [Diagnostics.PerformanceCounter]::new('Processor', '% Processor Time', '_Total')
$null = $cpuSampler.NextValue()
$machineLoad = [Collections.Generic.List[double]]::new()

for ($i = 0; $i -lt $Iterations; $i++) {
    $v = Invoke-Measured $claude @('--version')
    if ($v.ExitCode -ne 0 -or $v.Output -notmatch '^\d+\.\d+\.\d+ \(Claude Code\)') { $errors++ }
    $version.Add($v.CpuMs); $versionWall.Add($v.WallMs)

    $a = Invoke-Measured $claude @('agents', '--json', '--all')
    try {
        $parsed = @($a.Output | ConvertFrom-Json -NoEnumerate)
        if ($a.ExitCode -ne 0) { $errors++ } else { $listedTotal += @($parsed[0]).Count }
    } catch { $errors++ }
    $agents.Add($a.CpuMs); $agentsWall.Add($a.WallMs)

    if ($i % 2 -eq 0) {
        $s = Invoke-Measured $dotnet @($hostDll, '--not-a-mode')
        if ($s.ExitCode -ne 2) { $errors++ }
        $o = Invoke-Measured $dotnet @($hostDll, 'observe-claude', '--json')
    } else {
        $o = Invoke-Measured $dotnet @($hostDll, 'observe-claude', '--json')
        $s = Invoke-Measured $dotnet @($hostDll, '--not-a-mode')
        if ($s.ExitCode -ne 2) { $errors++ }
    }
    try {
        $capture = $o.Output | ConvertFrom-Json
        if ($capture.state -notin @('Observed', 'Partial')) { $errors++ }
    } catch { $errors++ }
    $observe.Add($o.CpuMs); $observeWall.Add($o.WallMs); $startup.Add($s.CpuMs)
    $machineLoad.Add($cpuSampler.NextValue())
}

$childCpu = for ($i = 0; $i -lt $Iterations; $i++) { $version[$i] + $agents[$i] }
$hostCpu = for ($i = 0; $i -lt $Iterations; $i++) { [Math]::Max(0, $observe[$i] - $startup[$i]) }
$pollCpu = for ($i = 0; $i -lt $Iterations; $i++) { $childCpu[$i] + $hostCpu[$i] }
$pollStats = Get-Stats $pollCpu
$report = [ordered]@{
    schema = 1
    measuredAt = [DateTimeOffset]::UtcNow.ToString('o')
    claudeVersion = (Invoke-Measured $claude @('--version')).Output.Trim()
    logicalProcessors = [Environment]::ProcessorCount
    machineCpuPercentDuringRun = Get-Stats $machineLoad
    iterations = $Iterations
    errors = $errors
    meanListedEntries = [Math]::Round($listedTotal / $Iterations, 1)
    versionCommand = [ordered]@{ cpuMs = Get-Stats $version; wallMs = Get-Stats $versionWall }
    agentViewCommand = [ordered]@{ cpuMs = Get-Stats $agents; wallMs = Get-Stats $agentsWall }
    hostInProcessCpuMs = Get-Stats $hostCpu
    oneShotObserveWallMs = Get-Stats $observeWall
    pollCpuMs = $pollStats
    oneCoreSharePercent = [ordered]@{}
    machineSharePercent = [ordered]@{}
    limits = @(
        'Child CPU covers each claude.exe process only, not any grandchild it starts.',
        'Host in-process CPU is the one-shot observe process minus a startup-only Host run, so it includes JIT and is an upper bound for a warm loop.',
        'Wall time includes process creation and waits on the child commands.'
    )
}
foreach ($interval in $CandidateIntervalsSeconds) {
    $report.oneCoreSharePercent["${interval}s"] = [Math]::Round(100 * $pollStats.median / ($interval * 1000), 2)
    $report.machineSharePercent["${interval}s"] = [Math]::Round(100 * $pollStats.median / ($interval * 1000 * [Environment]::ProcessorCount), 3)
}
$report | ConvertTo-Json -Depth 5
