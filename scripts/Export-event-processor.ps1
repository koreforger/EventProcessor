<#
.SYNOPSIS
    Exports all C# source files from the EventProcessor project into a single concatenated file.
.DESCRIPTION
    Recursively finds every *.cs file under src/EventProcessor (excluding obj/bin),
    adds a header with the relative path, and writes the combined output to the
    main KoreForge folder as EventProcessor-Source.cs.
    Re-running the script always picks up newly added files.
#>
param(
    [string]$OutputFile = (Join-Path $PSScriptRoot '..\..\..\EventProcessor-Source.cs')
)

$ErrorActionPreference = 'Stop'
$projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\src\EventProcessor')
$OutputFile  = [System.IO.Path]::GetFullPath($OutputFile)

$separator = '/' * 100

$files = Get-ChildItem -Path $projectRoot -Filter '*.cs' -Recurse |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Sort-Object FullName

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("// EventProcessor — Combined C# Source Export")
[void]$sb.AppendLine("// Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine("// Files: $($files.Count)")
[void]$sb.AppendLine("// $separator")
[void]$sb.AppendLine()

foreach ($file in $files) {
    $relative = $file.FullName.Substring($projectRoot.Path.Length).TrimStart('\') -replace '\\','/'
    [void]$sb.AppendLine("// $separator")
    [void]$sb.AppendLine("// File: $relative")
    [void]$sb.AppendLine("// $separator")
    [void]$sb.AppendLine()
    [void]$sb.Append((Get-Content $file.FullName -Raw))
    [void]$sb.AppendLine()
}

Set-Content -Path $OutputFile -Value $sb.ToString() -Encoding UTF8 -NoNewline
Write-Host "Exported $($files.Count) C# files -> $OutputFile" -ForegroundColor Green
