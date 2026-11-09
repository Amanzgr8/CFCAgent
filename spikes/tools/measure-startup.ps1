param(
    [string]$Exe,
    [string]$Label,
    [int]$Runs = 5,
    [switch]$Demo
)

# Starts the app $Runs times, waits for it to settle, and records startup marks (from CFCAGENT_TRACE_FILE),
# memory, and whether its server child goes away on a normal close. Nothing here touches a database beyond the
# app's own read-only connection check at startup.
$results = @()
for ($i = 1; $i -le $Runs; $i++) {
    $trace = Join-Path $env:TEMP ("cfcagent-trace-" + [guid]::NewGuid().ToString("N") + ".txt")
    $env:CFCAGENT_TRACE_FILE = $trace
    $arguments = @()
    if ($Demo) { $arguments += "--demo" }
    $p = if ($arguments.Count -gt 0) { Start-Process $Exe -ArgumentList $arguments -PassThru } else { Start-Process $Exe -PassThru }

    $wanted = if ($Demo) { "window opened" } else { "connected" }
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt 40 -and -not $p.HasExited) {
        Start-Sleep -Milliseconds 100
        if ((Test-Path $trace) -and ((Get-Content $trace -Raw) -match $wanted)) { break }
    }
    if ($Demo) {
        # In demo mode "window opened" is not marked (no backend wiring); use the main window title instead.
        while ($sw.Elapsed.TotalSeconds -lt 40 -and [string]::IsNullOrEmpty($p.MainWindowTitle)) { Start-Sleep -Milliseconds 50; $p.Refresh() }
    }
    Start-Sleep -Seconds 4
    $p.Refresh()

    $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)")
    $childWs = 0
    foreach ($c in $children) { $cp = Get-Process -Id $c.ProcessId -ErrorAction SilentlyContinue; if ($cp) { $childWs += $cp.WorkingSet64 } }
    $appWs = $p.WorkingSet64
    $appPrivate = $p.PrivateMemorySize64
    $threads = $p.Threads.Count
    $responding = $p.Responding

    $null = $p.CloseMainWindow()
    $closed = $p.WaitForExit(15000)
    Start-Sleep -Seconds 2
    $stillRunning = 0
    foreach ($c in $children) { if (Get-Process -Id $c.ProcessId -ErrorAction SilentlyContinue) { $stillRunning++ } }

    $marks = @{}
    if (Test-Path $trace) {
        foreach ($line in Get-Content $trace) { $parts = $line -split "`t"; if ($parts.Count -ge 2) { $marks[$parts[1]] = [double]$parts[0] } }
        Remove-Item $trace -ErrorAction SilentlyContinue
    }
    $results += [pscustomobject]@{
        Run = $i
        MainMs = $marks["Main entered"]
        FrameworkMs = $marks["framework initialized"]
        WindowMs = $marks["window opened"]
        ConnectedMs = $marks["connected"]
        ExitBeginsMs = $marks["exit begins"]
        ServerDownMs = $marks["server shut down"]
        AppWorkingSetMB = [math]::Round($appWs / 1MB, 0)
        AppPrivateMB = [math]::Round($appPrivate / 1MB, 0)
        ServerWorkingSetMB = [math]::Round($childWs / 1MB, 0)
        Threads = $threads
        Responding = $responding
        ClosedCleanly = $closed
        ExitCode = $p.ExitCode
        ChildrenLeft = $stillRunning
    }
}
Remove-Item Env:CFCAGENT_TRACE_FILE -ErrorAction SilentlyContinue
"=== $Label ($Runs runs): startup marks (ms since process start) ==="
$results | Format-Table Run, MainMs, FrameworkMs, WindowMs, ConnectedMs, ExitBeginsMs, ServerDownMs -AutoSize | Out-String -Width 200
"=== $Label ($Runs runs): memory and shutdown ==="
$results | Format-Table Run, AppWorkingSetMB, AppPrivateMB, ServerWorkingSetMB, Threads, Responding, ClosedCleanly, ExitCode, ChildrenLeft -AutoSize | Out-String -Width 200
