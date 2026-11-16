<div align="center">

  <img src="src/CertManager.Desktop/Assets/certflow_logo.svg" width="128" height="128" alt="CertFlow Logo" />

  # CertFlow

  ### **Enterprise-Grade, Autonomous SSL/TLS Lifecycle & IIS Automation for Windows**

  [![License: Apache 2.0](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
  [![.NET 8.0 LTS](https://img.shields.io/badge/.NET-8.0%20LTS-512BD4.svg?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%2F11%20%7C%20Server-0078D6.svg?logo=windows&logoColor=white)](https://www.microsoft.com/windows)
  [![Single File Exe](https://img.shields.io/badge/Packaging-True%20Single%20EXE-0284C7.svg)]()
  [![Unit Tests](https://img.shields.io/badge/Tests-77%20Passing-16A34A.svg)](tests/)
  [![ACME RFC 8555 & 8737](https://img.shields.io/badge/ACME-RFC%208555%20%7C%20RFC%208737%20ARI-0EA5E9.svg)]()

  <br/>

  [![Download CertFlow.exe](https://img.shields.io/badge/Download-CertFlow.exe%20Single--File%20Binary-0284C7?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/Thiyagu-2003/SSL-Cert-Manager/raw/main/dist/CertFlow/CertFlow.exe)

  <p align="center">
    <b>A self-contained, open-source Windows certificate orchestration engine.</b><br/>
    Combines a hardware-accelerated desktop interface, a 24/7 background Windows Service, transactional IIS binding deployment with automated rollback, and real-time ACME Renewal Information (ARI) management.
  </p>

</div>

---

## 📖 Context & Why CertFlow?

Managing SSL/TLS certificates across Windows Server and Microsoft IIS environments has historically presented major operational hurdles:

* **Manual Friction & Outages**: System administrators frequently rely on manual GUI wizards or brittle PowerShell scripts to renew certificates every 90 days. A single missed renewal leads to downtime, broken HTTPS, and browser security warnings.
* **Binding Corruption Risks**: Updating an IIS HTTPS binding with an invalid certificate or improper SNI configuration can knock production websites offline.
* **Heavy Enterprise Tools**: Existing commercial solutions often require complex server infrastructure, agent dependencies, external web consoles, or expensive annual licenses.
* **Decoupling Gaps**: Many free ACME tools require leaving a console window open, or lack a clear administrative UI to review certificate health, inventory, and renewal schedules.

### How CertFlow Solves This

**CertFlow** bridges modern automation standards with Windows enterprise reliability:

1. **True Single-File Executable (`CertFlow.exe`)**: A 100% self-contained binary (~74 MB) with no pre-requisite .NET runtimes, external DLLs, or complex installers.
2. **Dual-Mode Autonomous Architecture**: Operates seamlessly as an official 24/7 Windows Service or as an in-process GUI engine for non-service environments.
3. **Transactional Deployment with Rollback**: Validates new IIS bindings and automatically rolls back to the previous known-good certificate if binding validation or TLS handshakes fail.
4. **Next-Generation ACME with ARI**: Adopts **RFC 8737 (ACME Renewal Information)** alongside dynamic percentage-based renewal windows (e.g., 33% remaining lifetime), moving beyond rigid 30-day timers.
5. **Multi-Protocol Challenges**: Full out-of-the-box support for both HTTP-01 (with non-intrusive `web.config` handlers) and DNS-01 (Cloudflare API with public DoH propagation verification + manual DNS).

---

## 🏛️ Architecture & System Design

CertFlow is structured according to **Clean Architecture** and **Domain-Driven Design (DDD)** principles:

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        CertFlow Desktop Client                         │
│       WPF / MVVM • Subpixel ClearType Display • Hardware-Accelerated    │
└───────────────────────────────────┬────────────────────────────────────┘
                                    │
               Authenticated Windows Named Pipe IPC (\\.\pipe\CertManager.Ipc)
               Length-Prefixed Framing • Windows Group RBAC (Administrators)
                                    │
┌───────────────────────────────────▼────────────────────────────────────┐
│                  CertFlow Core Automation Host                         │
│      Windows Service Worker Loop OR In-Process Engine Controller       │
└──────┬──────────────────────┬──────────────────────────┬───────────────┘
       │                      │                          │
┌──────▼──────┐        ┌──────▼──────┐            ┌──────▼──────┐
│ Application │        │ Persistence │            │ Integrations│
│ Workflows   │        │   SQLite    │            │             │
│ • Renewal   │        │ • WAL Mode  │            │ • IIS (MWA) │
│ • Discovery │        │ • Lease Lock│            │ • Cloudflare│
│ • Health    │        │ • Audit Log │            │ • Let's Enc │
└─────────────┘        └─────────────┘            │ • SMTP/Tg   │
                                                  └─────────────┘
```

### Key Architectural Tenets

* **Zero-Latency Named Pipe IPC**: Client and service communicate over an authenticated, length-prefixed Windows Named Pipe (`CertManager.Ipc`). Access is strictly enforced via Windows Security Descriptors (`BUILTIN\Administrators`).
* **Resilient SQLite Persistence**: Uses an embedded SQLite database configured with Write-Ahead Logging (WAL) and distributed renewal lease locks (`renew:cert:{id}`) to guarantee idempotency and prevent race conditions.
* **Transactional IIS Binding Provider**: Direct integration with `Microsoft.Web.Administration` ensures atomic binding updates on port 443 with Server Name Indication (SNI) and automatic rollback.
* **Proactive Multi-Channel Notifications**: Real-time operational dispatching across SMTP Email, Telegram Bot, and Windows Event Log channels with rate-limiting and token masking.

---

## ✨ Feature Highlights

### 🛡️ One-Click IIS Discovery & Inventory
- Scans local Microsoft IIS servers to discover all configured websites, hostnames, ports, and protocol bindings.
- Identifies unencrypted HTTP endpoints and recommends tailored certificate profiles.
- Automatic site auto-discovery maps newly added IIS sites to existing wildcard or apex certificates.

### ⚡ Flexible ACME Issuance (HTTP-01 & DNS-01)
- Seamless Let's Encrypt integration with instant switching between **Staging** (safe rate-limit testing) and **Production**.
- **HTTP-01**: Automatically provisions `.well-known/acme-challenge` tokens without disrupting existing IIS handlers.
- **DNS-01**: Automates wildcard certificates (`*.domain.com`) via Cloudflare API with scoped tokens, zone detection, and RFC 8484 DNS-over-HTTPS (DoH) propagation verification.

### 🔄 Intelligent Autonomous Renewals
- Autonomous background worker evaluates certificate renewal eligibility continuously.
- Supports **RFC 8737 ACME Renewal Information (ARI)** to honor CA-suggested renewal windows during early revocation or CA maintenance.
- Fallback to dynamic percentage-based thresholds (default: 33% of certificate lifetime) or fixed calendar countdowns.

### 🎨 Modern Fluent User Experience
- Crisp, pixel-aligned WPF ClearType typography optimized for high-DPI displays.
- Real-time breathing pulse status beacon reflecting IPC and background worker health.
- Quick-action workflows: New Certificate Wizard, IIS Scanner, and Staging Pre-flight tests.

---

## 🚀 Quick Start & Usage

### 📥 Direct Download (Single-File Binary)

> [!TIP]
> **Click below to download the latest compiled single-file executable:**
> 
> ### ⬇️ **[Download CertFlow.exe](https://github.com/Thiyagu-2003/SSL-Cert-Manager/raw/main/dist/CertFlow/CertFlow.exe)** *(Self-Contained x64 Windows Executable, ~74 MB)*
> 
> * **Direct Repository Link**: [`dist/CertFlow/CertFlow.exe`](dist/CertFlow/CertFlow.exe)
> * **SHA-256 Checksums**: [`dist/CertFlow/SHA256SUMS.txt`](dist/CertFlow/SHA256SUMS.txt)

---

### Option A: Standalone Single Executable (Ready to Run)

CertFlow is compiled as a self-contained executable with the .NET runtime and native SQLite engine embedded.

1. **Download**: Grab `CertFlow.exe` directly via **[Download CertFlow.exe](https://github.com/Thiyagu-2003/SSL-Cert-Manager/raw/main/dist/CertFlow/CertFlow.exe)** (or from [`dist/CertFlow/CertFlow.exe`](dist/CertFlow/CertFlow.exe)).
2. Verify integrity against [`dist/CertFlow/SHA256SUMS.txt`](dist/CertFlow/SHA256SUMS.txt).
3. **Run**:
   - **Double-click `CertFlow.exe`** to launch the desktop interface.
   - If running without the Windows Service installed, the engine will automatically start in-process (or click **⚡ Start In-Process Engine** on the dashboard).

### Option B: 24/7 Autonomous Windows Service

Run CertFlow headless in the background to ensure certificates renew even when no user is logged into the server.

> [!IMPORTANT]
> Administrative privileges are required to register and run Windows Services and interact with Microsoft IIS bindings.

Open an elevated PowerShell or Command Prompt:

```powershell
# 1. Register and start the background Windows Service
.\CertFlow.exe --install-service

# 2. Verify service status in Windows Services manager
Get-Service -Name "CertFlow"

# 3. Open the UI to monitor operations
.\CertFlow.exe

# 4. (Optional) Remove the Windows Service
.\CertFlow.exe --uninstall-service
```

### CLI Reference

| Flag / Command | Description |
|---|---|
| *(none)* | Launches the desktop graphical user interface (GUI). |
| `--service` | Runs the headless background engine directly (invoked by Windows Service Control Manager). |
| `--install-service` | Registers `CertFlow` with Windows Service Control Manager (`sc.exe`) set to Automatic start. |
| `--uninstall-service` | Gracefully stops and deletes the `CertFlow` Windows Service registration. |
| `--help` / `-h` | Displays command-line help and usage information. |

---

## 🛠️ Building & Developing from Source

### Prerequisites
- [Windows 10 / 11 or Windows Server 2016+](https://www.microsoft.com/windows)
- [.NET 8.0 SDK (LTS)](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft IIS](https://www.iis.net/) (for live binding integration tests; unit tests run independently)

### 1. Clone & Build
```powershell
# Clone the repository
git clone https://github.com/your-org/CertFlow.git
cd CertFlow

# Build solution in Debug configuration
dotnet build
```

### 2. Run Test Suite
CertFlow includes a comprehensive suite of **50 unit and integration tests** covering domain models, ACME workflows, IIS discovery, SQLite persistence, and Named Pipe IPC:

```powershell
dotnet test
```

### 3. Publish Single-File Executable
To bundle the self-contained single `.exe` (matching the official release binary in `dist/CertFlow/`):

```powershell
dotnet publish src/CertManager.Desktop/CertManager.Desktop.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:IncludeAllContentForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=none `
  -o ./dist/CertFlow/
```

Generate release SHA256 checksums:
```powershell
powershell -ExecutionPolicy Bypass -File "scripts/generate-checksums.ps1"
```

---

## 📂 Project Organization

```text
CertFlow/
├── src/
│   ├── CertManager.Core/               # Domain models, enums, IDN normalizer, interfaces
│   ├── CertManager.Application/        # Use cases, orchestrators, workflows (renewal, discovery)
│   ├── CertManager.Infrastructure/     # SQLite DB, Named Pipe IPC, Health, Notification dispatcher
│   ├── CertManager.Acme/               # RFC 8555 ACME client engine, ARI (RFC 8737), HTTP-01/DNS-01
│   ├── CertManager.Integrations.Iis/   # Microsoft.Web.Administration binding provider & rollback
│   ├── CertManager.Integrations.Dns/   # Cloudflare API integration, DoH verifier, manual DNS
│   ├── CertManager.Service/            # Worker background service, scheduler, IPC server
│   └── CertManager.Desktop/            # WPF desktop UI (MVVM), ClearType typography, assets
├── tests/
│   └── CertManager.UnitTests/          # xUnit test suite (50 passing tests)
├── dist/                               # Published release binaries and SHA256 checksums
├── scripts/                            # Icon generation, checksum, and automation scripts
├── docs/                               # Architectural plans and specifications
└── README.md
```

---

## 🔒 Security & Safe Operation

* **Least Privilege**: Named Pipe endpoints enforce Windows Access Control Lists (ACLs) restricted to administrators and authorized operators.
* **Secret Protection**: API tokens, private keys, and SMTP credentials are never printed to console logs or unencrypted outputs.
* **Non-Destructive Operations**: IIS discovery performs read-only inspection. ACME challenge files and DNS TXT records are cleaned up immediately following authorization.
* **Idempotent Locks**: Renewal jobs acquire distributed database leases, preventing simultaneous renewal attempts across multiple processes.

---

## 📄 License

This project is licensed under the **Apache License 2.0**. See the [LICENSE](LICENSE) file for complete details.
