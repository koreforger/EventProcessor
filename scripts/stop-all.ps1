<#
.SYNOPSIS
    Stops all EventProcessor processes and optionally tears down Docker.
.PARAMETER Volumes
    Also remove Docker volumes (wipes all data).
#>
param(
    [switch]$Volumes
)

$ErrorActionPreference = 'SilentlyContinue'

Write-Host ""
Write-Host "  Stopping EventProcessor stack..." -ForegroundColor Cyan

# Kill .NET processes running EventProcessor or DevProducer
Get-Process -Name "EventProcessor", "DevProducer" -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host "    Stopping $($_.Name) (PID $($_.Id))"; $_ | Stop-Process -Force }

# Kill node/vite dev server
Get-Process -Name "node" -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -match 'vite|dashboard' -or $_.CommandLine -match 'vite' } |
    ForEach-Object { Write-Host "    Stopping Node (PID $($_.Id))"; $_ | Stop-Process -Force }

# Docker
Write-Host "    Stopping Docker containers..." -ForegroundColor Gray
& "$PSScriptRoot\stop-docker.ps1" @( if ($Volumes) { '-Volumes' } )

Write-Host ""
Write-Host "  Done." -ForegroundColor Green
Write-Host ""
