#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Throughput benchmark — pre-fills Kafka with N messages then measures how fast
    the EventProcessor can drain the topic.

.DESCRIPTION
    Phase 1 – FILL:  Stops any running consumer, bulk-produces --Count messages
                     into fraud.transactions with no rate limiting.
    Phase 2 – DRAIN: Starts EventProcessor in the background, waits for it to
                     come up, then polls /api/metrics every second.
    Phase 3 – REPORT: Prints a summary when throughput drops to zero for 5 s.

.PARAMETER Count
    Number of messages to pre-fill. Default: 100000.
#>
param(
    [int]$Count = 100000
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Root        = Resolve-Path "$PSScriptRoot\.."
$ApiBase     = "http://localhost:5000"
$MetricsUrl  = "$ApiBase/api/metrics"
$BatchKey    = "kafka.consumer.pipeline.batch"
$ZeroLimit   = 5          # consecutive zero-rate seconds before we declare done
$PollMs      = 1000

# ─────────────────────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────────────────────
function Stop-ByName([string]$name) {
    Get-Process -Name $name -ErrorAction SilentlyContinue |
        ForEach-Object { $_.Kill(); Write-Host "  Stopped $name (PID $($_.Id))" }
}

function Wait-ForApi([string]$url, [int]$timeoutSec = 60) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
        try {
            $null = Invoke-RestMethod $url -TimeoutSec 2 -ErrorAction Stop
            return $true
        } catch { Start-Sleep -Milliseconds 500 }
    }
    return $false
}

function Get-BatchTotal([object]$metrics) {
    try { return [long]$metrics.counters.$BatchKey.total } catch { return 0L }
}

function Get-BatchRate([object]$metrics) {
    try { return [double]$metrics.rates.$BatchKey } catch { return 0.0 }
}

# ─────────────────────────────────────────────────────────────────────────────
# Phase 1 – FILL
# ─────────────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════╗"
Write-Host "║   EventProcessor Throughput Benchmark                ║"
Write-Host "╚══════════════════════════════════════════════════════╝"
Write-Host ""
Write-Host "Phase 1 — Filling Kafka topic with $($Count.ToString('N0')) messages..."
Write-Host ""

Stop-ByName "EventProcessor"
Stop-ByName "dotnet"   # catches any leftover DevProducer

Start-Sleep -Seconds 1

Push-Location $Root
dotnet run --project tools/DevProducer -c Release -- --bulk $Count
Pop-Location

Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 2 – DRAIN
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "Phase 2 — Starting EventProcessor..."
Write-Host ""

$proc = Start-Process dotnet `
    -ArgumentList "run --project src/EventProcessor -c Release" `
    -WorkingDirectory $Root `
    -PassThru `
    -WindowStyle Hidden

Write-Host "  PID $($proc.Id) — waiting for API at $ApiBase ..."

if (-not (Wait-ForApi "$ApiBase/" 90)) {
    Write-Error "EventProcessor did not start within 90 seconds."
    exit 1
}

Write-Host "  API is up."
Write-Host ""

# ─────────────────────────────────────────────────────────────────────────────
# Phase 3 – MONITOR
# ─────────────────────────────────────────────────────────────────────────────
Write-Host "Phase 3 — Monitoring throughput (Ctrl+C to abort)..."
Write-Host ""
Write-Host ("{0,-8} {1,12} {2,12} {3,10}" -f "Elapsed", "Total", "Rate/s", "Peak/s")
Write-Host ("-" * 48)

$sw          = [System.Diagnostics.Stopwatch]::StartNew()
$zeroCount   = 0
$peakRate    = 0.0
$prevTotal   = 0L
$firstTotal  = $null

try {
    while ($true) {
        Start-Sleep -Milliseconds $PollMs

        try {
            $m     = Invoke-RestMethod $MetricsUrl -TimeoutSec 3 -ErrorAction Stop
            $total = Get-BatchTotal $m
            $rate  = Get-BatchRate  $m

            if ($null -eq $firstTotal) { $firstTotal = $total }

            if ($rate -gt $peakRate) { $peakRate = $rate }

            $elapsed = [int]$sw.Elapsed.TotalSeconds
            Write-Host ("{0,-8} {1,12:N0} {2,12:N2} {3,10:N2}" -f `
                "${elapsed}s", $total, $rate, $peakRate)

            if ($rate -lt 0.01 -and $total -gt 0 -and $total -eq $prevTotal) {
                $zeroCount++
                if ($zeroCount -ge $ZeroLimit) { break }
            } else {
                $zeroCount = 0
            }

            $prevTotal = $total
        }
        catch {
            Write-Host "  [poll error] $_"
        }
    }
}
finally {
    $sw.Stop()
}

# ─────────────────────────────────────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────────────────────────────────────
$processed = $prevTotal - ($firstTotal ?? 0)
$totalSec  = [Math]::Max(1, $sw.Elapsed.TotalSeconds)
$avgRate   = if ($totalSec -gt 0) { $processed / $totalSec } else { 0 }

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════╗"
Write-Host "║   Benchmark Complete                                 ║"
Write-Host "╠══════════════════════════════════════════════════════╣"
Write-Host ("║  Filled     : {0,10:N0} messages" -f $Count)
Write-Host ("║  Processed  : {0,10:N0} messages" -f $processed)
Write-Host ("║  Time       : {0,10:F1} seconds"  -f $totalSec)
Write-Host ("║  Peak rate  : {0,10:N1} msg/s"    -f $peakRate)
Write-Host ("║  Avg rate   : {0,10:N1} msg/s"    -f $avgRate)
Write-Host "╚══════════════════════════════════════════════════════╝"
Write-Host ""
