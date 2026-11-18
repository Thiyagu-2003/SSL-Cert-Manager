# CertFlow — Feature Inventory & Architectural Reference

> **CertFlow** is a modern, high-performance automated SSL/TLS certificate lifecycle manager designed specifically for Microsoft Windows and IIS environments. It combines kernel-level HTTP.sys challenge handling, automated IIS binding synchronization, ACME Renewal Information (ARI) monitoring, pre-flight network diagnostics, and team alerting into a unified, lightweight desktop application and background service.

---

## 🌟 Executive Summary: CertFlow vs. Competitors

| Feature Category | CertFlow (v2.2) | Traditional GUI ACME Tools | win-acme (wacs) |
| :--- | :--- | :--- | :--- |
| **User Interface** | Modern WPF, vector icons, dark accent sidebar | Legacy WPF Metro, dense tabbed dialogs | Console / CLI interactive only |
| **Challenge Engine** | Embedded HTTP.sys (`http://+:80/.well-known/...`) | Embedded HTTP.sys server | Self-hosted HTTP listener or IIS webroot |
| **Pre-Flight Diagnostics** | 🔍 1-Click "Test Readiness" (DNS, IP, Port 80, HTTP.sys) | Internal retry loop (no standalone UI test) | Basic verification flags |
| **IIS Hardening & HSTS** | 🔀 1-Click "Enforce HTTPS Redirect" + HSTS in `web.config` | Manual configuration / scripts required | Plugin available |
| **Schannel TLS Hardener** | 🛡️ 1-Click Windows Registry TLS 1.3/1.2 & Insecure Cipher Blocker | Requires 3rd-party tools (e.g. IIS Crypto) | Not included |
| **Certificate Export** | 📦 1-Click PFX, PEM (CRT+KEY for Nginx/Apache), & CCS | Custom PowerShell scripts required | CLI flags / manual export |
| **Team Alerts & Webhooks** | 🔔 Native Discord, Slack, Teams, Telegram, Pushover, Gotify & SMTP | Paid / complex email relays | Email / script execution |
| **Multi-CA & Failover** | 🌐 Instant Switcher: Let's Encrypt, ZeroSSL, Google, BuyPass + Auto-Failover | Multi-tab configuration / complex setup | CLI argument flags |
| **Modern Cryptography** | ⚡ 1-Click ECDSA (P-256 / P-384) + Auto-Prune Superseded Certs | Legacy RSA 2048 default / unbounded store bloat | Manual flags / basic store |
| **Multi-Domain & Wildcards** | HTTP-01 + Cloudflare, AWS Route 53, Azure DNS, Manual DNS | HTTP-01 + PoshACME plugins | HTTP-01 + DNS plugins |
| **Remote Desktop (RDP)** | 🖥️ 1-Click Bind Certificate to Windows RDP Listener | Manual WMI / PowerShell required | Plugin / script required |
| **Live TLS Diagnostic** | 🔎 In-App Live TLS Handshake & SSL Labs Grade Check | Not supported (requires external browser) | Not supported |
| **Post-Renewal Hooks** | ⚙️ PowerShell (`.ps1`), Batch (`.bat`), & Service Restarts | PowerShell scripts only | Process execution |
| **Encrypted Vault Backup** | 🔐 1-Click AES-256-GCM Encrypted Vault Export/Import | Unencrypted XML/JSON registry exports | File copies |
| **Runtime Architecture** | Standalone In-Process OR Windows Service | Strict client-service requirement (`127.0.0.2:9696`) | Scheduled Task |
| **ACME ARI Support** | ✅ RFC-draft ACME Renewal Information (ARI) | ✅ ARI supported | ⚠️ Basic schedule window |
| **Unit Test Coverage** | 128 automated xUnit tests passing | Closed source core / partial repo | Open source tests |

---

## 🛠️ Complete Feature Inventory

### 1. ACME Protocol & Multi-CA Engine *(NEW)*
* **RFC 8555 Standard Compliance**: Full automated ACME order creation, authorizations, challenge responses, and certificate finalization.
* **Instant Multi-CA Switcher**: 1-click switcher between trusted public Certificate Authorities:
  * **Let's Encrypt Production**: Official public trusted certificates (`https://acme-v02.api.letsencrypt.org/directory`).
  * **ZeroSSL**: Fast DV90 ACME certificates (`https://acme.zerossl.com/v2/DV90`).
  * **Google Trust Services (GTS)**: High-availability Google public ACME CA (`https://dv.acme-v02.api.pki.goog/directory`).
  * **BuyPass Go SSL**: European CA with 180-day certificates (`https://api.buypass.com/acme/directory`).
  * **Custom ACME Directory**: Enterprise or private internal ACME endpoints (e.g. Smallstep, HashiCorp Vault, Microsoft Active Directory CS).
  * **Self-Signed / Local Development**: Instant local certificate generator for test environments with zero external dependencies.
* **Automatic Multi-CA Failover**: If the primary CA encounters rate limits (`too many orders`), maintenance downtime, or authorization errors, CertFlow automatically fails over to the designated secondary CA (e.g. ZeroSSL or Google Trust Services) without burning rate limits or failing deployment.
* **Dual Validation Modes**:
  * **HTTP-01**: Automatic web server challenge validation over HTTP port 80.
  * **DNS-01**: Automatic TXT record provisioning for apex and wildcard certificates (`*.domain.com`).
* **Cloudflare DNS Provider Integration**: Instant automatic DNS-01 verification with automatic TXT cleanup upon order completion.
* **Manual DNS Fallback**: Interactive DNS-01 instructions for unsupported DNS registrars.

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

### 6. Modern Cryptography & Automated Store Pruning *(NEW)*
* **Next-Gen Elliptic Curve Cryptography (ECDSA)**:
  * **ECDSA P-256 (ES256)**: 10x faster TLS handshakes, smaller certificates, and modern browser compliance.
  * **ECDSA P-384 (ES384)**: High-security curve for enterprise compliance (CNSA / FIPS requirements).
  * **RSA 2048 & RSA 4096**: Full backward compatibility for legacy clients and older intermediate infrastructure.
* **Automated Certificate Store Pruning**:
  * Seamlessly cleans up superseded historical certificates for the domain from `LocalMachine\My`.
  * Prevents Windows Certificate Store bloat, TLS performance degradation, and administrative clutter while preserving the active binding thumbprint.

---

### 7. Automated Renewal & ARI Monitoring
* **ACME Renewal Information (ARI)**: Polls the ACME CA for optimal certificate renewal windows rather than arbitrary static dates.
* **Configurable Renewal Thresholds**: Default 33% remaining lifetime policy (typically renewing at 60 days for a 90-day certificate).
* **Background Windows Service**: Headless worker service (`Worker.cs`) that monitors, renews, and re-binds certificates autonomously.
* **Dual Deployment Model**: Runs standalone in-process out-of-the-box, or installs as a background Windows Service (`CertFlow.exe --install-service`).

---

### 8. Observability, Notifications & Team Alerts *(EXPANDED)*
* **Dedicated Notifications View**: Full-featured desktop management console (`Settings & System -> Notifications & Alerts`) to configure, enable/disable, and test all notification channels.
* **1-Click "Send Test" Verification**: Immediate test dispatch with real-time feedback indicator confirming endpoint connectivity.
* **Broadest Alert Channel Ecosystem**:
  * **Microsoft Teams**: Office 365 Connector MessageCards and Adaptive Cards with color severity bands and activity facts.
  * **Discord Webhooks**: Rich color-coded embeds with timestamps and footer attribution.
  * **Slack Incoming Webhooks**: Formatted attachments with status colors and markdown.
  * **Telegram Bot API**: Instant markdown messages delivered to private chats or DevOps groups.
  * **Pushover**: Instant mobile push notifications to iOS and Android devices with priority levels.
  * **Gotify**: Private self-hosted push notifications for internal homelabs and Kubernetes clusters.
  * **Generic Custom Webhooks**: Structured JSON POST payloads for Zapier, n8n, Make, Datadog, and PagerDuty.
  * **Email / SMTP Relay**: Direct SMTP server alerts with credentials masking.
* **Automated Expiry & Lifecycle Triggers**:
  * **Configurable Expiry Warnings**: 30-day, 14-day, 7-day, 3-day, and 1-day advance warnings with remaining time counters.
  * **Autonomous Renewal Succeeded**: Real-time notification with new certificate thumbprint and expiration dates.
  * **Autonomous Renewal Failed**: Critical alert dispatched immediately upon ACME or DNS validation errors.
  * **Secret Masking**: Automatically redacts API keys, Bearer tokens, and passwords from logs and payloads.
* **Real-Time Audit Trail**: Complete log of certificate issuance, challenge attempts, and binding updates in SQLite (`certmanager.db`).
* **KPI Metric Strip**: Real-time counters for Total Events, Informational, Warnings, and Critical Errors.
* **Export & Search**: Instant full-text search, level filter (Errors, Warnings, Info), CSV export, and TXT raw log export.

---

### 9. Modern Desktop Experience
* **Vector XAML Icons**: Clean, resolution-independent SVG vector path geometry throughout all views (zero blurry emojis or raster bitmaps).
* **Global Account Setup**: Configure Let's Encrypt email and environment once across all websites without repetitive prompts.
* **Built-in Guides & Tutorials**: Step-by-step documentation for IIS setup, DNS-01 Cloudflare automation, router port forwarding, and background service setup.

---

### 10. 1-Click Windows TLS & Schannel Security Hardening *(NEW)*
* **Native Windows Schannel Registry Optimizer**: Eliminates the need for separate tools to secure Windows Server.
* **1-Click TLS 1.3 & 1.2 Enforcer**: Explicitly enables modern TLS 1.2 and TLS 1.3 protocol handlers for Server and Client subkeys.
* **Legacy Protocol Deprecation**: Disables SSL 2.0, SSL 3.0, TLS 1.0, and TLS 1.1 across `HKLM\SYSTEM\CurrentControlSet\Control\SecurityProviders\SCHANNEL\Protocols`.
* **Insecure Cipher & Hash Elimination**: Blocks RC4 (128/64/56/40), Triple DES 168 (3DES), DES, NULL ciphers, and MD5 hashes.
* **PCI-DSS & HIPAA Compliance**: Instant evaluation and automated compliance scoring achieving Qualys SSL Labs A+ grade.
* **1-Click HSTS Deployment**: Enforces HTTP Strict Transport Security (`max-age=31536000; includeSubDomains; preload`) directly into IIS site bindings and `web.config`.

---

### 11. Live In-App TLS Health & Handshake Diagnostic Probe *(NEW)*
* **Zero-Browser Verification**: Tests live HTTPS websites directly from the CertFlow UI without opening external browsers or third-party web scanners.
* **Port 443 Handshake Probe**: Performs an authentic client TLS handshake sending Server Name Indication (SNI) and negotiates cipher suites in real time.
* **Comprehensive Health Inspection**:
  * **Negotiated Protocol**: Inspects live negotiated TLS version (TLS 1.3 / TLS 1.2).
  * **Negotiated Cipher Suite**: Displays active encryption algorithm and key exchange parameters.
  * **Trust Chain Completeness**: Validates intermediate CA certificates and root CA trust status.
  * **HSTS Header Detection**: Queries HTTP response headers for active `Strict-Transport-Security` enforcement.
  * **Automated SSL Health Grade**: Assigns an instant grade (**A+**, **A**, **B**, **C**, **F**) with actionable remediation tips.

---

### 12. Remote Desktop Services (RDP/RDS) Auto-Binding *(NEW)*
* **Beyond IIS Workloads**: Extends certificate management to Windows Server Remote Desktop Services.
* **1-Click RDP Certificate Deployment**: Automatically updates the Windows Remote Desktop listener (`WinStations\RDP-Tcp\SSLCertificateSHA1Hash`).
* **Private Key Permission Management**: Grants read permissions to `NETWORK SERVICE` ensuring smooth TLS RDP connections.
* **Zero Security Warnings**: Eliminates client-side "The identity of the remote computer cannot be verified" warnings during RDP sessions.

---

### 13. Expanded Cloud DNS-01 Provider Ecosystem *(NEW)*
* **Cloudflare DNS**: Full automated TXT record provisioning and automatic cleanup.
* **AWS Route 53 Integration**: Automated zone discovery and TXT record UPSERT using AWS IAM credentials.
* **Azure DNS Integration**: Resource Group and DNS Zone automated provisioning for Azure-hosted virtual machines and apps.
* **Manual DNS Interactive Mode**: Step-by-step TXT record instructions for unsupported registrars.

---

### 14. Post-Renewal Automation Hooks & Custom Script Triggers *(NEW)*
* **Flexible Automation Hooks**: Executes custom tasks immediately following successful certificate issuance or renewal.
* **Supported Hook Types**:
  * **PowerShell Scripts (`.ps1`)**: Runs scripts with `-ExecutionPolicy Bypass` and parameter interpolation.
  * **Batch Files (`.bat` / `.cmd`)**: Windows shell commands for legacy orchestration.
  * **Windows Service Restarts**: 1-Click safe restarts of dependent services (e.g. `FTPSVC`, `MSExchangeIS`, `W3SVC`, `OpenVPN`).
  * **Custom Executables**: Invokes arbitrary binaries with custom timeout handling and output capture.
* **Dynamic Variable Substitution**: Automatically injects `{Domain}`, `{Thumbprint}`, `{PfxPath}`, and `{Expires}` into script arguments.

---

### 15. 1-Click Encrypted Vault Backup & Server Migration *(NEW)*
* **Disaster Recovery & Migration**: Back up the entire CertFlow state in seconds for seamless transfer between servers.
* **Military-Grade Encryption**: Protects backup archives using **AES-256-GCM** authenticated encryption with PBKDF2 (100,000 SHA256 iterations) and 16-byte random salts.
* **Complete State Preservation**: Archives ACME account private keys, certificate records, notification webhook configurations, and profile policies into a portable `.vault` bundle.
* **1-Click Restore**: Instant passphrase-verified decryption and database restoration on any new server with zero manual re-entry.
