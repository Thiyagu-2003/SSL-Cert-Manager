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

### 1. Pre-built Executables
Single-file executables are available directly in the `dist/` directory:

- **Desktop UI**: `dist/CertFlow/CertFlow.exe`
- **Background Service**: `dist/CertFlow/Service/CertManager.Service.exe`

### 2. Building from Source
Ensure [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) is installed.

```powershell
# Clone the repository
git clone https://github.com/your-org/CertFlow.git
cd CertFlow

# Build the entire solution
dotnet build

# Run unit tests
dotnet test

# Launch the Background Service (Terminal 1)
dotnet run --project src/CertManager.Service

# Launch the Desktop UI (Terminal 2)
dotnet run --project src/CertManager.Desktop
```

---

## 📂 Repository Structure

```text
├── src/
│   ├── CertManager.Core/               # Domain entities, enums, IDN normalizer, abstractions
│   ├── CertManager.Application/        # Use cases and workflow orchestrators
│   ├── CertManager.Infrastructure/     # EF Core SQLite database, Named Pipe IPC, Windows auth
│   ├── CertManager.Acme/               # ACME client abstractions and challenge workflows
│   ├── CertManager.Integrations.Iis/   # Microsoft.Web.Administration IIS discovery and bindings
│   ├── CertManager.Service/            # .NET Worker Service and IPC host
│   └── CertManager.Desktop/            # WPF desktop UI (MVVM), assets, and Fluent styles
├── tests/
│   └── CertManager.UnitTests/          # xUnit test suite (Core, Database, IPC, Normalization)
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
- [ ] **Phase 2: IIS Discovery & Site Enumeration** *(Next Up)*
  - Non-destructive site discovery via `Microsoft.Web.Administration`
  - Identification of existing hostnames, ports, and HTTPS bindings
  - Detection of unencrypted HTTP sites needing certificates
- [ ] **Phase 3: ACME Staging Issuance (HTTP-01)**
  - Let's Encrypt staging order creation and challenge response
- [ ] **Phase 4: IIS Deployment & Rollback**
  - Transactional binding installation with automatic fallback
- [ ] **Phase 5: Automated Background Renewals**
  - Unattended renewals via Windows Worker Service

---

## 📄 License

This project is licensed under the [Apache License 2.0](LICENSE).
