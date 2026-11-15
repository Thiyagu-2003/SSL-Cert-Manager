<#
.SYNOPSIS
    Installs and registers CertFlow as a native Windows Background Service.
.DESCRIPTION
    Requires Administrative elevation. Configures CertFlow Service with auto-start.
#>

[CmdletBinding()]
param(
    [string]$ServiceName = "CertFlowService",
    [string]$DisplayName = "CertFlow SSL/TLS Automation Service",
    [string]$ServiceBinPath = ""
)

# Ensure script is running elevated
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "Administrator privileges are required to install Windows Services. Please run PowerShell as Administrator."
    exit 1
}

# Resolve default binary path if not provided
if ([string]::IsNullOrWhiteSpace($ServiceBinPath)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $ServiceBinPath = [System.IO.Path]::GetFullPath((Join-Path $scriptDir "..\dist\CertFlow\Service\CertManager.Service.exe"))
}

if (-not (Test-Path $ServiceBinPath)) {
    Write-Error "Service executable not found at: $ServiceBinPath. Please run 'dotnet publish' first."
    exit 1
}

Write-Host "=========================================" -ForegroundColor Cyan
Write-Host " CertFlow — Windows Service Installer" -ForegroundColor Cyan
Write-Host "=========================================" -ForegroundColor Cyan
Write-Host "Binary:  $ServiceBinPath"
Write-Host "Service: $ServiceName"

# Check if service already exists
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping and removing existing service..." -ForegroundColor Yellow
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 1
}

# Create service with auto-start
Write-Host "Registering service with Windows Service Control Manager..." -ForegroundColor Green
$createResult = sc.exe create $ServiceName binPath= "`"$ServiceBinPath`"" start= auto DisplayName= "`"$DisplayName`""
if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to create service. sc.exe returned exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}

# Configure description and recovery
sc.exe description $ServiceName "Autonomous SSL/TLS certificate lifecycle, background renewal, and IIS binding manager." | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/""/60000 | Out-Null

# Start service
Write-Host "Starting $ServiceName..." -ForegroundColor Green
Start-Service -Name $ServiceName
Start-Sleep -Seconds 2

$status = Get-Service -Name $ServiceName
Write-Host "Service Status: $($status.Status)" -ForegroundColor Cyan
Write-Host "CertFlow Service installed and running successfully!" -ForegroundColor Green
