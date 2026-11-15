<#
.SYNOPSIS
    Generates SHA-256 release checksums for CertFlow release binaries.
#>

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\dist\CertFlow"))
$outputFile = Join-Path $distDir "SHA256SUMS.txt"

if (-not (Test-Path $distDir)) {
    Write-Error "dist directory does not exist. Please publish binaries first."
    exit 1
}

$files = Get-ChildItem -Path $distDir -Recurse -File -Include "*.exe", "*.dll"
$results = @()

foreach ($file in $files) {
    $hash = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $relPath = $file.FullName.Substring($distDir.Length).TrimStart('\', '/').Replace("\", "/")
    $results += "$hash  $relPath"
}

$results | Out-File -FilePath $outputFile -Encoding utf8
Write-Host "Generated SHA256 checksums at: $outputFile" -ForegroundColor Green
Get-Content $outputFile
