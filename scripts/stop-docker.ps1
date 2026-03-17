<#
.SYNOPSIS
    Stops the EventProcessor development infrastructure.
.PARAMETER Volumes
    Also remove named volumes (wipes all data).
#>
param(
    [switch]$Volumes
)

$ErrorActionPreference = 'Stop'
$dockerDir = Join-Path $PSScriptRoot '..\docker'

Push-Location $dockerDir
try {
    Write-Host '=== Stopping EventProcessor infrastructure ===' -ForegroundColor Cyan
    if ($Volumes) {
        docker compose down -v
        Write-Host 'Containers stopped and volumes removed.' -ForegroundColor Yellow
    } else {
        docker compose down
        Write-Host 'Containers stopped. Data volumes preserved.' -ForegroundColor Green
    }
} finally {
    Pop-Location
}
