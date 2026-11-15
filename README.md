# CertFlow 🛡️⚡

> **Open-Source, Automated SSL/TLS Lifecycle Management for Windows & Microsoft IIS**

[![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11%20%7C%20Server-0078D6.svg)](https://www.microsoft.com/windows)
[![Tests](https://img.shields.io/badge/Tests-25%20Passing-brightgreen.svg)](tests/)

CertFlow is a modern, lightweight, community-driven desktop and background service tool that completely automates discovering websites, requesting free Let's Encrypt certificates, validating ACME challenges, updating IIS bindings with rollback protection, and managing recurring renewals.

---

## ✨ Key Features

- **Decoupled Background Service**: Scheduled renewals run independently as a Windows service—your certificates renew on time even when the desktop client is closed.
- **Modern Fluent Desktop Interface**: Native Windows UI with smooth animations, live background status beacons, and instant inventory metrics.
- **Zero-Latency Named Pipe IPC**: Secure local inter-process communication using Windows Named Pipes and Windows Group role-based access control (`BUILTIN\Administrators`, `CertManager Operators`, `CertManager Auditors`).
- **Transactional IIS Deployments**: Automates HTTPS binding updates with automatic rollback if binding validation fails.
- **ARI & Percentage-Based Renewals**: Follows RFC 8737 (ACME Renewal Information) alongside configurable percentage thresholds rather than rigid 30-day deadlines.
- **Embedded SQLite Persistence**: Resilient local database tracking ACME orders, challenge cleanups, and idempotency locks with crash recovery.
- **IDN & Punycode Support**: Full RFC 5890 normalization for internationalized domains (`xn--...`) and wildcards (`*.example.com`).

---

## 🏛️ Architecture

CertFlow uses a clean, decoupled architecture:

```text
┌──────────────────────────────┐
│     CertFlow Desktop UI      │  (WPF / MVVM / Hardware-Accelerated)
└──────────────┬───────────────┘
               │  Authenticated IPC (Windows Named Pipes)
┌──────────────▼───────────────┐
│   CertFlow Background Worker │  (Background Scheduler / Health / Recovery)
└──────────────┬───────────────┘
               ├────────────────────────────┬─────────────────────────────┐
        ┌──────▼──────┐              ┌──────▼──────┐               ┌──────▼──────┐
        │ Application │              │ Persistence │               │ Integrations│
        │ Workflows   │              │   SQLite    │               │ (IIS, ACME, │
        └─────────────┘              └─────────────┘               │  DNS, Notif)│
                                                                   └─────────────┘
```

---

## 🚀 Quick Start

### 1. True Single-File Executable (All-in-One)
CertFlow is packaged as a **100% self-contained single `.exe`** with zero external dependencies, no pre-requisite .NET install required, and no companion DLLs:

- **Download**: [dist/CertFlow/CertFlow.exe](dist/CertFlow/CertFlow.exe) (verified in [SHA256SUMS.txt](dist/CertFlow/SHA256SUMS.txt))

| Mode | Command / Action | Description |
|---|---|---|
| **Interactive GUI** | Double-click `CertFlow.exe` | Launches the hardware-accelerated WPF application with fluid transitions. If the Windows service is not running, click **⚡ Start In-Process Engine** on the dashboard to run renewals in-app. |
| **Install Service** | `CertFlow.exe --install-service` | Automatically registers and starts CertFlow as an automatic 24/7 Windows Service via `sc.exe`. |
| **Run Service** | `CertFlow.exe --service` | Runs the headless background engine directly (used by Windows Service Manager). |
| **Uninstall Service** | `CertFlow.exe --uninstall-service` | Gracefully stops and deletes the Windows Service registration. |

### 2. Building from Source
Ensure [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) is installed.

```powershell
# Clone the repository
git clone https://github.com/your-org/CertFlow.git
cd CertFlow

# Build the entire solution
dotnet build

# Run unit tests (50 tests passing)
dotnet test

# Publish standalone True Single EXE
dotnet publish src/CertManager.Desktop/CertManager.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o ./dist/CertFlow/
```

---

## 📂 Repository Structure

```text
├── src/
│   ├── CertManager.Core/               # Domain entities, enums, IDN normalizer, abstractions
│   ├── CertManager.Application/        # Use cases and workflow orchestrators
│   ├── CertManager.Infrastructure/     # EF Core SQLite database, Named Pipe IPC, Notifications & Health checks
│   ├── CertManager.Acme/               # RFC 8555 ACME client abstractions (HTTP-01 & DNS-01)
│   ├── CertManager.Integrations.Iis/   # Microsoft.Web.Administration IIS discovery and bindings
│   ├── CertManager.Integrations.Dns/   # Cloudflare API and Manual DNS-01 challenge providers
│   ├── CertManager.Service/            # .NET Worker Service, scheduler, and IPC host
│   └── CertManager.Desktop/            # WPF desktop UI (MVVM), assets, and Fluent styles
├── tests/
│   └── CertManager.UnitTests/          # xUnit test suite (Core, Dns, Notifications, Discovery, Database, IPC)
├── dist/                               # Published single-file binaries
├── docs/                               # Architecture decision records and implementation plans
└── CertManager_Project_Specification_v2.md # Complete engineering specification
```

---

## 🗺️ Roadmap & Milestones

- [x] **Phase 0 & 1: Foundation & Shell**
  - Modular clean architecture solution setup
  - SQLite database with idempotency and workflow locking
  - Length-prefixed Named Pipe IPC with Windows role authorization
  - Modern animated WPF dashboard and status monitor
- [x] **Phase 2: IIS Discovery & Site Enumeration**
  - Non-destructive site discovery via `Microsoft.Web.Administration`
  - Identification of existing hostnames, ports, and HTTPS bindings
  - Detection of unencrypted HTTP sites needing certificates
- [x] **Phase 3: ACME Issuance Engine (HTTP-01)**
  - RFC 8555 client protocol integration (`Certes`)
  - Let's Encrypt Staging and Production directory switching
  - Non-intrusive `.well-known/acme-challenge` HTTP-01 responder with web.config MIME mapping
- [x] **Phase 4: IIS Deployment & Rollback**
  - Transactional HTTPS binding installation on port 443 with SNI
  - Automatic rollback to previous certificate if commit fails
  - Interactive Desktop Certificate Wizard modal dialog
- [x] **Phase 5: Automated Background Renewals**
  - Autonomous background worker renewal scheduler loop with lease locks (`renew:cert:{id}`)
  - Percentage-based renewal threshold evaluator (33% lifetime threshold, 90d/45d/6d) & ARI support
  - Certificates Inventory View with lifetime progress bars, expiration badges, and force-renewal
- [x] **Phase 6: DNS-01 & Wildcard Certificate Management**
  - `IDnsProvider` abstraction with RFC 8555 `_acme-challenge` TXT calculation
  - Cloudflare API integration with scoped API token auth, zone discovery, TXT provisioning, and automatic cleanup
  - DNS-over-HTTPS (DoH) public propagation verifier
  - Support for wildcard certificates (`*.domain.com`) and apex + wildcard SANs
  - Certificate Wizard UI support for DNS-01 and Cloudflare tokens
- [x] **Phase 7: Monitoring, Notifications & Site Auto-Discovery**
  - Notification dispatcher supporting SMTP, Telegram, and Windows Event Log channels
  - Duplicate alert suppression (cooldown cache) and secret masking
  - End-to-end HTTPS endpoint health checker with certificate chain inspection
  - IIS Site Auto-Discovery engine matching active certificates and wildcards against new websites
  - Modern Activity Logs & Notifications UI in Desktop application

---

## 📄 License

This project is licensed under the [Apache License 2.0](LICENSE).
