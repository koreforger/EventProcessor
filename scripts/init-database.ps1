<#
.SYNOPSIS
    Initializes the database schema and seeds settings.
.DESCRIPTION
    Runs init.sql (schema) then seed.sql (settings) against the SQL Edge container.
    Safe to re-run — both scripts are idempotent.
#>
param(
    [string]$Container = 'eventprocessor-sqledge',
    [string]$Password  = 'FraudEngine!Pass123'
)

$ErrorActionPreference = 'Stop'
$sqlDir = Join-Path $PSScriptRoot '..\docker\sql'

function Invoke-SqlFile {
    param([string]$File, [string]$Label)

    $path = Join-Path $sqlDir $File
    if (-not (Test-Path $path)) {
        Write-Error "$Label file not found: $path"
        return
    }

    Write-Host "  Running $Label ($File)..." -ForegroundColor Gray
    $sql = Get-Content $path -Raw
    # Pipe the SQL into sqlcmd inside the container
    $sql | docker exec -i $Container /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P $Password -b
    if ($LASTEXITCODE -ne 0) {
        Write-Error "$Label failed (exit code $LASTEXITCODE)"
    }
}

Write-Host '=== Initializing EventProcessor database ===' -ForegroundColor Cyan

# Wait for SQL to be ready
Write-Host '  Waiting for SQL Edge...' -ForegroundColor Gray
$retries = 0
while ($retries -lt 30) {
    $result = docker exec $Container /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P $Password -Q "SELECT 1" 2>$null
    if ($LASTEXITCODE -eq 0) { break }
    Start-Sleep -Seconds 2
    $retries++
}
if ($retries -ge 30) { Write-Error 'SQL Edge did not become ready in time.' }

Invoke-SqlFile 'init.sql' 'Schema'
Invoke-SqlFile 'seed.sql' 'Settings'

Write-Host ''
Write-Host 'Database ready.' -ForegroundColor Green
