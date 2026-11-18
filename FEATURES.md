# CertFlow — Feature Inventory & Architectural Reference

> **CertFlow** is a modern, high-performance automated SSL/TLS certificate lifecycle manager designed specifically for Microsoft Windows and IIS environments. It combines kernel-level HTTP.sys challenge handling, automated IIS binding synchronization, ACME Renewal Information (ARI) monitoring, pre-flight network diagnostics, and team alerting into a unified, lightweight desktop application and background service.

---

## 🌟 Executive Summary: CertFlow vs. Competitors

| Feature Category | CertFlow (v2.1) | Traditional GUI ACME Tools | win-acme (wacs) |
| :--- | :--- | :--- | :--- |
| **User Interface** | Modern WPF, vector icons, dark accent sidebar | Legacy WPF Metro, dense tabbed dialogs | Console / CLI interactive only |
| **Challenge Engine** | Embedded HTTP.sys (`http://+:80/.well-known/...`) | Embedded HTTP.sys server | Self-hosted HTTP listener or IIS webroot |
| **Pre-Flight Diagnostics** | 🔍 1-Click "Test Readiness" (DNS, IP, Port 80, HTTP.sys) | Internal retry loop (no standalone UI test) | Basic verification flags |
| **IIS Hardening** | 🔀 1-Click "Enforce HTTPS Redirect" in `web.config` | Manual configuration / scripts required | Plugin available |
| **Certificate Export** | 📦 1-Click PFX, PEM (CRT+KEY for Nginx/Apache), & CCS | Custom PowerShell scripts required | CLI flags / manual export |
| **Team Alerts & Webhooks** | 🔔 Native Discord, Slack, Teams, & Telegram cards | Paid / complex email relays | Email / script execution |
| **Multi-Domain & Wildcards** | HTTP-01 + Cloudflare DNS-01 + Manual DNS | HTTP-01 + PoshACME plugins | HTTP-01 + DNS plugins |
| **Runtime Architecture** | Standalone In-Process OR Windows Service | Strict client-service requirement (`127.0.0.2:9696`) | Scheduled Task |
| **ACME ARI Support** | ✅ RFC-draft ACME Renewal Information (ARI) | ✅ ARI supported | ⚠️ Basic schedule window |
| **Unit Test Coverage** | 101 automated xUnit tests passing | Closed source core / partial repo | Open source tests |

---

## 🛠️ Complete Feature Inventory

### 1. ACME Protocol & Certificate Issuance
* **RFC 8555 Standard Compliance**: Full automated ACME order creation, authorizations, challenge responses, and certificate finalization.
* **Dual Validation Modes**:
  * **HTTP-01**: Automatic web server challenge validation over HTTP port 80.
  * **DNS-01**: Automatic TXT record provisioning for apex and wildcard certificates (`*.domain.com`).
* **Cloudflare DNS Provider Integration**: Instant automatic DNS-01 verification with automatic TXT cleanup upon order completion.
* **Manual DNS Fallback**: Interactive DNS-01 instructions for unsupported DNS registrars.
* **Multi-Environment Support**:
  * **Let's Encrypt Production**: Official public trusted certificates.
  * **Let's Encrypt Staging**: Safe test environment with generous rate limits.
  * **Self-Signed / Local Development**: Instant RSA-2048 local certificate generator for test environments with zero external dependencies.

---

### 2. Pre-Flight Domain Readiness Diagnostics *(NEW)*
* **1-Click "Test Readiness" Engine**: Non-destructive pre-flight test before contacting ACME CAs to prevent hitting Let's Encrypt rate limits (`failed validation limit`).
* **DNS Resolution Check**: Queries public DNS servers to resolve domain A/AAAA records.
* **Public IP Verification**: Discovers the server's external public IP (via `api.ipify.org` / `icanhazip.com` with local NIC fallback) and confirms DNS points to this machine.
* **Port 80 Reachability Probe**: Verifies that port 80 is listening and accessible through Windows Firewall.
* **HTTP.sys Loopback Challenge Simulator**: Sends an internal loopback probe with custom `Host` headers to verify that ACME challenge responses can be answered.
* **Actionable Recommendations**: Displays clear diagnostic suggestions (e.g. router port forwarding, DNS A-record updates, or switching to DNS-01 for wildcards).

---

### 3. Embedded HTTP.sys Challenge Server *(NEW)*
* **Kernel-Mode Interception**: Listens dynamically on `http://+:80/.well-known/acme-challenge/` using Windows HTTP.sys kernel listener.
* **Longest-Prefix Routing**: Intercepts ACME challenge requests directly in memory, answering incoming Let's Encrypt probes with zero port conflicts.
* **Independent of IIS State**: Answers challenges even if the target IIS site is stopped, listening on a non-standard port (e.g. 3345, 8080, 9999), or blocked by application pool crashes.
* **Dual-Path Webroot Fallback**: Concurrently writes token files to both the target site folder and `C:\inetpub\wwwroot` with an optimized `web.config` (`mimeMap fileExtension="." mimeType="text/plain"`, `<allow users="*" />`) ensuring 100% answer reliability.

---

### 4. Windows IIS Server Integration
* **Auto-Discovery**: Scans local IIS installations and identifies all websites, ports, hostnames, and current SSL/TLS coverage.
* **Zero-Downtime SNI Binding**: Deploys certificates directly into IIS port 443 HTTPS bindings with Server Name Indication (SNI) enabled.
* **Rollback Protection**: If an IIS binding update fails, CertFlow restores the previous certificate and binding configuration.
* **1-Click "Enforce HTTPS Redirect" *(NEW)***: Toggle switch in the Websites view that injects or removes the standard URL Rewrite rule in `web.config`, redirecting all HTTP traffic to HTTPS permanently.

---

### 5. Certificate Inventory & Multi-Format Export *(NEW)*
* **Windows Certificate Store Management**: Installs and verifies certificates in `LocalMachine\My` and `CurrentUser\My`.
* **Multi-Format Export**:
  * **PFX / PKCS#12**: Standard Windows bundle with optional password protection.
  * **PEM Bundle (CRT + KEY)**: Exports `domain.crt`, `domain.key`, and `domain.fullchain.pem` for Nginx, Apache, Node.js, and Docker environments.
  * **Central Certificate Store (CCS)**: Automatically copies `<domain>.pfx` to the Windows Central Certificate Store folder.
* **Visual Expiry Radar**: Badges showing remaining days (`🟢 >30 days`, `🟡 Renewal Due`, `🔴 Expired`) with real-time countdown progress.

---

### 6. Automated Renewal & ARI Monitoring
* **ACME Renewal Information (ARI)**: Polls the ACME CA for optimal certificate renewal windows rather than arbitrary static dates.
* **Configurable Renewal Thresholds**: Default 33% remaining lifetime policy (typically renewing at 60 days for a 90-day certificate).
* **Background Windows Service**: Headless worker service (`Worker.cs`) that monitors, renews, and re-binds certificates autonomously.
* **Dual Deployment Model**: Runs standalone in-process out-of-the-box, or installs as a background Windows Service (`CertFlow.exe --install-service`).

---

### 7. Observability, Audit Logs & Notifications
* **Real-Time Audit Trail**: Complete log of certificate issuance, challenge attempts, and binding updates in SQLite (`certmanager.db`).
* **KPI Metric Strip**: Real-time counters for Total Events, Informational, Warnings, and Critical Errors.
* **Export & Search**: Instant full-text search, level filter (Errors, Warnings, Info), CSV export, and TXT raw log export.
* **Team Webhook Notifications *(NEW)***:
  * **Discord Webhooks**: Rich embedded cards with color-coded alerts (green, amber, red) and timestamps.
  * **Slack Webhooks**: Slack message attachments with formatted alert fields.
  * **Microsoft Teams / Generic JSON**: Universal JSON webhook support for incident management systems.
* **Telegram & SMTP Integration**: Built-in bot alerts and email notifications with secret masking (`Bearer ***`, `token=***`).

---

### 8. Modern Desktop Experience
* **Vector XAML Icons**: Clean, resolution-independent SVG vector path geometry throughout all 10 views (zero blurry emojis or raster bitmaps).
* **Global Account Setup**: Configure Let's Encrypt email and environment once across all websites without repetitive prompts.
* **Built-in Guides & Tutorials**: Step-by-step documentation for IIS setup, DNS-01 Cloudflare automation, router port forwarding, and background service setup.
