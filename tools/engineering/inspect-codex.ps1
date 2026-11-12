function InspectCodex($Entries) {
    $codex = (Get-Command codex -CommandType Application | Select-Object -First 1).Source
    $inspectionHome = Join-Path $StateRoot ('inspection/codex-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $inspectionHome | Out-Null
    $start = [Diagnostics.ProcessStartInfo]::new($codex)
    $start.ArgumentList.Add('app-server')
    $start.ArgumentList.Add('--stdio')
    $start.WorkingDirectory = $ProjectRoot
    $start.Environment['CODEX_HOME'] = $inspectionHome
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $processStarted = $false
    function ReadResponse([int]$Id) {
        $deadline = [DateTime]::UtcNow.AddSeconds(45)
        while ([DateTime]::UtcNow -lt $deadline) {
            $remaining = [Math]::Max(1,[int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
            $read = $process.StandardOutput.ReadLineAsync()
            if (!$read.Wait($remaining)) { throw "Codex app-server response timed out: $Id" }
            $line = $read.Result
            if ($null -eq $line) { throw 'Codex app-server closed its output before discovery completed' }
            $message = $line | ConvertFrom-Json
            if ($message.PSObject.Properties['id'] -and $message.id -eq $Id) {
                if ($message.PSObject.Properties['error']) { throw "Codex app-server rejected discovery: $($message.error | ConvertTo-Json -Compress)" }
                return $message.result
            }
        }
        throw 'Codex discovery deadline reached'
    }
    function SendRpc($Message) { $process.StandardInput.WriteLine(($Message | ConvertTo-Json -Depth 12 -Compress)); $process.StandardInput.Flush() }
    try {
        if (!$process.Start()) { throw 'Could not start owned Codex app-server' }
        $processStarted = $true
        # Drain stderr asynchronously so native diagnostics cannot block stdout. Never persist raw metadata.
        $stderr = $process.StandardError.ReadToEndAsync()
        SendRpc @{id=1; method='initialize'; params=@{clientInfo=@{name='engineering_stack_inspection';version='1.0.0'};capabilities=@{experimentalApi=$true;explicitGatewayOauth=$true}}}
        ReadResponse 1 | Out-Null
        SendRpc @{method='initialized';params=@{}}
        SendRpc @{id=2;method='skills/list';params=@{cwds=@($ProjectRoot);forceReload=$true}}
        $response = ReadResponse 2
        $data = @($response.data | Where-Object { [IO.Path]::GetFullPath($_.cwd) -eq $ProjectRoot })
        if ($data.Count -ne 1) { throw 'Codex returned no unique project skill inventory' }
        $errors = @($data[0].errors | Where-Object { $_.path -like '*dwb-*' })
        if ($errors.Count) { throw "Codex reported $($errors.Count) namespaced skill parse errors" }
        $observed = @($data[0].skills | Where-Object { $_.name -like 'dwb-*' -and [IO.Path]::GetFullPath($_.path).StartsWith($ProjectRoot + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) })
        if (@(Compare-Object @($Entries.name | Sort-Object) @($observed.name | Sort-Object)).Count) { throw 'Codex discovered skill names do not match the catalog' }
        SaveReceipt ([ordered]@{harness='codex';version=((& $codex --version) -join ' ');catalog_sha256=(Digest ([IO.File]::ReadAllBytes($CatalogPath)));observed_project_names=@($observed.name | Sort-Object);skill_count=$observed.Count;parse_error_count=$errors.Count;limitations=@('Fresh isolated app-server native discovery only; no model turn or workflow execution.')})
    } finally {
        try {
            if ($processStarted -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit(5000) | Out-Null }
        } finally { $process.Dispose() }
    }
}
