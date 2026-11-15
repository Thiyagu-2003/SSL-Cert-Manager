<#
.SYNOPSIS
    Safely uninstalls the CertFlow Windows Background Service.
.DESCRIPTION
    Requires Administrative elevation. Stops and unregisters the service.
    Specifically preserves customer certificates in the Windows Certificate Store
    and preserves the SQLite database (Section 25.3 Specification Compliance).
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "CertFlowService"
)

# Ensure script is running elevated
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrator privileges are required to uninstall Windows Services. Please run PowerShell as Administrator."
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " CertFlow — Service Uninstaller" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $existing) {
    Write-Host "Service '$ServiceName' is not installed." -ForegroundColor Yellow
    exit 0
}

Write-Host "Stopping $ServiceName..." -ForegroundColor Yellow
Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "Removing service registration..." -ForegroundColor Yellow
sc.exe delete $ServiceName | Out-Null

Write-Host "Service '$ServiceName' uninstalled successfully." -ForegroundColor Green
Write-Host "[NOTE] Database and installed Windows certificates are preserved safely." -ForegroundColor Cyan
