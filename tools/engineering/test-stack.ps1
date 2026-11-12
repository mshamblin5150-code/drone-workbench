#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$tool = Join-Path $root 'tools/engineering-stack.ps1'
$pwsh = (Get-Process -Id $PID).Path
function Run([string]$Action) {
    $result = & $pwsh -NoProfile -File $tool -Action $Action 2>&1 | Out-String
    if ($LASTEXITCODE) { throw "$Action failed: $result" }
}
Run Verify
Run Verify
Run Install
$target = Join-Path $root '.agents/skills/dwb-pstack-unslop/SKILL.md'
$original = [IO.File]::ReadAllBytes($target)
try {
    [IO.File]::AppendAllText($target,"`nFixture drift`n")
    $output = & $pwsh -NoProfile -File $tool -Action Verify 2>&1 | Out-String
    if (!$LASTEXITCODE -or $output -notmatch 'Generated-file drift') { throw 'Verify failed to detect wrapper drift' }
    $output = & $pwsh -NoProfile -File $tool -Action Install 2>&1 | Out-String
    if (!$LASTEXITCODE -or $output -notmatch 'Refusing to overwrite existing file') { throw 'Install failed to protect wrapper drift' }
    if ([IO.File]::ReadAllText($target) -notmatch 'Fixture drift') { throw 'Install overwrote modified wrapper' }
} finally { [IO.File]::WriteAllBytes($target,$original) }
Run Verify
[ordered]@{verify_repeated=$true;install_idempotent=$true;wrapper_drift_detected=$true;modified_wrapper_preserved=$true;fixture_restored=$true} | ConvertTo-Json
