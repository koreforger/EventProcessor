#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Catch-up benchmark.

    Proves the EventProcessor can drain a pre-filled backlog while a live stream
    continues producing — and eventually keep pace indefinitely.

    Phase 1 — FILL   : bulk-produce FillCount messages, no consumer running
    Phase 2 — STREAM : start the streaming producer at StreamRate msg/s
    Phase 3 — DRAIN  : start EventProcessor; monitor until RunSeconds elapsed

.PARAMETER FillCount
    Messages to pre-fill before starting the consumer. Default: 50000.
.PARAMETER StreamRate
    Steady-state streaming rate (msg/s) while the consumer is draining. Min 4. Default: 10.
.PARAMETER RunSeconds
    How long to monitor after the consumer is up. Default: 120.

.EXAMPLE
    .\catchup-bench.ps1
    .\catchup-bench.ps1 -FillCount 20000 -StreamRate 20 -RunSeconds 90
#>
param(
    [int] $FillCount  = 50000,
    [int] $StreamRate = 10,
    [int] $RunSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$StreamRate = [Math]::Max(4, $StreamRate)
$Root       = Resolve-Path "$PSScriptRoot\.."
$ApiBase    = 'http://localhost:5000'
$MetricsUrl = "$ApiBase/api/metrics"

# ── Helpers ───────────────────────────────────────────────────────────────────

function Write-Phase([string]$phase, [string]$detail) {
    $line = '─' * 68
    Write-Host ''
    Write-Host $line
    Write-Host "  $phase  |  $detail"
    Write-Host $line
    Write-Host ''
}

function Stop-App([string]$name) {
    Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "    Stopped $name (PID $($_.Id))"
        $_.Kill()
        $null = $_.WaitForExit(3000)
    }
}

function Wait-ForApi([int]$timeoutSec = 90) {
    Write-Host -NoNewline '  Waiting for API'
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        try {
            Invoke-RestMethod "$ApiBase/" -TimeoutSec 2 -ErrorAction Stop | Out-Null
            Write-Host '  ✓ up'
            return $true
        } catch {
            Write-Host -NoNewline '.'
            Start-Sleep -Milliseconds 500
        }
    }
    Write-Host ' TIMED OUT'
    return $false
}

function Get-Metrics {
    try   { return Invoke-RestMethod $MetricsUrl -TimeoutSec 3 -ErrorAction Stop }
    catch { return $null }
}

function Get-ConsumedTotal([object]$m) {
    if ($null -eq $m) { return 0L }
    $p = $m.counters.PSObject.Properties['kafka.consumer.pipeline.batch.total']
    if ($p) { return [long]$p.Value }
    return 0L
}

function Get-ConsumedRate([object]$m) {
    if ($null -eq $m) { return 0.0 }
    $p = $m.rates.PSObject.Properties['kafka.consumer.pipeline.batch.rate']
    if ($p) { return [double]$p.Value }
    return 0.0
}

# ── Pre-build ─────────────────────────────────────────────────────────────────

Write-Phase 'PRE-BUILD' 'compiling both projects so no build time during the benchmark'

Write-Host "  [$(Get-Date -f 'HH:mm:ss')] Building DevProducer..."
dotnet build "$Root/tools/DevProducer" -c Release 2>&1
Write-Host "  [$(Get-Date -f 'HH:mm:ss')] Building EventProcessor..."
dotnet build "$Root/src/EventProcessor" -c Release 2>&1

# ── Phase 1: FILL ─────────────────────────────────────────────────────────────

Write-Phase 'PHASE 1 — FILL' "$($FillCount.ToString('N0')) messages, no consumer running"

Write-Host '  Stopping any running apps...'
Stop-App 'EventProcessor'
Stop-App 'DevProducer'
Start-Sleep -Seconds 1

Write-Host '  Resetting topic (delete + recreate with 4 partitions for a clean baseline)...'
$topicName = 'fraud.transactions'
$container = 'eventprocessor-redpanda'
docker exec $container rpk topic delete $topicName 2>&1 | Out-Null
Start-Sleep -Milliseconds 500
docker exec $container rpk topic create $topicName --partitions 4 --replicas 1 2>&1 | Out-Null
Write-Host "  ✓ Topic '$topicName' recreated (4 partitions)"
Write-Host ''
Write-Host "  Filling topic with $($FillCount.ToString('N0')) messages as fast as possible..."
Write-Host "  (DevProducer prints a progress line every 10 000 messages)"
Write-Host ''

Push-Location $Root
dotnet run --project tools/DevProducer -c Release --no-build -- --bulk $FillCount
Pop-Location

Write-Host ''
Write-Host "  ✓ Backlog ready — $($FillCount.ToString('N0')) unread messages waiting in topic."

# ── Phase 2: START STREAMING PRODUCER ────────────────────────────────────────

Write-Phase 'PHASE 2 — STREAM' "Starting live producer at $StreamRate msg/s — consumer not yet running"

$streamLog  = "$env:TEMP\catchup-stream.log"
$streamProc = Start-Process dotnet `
    -ArgumentList "run --project tools/DevProducer -c Release --no-build -- --rate $StreamRate" `
    -WorkingDirectory $Root `
    -PassThru `
    -RedirectStandardOutput $streamLog `
    -RedirectStandardError  "$env:TEMP\catchup-stream-err.log"

$streamSw = [System.Diagnostics.Stopwatch]::StartNew()

Write-Host "  ✓ Streaming producer running  PID=$($streamProc.Id)  rate=$StreamRate/s"
Write-Host "    Log: $streamLog"

# ── Phase 3: START CONSUMER + MONITOR ────────────────────────────────────────

Write-Phase 'PHASE 3 — DRAIN + MONITOR' "EventProcessor starting; will monitor for $RunSeconds seconds"

$consumerLog  = "$env:TEMP\catchup-consumer.log"
$consumerProc = Start-Process dotnet `
    -ArgumentList "run --project src/EventProcessor -c Release --no-build" `
    -WorkingDirectory $Root `
    -PassThru `
    -RedirectStandardOutput $consumerLog `
    -RedirectStandardError  "$env:TEMP\catchup-consumer-err.log"

Write-Host "  ✓ EventProcessor starting     PID=$($consumerProc.Id)"
Write-Host "    Log: $consumerLog"

if (-not (Wait-ForApi 90)) {
    Write-Error 'EventProcessor did not respond within 90 s.'
    Stop-Process -Id $streamProc.Id -Force
    exit 1
}

# ── Monitor loop ──────────────────────────────────────────────────────────────

$monitorSw = [System.Diagnostics.Stopwatch]::StartNew()
$baseTotal = $null
$peakRate  = 0.0

Write-Host ''
Write-Host ('  {0,-7}  {1,12}  {2,12}  {3,10}  {4,8}  {5,10}  {6}' -f `
    'Secs', 'Consumed', 'Backlog', 'Con-Rate', 'Prod', 'Surplus', 'Status')
Write-Host ('  ' + '─' * 77)

while ($monitorSw.Elapsed.TotalSeconds -lt $RunSeconds) {
    Start-Sleep -Milliseconds 1000

    $elapsed  = [int]$monitorSw.Elapsed.TotalSeconds
    $streamed = [long]($StreamRate * $streamSw.Elapsed.TotalSeconds)
    $totalIn  = $FillCount + $streamed

    $m        = Get-Metrics
    $rawTotal = Get-ConsumedTotal $m
    $conRate  = Get-ConsumedRate  $m

    if ($null -eq $baseTotal) { $baseTotal = $rawTotal }
    $consumed = $rawTotal - $baseTotal

    if ($conRate -gt $peakRate) { $peakRate = $conRate }

    $backlog    = [Math]::Max(0, $totalIn - $consumed)
    $surplus    = $conRate - $StreamRate
    $surplusStr = if ($surplus -ge 0) { "+$($surplus.ToString('N1'))" } else { $surplus.ToString('N1') }

    $status = if ($backlog -eq 0)  { '✓ CAUGHT UP — keeping pace' }
         elseif ($surplus -gt 1)   { '↑ CATCHING UP' }
         elseif ($surplus -lt -1)  { '↓ FALLING BEHIND' }
         else                       { '≈ HOLDING STEADY' }

    Write-Host ('  {0,-7}  {1,12:N0}  {2,12:N0}  {3,10:N2}  {4,8}  {5,10}  {6}' -f `
        "${elapsed}s", $consumed, $backlog, $conRate, "${StreamRate}/s", $surplusStr, $status)
}

# ── Summary ───────────────────────────────────────────────────────────────────

$finalStreamed = [long]($StreamRate * $streamSw.Elapsed.TotalSeconds)
$finalM        = Get-Metrics
$finalRaw      = Get-ConsumedTotal $finalM
$finalConsumed = $finalRaw - ($baseTotal ?? $finalRaw)
$finalBacklog  = [Math]::Max(0, $FillCount + $finalStreamed - $finalConsumed)
$finalRate     = Get-ConsumedRate $finalM

$verdict = if   ($peakRate -gt $StreamRate * 2) { 'PASS — consumer out-ran the stream; caught up and held pace' }
           else { "REVIEW — peak rate ($($peakRate.ToString('N1'))/s) was close to stream rate ($StreamRate/s)" }

Write-Host ''
Write-Host '┌──────────────────────────────────────────────────────────────┐'
Write-Host '│  CATCH-UP BENCHMARK SUMMARY                                  │'
Write-Host '├──────────────────────────────────────────────────────────────┤'
Write-Host ("│  Pre-filled      : {0,10:N0}  messages"              -f $FillCount)
Write-Host ("│  Streamed in     : {0,10:N0}  messages  ({1}/s)"      -f $finalStreamed, $StreamRate)
Write-Host ("│  Total produced  : {0,10:N0}  messages"              -f ($FillCount + $finalStreamed))
Write-Host ("│  Total consumed  : {0,10:N0}  messages"              -f $finalConsumed)
Write-Host ("│  Remaining       : {0,10:N0}  messages"              -f $finalBacklog)
Write-Host ("│  Peak rate       : {0,10:N2}  msg/s"                 -f $peakRate)
Write-Host ("│  Final rate      : {0,10:N2}  msg/s"                 -f $finalRate)
Write-Host  "│                                                              │"
Write-Host ("│  Verdict: $verdict")
Write-Host '└──────────────────────────────────────────────────────────────┘'
Write-Host ''
Write-Host '  Still running (stop when done):'
Write-Host "    Stop-Process -Id $($streamProc.Id)   # streaming producer"
Write-Host "    Stop-Process -Id $($consumerProc.Id)   # EventProcessor"
Write-Host ''
