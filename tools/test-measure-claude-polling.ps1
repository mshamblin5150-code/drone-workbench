#Requires -Version 7.0
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -Path (Join-Path $PSScriptRoot 'measure/BoundedRun.cs')

$pwsh = (Get-Process -Id $PID).Path
$script = Join-Path $PSScriptRoot 'measure-claude-polling.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) ('measure-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$noVariables = [Collections.Generic.Dictionary[string, string]]::new()
$failures = [Collections.Generic.List[string]]::new()

function Assert-Case([string] $Name, [bool] $Condition, [string] $Detail) {
    if ($Condition) { Write-Host "PASS $Name" } else { Write-Host "FAIL $Name ($Detail)"; $failures.Add($Name) }
}

function Get-MarkedProcesses([string] $Marker) {
    @(Get-CimInstance Win32_Process -Filter "Name='pwsh.exe'" | Where-Object { $_.CommandLine -like "*$Marker*" })
}

function Stop-Marked([string] $Marker) {
    foreach ($process in Get-MarkedProcesses $Marker) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
}

$markers = [Collections.Generic.List[string]]::new()
function New-Marker { $marker = 'measure-test-' + [guid]::NewGuid().ToString('N').Substring(0, 12); $markers.Add($marker); $marker }

function Invoke-Script([hashtable] $Variables, [string[]] $Arguments) {
    $saved = @{}
    foreach ($name in $Variables.Keys) { $saved[$name] = [Environment]::GetEnvironmentVariable($name); [Environment]::SetEnvironmentVariable($name, $Variables[$name]) }
    try {
        $output = & $pwsh -NoProfile -File $script @Arguments 2>&1 | Out-String
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
    } finally {
        foreach ($name in $saved.Keys) {
            if ($null -eq $saved[$name]) { Remove-Item -LiteralPath "Env:\$name" -ErrorAction SilentlyContinue } else { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
        }
    }
}

try {
    $marker = New-Marker
    $parent = "`$start = [Diagnostics.ProcessStartInfo]::new('$pwsh'); `$start.UseShellExecute = `$false; " +
        "`$start.ArgumentList.Add('-NoProfile'); `$start.ArgumentList.Add('-Command'); `$start.ArgumentList.Add('Start-Sleep 60 # $marker'); " +
        "`$null = [Diagnostics.Process]::Start(`$start); exit 0"
    $result = [BoundedRun]::Run($pwsh, @('-NoProfile', '-Command', $parent), $work, $noVariables, @(), 3000, 1048576)
    Start-Sleep -Milliseconds 500
    $left = @(Get-MarkedProcesses $marker).Count
    Assert-Case 'exited parent: process was placed in a job' $result.Contained 'job assignment failed'
    Assert-Case 'exited parent: reported as Timeout' ($result.Failure -eq 'Timeout') "failure=$($result.Failure)"
    Assert-Case 'exited parent: child holding stdout is stopped' ($left -eq 0) "left=$left"
    Stop-Marked $marker

    $marker = New-Marker
    $sleeper = "Start-Process -NoNewWindow -FilePath '$pwsh' -ArgumentList '-NoProfile','-Command','Start-Sleep 60 # $marker'; Start-Sleep 60"
    $result = [BoundedRun]::Run($pwsh, @('-NoProfile', '-Command', $sleeper), $work, $noVariables, @(), 3000, 1048576)
    Start-Sleep -Milliseconds 500
    $left = @(Get-MarkedProcesses $marker).Count
    Assert-Case 'live parent: reported as Timeout' ($result.Failure -eq 'Timeout') "failure=$($result.Failure)"
    Assert-Case 'live parent: grandchild is stopped' ($left -eq 0) "left=$left"
    Stop-Marked $marker

    $result = [BoundedRun]::Run($pwsh, @('-NoProfile', '-Command', "[Console]::Out.Write('x' * 3000000); Start-Sleep 60"), $work, $noVariables, @(), 30000, 1048576)
    Assert-Case 'output over the cap is refused' ($result.Failure -eq 'OutputTooLarge' -and $result.Output.Length -eq 0) "failure=$($result.Failure)"

    $result = [BoundedRun]::Run($pwsh, @('-NoProfile', '-Command', "'hello'"), $work, $noVariables, @(), 30000, 1048576)
    Assert-Case 'normal run keeps output' ($result.Failure -eq 'None' -and $result.ExitCode -eq 0 -and [Text.Encoding]::UTF8.GetString($result.Output).Trim() -eq 'hello') "failure=$($result.Failure)"

    $env:CLAUDE_CONFIG_DIR = 'C:\set-in-parent'
    try {
        $result = [BoundedRun]::Run($pwsh, @('-NoProfile', '-Command', '[bool]$env:CLAUDE_CONFIG_DIR'), $work, $noVariables, @('CLAUDE_CONFIG_DIR'), 30000, 1048576)
    } finally {
        Remove-Item Env:\CLAUDE_CONFIG_DIR
    }
    Assert-Case 'removed variable is absent in the child' ([Text.Encoding]::UTF8.GetString($result.Output).Trim() -eq 'False') 'variable leaked'

    $emptyRoot = New-Item -ItemType Directory -Path (Join-Path $work 'empty-root')
    foreach ($root in @('relative-root', $emptyRoot.FullName)) {
        $run = Invoke-Script @{ CLAUDE_CONFIG_DIR = $root; PATH = '' } @('-Iterations', '5')
        Assert-Case "configuration root '$([IO.Path]::GetFileName($root))' is refused" ($run.ExitCode -ne 0 -and $run.Output -match 'Measurement refused') "exit=$($run.ExitCode)"
    }

    foreach ($interval in @('0', '-15')) {
        $run = Invoke-Script @{ PATH = '' } @('-Iterations', '5', '-CandidateIntervalsSeconds', $interval)
        Assert-Case "interval $interval is refused" ($run.ExitCode -ne 0 -and $run.Output -match 'CandidateIntervalsSeconds') "exit=$($run.ExitCode)"
    }
} finally {
    foreach ($marker in $markers) { Stop-Marked $marker }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "$($failures.Count) failed"
    exit 1
}
Write-Host 'All measurement runner checks passed.'
