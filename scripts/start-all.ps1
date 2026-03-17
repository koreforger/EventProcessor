<#
.SYNOPSIS
    Starts the entire EventProcessor solution end-to-end.
.DESCRIPTION
    1. Ensures Docker infrastructure is healthy (Redpanda, SQL Edge, Console)
    2. Seeds the database
    3. Starts the EventProcessor API
    4. Starts the Dev Producer (synthetic Kafka messages)
    5. Starts the dashboard Vite dev server
    6. Opens the browser
.PARAMETER SkipDocker
    Skip docker compose up (use when containers are already running).
.PARAMETER SkipSeed
    Skip database seed (use when schema + settings are already correct).
.PARAMETER NoBrowser
    Don't open the browser automatically.
#>
param(
    [switch]$SkipDocker,
    [switch]$SkipSeed,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path "$PSScriptRoot\.."

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "  $msg" -ForegroundColor Cyan
    Write-Host ""
}

function Wait-Healthy([string]$url, [string]$label, [int]$timeoutSec = 60) {
    $elapsed = 0
    while ($elapsed -lt $timeoutSec) {
        try {
            $r = Invoke-WebRequest $url -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
            if ($r.StatusCode -lt 500) {
                Write-Host "    $label is up ($($r.StatusCode))" -ForegroundColor Green
                return
            }
        } catch { }
        Start-Sleep -Seconds 2
        $elapsed += 2
    }
    Write-Error "$label did not become healthy within ${timeoutSec}s"
}

# ── Banner ────────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "=======================================" -ForegroundColor Magenta
Write-Host "  EventProcessor  —  Full Stack Start  " -ForegroundColor Magenta
Write-Host "=======================================" -ForegroundColor Magenta

# ── 1. Docker ─────────────────────────────────────────────────────────────────
if (-not $SkipDocker) {
    Write-Step "Step 1/5  Starting Docker infrastructure..."
    & "$PSScriptRoot\start-docker.ps1"
} else {
    Write-Host "  [skip] Docker (--SkipDocker)" -ForegroundColor Gray
}

# ── 2. Database ───────────────────────────────────────────────────────────────
if (-not $SkipSeed) {
    Write-Step "Step 2/5  Seeding database..."
    & "$PSScriptRoot\init-database.ps1"
} else {
    Write-Host "  [skip] Database seed (--SkipSeed)" -ForegroundColor Gray
}

# ── 3. EventProcessor API ─────────────────────────────────────────────────────
Write-Step "Step 3/5  Starting EventProcessor API..."
$apiJob = Start-Process -FilePath "pwsh" `
    -ArgumentList "-NoLogo", "-NoProfile", "-Command",
        "Push-Location '$root'; dotnet run --project src/EventProcessor -c Release" `
    -PassThru -WindowStyle Normal

Write-Host "    Waiting for API on http://localhost:5000..." -ForegroundColor Gray
Wait-Healthy "http://localhost:5000/" "EventProcessor API"

# ── 4. Dev Producer ───────────────────────────────────────────────────────────
Write-Step "Step 4/5  Starting Dev Producer (Kafka synthetic messages)..."
$producerJob = Start-Process -FilePath "pwsh" `
    -ArgumentList "-NoLogo", "-NoProfile", "-Command",
        "Push-Location '$root\tools\DevProducer'; dotnet run -c Release" `
    -PassThru -WindowStyle Normal

Write-Host "    Dev Producer running (PID $($producerJob.Id))" -ForegroundColor Green

# ── 5. Dashboard ──────────────────────────────────────────────────────────────
Write-Step "Step 5/5  Starting Dashboard (Vite dev server)..."
$dashJob = Start-Process -FilePath "pwsh" `
    -ArgumentList "-NoLogo", "-NoProfile", "-Command",
        "Push-Location '$root\dashboard'; npm run dev" `
    -PassThru -WindowStyle Normal

Write-Host "    Waiting for Dashboard on http://localhost:5173..." -ForegroundColor Gray
$dashElapsed = 0
while ($dashElapsed -lt 30) {
    try {
        $r = Invoke-WebRequest "http://localhost:5173" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
        if ($r.StatusCode -eq 200) { break }
    } catch { }
    Start-Sleep -Seconds 2
    $dashElapsed += 2
}
Write-Host "    Dashboard is up." -ForegroundColor Green

# ── Summary ───────────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "=======================================" -ForegroundColor Magenta
Write-Host "  All services started successfully!   " -ForegroundColor Green
Write-Host "=======================================" -ForegroundColor Magenta
Write-Host ""
Write-Host "  Dashboard        http://localhost:5173" -ForegroundColor White
Write-Host "  API              http://localhost:5000" -ForegroundColor White
Write-Host "  Health           http://localhost:5000/health" -ForegroundColor White
Write-Host "  Redpanda Console http://localhost:8080" -ForegroundColor White
Write-Host "  SQL Edge         localhost,14333  (sa / FraudEngine!Pass123)" -ForegroundColor White
Write-Host ""
Write-Host "  PIDs — API: $($apiJob.Id)  Producer: $($producerJob.Id)  Dashboard: $($dashJob.Id)" -ForegroundColor Gray
Write-Host "  To stop everything: .\scripts\stop-all.ps1" -ForegroundColor Gray
Write-Host ""

# ── Open browser ──────────────────────────────────────────────────────────────
if (-not $NoBrowser) {
    Start-Process "http://localhost:5173"
}
