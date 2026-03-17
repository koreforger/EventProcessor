<#
.SYNOPSIS
    One-shot setup: starts containers, creates Kafka topics, initializes database.
.DESCRIPTION
    1. docker compose up (Redpanda + SQL Edge + Console)
    2. Wait for services to become healthy
    3. Create Kafka topics
    4. Run init.sql + seed.sql
#>
param(
    [string]$Container  = 'eventprocessor-sqledge',
    [string]$Redpanda   = 'eventprocessor-redpanda',
    [string]$Password   = 'FraudEngine!Pass123'
)

$ErrorActionPreference = 'Stop'
$scriptsDir = $PSScriptRoot

# ── 1. Start containers ─────────────────────────────────────────────
Write-Host '=== Step 1: Starting containers ===' -ForegroundColor Cyan
& "$scriptsDir\start-docker.ps1"

# ── 2. Wait for Redpanda ────────────────────────────────────────────
Write-Host ''
Write-Host '=== Step 2: Waiting for Redpanda ===' -ForegroundColor Cyan
$retries = 0
while ($retries -lt 30) {
    docker exec $Redpanda rpk cluster info 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) { break }
    Write-Host '  Waiting...' -ForegroundColor Gray
    Start-Sleep -Seconds 2
    $retries++
}
if ($retries -ge 30) { Write-Error 'Redpanda did not become ready in time.' }
Write-Host '  Redpanda is healthy.' -ForegroundColor Green

# ── 3. Create Kafka topics ──────────────────────────────────────────
Write-Host ''
Write-Host '=== Step 3: Creating Kafka topics ===' -ForegroundColor Cyan

$topics = @(
    @{ Name = 'fraud.transactions'; Partitions = 6;  Description = 'Incoming transaction events' }
    @{ Name = 'fraud.events';       Partitions = 6;  Description = 'Internal fraud engine events' }
    @{ Name = 'fraud.decisions';    Partitions = 6;  Description = 'Fraud scoring decisions' }
    @{ Name = 'fraud.alerts';       Partitions = 3;  Description = 'High-score fraud alerts' }
)

foreach ($t in $topics) {
    Write-Host "  Creating topic: $($t.Name) ($($t.Partitions) partitions)" -ForegroundColor Gray
    docker exec $Redpanda rpk topic create $t.Name -p $t.Partitions 2>&1 | Out-Null
    # rpk returns 0 even if topic exists, so no error check needed
}
Write-Host '  Topics ready.' -ForegroundColor Green

# ── 4. Initialize database ──────────────────────────────────────────
Write-Host ''
Write-Host '=== Step 4: Initializing database ===' -ForegroundColor Cyan
& "$scriptsDir\init-database.ps1" -Container $Container -Password $Password

# ── Done ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ' EventProcessor infrastructure is ready' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Cyan
Write-Host ''
Write-Host 'You can now run the application with:'
Write-Host '  dotnet run --project src/EventProcessor'
Write-Host ''
