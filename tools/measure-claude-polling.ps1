#Requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(5, 200)][int] $Iterations = 20,
    [int[]] $CandidateIntervalsSeconds = @(5, 10, 15, 30, 60),
    [string] $HostDll,
    [ValidateRange(1, 120)][int] $CommandTimeoutSeconds = 10,
    [ValidateRange(1, 300)][int] $HostTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$projectRoot = Split-Path -Parent $PSScriptRoot
$outputLimitBytes = 1024 * 1024

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public static class BoundedRun
{
    public sealed class Result
    {
        public string Failure = "None";
        public int ExitCode = -1;
        public double WallMs;
        public double CpuMs;
        public byte[] Output = new byte[0];
    }

    public static Result Run(string file, string[] arguments, string workingDirectory,
        IDictionary<string, string> setVariables, string[] removeVariables, int timeoutMs, int outputLimit)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        foreach (var name in removeVariables) { start.Environment.Remove(name); }
        foreach (var pair in setVariables) { start.Environment[pair.Key] = pair.Value; }

        var result = new Result();
        var clock = Stopwatch.StartNew();
        Process process = null;
        try
        {
            process = Process.Start(start);
            if (process == null) { result.Failure = "StartFailed"; return result; }
            process.StandardInput.Close();
            using var overLimit = new CancellationTokenSource();
            var consumed = 0;
            async Task<byte[]> ReadAsync(Stream stream, bool keep)
            {
                using var kept = new MemoryStream();
                var buffer = new byte[4096];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, overLimit.Token)) > 0)
                {
                    if (Interlocked.Add(ref consumed, count) > outputLimit) { overLimit.Cancel(); break; }
                    if (keep) { kept.Write(buffer, 0, count); }
                }
                return kept.ToArray();
            }

            var output = ReadAsync(process.StandardOutput.BaseStream, true);
            var errors = ReadAsync(process.StandardError.BaseStream, false);
            var finished = false;
            try
            {
                finished = Task.WhenAll(output, errors, process.WaitForExitAsync()).Wait(timeoutMs, overLimit.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException)
            {
                finished = process.HasExited;
            }

            if (overLimit.IsCancellationRequested || !finished)
            {
                KillTree(process);
                try { Task.WhenAll(output, errors).Wait(5000); } catch (AggregateException) { }
                result.Failure = overLimit.IsCancellationRequested ? "OutputTooLarge" : "Timeout";
                return result;
            }

            result.ExitCode = process.ExitCode;
            result.Output = output.IsCompletedSuccessfully ? output.Result : new byte[0];
            return result;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is InvalidOperationException || error is IOException)
        {
            result.Failure = "StartFailed";
            return result;
        }
        finally
        {
            if (process != null)
            {
                KillTree(process);
                clock.Stop();
                result.WallMs = clock.Elapsed.TotalMilliseconds;
                try { result.CpuMs = process.TotalProcessorTime.TotalMilliseconds; }
                catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception || error is NotSupportedException) { }
                process.Dispose();
            }
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception ||
            error is AggregateException || error is NotSupportedException)
        {
        }
    }
}
'@

function Find-Claude {
    foreach ($directory in (($env:PATH ?? '') -split [IO.Path]::PathSeparator)) {
        $candidate = $directory.Trim('"')
        if (-not [IO.Path]::IsPathFullyQualified($candidate)) { continue }
        try {
            $path = [IO.Path]::Combine($candidate, 'claude.exe')
            if ([IO.File]::Exists($path)) { return $path }
        } catch [ArgumentException] {
        }
    }
    throw 'claude.exe was not found in a fully qualified PATH directory.'
}

function Resolve-ClaudeRoot {
    $configured = [Environment]::GetEnvironmentVariable('CLAUDE_CONFIG_DIR')
    $scope = if ([string]::IsNullOrEmpty($configured)) { 'DefaultRoot' } else { 'ConfiguredRoot' }
    $root = if ($scope -eq 'DefaultRoot') {
        Join-Path ([Environment]::GetFolderPath('UserProfile')) '.claude'
    } else {
        $configured
    }
    if (-not [IO.Path]::IsPathFullyQualified($root) -or -not (Test-Path -LiteralPath $root -PathType Container) -or
        -not (Get-ChildItem -LiteralPath $root -Force | Select-Object -First 1)) {
        throw "The $scope configuration root is not an absolute, existing, non-empty directory. Measurement refused."
    }
    [pscustomobject]@{ Scope = $scope; Path = [IO.Path]::GetFullPath($root) }
}

function Invoke-Claude([string[]] $Arguments) {
    $set = [Collections.Generic.Dictionary[string, string]]::new()
    $remove = @()
    if ($root.Scope -eq 'DefaultRoot') { $remove = @('CLAUDE_CONFIG_DIR') } else { $set['CLAUDE_CONFIG_DIR'] = $root.Path }
    [BoundedRun]::Run($claude, $Arguments, $root.Path, $set, $remove, $CommandTimeoutSeconds * 1000, $outputLimitBytes)
}

function Invoke-Host([string[]] $Arguments) {
    $all = @($hostDll) + $Arguments
    [BoundedRun]::Run($dotnet, $all, $projectRoot, [Collections.Generic.Dictionary[string, string]]::new(), @(),
        $HostTimeoutSeconds * 1000, $outputLimitBytes)
}

function Get-Version([BoundedRun+Result] $Run) {
    if ($Run.Failure -ne 'None' -or $Run.ExitCode -ne 0 -or $Run.Output.Length -gt 128) { return $null }
    $match = [regex]::Match([Text.Encoding]::UTF8.GetString($Run.Output).Trim(), '^([0-9]{1,4}\.[0-9]{1,4}\.[0-9]{1,4}) \(Claude Code\)$')
    if ($match.Success) { $match.Groups[1].Value } else { $null }
}

function Get-Stats([double[]] $Values) {
    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    $median = if ($sorted.Count % 2) { $sorted[$middle] } else { ($sorted[$middle - 1] + $sorted[$middle]) / 2 }
    [pscustomobject]@{ median = [Math]::Round($median, 1); min = [Math]::Round($sorted[0], 1); max = [Math]::Round($sorted[-1], 1); runs = $sorted.Count }
}

$claude = Find-Claude
$root = Resolve-ClaudeRoot
$dotnet = & (Join-Path $PSScriptRoot 'bootstrap-dotnet.ps1')
$hostDll = if ($HostDll) { $HostDll } else { Join-Path $projectRoot '.local/publish/Host/Workbench.Host.dll' }
if (-not (Test-Path -LiteralPath $hostDll -PathType Leaf)) {
    throw 'Run tools/verify-dotnet.ps1 first so the published Host exists.'
}

$samples = [Collections.Generic.List[object]]::new()
$failures = [ordered]@{}
$versions = [Collections.Generic.HashSet[string]]::new()
$cpuSampler = [Diagnostics.PerformanceCounter]::new('Processor', '% Processor Time', '_Total')
$null = $cpuSampler.NextValue()
$machineLoad = [Collections.Generic.List[double]]::new()

function Add-Failure([string] $Name) { $failures[$Name] = 1 + ($failures[$Name] ?? 0) }

for ($i = 0; $i -lt $Iterations; $i++) {
    $v = Invoke-Claude @('--version')
    $version = Get-Version $v
    if ($null -eq $version) { Add-Failure "version:$($v.Failure)"; continue }
    $null = $versions.Add($version)

    $a = Invoke-Claude @('agents', '--json', '--all')
    $listed = $null
    if ($a.Failure -eq 'None' -and $a.ExitCode -eq 0) {
        try {
            $parsed = [Text.Encoding]::UTF8.GetString($a.Output) | ConvertFrom-Json -NoEnumerate
            if ($parsed -is [array]) { $listed = $parsed.Count }
        } catch { $listed = $null }
    }
    if ($null -eq $listed) { Add-Failure "agents:$($a.Failure)"; continue }

    $order = if ($i % 2 -eq 0) { @('startup', 'observe') } else { @('observe', 'startup') }
    $runs = @{}
    foreach ($step in $order) {
        $runs[$step] = if ($step -eq 'startup') { Invoke-Host @('--not-a-mode') } else { Invoke-Host @('observe-claude', '--json') }
    }
    if ($runs.startup.Failure -ne 'None' -or $runs.startup.ExitCode -ne 2) { Add-Failure "hostStartup:$($runs.startup.Failure)"; continue }
    $state = $null
    if ($runs.observe.Failure -eq 'None') {
        try { $state = ([Text.Encoding]::UTF8.GetString($runs.observe.Output) | ConvertFrom-Json).state } catch { $state = $null }
    }
    if ($state -notin @('Observed', 'Partial')) { Add-Failure "observe:$($runs.observe.Failure)"; continue }

    $samples.Add([pscustomobject]@{
        VersionCpu = $v.CpuMs; VersionWall = $v.WallMs
        AgentsCpu = $a.CpuMs; AgentsWall = $a.WallMs; Listed = $listed
        HostCpu = [Math]::Max(0, $runs.observe.CpuMs - $runs.startup.CpuMs); ObserveWall = $runs.observe.WallMs
    })
    $machineLoad.Add($cpuSampler.NextValue())
}

$pollCpu = @($samples | ForEach-Object { $_.VersionCpu + $_.AgentsCpu + $_.HostCpu })
$pollStats = Get-Stats $pollCpu
$report = [ordered]@{
    schema = 2
    measuredAt = [DateTimeOffset]::UtcNow.ToString('o')
    claudeVersions = @($versions)
    configurationScope = $root.Scope
    logicalProcessors = [Environment]::ProcessorCount
    iterations = $Iterations
    completedSamples = $samples.Count
    failures = $failures
    commandTimeoutSeconds = $CommandTimeoutSeconds
    outputLimitBytes = $outputLimitBytes
    machineCpuPercentDuringRun = Get-Stats $machineLoad
    meanListedEntries = if ($samples.Count) { [Math]::Round(($samples | Measure-Object Listed -Average).Average, 1) } else { $null }
    versionCommand = [ordered]@{ cpuMs = Get-Stats @($samples | ForEach-Object { $_.VersionCpu }); wallMs = Get-Stats @($samples | ForEach-Object { $_.VersionWall }) }
    agentViewCommand = [ordered]@{ cpuMs = Get-Stats @($samples | ForEach-Object { $_.AgentsCpu }); wallMs = Get-Stats @($samples | ForEach-Object { $_.AgentsWall }) }
    hostInProcessCpuMs = Get-Stats @($samples | ForEach-Object { $_.HostCpu })
    oneShotObserveWallMs = Get-Stats @($samples | ForEach-Object { $_.ObserveWall })
    pollCpuMs = $pollStats
    oneCoreSharePercent = [ordered]@{}
    machineSharePercent = [ordered]@{}
    limits = @(
        'Only completed samples are in the statistics. Failed samples are counted under failures.',
        'Child CPU covers each claude.exe process only, not any grandchild it starts.',
        'Host in-process CPU is the one-shot observe process minus a startup-only Host run, so it includes JIT and is an upper bound for a warm loop.',
        'Wall time includes process creation and waits on the child commands.'
    )
}
if ($pollStats) {
    foreach ($interval in $CandidateIntervalsSeconds) {
        $report.oneCoreSharePercent["${interval}s"] = [Math]::Round(100 * $pollStats.median / ($interval * 1000), 2)
        $report.machineSharePercent["${interval}s"] = [Math]::Round(100 * $pollStats.median / ($interval * 1000 * [Environment]::ProcessorCount), 3)
    }
}
$report | ConvertTo-Json -Depth 5
if ($failures.Count -gt 0) { exit 1 }
