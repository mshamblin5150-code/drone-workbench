#requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Install','Verify','InspectCodex','InspectClaude','StartClaude')][string]$Action,
    [string]$SourceRoot,
    [string[]]$ClaudeArguments = @()
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$StateRoot = Join-Path $ProjectRoot '.local/engineering-stack'
$Sources = Join-Path $StateRoot 'sources'
$Lock = Get-Content (Join-Path $ProjectRoot 'docs/toolchain/engineering-sources.lock.json') -Raw | ConvertFrom-Json
$CatalogPath = Join-Path $ProjectRoot 'docs/toolchain/engineering-catalog.json'
$Utf8 = [Text.UTF8Encoding]::new($false)
function Git([string]$Directory, [string[]]$Arguments) {
    $result = & git.exe --no-optional-locks -c "core.hooksPath=$StateRoot/disabled-hooks" -C $Directory @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $result" }
    return ($result -join "`n")
}
function Digest([byte[]]$Bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($Bytes)).ToLowerInvariant() }
function ReadText([string]$Path) { [IO.File]::ReadAllText($Path) }
function AssertSource($Package, [string]$CheckoutOverride) {
    $checkout = if ($CheckoutOverride) { $CheckoutOverride } else { Join-Path $Sources $Package.checkout }
    $base = if ($Package.scope -eq '.') { $checkout } else { Join-Path $checkout $Package.scope }
    if (!(Test-Path -LiteralPath (Join-Path $checkout '.git'))) { throw "Missing source checkout: $($Package.checkout). Run Install. If its destination already exists, inspect and relocate it manually before retrying; Install never replaces it." }
    if ((Git $checkout @('rev-parse','HEAD')) -ne $Package.commit) { throw "Source commit drift: $($Package.name)" }
    $treeSpec = if ($Package.scope -eq '.') { "$($Package.commit)^{tree}" } else { "$($Package.commit):$($Package.scope)" }
    if ((Git $checkout @('rev-parse',$treeSpec)) -ne $Package.tree) { throw "Source tree drift: $($Package.name)" }
    $tree = Git $checkout @('ls-tree','-r',$treeSpec)
    $expected = @($Package.files | ForEach-Object { "$($_.mode) blob $($_.git_blob)`t$($_.path)" })
    if (($tree -split "`n" | Sort-Object) -join "`n" -cne (($expected | Sort-Object) -join "`n")) { throw "Lock/tree file-set mismatch: $($Package.name)" }
    foreach ($file in $Package.files) {
        $path = Join-Path $base $file.path
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing locked file: $($Package.name)/$($file.path)" }
        $bytes = [IO.File]::ReadAllBytes($path)
        if ((Digest $bytes) -ne $file.sha256) { throw "Source SHA256 drift: $($Package.name)/$($file.path)" }
        $header = [Text.Encoding]::UTF8.GetBytes("blob $($bytes.Length)`0")
        $blob = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData([byte[]]($header + $bytes))).ToLowerInvariant()
        if ($blob -ne $file.git_blob) { throw "Source Git blob drift: $($Package.name)/$($file.path)" }
    }
    $actual = @(Get-ChildItem -LiteralPath $base -Recurse -Force -File | Where-Object { $_.FullName -notlike "$checkout\.git\*" } | ForEach-Object { [IO.Path]::GetRelativePath($base,$_.FullName).Replace('\','/') })
    if (@(Compare-Object @($Package.files.path) $actual).Count) { throw "Source file-set drift: $($Package.name)" }
}
function GetFrontmatter([string]$Text) {
    $match = [regex]::Match($Text,'(?s)\A---\r?\n(.*?)\r?\n---(?:\r?\n|$)')
    if (!$match.Success) { throw 'Skill missing frontmatter' }
    $match.Groups[1].Value.Replace("`r`n","`n")
}
function GetEntries {
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($packageName in @('pocock','pstack','cursor-team-kit')) {
        $package = $Lock.packages | Where-Object name -eq $packageName
        $base = Join-Path $Sources $package.local_path
        $paths = if ($packageName -eq 'pocock') {
            $plugin = ReadText (Join-Path $base '.claude-plugin/plugin.json') | ConvertFrom-Json
            @($plugin.skills | ForEach-Object { Join-Path $base "$_/SKILL.md" })
        } else { @(Get-ChildItem -LiteralPath (Join-Path $base 'skills') -Directory | ForEach-Object { Join-Path $_.FullName 'SKILL.md' } | Where-Object { Test-Path -LiteralPath $_ }) }
        $prefix = switch ($packageName) { 'pocock' {'pocock'} 'pstack' {'pstack'} 'cursor-team-kit' {'kit'} }
        foreach ($path in ($paths | Sort-Object)) {
            $frontmatter = GetFrontmatter (ReadText $path)
            $nameMatch = [regex]::Match($frontmatter,'(?m)^name:\s*["'']?([^\r\n"'']+)["'']?\s*$')
            if (!$nameMatch.Success) { throw "Skill missing name: $path" }
            $upstreamName = $nameMatch.Groups[1].Value.Trim()
            $name = "dwb-$prefix-$upstreamName"
            $relative = [IO.Path]::GetRelativePath($ProjectRoot,$path).Replace('\','/')
            $invocation = if ($frontmatter -match '(?m)^disable-model-invocation:\s*true\s*$') { 'user-only' } else { 'model-and-user' }
            $entries.Add([ordered]@{ package=$packageName; name=$name; upstream_name=$upstreamName; path=$relative; invocation=$invocation })
        }
    }
    return @($entries | Sort-Object name)
}
function GetGenerated($Entries) {
    $generated = [ordered]@{}
    $catalog = [ordered]@{schema_version=1; generator='tools/engineering-stack.ps1'; entries=@($Entries)}
    $generated['docs/toolchain/engineering-catalog.json'] = ($catalog | ConvertTo-Json -Depth 8).Replace("`r`n","`n") + "`n"
    foreach ($entry in $Entries) {
        $sourcePath = Join-Path $ProjectRoot $entry.path
        $frontmatter = GetFrontmatter (ReadText $sourcePath)
        $frontmatter = [regex]::Replace($frontmatter,'(?m)^name:.*$',"name: $($entry.name)")
        $wrapper = @"
---
$frontmatter
---

<!-- Generated by tools/engineering-stack.ps1. Update the generator in a reviewed change; do not edit this file by hand. -->

Read ``docs/agents/engineering-stack.md`` from the project root, then read the pristine skill at ``$($entry.path)`` completely before acting. Resolve that skill's relative resources and companion agents from its pristine source directory. Apply its workflow through the documented harness compatibility rules. Use the namespaced entries in ``docs/toolchain/engineering-catalog.json`` when the workflow names another skill.
"@
        $generated[".agents/skills/$($entry.name)/SKILL.md"] = $wrapper.Replace("`r`n","`n") + "`n"
        $policy = Join-Path (Split-Path $sourcePath -Parent) 'agents/openai.yaml'
        if (Test-Path -LiteralPath $policy) { $generated[".agents/skills/$($entry.name)/agents/openai.yaml"] = (ReadText $policy).Replace("`r`n","`n") }
        elseif ($entry.invocation -eq 'user-only') { $generated[".agents/skills/$($entry.name)/agents/openai.yaml"] = "policy:`n  allow_implicit_invocation: false`n" }
        if ($entry.invocation -eq 'user-only' -and $generated[".agents/skills/$($entry.name)/agents/openai.yaml"] -notmatch '(?m)^  allow_implicit_invocation:[ \t]*false[ \t]*(?:#.*)?$') { throw "User-only skill lacks explicit implicit-invocation protection: $($entry.name)" }
    }
    return $generated
}
function AssertGenerated($Generated) {
    foreach ($path in $Generated.Keys) {
        $target = Join-Path $ProjectRoot $path
        if (!(Test-Path -LiteralPath $target -PathType Leaf) -or (ReadText $target) -cne $Generated[$path]) { throw "Generated-file drift: $path. Review before regenerating." }
    }
    $skillRoot = Join-Path $ProjectRoot '.agents/skills'
    foreach ($directory in @(Get-ChildItem -LiteralPath $skillRoot -Directory -Filter 'dwb-*' -ErrorAction SilentlyContinue)) {
        foreach ($file in Get-ChildItem -LiteralPath $directory.FullName -Recurse -Force -File) {
            $relative = [IO.Path]::GetRelativePath($ProjectRoot,$file.FullName).Replace('\','/')
            if (!$Generated.Contains($relative)) { throw "Unexpected generated namespace file: $relative" }
        }
    }
}
function VerifyStack {
    foreach ($package in $Lock.packages) { AssertSource $package }
    foreach ($checkout in ($Lock.packages.checkout | Sort-Object -Unique)) {
        if (Git (Join-Path $Sources $checkout) @('status','--porcelain','--untracked-files=all')) { throw "Source checkout is modified: $checkout" }
    }
    $entries = @(GetEntries)
    AssertGenerated (GetGenerated $entries)
    return $entries
}
function InstallStack {
    New-Item -ItemType Directory -Force -Path $Sources,(Join-Path $StateRoot 'disabled-hooks') | Out-Null
    foreach ($group in ($Lock.packages | Group-Object checkout)) {
        $package = $group.Group[0]
        $destination = Join-Path $Sources $group.Name
        if (!(Test-Path -LiteralPath $destination)) {
            $attemptId = [guid]::NewGuid().ToString('N')
            $sourceStage = [ordered]@{state='staging';commit=$package.commit;checkout=$group.Name;artifact_path=".local/engineering-stack/sources/.install-$($group.Name)-$attemptId"}
            $stage = Join-Path $ProjectRoot $sourceStage.artifact_path
            $attemptRoot = Join-Path $StateRoot 'install-attempts'
            New-Item -ItemType Directory -Force -Path $attemptRoot | Out-Null
            $attemptPath = Join-Path $attemptRoot "$($group.Name)-$attemptId.json"
            [IO.File]::WriteAllText($attemptPath,($sourceStage | ConvertTo-Json) + "`n",$Utf8)
            try {
                $origin = if ($SourceRoot) { Join-Path $SourceRoot $group.Name } else { $package.repository }
                if ($SourceRoot) {
                    # Import objects only: local sparse/partial clones cannot serve missing unrelated blobs.
                    $objectRoot = Git $origin @('rev-parse','--path-format=absolute','--git-path','objects')
                    if (Test-Path -LiteralPath (Join-Path $objectRoot 'info/alternates')) { throw 'SourceRoot uses nonportable object alternates; use remote hydration.' }
                    & git.exe -c core.autocrlf=false -c core.symlinks=false init --quiet $stage
                    if ($LASTEXITCODE -ne 0) { throw 'Owned source initialization failed' }
                    foreach ($child in Get-ChildItem -LiteralPath $objectRoot -Force) {
                        Copy-Item -LiteralPath $child.FullName -Destination (Join-Path $stage '.git/objects') -Recurse -Force
                    }
                    Git $stage @('remote','add','origin',$package.repository) | Out-Null
                } else {
                    & git.exe -c core.autocrlf=false -c core.symlinks=false clone --filter=blob:none --no-checkout --depth 1 --revision=$($package.commit) -- $origin $stage
                    if ($LASTEXITCODE -ne 0) { throw "Source clone failed: $($group.Name)" }
                }
                Git $stage @('config','core.autocrlf','false') | Out-Null
                Git $stage @('config','core.symlinks','false') | Out-Null
                try { Git $stage @('cat-file','-e',"$($package.commit)^{commit}") | Out-Null }
                catch { Git $stage @('fetch','--no-tags','origin',$package.commit) | Out-Null }
                if ($package.scope -ne '.') { Git $stage (@('sparse-checkout','set','--cone') + @($group.Group.scope)) | Out-Null }
                Git $stage @('checkout','--detach',$package.commit) | Out-Null
                foreach ($pin in $group.Group) { AssertSource $pin $stage }
                if (Git $stage @('status','--porcelain','--untracked-files=all')) { throw "Staged source checkout is modified: $($group.Name)" }
                $sourcesPath = [IO.Path]::GetFullPath($Sources).TrimEnd([IO.Path]::DirectorySeparatorChar)
                foreach ($path in @($stage,$destination)) {
                    if (![string]::Equals([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($path)),$sourcesPath,[StringComparison]::OrdinalIgnoreCase)) { throw 'Source publication paths must be direct children of the sources directory' }
                }
                if (Test-Path -LiteralPath $destination) { throw "Source destination appeared during installation; retained stage: $stage" }
                Move-Item -LiteralPath $stage -Destination $destination
                $sourceStage.state = 'published'
                $sourceStage.artifact_path = ".local/engineering-stack/sources/$($group.Name)"
            } catch {
                $sourceStage.state = 'failed'
                throw
            } finally {
                [IO.File]::WriteAllText($attemptPath,($sourceStage | ConvertTo-Json) + "`n",$Utf8)
            }
        }
        try {
            foreach ($pin in $group.Group) { AssertSource $pin }
            if (Git $destination @('status','--porcelain','--untracked-files=all')) { throw "Existing source checkout is modified: $($group.Name)" }
        } catch { throw "Existing source failed verification: $($group.Name). Inspect and relocate an incomplete destination manually before retrying; Install never replaces it. $($_.Exception.Message)" }
    }
    $entries = @(GetEntries)
    $generated = GetGenerated $entries
    foreach ($relative in $generated.Keys) {
        $path = Join-Path $ProjectRoot $relative
        if ((Test-Path -LiteralPath $path) -and (ReadText $path) -cne $generated[$relative]) { throw "Refusing to overwrite existing file: $relative" }
    }
    foreach ($entry in $entries) {
        $directory = Join-Path $ProjectRoot ".agents/skills/$($entry.name)"
        if (Test-Path -LiteralPath $directory) {
            foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -Force -File) {
                $relative = [IO.Path]::GetRelativePath($ProjectRoot,$file.FullName).Replace('\','/')
                if (!$generated.Contains($relative)) { throw "Refusing unrelated skill content: $relative" }
            }
        }
    }
    foreach ($relative in $generated.Keys) {
        $path = Join-Path $ProjectRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
        if (!(Test-Path -LiteralPath $path)) { [IO.File]::WriteAllText($path,$generated[$relative],$Utf8) }
    }
    VerifyStack | Out-Null
    [ordered]@{action='Install'; verified=$true; skills=$entries.Count; packages=$Lock.packages.Count} | ConvertTo-Json
}
function SaveReceipt($Receipt) {
    $directory = Join-Path $StateRoot 'inspection'
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $json = $Receipt | ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText((Join-Path $directory "$($Receipt.harness)-discovery.json"),$json + "`n",$Utf8)
    $json
}
function GetPluginArguments {
    @('--plugin-dir',(Join-Path $Sources 'pocock'),'--plugin-dir',(Join-Path $Sources 'pstack/harness/pstack/claude-code'),'--plugin-dir',(Join-Path $Sources 'pstack/cursor-team-kit'))
}
function InspectClaude($Entries) {
    $observed = @()
    foreach ($name in @('mattpocock-skills','pstack','cursor-team-kit')) {
        $output = & claude @(GetPluginArguments) plugin details $name 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) { throw "Claude plugin discovery failed: $name $output" }
        if ($output -notmatch ('(?im)^\s*Source:\s*' + [regex]::Escape($name) + '@inline\s*$')) { throw "Claude resolved a non-session plugin: $name" }
        $skills = [regex]::Match($output,'(?im)^\s*Skills \((\d+)\)[ \t]*(.*)$')
        $agents = [regex]::Match($output,'(?im)^\s*Agents \((\d+)\)[ \t]*(.*)$')
        if (!$skills.Success -or !$agents.Success) { throw "Unrecognized Claude discovery format for $name" }
        $packageName = if ($name -eq 'mattpocock-skills') { 'pocock' } else { $name }
        $skillNames = @($skills.Groups[2].Value.Trim() -split ', ' | Sort-Object)
        $catalogNames = @($Entries | Where-Object package -eq $packageName | ForEach-Object { $_.upstream_name } | Sort-Object)
        $expectedSkills = $catalogNames.Count
        $agentRoot = switch ($name) {
            'mattpocock-skills' { Join-Path $Sources 'pocock/agents' }
            'pstack' { Join-Path $Sources 'pstack/harness/pstack/claude-code/agents' }
            'cursor-team-kit' { Join-Path $Sources 'pstack/cursor-team-kit/agents' }
        }
        $expectedAgentNames = @(Get-ChildItem -LiteralPath $agentRoot -File -Filter '*.md' -ErrorAction SilentlyContinue | ForEach-Object {
            $metadata = GetFrontmatter (ReadText $_.FullName)
            $match = [regex]::Match($metadata,'(?m)^name:\s*["'']?([^\r\n"'']+)["'']?\s*$')
            if (!$match.Success) { throw 'Agent missing name' }
            $match.Groups[1].Value.Trim()
        } | Sort-Object)
        $expectedAgents = $expectedAgentNames.Count
        $agentNames = @($agents.Groups[2].Value.Trim() -split ', ' | Where-Object { $_ } | Sort-Object)
        if ([int]$skills.Groups[1].Value -ne $expectedSkills -or [int]$agents.Groups[1].Value -ne $expectedAgents) { throw "Claude plugin count mismatch: $name" }
        if (@(Compare-Object $catalogNames $skillNames).Count) { throw "Claude plugin skill-name mismatch: $name" }
        if (@(Compare-Object $expectedAgentNames $agentNames).Count) { throw "Claude plugin agent-name mismatch: $name" }
        $observed += [ordered]@{name=$name; skills=$expectedSkills; agents=$expectedAgents;skill_names=$skillNames;agent_names=$agentNames}
    }
    SaveReceipt ([ordered]@{harness='claude'; version=((& claude --version) -join ' '); catalog_sha256=(Digest ([IO.File]::ReadAllBytes($CatalogPath))); observed=$observed; limitations=@('Native plugin metadata discovery only; no model turn or workflow execution.')})
}
switch ($Action) {
    'Install' { InstallStack }
    'Verify' { $entries = @(VerifyStack); [ordered]@{action='Verify'; verified=$true; skills=$entries.Count; packages=$Lock.packages.Count} | ConvertTo-Json }
    'InspectClaude' { $entries = @(VerifyStack); InspectClaude $entries }
    'InspectCodex' { $entries = @(VerifyStack); . (Join-Path $PSScriptRoot 'engineering/inspect-codex.ps1'); InspectCodex $entries }
    'StartClaude' { VerifyStack | Out-Null; & claude @(GetPluginArguments) @ClaudeArguments; if ($LASTEXITCODE -ne 0) { throw "Claude exited with code $LASTEXITCODE" } }
}
