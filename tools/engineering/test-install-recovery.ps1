#requires -Version 7.4
[CmdletBinding()]
param([string]$SourceRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (!$SourceRoot) { $SourceRoot = Join-Path $project '.local/engineering-stack/sources' }
$fixture = Join-Path $project ('.local/engineering-stack/test-fixtures/recovery-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'tools'),(Join-Path $fixture 'docs/toolchain') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $project 'tools/engineering-stack.ps1') -Destination (Join-Path $fixture 'tools/engineering-stack.ps1')
$lockPath = Join-Path $fixture 'docs/toolchain/engineering-sources.lock.json'
$lockText = Get-Content -LiteralPath (Join-Path $project 'docs/toolchain/engineering-sources.lock.json') -Raw
$badLock = $lockText | ConvertFrom-Json
$badLock.packages[0].files[0].sha256 = '0' * 64
$badLock | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $lockPath
$installer = Join-Path $fixture 'tools/engineering-stack.ps1'
$state = Join-Path $fixture '.local/engineering-stack'
$savedHttp = $env:GIT_CONFIG_COUNT
$savedKey0 = $env:GIT_CONFIG_KEY_0
$savedValue0 = $env:GIT_CONFIG_VALUE_0
$savedKey1 = $env:GIT_CONFIG_KEY_1
$savedValue1 = $env:GIT_CONFIG_VALUE_1
try {
    $env:GIT_CONFIG_COUNT = '2'
    $env:GIT_CONFIG_KEY_0 = 'protocol.http.allow'
    $env:GIT_CONFIG_VALUE_0 = 'never'
    $env:GIT_CONFIG_KEY_1 = 'protocol.https.allow'
    $env:GIT_CONFIG_VALUE_1 = 'never'
    $failure = $null
    try { & $installer -Action Install -SourceRoot $SourceRoot | Out-Null } catch { $failure = $_ }
    if (!$failure -or $failure.ToString() -notmatch 'Source SHA256 drift') { throw "Expected deliberate locked-file failure: $failure" }
    if (Test-Path -LiteralPath (Join-Path $state "sources/$($badLock.packages[0].checkout)")) { throw 'Failed hydration published a partial final checkout' }
    $failed = @(Get-ChildItem -LiteralPath (Join-Path $state 'install-attempts') -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } | Where-Object state -eq 'failed')
    if ($failed.Count -ne 1 -or !(Test-Path -LiteralPath (Join-Path $fixture $failed[0].artifact_path))) { throw 'Failed stage or receipt was not retained' }
    [IO.File]::WriteAllText($lockPath,$lockText)
    & $installer -Action Install -SourceRoot $SourceRoot | Out-Null
    & $installer -Action Verify | Out-Null
    $published = @(Get-ChildItem -LiteralPath (Join-Path $state 'install-attempts') -File | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } | Where-Object state -eq 'published')
    $checkoutCount = @($badLock.packages.checkout | Sort-Object -Unique).Count
    if ($published.Count -ne $checkoutCount) { throw 'Retry did not publish every checkout with a receipt' }
    foreach ($receipt in $published) {
        if (!(Test-Path -LiteralPath (Join-Path $fixture $receipt.artifact_path))) { throw 'Published artifact is missing' }
    }
    if (!(Test-Path -LiteralPath (Join-Path $fixture $failed[0].artifact_path))) { throw 'Retry removed the failed stage' }
    [ordered]@{verified=$true;fixture=$fixture;failed_stages=$failed.Count;published_checkouts=$published.Count;http_https='disabled'} | ConvertTo-Json
} finally {
    $env:GIT_CONFIG_COUNT = $savedHttp
    $env:GIT_CONFIG_KEY_0 = $savedKey0
    $env:GIT_CONFIG_VALUE_0 = $savedValue0
    $env:GIT_CONFIG_KEY_1 = $savedKey1
    $env:GIT_CONFIG_VALUE_1 = $savedValue1
}
