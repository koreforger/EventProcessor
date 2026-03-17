<#
.SYNOPSIS
    Starts the EventProcessor development infrastructure.
.DESCRIPTION
    Brings up Redpanda (Kafka), Azure SQL Edge, and Redpanda Console via docker compose.
    Does NOT start the devcontainer service.
#>
param(
    [switch]$All   # Also start the devcontainer
)

$ErrorActionPreference = 'Stop'
$dockerDir = Join-Path $PSScriptRoot '..\docker'

Push-Location $dockerDir
try {
    $services = @('redpanda', 'sqledge', 'console')
    if ($All) { $services = @() }  # empty = all services

    Write-Host '=== Starting EventProcessor infrastructure ===' -ForegroundColor Cyan
    if ($services.Count -gt 0) {
        docker compose up -d @services
    } else {
        docker compose up -d
    }

    Write-Host ''
    Write-Host 'Services:' -ForegroundColor Green
    Write-Host '  Redpanda (Kafka) : localhost:29092'
    Write-Host '  SQL Edge         : localhost:14333  (sa / FraudEngine!Pass123)'
    Write-Host '  Redpanda Console : http://localhost:8080'
    Write-Host ''
    Write-Host 'Run scripts/configure-infra.ps1 to create topics and seed the database.' -ForegroundColor Yellow
} finally {
    Pop-Location
}
