# Open-Source SSL/TLS Certificate Manager — Project Specification

> **Project naming:** The project is intended to be vendor-neutral and community-driven. No final name has been selected. The following four names are candidates for discussion:
>
> 1.  **CertPilot** — suggests guided, automated certificate lifecycle management.
> 2.  **CertFlow** — emphasizes streamlined issuance, deployment, and renewal workflows.
> 3.  **OpenCert Manager** — clearly communicates an open-source certificate management tool.
> 4.  **CertOps** — emphasizes operational automation and management at scale.
>
> These are working name ideas only. Check domain, GitHub organization/repository, and trademark availability before choosing one.
>
> **Working code name:** `CertManager` is used for the solution, project, namespace and folder names in this document so that code can start before a public name is chosen. It is a placeholder. Renaming later is a mechanical change tracked by ADR-002 (Appendix D).
>
> This document describes a generic open-source project. It must not contain company-specific branding, proprietary company assumptions, or vendor lock-in.

## Open-Source Project Identity and Governance

- **Project type:** Public, vendor-neutral open-source software.
- **Suggested license:** Apache License 2.0, subject to maintainer review.
- **Repository:** Public Git repository with tagged releases and a documented release process.
- **Community files:** Include `LICENSE`, `README.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, and issue/PR templates.
- **Contributions:** Accept contributions through reviewed pull requests, automated tests, and documented coding standards.
- **Transparency:** Track feature requests, bugs, security advisories, and roadmap decisions publicly.
- **No vendor lock-in:** Keep certificate data, configuration, and integrations portable; document export and recovery procedures.
- **Naming:** Keep the product name configurable in branding assets until one of the four candidate names is selected.
- **Governance and supply chain:** Decision-making, DCO sign-off, security disclosure, signed releases, SBOMs and dependency policy are defined in Appendix A.

| Field                         | Value                                                                |
|:------------------------------|:---------------------------------------------------------------------|
| Product                       | Open-Source SSL/TLS Certificate Manager                              |
| Document type                 | Product requirements and technical architecture                      |
| Version | 2.0 |
| Status | Proposed — planning baseline (revised in v2.0) |
| Working code name | `CertManager` (placeholder, see ADR-002) |
| Primary platform              | Windows 10/11 and Windows Server                                     |
| Initial server integration    | Microsoft IIS                                                        |
| Future integrations           | Apache, Nginx, Linux, remote agents                                  |
| Primary certificate authority | Let’s Encrypt                                                        |
| Distribution goal             | Free/open-source-friendly, with no mandatory per-server subscription |

> **Document purpose:** This is the master planning document for designing, implementing, testing, packaging and maintaining CertManager (working code name). It is a target specification, not a claim that these features have already been implemented.

------------------------------------------------------------------------

# Document information

## Version history

| Version | Date | Summary |
|:--------|:-----|:--------|
| 1.1 | Earlier | Vendor-neutral planning baseline. |
| 2.0 | 2026-11-15 | Review-driven revision: valid project/namespace names, complete database model, consistent roadmap and releases, decided ACME-engine process, percentage-based renewal policy with ARI, identifier handling (IDN, IPv6, CAA), concurrency and idempotency rules, governance and supply-chain policy, update and telemetry policy, requirement IDs with traceability, competitor comparison, effort estimates, glossary, and cleaned formatting. |

### Review findings addressed in 2.0

| # | Finding in v1.1 | Resolution in v2.0 |
|:--|:----------------|:-------------------|
| 1 | Invalid project and namespace names; three different product names | Single code name `CertManager`; see Section 21 and ADR-002 |
| 2 | No tables for ACME orders, challenges or workflow state | New tables in Section 16 |
| 3 | Profile table missing fields required by Section 8.4 | `CertificateProfiles` and `ProfileDomains` redesigned in Section 16 |
| 4 | `ValidationMethod` duplicated on domains and profiles | Single source of truth rules in Section 16.2 |
| 5 | Phases and releases misaligned; MVP notification channel undefined; installer too late | Section 4.1, 15.2 and 23.1 |
| 6 | ACME engine decision deferred without a process | Section 6.3 time-boxed spike and ADR-001 |
| 7 | Fixed 30-day renewal threshold | Section 9.3 percentage-based policy with ARI |
| 8 | Requirements had no IDs | Appendix B register and Section 24.0 |
| 9 | IDN, IPv6, CAA and cross-profile locking missing | Sections 10.5 and 10.6 |
| 10 | Governance thin | Appendix A |
| 11 | No application update mechanism or telemetry policy | Section 25.4 |
| 12 | Markdown rendering errors | Whole document normalized |
| 13 | No table of contents, changelog or glossary | This section, Appendix C |
| 14 | No effort estimates or competitor comparison | Sections 4.5 and 23.1 |

## Contents

- [1. Executive summary](#1-executive-summary)
- [2. Important certificate and domain concepts](#2-important-certificate-and-domain-concepts)
- [3. Cost model](#3-cost-model)
- [4. Product scope and release strategy](#4-product-scope-and-release-strategy)
- [5. Users, roles and permissions](#5-users-roles-and-permissions)
- [6. Technology stack](#6-technology-stack)
- [7. System architecture](#7-system-architecture)
- [8. Core modules](#8-core-modules)
- [9. Certificate lifecycle workflows](#9-certificate-lifecycle-workflows)
- [10. Wildcard and SAN management](#10-wildcard-and-san-management)
- [11. DNS provider architecture](#11-dns-provider-architecture)
- [12. IIS integration](#12-iis-integration)
- [13. Other server integrations](#13-other-server-integrations)
- [14. Multi-server management](#14-multi-server-management)
- [15. Monitoring and notifications](#15-monitoring-and-notifications)
- [16. Database design](#16-database-design)
- [17. Security design](#17-security-design)
- [18. Performance and reliability requirements](#18-performance-and-reliability-requirements)
- [19. User interface specification](#19-user-interface-specification)
- [20. Logging, diagnostics and support](#20-logging-diagnostics-and-support)
- [21. Project structure](#21-project-structure)
- [22. Suggested interfaces](#22-suggested-interfaces)
- [23. Development roadmap and acceptance criteria](#23-development-roadmap-and-acceptance-criteria)
- [24. Testing strategy](#24-testing-strategy)
- [25. Packaging, installation and upgrades](#25-packaging-installation-and-upgrades)
- [26. Operational requirements](#26-operational-requirements)
- [27. Product quality metrics](#27-product-quality-metrics)
- [28. Risks and mitigations](#28-risks-and-mitigations)
- [29. Documentation deliverables](#29-documentation-deliverables)
- [30. Definition of done for the first production release](#30-definition-of-done-for-the-first-production-release)
- [31. Final product vision](#31-final-product-vision)
- [32. Official technical references](#32-official-technical-references)
- [Appendix A: Open-source governance and supply-chain policy](#appendix-a-open-source-governance-and-supply-chain-policy)
- [Appendix B: Requirements register and traceability](#appendix-b-requirements-register-and-traceability)
- [Appendix C: Glossary](#appendix-c-glossary)
- [Appendix D: ADR index](#appendix-d-adr-index)

---

# 1. Executive summary

Open-Source SSL/TLS Certificate Manager is intended to be a reliable SSL/TLS certificate lifecycle management application. It will automate certificate discovery, issuance, validation, installation, binding configuration, renewal, monitoring and reporting.

The first release will focus on Windows and IIS. The architecture must leave room for additional web servers, DNS providers, certificate authorities and remote managed servers without requiring a rewrite of the core.

The product should aim to be faster and easier to operate than manual certificate workflows and competitive with products such as Certify The Web. “Better” must be demonstrated through measurable reliability, usability, supported integrations, resource use and recovery behaviour—not assumed in advance.

## 1.1 Product goals

1.  Issue free certificates using compatible ACME certificate authorities.
2.  Support single-domain, SAN and wildcard certificates.
3.  Automate HTTP-01 and DNS-01 validation, and support TLS-ALPN-01 where the chosen server adapter can safely implement it.
4.  Install certificates into the appropriate certificate store.
5.  Configure and verify IIS HTTPS bindings.
6.  Renew certificates before expiry without requiring the desktop UI to remain open.
7.  Safely update all bindings using a renewed certificate.
8.  Support multiple domains and multiple websites.
9.  Support multiple DNS providers through modular plugins.
10. Provide secure credential and private-key handling.
11. Provide useful logs, audit history, notifications and recovery.
12. Design for future remote-server management and other web servers.

## 1.2 Non-goals for the first release

- Becoming a public certificate authority.
- Selling certificates or reselling a certificate authority’s service.
- Replacing DNS hosting.
- Automatically creating arbitrary public DNS zones without provider authorization.
- Guaranteeing that a domain is publicly reachable.
- Automatically changing firewall, router or NAT configuration without explicit administrator approval.
- Managing every possible load balancer or hosting platform in the first release.
- Claiming compatibility with every DNS provider before its integration is tested.

------------------------------------------------------------------------

# 2. Important certificate and domain concepts

## 2.1 Root domain, hostname and wildcard

For the domain `example.com`:

| Certificate identifier             | Covered hostname examples                                 |
|:-----------------------------------|:----------------------------------------------------------|
| `example.com`                      | `example.com` only                                        |
| `www.example.com`                  | `www.example.com` only                                    |
| `*.example.com`                    | `www.example.com`, `api.example.com`, `admin.example.com` |
| `*.example.com` plus `example.com` | Both the root domain and first-level subdomains           |

A wildcard identifier such as `*.example.com` does **not** cover `example.com` itself. It also does not cover nested names such as `test.api.example.com`.

If both the apex/root domain and first-level subdomains are needed, request both `example.com` and `*.example.com` as identifiers in the same certificate order, subject to CA policy.

## 2.2 A wildcard certificate does not create DNS records

A wildcard certificate proves authorization for the requested DNS identifier. It does not:

- Create `api.example.com` in DNS.
- Point the hostname to the server.
- Create an IIS website.
- Configure a reverse proxy.
- Open firewall ports.
- Make an unreachable server publicly accessible.

Those are separate infrastructure tasks. The application may help detect missing DNS or IIS configuration, but must not imply that certificate issuance performs those tasks.

## 2.3 Validation methods

### HTTP-01

- The CA requests a challenge response over HTTP.
- Typically requires inbound TCP port 80 to reach the challenge responder.
- Suitable for many ordinary hostnames.
- Does not support wildcard issuance.
- The application must avoid disrupting existing IIS sites while temporarily serving challenge responses.

### DNS-01

- The application creates a TXT record under `_acme-challenge`.
- The CA checks the public DNS record.
- Required for wildcard certificates.
- Useful when port 80 is unavailable or the server is not directly exposed.
- Requires DNS API access or a manual DNS workflow.
- Must wait for DNS propagation and verify the exact challenge value before completing validation.

### TLS-ALPN-01

- Uses a special TLS handshake on port 443.
- Must be implemented only where the server adapter can safely handle the challenge.
- May conflict with an existing HTTPS listener, proxy or load balancer.
- Must not be advertised as universally supported.

------------------------------------------------------------------------

# 3. Cost model

## 3.1 Expected software costs

The initial architecture can be built using free development tools and open-source components:

| Item                       |                                     Expected cost | Notes                                     |
|:---------------------------|--------------------------------------------------:|:------------------------------------------|
| Let’s Encrypt certificates |                                              Free | Subject to CA policies and rate limits    |
| ACME protocol              |                                   No protocol fee | Open standard                             |
| .NET SDK                   |                                              Free | Check current supported release lifecycle |
| WPF                        |    Included with .NET Windows desktop development | Windows desktop only                      |
| SQLite                     |                                              Free | Embedded database                         |
| xUnit                      |                                              Free | Test framework                            |
| Serilog                    |                                  Free/open source | Verify package licence                    |
| WiX Toolset                |                                  Free/open source | Verify version and packaging requirements |
| DNS APIs                   |                      Often free for basic API use | Provider plan and API limits vary         |
| Existing Windows server    | No additional server purchase if already licensed | Existing Windows licensing still applies  |

Potential costs include:

- Domain registration and renewal.
- Windows Server licences or additional server capacity.
- Optional code-signing certificate for a trusted publisher identity.
- Optional cloud hosting for centralized management.
- Paid DNS provider plans or premium DNS features.
- Monitoring, email or messaging services if a paid plan is selected.
- Development, testing, support and maintenance time.

The application itself should not charge per certificate or per managed server in the initial product model. Certificate authority rate limits still apply.

## 3.2 Certificate authority limits

Let’s Encrypt has rate limits, which can change. The implementation must read current official CA documentation before production rollout. Use staging endpoints for development and integration testing. Never repeatedly retry production issuance in a tight loop.

The system should implement:

- Exponential backoff.
- Retry ceilings.
- Rate-limit error recognition.
- Per-account and per-domain request throttling.
- Clear operator guidance when a CA limit is reached.

------------------------------------------------------------------------

# 4. Product scope and release strategy

## 4.1 MVP (first usable release)

The MVP should include:

1.  Windows desktop application.
2.  Windows background service, with a minimal installer from Phase 1 (full packaging hardening in Phase 10).
3.  Secure local communication between UI and service.
4.  SQLite database and migrations.
5.  IIS discovery.
6.  ACME account management.
7.  Let’s Encrypt staging and production configuration.
8.  Single-domain certificate issuance.
9.  HTTP-01 validation.
10. Certificate installation into Windows Certificate Store.
11. IIS HTTPS binding management.
12. Manual renewal action.
13. Scheduled renewal.
14. Logs and renewal history.
15. Basic expiry alerts shown in the dashboard and written to the Windows Event Log (external channels such as SMTP and Telegram arrive in Release 2).
16. Safe rollback for failed binding updates.

## 4.2 Release 2

- DNS-01.
- Cloudflare DNS integration.
- AWS Route 53 integration.
- SAN certificates.
- Wildcard certificates.
- Automatic discovery of newly created IIS websites.
- Multiple websites sharing a suitable certificate.
- Telegram and configurable SMTP notifications (the first external notification channels).
- Certificate export with secure handling.
- More complete monitoring and health checks.

## 4.3 Release 3

- Multiple managed Windows servers.
- Remote agent enrollment.
- Centralized management.
- Role-based access control.
- Additional ACME certificate authorities.
- Additional DNS provider plugins.
- Apache and Nginx adapters.
- Linux agent.

## 4.4 Future releases

- REST API.
- Web dashboard.
- Organization and tenant management.
- Kubernetes ingress integrations.
- Reverse-proxy and load-balancer integrations.
- Certificate inventory and reporting across an organization.

## 4.5 Competitive context and parity checklist

The project aims to be competitive with existing Windows ACME tools. The table below is a **starting point for research**, not a claim. Every cell marked "verify" must be checked against the current version of each product before the comparison is published.

| Aspect | CertManager target | Certify The Web | win-acme | Posh-ACME |
|:-------|:-------------------|:----------------|:---------|:----------|
| Interface | Desktop UI plus independent service | Desktop UI plus service (verify) | Console wizard and scheduled task (verify) | PowerShell module (verify) |
| Primary platform | Windows and IIS first; Linux later | Windows (verify) | Windows (verify) | Cross-platform PowerShell (verify) |
| Licensing model | Apache-2.0, no per-server fee | Verify free and paid tiers | Verify | Verify |
| Typical user | Administrators who want a GUI plus safe automation | Verify | Administrators comfortable with CLI | Scripters and DevOps |

**Parity checklist (research task before Release 2 planning):** automatic IIS binding, DNS-01 provider coverage, wildcard support, renewal without the UI open, notification channels, rollback behaviour, multi-server management, pre/post-deployment scripting hooks, and scripting or API access. For each item record which tools support it, and whether CertManager needs it for parity or can deliberately differ.

**Differentiation hypotheses to validate (not assumed):**

- Strict separation of UI and privileged service with authenticated, audited IPC.
- Persisted, resumable workflows with transactional rollback of IIS binding changes.
- Least-privilege service identity and protected secret storage.
- Portable data with documented export and recovery.

Do not publish superiority claims until measured under comparable conditions (see Section 27).

------------------------------------------------------------------------

# 5. Users, roles and permissions

## 5.1 Local roles

### Administrator

- Configure ACME accounts and DNS providers.
- Issue, renew, revoke and deploy certificates.
- Manage service settings.
- Register servers.
- View and export logs.
- Change security settings.

### Operator

- View certificates and websites.
- Request permitted issuance or renewal operations.
- Review task history.
- Cannot view or export private keys.
- Cannot change protected credentials unless granted permission.

### Read-only auditor

- View certificate inventory.
- View renewal and audit history.
- Export non-secret reports.
- Cannot modify configuration or initiate certificate operations.

The MVP may initially use Windows local group membership rather than a full application user database. The authorization design must nevertheless be explicit.

**Windows group mapping (MVP).** The installer creates three local groups. The service evaluates the caller's group membership on **every request**, not only when the pipe connects.

| Role | Windows group | Notes |
|:-----|:--------------|:------|
| Administrator | `BUILTIN\Administrators` and `CertManager Administrators` | Full control |
| Operator | `CertManager Operators` | No private-key export; no protected-credential changes |
| Read-only auditor | `CertManager Auditors` | Read and non-secret export only |

If a caller belongs to several groups, the highest role applies. A caller in none of these groups is denied. Membership changes are visible in the Windows Security log and are referenced in the application audit log when the service first sees the new role.

## 5.2 Permission checks

Every privileged operation must be authorized by the service, not merely hidden or disabled in the UI. The service must verify the caller’s Windows identity and permitted operation.

------------------------------------------------------------------------

# 6. Technology stack

## 6.1 Recommended initial stack

| Layer                    | Technology                                                    | Purpose                                     |
|:-------------------------|:--------------------------------------------------------------|:--------------------------------------------|
| Desktop UI               | C# WPF                                                        | Native Windows desktop experience           |
| Runtime                  | .NET 10 or the latest supported LTS selected at project start | Application runtime                         |
| Background service       | .NET Worker Service                                           | Independent scheduling and execution        |
| Domain/application logic | C# class libraries                                            | Business rules and workflows                |
| Database                 | SQLite                                                        | Local persistence                           |
| Data access              | EF Core or a small repository layer                           | Data persistence                            |
| Logging                  | Serilog or Microsoft.Extensions.Logging                       | Structured logs                             |
| IIS integration          | Microsoft.Web.Administration                                  | IIS discovery and binding changes           |
| Certificate APIs         | System.Security.Cryptography.X509Certificates                 | Certificate inspection and store operations |
| ACME                     | Maintained ACME library or external win-acme integration      | ACME protocol operations                    |
| DNS                      | Provider-specific adapters                                    | DNS-01 automation                           |
| Scheduling               | Hosted service with persisted due jobs; optionally Quartz.NET | Renewal scheduling                          |
| Installer                | WiX Toolset                                                   | MSI installation                            |
| Tests                    | xUnit                                                         | Unit and integration tests                  |

**Version policy:** Verify the currently supported .NET release and package compatibility when implementation begins. Do not hard-code an obsolete runtime merely because it appeared in an early proposal.

## 6.2 ACME implementation strategy

Two approaches are possible.

### Approach A — integrate win-acme first

Advantages:

- Mature Windows ACME client.
- Faster path to a working IIS-focused prototype.
- Existing challenge and renewal functionality can reduce initial engineering effort.

Risks:

- Process invocation and output parsing need careful handling.
- Version compatibility and configuration management must be tested.
- Its licence and redistribution requirements must be reviewed before bundling or distributing it.
- Avoid creating two competing schedulers or two sources of truth.

### Approach B — implement ACME through a maintained .NET library

Advantages:

- More direct integration with application workflows.
- Better control over UI progress, errors and persistence.
- Easier to build a unified job and audit model.

Risks:

- More development and protocol testing.
- Need to implement robust account, order, challenge, key, CSR and renewal flows.
- Must handle protocol changes and CA-specific behaviour.

### Recommendation

Build an `ICertificateAuthorityClient` abstraction first. Do not write a new ACME protocol implementation unless the spike in Section 6.3 shows a clear technical reason. The engine choice is made through ADR-001 using the process below, not left open.

## 6.3 ACME engine decision process (ADR-001)

**Time box:** 2 weeks in Phase 0. **Decision owners:** the maintainers. **Output:** ADR-001 merged before Phase 3 starts.

**Candidates:** win-acme (external process or reference), plus at least one maintained .NET ACME library. Verify each candidate's current maintenance status, licence and release history at spike time; do not rely on this document for that.

**Spike scope (identical for each candidate):**

1. Register an account against the Let's Encrypt staging directory.
2. Issue a single-domain certificate with HTTP-01 against a test IIS site, at least 50 runs.
3. Simulate failures: network drop, challenge failure, rate-limit response, process kill mid-order.
4. Check that the engine can be driven through `ICertificateAuthorityClient` without leaking engine-specific types.
5. Check support for ARI, external account binding and CA profile selection (record as supported, partial or unsupported).

**Scoring (weights total 100):**

| Criterion | Weight | Evidence |
|:----------|-------:|:---------|
| Reliability in the 50-run test and failure simulations | 25 | Success rate, error clarity |
| Fit with the service model (embedding, persistence, progress, cancellation) | 20 | Spike code review |
| Licence and redistribution compatibility with Apache-2.0 | 15 | Licence review (a fail disqualifies the candidate) |
| Maintenance activity and bus factor | 15 | Release and issue history |
| Testability (mocking, staging tests) | 10 | Spike tests |
| Feature coverage (DNS-01, ARI, EAB, profiles) | 10 | Feature matrix |
| Security of integration (no secrets on command lines, input handling) | 5 | Threat review |

**Decision rule:** highest weighted score wins, provided it scores at least 70 and has no licence failure. If no candidate reaches 70, implement a thin ACME client on top of a maintained library behind `ICertificateAuthorityClient`, and record the justification in ADR-001. Whatever is chosen, there must be exactly one scheduler and one source of truth for orders (our database).

------------------------------------------------------------------------

# 7. System architecture

## 7.1 Logical components

### Desktop application

Responsibilities:

- Display dashboard and inventory.
- Create and edit certificate profiles.
- Discover local IIS websites.
- Show task progress and operation results.
- Configure notifications.
- Request actions from the service.
- Display logs and audit history.

The UI must not own the renewal scheduler. Closing the desktop application must not stop renewals.

### Windows service

Responsibilities:

- Load persisted jobs.
- Schedule and execute renewal tasks.
- Run challenge workflows.
- Request and validate certificates.
- Install certificates.
- Update server bindings.
- Run health checks.
- Record operation history.
- Deliver notifications.
- Recover safely after restart.

### Core application layer

Contains business workflows and validation rules. It should not depend directly on WPF or IIS-specific APIs.

### Infrastructure layer

Contains SQLite repositories, Windows certificate-store operations, logging, secret protection, IPC and operating-system integration.

### Provider and server adapters

Implement ACME authority, DNS provider, challenge provider, server deployment and notification interfaces.

## 7.2 Architecture diagram

``` text
+-----------------------------+
|     WPF Desktop Client      |
| Dashboard / Wizard / Logs   |
+--------------+--------------+
               |
        Authenticated IPC
       (Windows Named Pipes)
               |
+--------------v--------------+
|      Windows Service        |
| Job Scheduler / Workflows   |
+--------------+--------------+
               |
       +-------+--------+
       |                |
+------v------+  +------v-------+
| Application |  | Persistence  |
| Workflows   |  | SQLite       |
+------+------+\ +--------------+
       |
+------v--------------------------------+
| Provider and Integration Abstractions |
+------+------------+-------------------+
       |            | 
  +----v----+  +----v-----+  +-----------+
  | ACME CA |  | DNS API  |  | IIS       |
  | Client  |  | Adapters |  | Adapter   |
  +---------+  +----------+  +-----------+
       |
  +----v-------------------------------+
  | Certificate Store / Health Checks  |
  +------------------------------------+
```

## 7.3 Communication

Use Windows Named Pipes for local desktop-to-service communication in the MVP.

Security requirements:

- Restrict pipe access with Windows ACLs.
- Authenticate the caller using Windows identity.
- Authorize every command.
- Validate request payloads.
- Apply request size and timeout limits.
- Do not expose a listening unauthenticated TCP port.
- Record privileged operations in the audit log.

For remote management, use a separate authenticated and encrypted agent protocol. Do not expose the local named pipe over the network.

------------------------------------------------------------------------

# 8. Core modules

## 8.1 Dashboard module

Display:

- Total certificates.
- Valid certificates.
- Certificates expiring soon.
- Expired certificates.
- Failed renewal jobs.
- Websites without HTTPS.
- Registered servers.
- Recent operations.
- Last successful renewal.
- Current service status.

Dashboard queries should be indexed and paginated. Avoid loading every log record at startup.

## 8.2 Domain management

Functions:

- Add a domain.
- Add SAN identifiers.
- Identify apex/root and wildcard names.
- Associate DNS provider.
- Select validation method.
- Check public DNS resolution.
- Check HTTP reachability where applicable.
- Detect duplicate or conflicting profiles.
- Display validation prerequisites.

The application must distinguish a domain name from a website and from a DNS zone. A domain can have many hostnames and a hostname can be hosted by a server not managed by the application.

## 8.3 ACME account management

Functions:

- Register account.
- Store account key securely.
- Associate account with CA directory URL.
- Configure contact email.
- Select staging or production.
- View account status.
- Support account key rollover if supported by the ACME client.
- Deactivate an account where supported.

Store the ACME account reference and protected key material. Never log account private keys.

## 8.4 Certificate profile management

A certificate profile describes the intended certificate request and deployment.

Suggested fields:

- Profile name.
- Certificate authority.
- ACME account.
- Domain identifiers.
- Validation method.
- DNS provider.
- Key algorithm and size/curve.
- Renewal threshold (percentage of certificate lifetime) and ARI preference.
- Target server.
- Target certificate store.
- Deployment adapter.
- Notification policy.
- Enabled/disabled state.

A profile is configuration; an issued certificate is a concrete certificate instance. Keep these separate in the database.

## 8.5 Certificate inventory

Display:

- Friendly name.
- Subject.
- Subject Alternative Names.
- Issuer.
- Serial number.
- Thumbprint/fingerprint.
- Not-before date.
- Not-after date.
- Key algorithm.
- Key availability.
- Certificate chain status.
- Store location.
- Server.
- Deployment status.
- Renewal profile.

Do not expose private key material in the inventory UI.

## 8.6 Certificate actions

- Issue.
- Renew now.
- Re-deploy.
- Inspect.
- Export, only with explicit authorization and secure handling.
- Revoke, with strong confirmation and reason.
- Disable automation.
- Remove application metadata.
- Remove certificate from store, only after checking dependencies and confirming intent.

Deleting application metadata must not silently delete a certificate from the operating-system store.

------------------------------------------------------------------------

# 9. Certificate lifecycle workflows

## 9.1 Issuance workflow

1.  Validate profile configuration.
2.  Validate domain identifier syntax.
3.  Check duplicate profile and active order state.
4.  Verify the selected ACME account.
5.  Create ACME order.
6.  Create the required challenge response.
7.  Complete domain validation.
8.  Generate private key and CSR according to policy.
9.  Finalize the ACME order.
10. Retrieve the issued certificate chain.
11. Verify SANs, dates, chain and key association.
12. Install certificate in the target store.
13. Deploy to the selected server adapter.
14. Run a health check.
15. Record certificate, deployment and audit history.
16. Schedule renewal.

Every step should have a persisted state so that a service restart can recover or safely mark the operation for review.

## 9.2 Renewal workflow

1.  Load the certificate profile and current certificate.
2.  Confirm renewal is due.
3.  Check CA rate-limit and retry state.
4.  Confirm credentials and provider availability.
5.  Request a replacement certificate.
6.  Validate the new certificate and private key.
7.  Install the new certificate without removing the old one.
8.  Update all managed bindings that reference the old certificate.
9.  Verify the affected endpoints.
10. Roll back failed binding updates where possible.
11. Retain the old certificate for a configurable safety period.
12. Record result and schedule the next evaluation.

## 9.3 Renewal policy

Certificate lifetimes are getting shorter across the industry, and CAs may offer short-lived certificates. Fixed day counts therefore do not work. All thresholds are **fractions of the certificate's actual lifetime** (`NotAfter - NotBefore`). Verify current CA lifetime policy when implementing.

| Setting | Suggested default | Notes |
|:--------|:------------------|:------|
| Renewal evaluation | At least daily; hourly for certificates with a lifetime of 10 days or less | Configurable |
| Renewal target | When 33% or less of the lifetime remains | Per-profile override via `RenewalThresholdPercent` |
| ARI | Use the CA's suggested renewal window when available, with jitter | ACME Renewal Information extension (RFC 9773, verify CA support). Fall back to the percentage rule if ARI is unavailable or errors |
| Warning alert | 15% remaining | |
| Critical alert | 8% remaining | |
| Emergency alert | 3% remaining | |
| Retry | Exponential backoff with jitter | Total retry budget must fit inside the remaining lifetime |
| Concurrent renewals | Configurable, conservative default (2) | |

Worked examples:

| Certificate lifetime | Renewal starts | Warning | Critical | Emergency |
|:---------------------|:---------------|:--------|:---------|:----------|
| 90 days | about 30 days left | about 13.5 days | about 7.2 days | about 2.7 days |
| 45 days | about 15 days left | about 6.8 days | about 3.6 days | about 1.4 days |
| 6 days | about 2 days left | about 22 hours | about 11.5 hours | about 4.3 hours |

For 90-day certificates these defaults closely reproduce the earlier 30 / 14 / 7 / 3 day schedule. They are configurable policy defaults, not CA requirements. The application must use the actual certificate dates, an ARI window when offered, and current CA policy.

An ARI signal that a certificate should be replaced early (for example after a revocation event) triggers renewal immediately, subject to rate-limit checks.

## 9.4 Retry policy

- Retry transient network and provider errors.
- Do not rapidly retry invalid domain, invalid credential or authorization errors.
- Respect `Retry-After` when supplied.
- Add random jitter to avoid synchronized retries.
- Apply per-profile and global retry ceilings.
- Alert on persistent failures.
- Keep the last working certificate and binding active.

------------------------------------------------------------------------

# 10. Wildcard and SAN management

## 10.1 Wildcard request

For `example.com`, the common wildcard request is:

- `example.com`
- `*.example.com`

The DNS-01 challenge is required for wildcard issuance.

## 10.2 Multiple websites using one certificate

A certificate can be reused by multiple IIS websites when:

- The certificate SAN covers each hostname.
- The private key is available to the relevant server process.
- The server is the same machine or the certificate is securely deployed to each relevant machine.
- The IIS binding configuration is correct.

For remote servers, each server requires a secure certificate deployment workflow. Do not assume a certificate installed on one server is available on another.

## 10.3 Automatic website discovery

When a new IIS website is detected:

1.  Read the website’s configured hostnames.
2.  Normalize and validate each hostname.
3.  Match it against enabled certificate profiles.
4.  Check whether a suitable certificate is installed.
5.  Check whether DNS resolves as expected when applicable.
6.  Show a proposed deployment.
7.  If an administrator has explicitly enabled an approved auto-deployment policy, apply the binding.
8.  Verify HTTPS.
9.  Record the change.

Do not automatically bind a certificate merely because the hostname shares a text suffix with a domain. Use proper DNS name matching and certificate SAN validation.

## 10.4 SNI

Support Server Name Indication for multiple HTTPS websites sharing an IP address and port. Preserve existing bindings and avoid changing IP/port combinations without explicit approval.

## 10.5 Identifier handling (IDN, IPv6, CAA)

**Internationalized domain names (IDN).**

- Accept Unicode or A-label (punycode) input. Store the normalized, lower-case A-label form. Display the Unicode form alongside the A-label.
- Use IDNA 2008 rules with UTS #46 mapping (non-transitional). Reject invalid labels.
- Strip a trailing dot. Enforce length limits (63 characters per label, 253 in total).
- A wildcard is valid only as the entire left-most label (`*.example.com`).
- Flag mixed-script or confusable names for administrator review.
- IP-address identifiers are out of scope for the MVP.

**IPv6.**

- Parse IIS binding addresses in IPv4, IPv6 and wildcard (`*`) forms, and preserve them exactly when updating bindings.
- Before HTTP-01, test reachability for **every** A and AAAA record of the identifier. A stale AAAA record is a common cause of validation failure; warn about it with a clear message.
- DNS-01 is unaffected by web-server IPv6 reachability.

**CAA records.**

- Before creating an order, query CAA records for each identifier (walking up the DNS tree) and check that the selected CA is permitted. Use `issuewild` for wildcard identifiers.
- If CAA explicitly forbids the CA, fail early with an actionable message instead of burning a CA request. If the lookup fails, warn and continue.
- Also detect a CNAME at `_acme-challenge` (delegated validation) and tell the administrator which zone will receive the TXT record.

## 10.6 Concurrency, idempotency and conflict control

**Lock keys.** The service uses persisted locks (table `WorkflowLocks`, with lease expiry so a crash never leaves a stale lock):

| Lock | Key | Protects |
|:-----|:----|:---------|
| Identifier lock | normalized identifier + CA environment | Duplicate or competing orders |
| ACME account lock | account ID | Order creation rate and account state |
| DNS owner-name lock | zone + `_acme-challenge` owner name | Simultaneous TXT changes |
| Server lock | server ID | IIS and certificate-store changes (see Section 12.3) |

**Lock ordering** (to avoid deadlock): account, then identifiers (sorted alphabetically), then DNS owner name, then server.

**Order idempotency key.** `SHA-256` of: sorted normalized identifiers, CA authority ID, ACME account ID, key algorithm and validation method. If an active order with the same key exists, the workflow resumes or waits for it instead of creating another. This also protects against CA duplicate-certificate limits.

**Overlapping profiles.**

- Saving a profile whose identifiers, CA environment and account match another enabled profile is blocked as a duplicate.
- A wildcard profile that also covers names owned by another profile is allowed but flagged.
- An IIS binding has exactly one owning profile (`IisBindings.OwnerProfileId`). If two profiles want the same binding, the conflict is shown to the administrator; nothing is silently reassigned.

------------------------------------------------------------------------

# 11. DNS provider architecture

## 11.1 Provider interface

A DNS adapter should provide operations conceptually equivalent to:

``` csharp
public interface IDnsProvider
{
    Task<IReadOnlyList<DnsZone>> GetZonesAsync(
        CancellationToken cancellationToken);

    Task<DnsRecord> CreateTxtRecordAsync(
        string zoneId,
        string name,
        string value,
        CancellationToken cancellationToken);

    Task DeleteTxtRecordAsync(
        string zoneId,
        string recordId,
        CancellationToken cancellationToken);

    Task<bool> IsTxtRecordVisibleAsync(
        string name,
        string expectedValue,
        CancellationToken cancellationToken);
}
```

This is an illustrative contract. Provider APIs differ, so the final interface may need capabilities for record IDs, TTL, zone discovery, delegated challenge zones and cleanup.

## 11.2 Initial DNS integrations

### Cloudflare

- Use scoped API tokens.
- Restrict token to required zone and DNS edit permissions.
- Discover the zone.
- Create the TXT record.
- Poll authoritative/public DNS visibility.
- Delete only the record created for the current challenge.

### AWS Route 53

- Use least-privilege IAM permissions.
- Limit permissions to the intended hosted zone where possible.
- Create TXT record changes.
- Wait for change propagation.
- Verify the public record.
- Remove only the challenge record owned by the current operation.

## 11.3 Manual DNS mode

Manual DNS mode should be supported as a fallback:

- Display exact TXT record name and value.
- Provide copy buttons.
- Allow the user to request verification.
- Explain that issuance cannot complete until the record is publicly visible.
- Provide a safe timeout and cleanup procedure.

## 11.4 DNS safety

- Never store API tokens in plain text.
- Never delete unrelated TXT records.
- Handle multiple simultaneous challenges at the same owner name.
- Avoid overwriting records created by another process.
- Preserve existing TXT values.
- Record created record IDs and ownership.
- Clean up challenge records after completion or safe timeout.

------------------------------------------------------------------------

# 12. IIS integration

## 12.1 Discovery

Use Microsoft.Web.Administration to enumerate:

- Sites.
- Site IDs.
- Site state.
- Bindings.
- Hostnames.
- Protocols.
- Ports.
- IP addresses.
- SSL flags.
- Application pools where needed.

## 12.2 Certificate store

Support appropriate Windows certificate stores, normally `LocalMachine\My` for server certificates, subject to deployment requirements.

The service must:

- Verify the certificate has a private key.
- Set appropriate private-key permissions.
- Avoid broad key ACLs.
- Confirm certificate validity and SAN coverage.
- Keep certificate thumbprints and store locations in metadata.

## 12.3 Binding update

1.  Capture the existing binding.
2.  Confirm intended hostname, IP and port.
3.  Confirm SNI setting.
4.  Confirm the new certificate is installed and usable.
5.  Apply the binding update.
6.  Read back the binding.
7.  Test the endpoint.
8.  Roll back if the operation fails.

IIS configuration changes should be serialized or protected by a suitable lock to avoid conflicting concurrent modifications.

## 12.4 Website safety

- Do not delete sites or application pools.
- Do not change application pool settings as a side effect of SSL deployment.
- Do not replace unrelated certificates.
- Do not remove a certificate still referenced by another binding.
- Require explicit confirmation for destructive operations.

------------------------------------------------------------------------

# 13. Other server integrations

## 13.1 Apache

Potential deployment support:

- Certificate and chain file placement.
- Private-key file placement.
- VirtualHost configuration validation.
- Graceful reload.
- Post-deployment HTTPS test.

## 13.2 Nginx

Potential deployment support:

- Certificate and full-chain file placement.
- Private-key file placement.
- Configuration test using the server’s validation command.
- Graceful reload.
- HTTPS verification.

## 13.3 Linux

Use a remote agent or secure supported transport. Define file permissions, service reloads, secret storage and rollback for each supported distribution.

These integrations are future phases. Do not claim support until each server/version combination has automated tests.

------------------------------------------------------------------------

# 14. Multi-server management

## 14.1 Architecture

A central management component may coordinate multiple agents. The local MVP should not require cloud infrastructure.

Each remote server runs a lightweight agent that:

- Enrolls using a one-time or administrator-approved credential.
- Establishes authenticated encrypted communication.
- Receives only authorized tasks.
- Executes local certificate and server operations.
- Reports status and logs.
- Rotates credentials.
- Supports revocation and removal.

## 14.2 Remote agent security

- Mutual authentication where practical.
- Per-agent identity and credentials.
- TLS transport.
- Replay protection.
- Short-lived enrollment tokens.
- Least-privilege service account.
- Signed or authenticated updates.
- Audit logs for every remote command.
- No arbitrary unauthenticated shell execution.

## 14.3 Offline behaviour

- Retain local renewal configuration.
- Continue local renewal if the central controller is unavailable, when the architecture and ownership model permit it.
- Queue status updates for later delivery.
- Avoid duplicate issuance when reconnecting.
- Report stale synchronization state.

------------------------------------------------------------------------

# 15. Monitoring and notifications

## 15.1 Monitoring

Monitor:

- Expiry date.
- Certificate chain.
- Hostname match.
- HTTPS availability.
- TLS handshake success.
- Issuer and serial changes.
- Renewal job failures.
- Deployment failures.
- DNS validation readiness.
- Server-agent connectivity.

## 15.2 Notifications

MVP alerts are shown in the dashboard and written to the Windows Event Log (source `CertManager`). Release 2 adds these external channels:

- SMTP email.
- Telegram bot.

Future channels:

- Microsoft Teams.
- Discord.
- Webhooks.

## 15.3 Event types

- Certificate issued.
- Certificate renewed.
- Renewal failed.
- Certificate nearing expiry.
- Certificate expired.
- HTTPS endpoint unavailable.
- Hostname mismatch.
- Certificate chain problem.
- DNS challenge failed.
- Server disconnected.
- Deployment rolled back.

## 15.4 Notification controls

- Configurable severity.
- Duplicate suppression.
- Retry delivery.
- Notification history.
- Test notification.
- Secret masking.
- Per-domain and per-server routing.

------------------------------------------------------------------------

# 16. Database design

SQLite is suitable for the initial local deployment. Use migrations, foreign keys, transactions and indexes. Keep database access behind repository or data-access abstractions.

## 16.1 Proposed tables

### `Servers`

| Column | Purpose |
|:-------|:--------|
| ServerId | Primary key |
| ServerName | Display name |
| Hostname | Machine hostname |
| ServerType | IIS, Apache, Nginx, etc. |
| OperatingSystem | OS details |
| AgentId | Optional remote agent identity |
| ConnectionStatus | Current or last-known status |
| LastConnectedAt | Last successful contact |
| CreatedAt | Creation timestamp |

### `CertificateAuthorities`

| Column | Purpose |
|:-------|:--------|
| AuthorityId | Primary key |
| Name | Display name |
| DirectoryUrl | ACME directory URL (single source for the URL) |
| Environment | Staging or production |
| SupportsAri | Whether ARI was detected |
| Enabled | Availability flag |

### `AcmeAccounts`

| Column | Purpose |
|:-------|:--------|
| AcmeAccountId | Primary key |
| AuthorityId | FK to `CertificateAuthorities` (the directory URL is read from there, not duplicated) |
| ContactEmail | Contact address |
| AccountReference | CA account URL |
| ProtectedKeyReference | Reference to the protected account key |
| Status | Account status |
| CreatedAt | Creation timestamp |

### `DnsProviders`

| Column | Purpose |
|:-------|:--------|
| DnsProviderId | Primary key |
| ProviderName | Cloudflare, Route 53, manual, etc. |
| AccountLabel | Friendly label |
| ProtectedCredentialReference | Secret reference, never the secret |
| ConfigurationJson | Non-secret provider settings |
| Status | Last test result |

### `Domains`

| Column | Purpose |
|:-------|:--------|
| DomainId | Primary key |
| DomainName | Normalized A-label name (see Section 10.5) |
| DisplayName | Unicode display form |
| RootDomain | Associated DNS zone or root |
| DomainType | Apex, hostname, wildcard |
| DnsProviderId | Optional default provider for the zone |
| CreatedAt | Creation timestamp |

`ValidationMethod` is intentionally **not** stored here. See the source-of-truth rules in Section 16.2.

### `CertificateProfiles`

| Column | Purpose |
|:-------|:--------|
| ProfileId | Primary key |
| ProfileName | Friendly name |
| AcmeAccountId | ACME account |
| AuthorityId | Certificate authority |
| DefaultValidationMethod | HTTP-01, DNS-01 or TLS-ALPN-01 (profile default) |
| DefaultDnsProviderId | Default DNS integration |
| KeyAlgorithm | RSA or ECDSA |
| KeySizeOrCurve | For example 2048 or P-256 |
| RenewalThresholdPercent | Percentage of lifetime remaining at which renewal starts (default 33) |
| UseAri | Prefer the CA's ARI window when available |
| ServerId | Target server |
| TargetStoreName | For example `My` |
| TargetStoreLocation | `LocalMachine` or `CurrentUser` |
| DeploymentAdapter | IIS, Apache, Nginx, etc. |
| DeploymentSettingsJson | Adapter-specific, non-secret settings |
| NotificationPolicyId | FK to `NotificationPolicies` |
| Enabled | Automation state |
| CreatedAt | Creation timestamp |

### `ProfileDomains`

| Column | Purpose |
|:-------|:--------|
| Id | Primary key |
| ProfileId | FK to `CertificateProfiles` |
| DomainId | FK to `Domains` |
| IdentifierType | Apex, hostname or wildcard |
| ValidationMethodOverride | Optional per-identifier override of the profile default |
| DnsProviderIdOverride | Optional per-identifier override |

### `Certificates`

| Column | Purpose |
|:-------|:--------|
| CertificateId | Primary key |
| ProfileId | Source profile |
| ServerId | Server holding the certificate |
| AcmeOrderId | The order that produced it |
| PreviousCertificateId | Certificate this one replaced |
| Subject | Certificate subject |
| Issuer | Issuer name |
| SerialNumber | Serial |
| Thumbprint | Certificate fingerprint |
| NotBefore | Validity start |
| NotAfter | Validity end |
| KeyAlgorithm | RSA or ECDSA details |
| StoreName | Windows store |
| StoreLocation | LocalMachine or CurrentUser |
| Status | Current status |
| RetainUntil | End of the safety period for the old certificate (see Section 9.2) |
| CreatedAt | Record creation |

### `CertificateDomains`

| Column | Purpose |
|:-------|:--------|
| Id | Primary key |
| CertificateId | FK to `Certificates` |
| DomainId | FK to `Domains` |
| IdentifierType | Apex, hostname or wildcard |

### `AcmeOrders`

| Column | Purpose |
|:-------|:--------|
| AcmeOrderId | Primary key |
| ProfileId | Owning profile |
| AcmeAccountId | Account used |
| OrderReference | CA order URL |
| IdempotencyKey | Hash defined in Section 10.6 (unique among active orders) |
| Status | Pending, ready, processing, valid, invalid, expired |
| RetryAfterUtc | Earliest next attempt (from `Retry-After` or backoff) |
| CreatedAt | Creation timestamp |
| CompletedAt | Completion timestamp |
| ErrorCode | Machine-readable failure code |

### `AcmeChallenges`

| Column | Purpose |
|:-------|:--------|
| ChallengeId | Primary key |
| AcmeOrderId | FK to `AcmeOrders` |
| DomainId | Identifier being validated |
| ChallengeType | HTTP-01, DNS-01 or TLS-ALPN-01 |
| Status | Pending, processing, valid, invalid |
| ProviderRecordId | DNS record ID or challenge file reference created by us |
| CleanupState | Not needed, pending or done (see Section 11.4) |
| CreatedAt | Creation timestamp |

### `WorkflowSteps`

| Column | Purpose |
|:-------|:--------|
| StepId | Primary key |
| OperationId | FK to `OperationHistory` |
| StepName | One of the steps in Section 9.1 or 9.2 |
| State | Pending, running, completed, failed, needs review |
| Attempt | Attempt counter |
| StartedAt | Start timestamp |
| CompletedAt | Completion timestamp |
| SafeDetails | Non-secret details for recovery decisions |

### `WorkflowLocks`

| Column | Purpose |
|:-------|:--------|
| LockKey | Primary key (see Section 10.6) |
| OwnerOperationId | Operation holding the lock |
| AcquiredAt | Acquisition time |
| LeaseExpiresAt | Lease expiry so crashes never leave a stale lock |

### `IisWebsites`

| Column | Purpose |
|:-------|:--------|
| WebsiteId | Primary key |
| ServerId | FK to `Servers` |
| IisSiteId | IIS site ID |
| WebsiteName | Display name |
| State | Started, stopped, etc. |
| CreatedAt | Creation timestamp |
| LastDiscoveredAt | Last discovery run |

### `IisBindings`

| Column | Purpose |
|:-------|:--------|
| BindingId | Primary key |
| WebsiteId | FK to `IisWebsites` |
| CertificateId | Currently bound certificate |
| OwnerProfileId | The one profile allowed to manage this binding |
| Protocol | http or https |
| Hostname | Host header |
| IpAddress | IPv4, IPv6 or `*` |
| Port | Port |
| SniEnabled | SNI flag |
| LastVerifiedAt | Last successful verification |

### `RenewalJobs`

Scheduling only: when work is next due.

| Column | Purpose |
|:-------|:--------|
| JobId | Primary key |
| ProfileId | Owning profile |
| CertificateId | Current certificate |
| NextRunAt | Next evaluation time |
| LastRunAt | Last evaluation time |
| RetryCount | Retries in the current cycle |
| Status | Scheduled, running, backing off, failed |
| LastErrorCode | Last error |

### `OperationHistory`

User-visible record of operations that ran. Steps live in `WorkflowSteps`; scheduling lives in `RenewalJobs`.

| Column | Purpose |
|:-------|:--------|
| OperationId | Primary key |
| OperationType | Issue, renew, deploy, revoke, etc. |
| ProfileId | Related profile |
| CertificateId | Related certificate |
| StartedAt | Start timestamp |
| CompletedAt | Completion timestamp |
| Status | Result |
| DurationMilliseconds | Duration |
| ErrorCode | Machine-readable error |
| SafeErrorMessage | Message safe to display |

### `NotificationPolicies`

| Column | Purpose |
|:-------|:--------|
| NotificationPolicyId | Primary key |
| Name | Policy name |
| MinimumSeverity | Lowest severity delivered |
| Channels | Enabled channels |
| SuppressionWindowMinutes | Duplicate suppression window |

### `Notifications`

| Column | Purpose |
|:-------|:--------|
| NotificationId | Primary key |
| EventType | Event |
| Channel | Event Log, SMTP, Telegram, etc. |
| DestinationReference | Secret or address reference |
| Status | Result |
| AttemptCount | Delivery attempts |
| CreatedAt | Creation timestamp |
| SentAt | Delivery timestamp |

### `Settings`

| Column | Purpose |
|:-------|:--------|
| Key | Primary key |
| Value | Non-secret value |
| UpdatedAt | Last change |
| UpdatedBy | Actor |

### `AuditLogs`

| Column | Purpose |
|:-------|:--------|
| AuditId | Primary key |
| Actor | Windows identity |
| Action | Operation |
| EntityType | Affected entity type |
| EntityId | Affected entity |
| Timestamp | UTC time |
| Result | Outcome |
| CorrelationId | Workflow correlation |

## 16.2 Database rules

- Use UTC timestamps internally. Convert to local time only for display.
- Store normalized domain names (A-label, lower case).
- Use foreign keys and indexes.
- Do not store raw private keys or DNS tokens in ordinary tables. Store secret references, not secret values.
- Use transactions for certificate deployment metadata changes.
- Add schema migrations. Define retention and cleanup policies.
- Back up the database consistently.

**Source-of-truth rules.**

| Fact | Single source | Others |
|:-----|:--------------|:-------|
| Validation method | `CertificateProfiles.DefaultValidationMethod`, optionally overridden in `ProfileDomains` | `Domains` holds none |
| ACME directory URL | `CertificateAuthorities.DirectoryUrl` | `AcmeAccounts` references the authority |
| Next run time | `RenewalJobs.NextRunAt` | `OperationHistory` is a log, not a schedule |
| Step-level recovery state | `WorkflowSteps` | `OperationHistory` summarizes |
| Binding ownership | `IisBindings.OwnerProfileId` | Profiles list bindings by query |

**Key relationships.** Profile → many `ProfileDomains` → `Domains`. Profile → many `Certificates` (via renewals), each from one `AcmeOrders` row with many `AcmeChallenges`. `Certificates` ↔ `Domains` through `CertificateDomains`. `IisBindings` → `IisWebsites` → `Servers`, and each binding points at one `Certificates` row and one owning profile.

------------------------------------------------------------------------

# 17. Security design

## 17.1 Threat model

Protect against:

- Local unprivileged users invoking privileged service actions.
- Theft of DNS API credentials.
- Theft of ACME account keys.
- Private-key disclosure.
- Malicious or malformed domain input.
- Command injection through external tools.
- Unauthorized remote agent commands.
- Certificate substitution.
- Accidental removal of a working certificate.
- Log leakage of secrets.

## 17.2 Secret storage

Use Windows DPAPI or Windows Credential Manager for local secrets. Prefer machine-scoped protection for service-owned credentials where appropriate.

Requirements:

- Restrict access to the service identity and authorized administrators.
- Never log secret values.
- Mask secrets in UI and diagnostics.
- Support credential rotation.
- Avoid placing secrets in command-line arguments.
- Avoid storing secrets in plain-text configuration files.
- Protect backups containing secret material.

## 17.3 Private keys

- Generate keys using supported cryptographic APIs.
- Prefer non-exportable keys where deployment requirements allow.
- Use appropriate key ACLs.
- Do not export keys unless explicitly requested and authorized.
- Encrypt exported PFX files with strong protection.
- Explain that copying a certificate to another server requires securely transferring its private key or issuing a separate certificate.

## 17.4 Least privilege

The Windows service needs privileges for certificate store and IIS configuration operations. Use a dedicated service identity where feasible and grant only necessary rights. Document any operation that requires LocalSystem or elevated privileges and justify it.

## 17.5 Audit

Record who initiated privileged operations, what changed, which certificate or binding was affected, when it occurred and whether it succeeded. Do not include credentials or private key material in audit records.

------------------------------------------------------------------------

# 18. Performance and reliability requirements

These are proposed targets to validate through profiling; they are not guaranteed specifications.

| Metric              | Initial target                                                      |
|:--------------------|:--------------------------------------------------------------------|
| Service idle memory | Aim below 50 MB                                                     |
| Desktop idle memory | Aim below 150 MB                                                    |
| Dashboard load      | Under 2 seconds for 1,000 certificate records on reference hardware |
| Service recovery    | Resume persisted jobs after restart                                 |
| Renewal evaluation  | Daily by default                                                    |
| UI responsiveness   | No blocking network or certificate operations on UI thread          |
| Database queries    | Indexed and paginated                                               |
| Parallel jobs       | Configurable and bounded                                            |

## 18.1 Performance practices

- Use asynchronous I/O.
- Use bounded job queues.
- Avoid tight polling loops.
- Persist job state.
- Use cancellation tokens.
- Apply timeouts to network calls.
- Reuse HTTP clients appropriately.
- Cache discovery results with explicit invalidation.
- Paginate logs and inventories.
- Avoid unnecessary IIS rescans.
- Dispose cryptographic and file resources correctly.

## 18.2 Reliability practices

- Idempotent job execution.
- Correlation IDs for workflows.
- Transactional metadata updates.
- Safe retry policy.
- Checkpointed workflow states.
- Health checks after deployment.
- Rollback where possible.
- Preserve the previous working certificate.
- Detect and recover from service restarts.

------------------------------------------------------------------------

# 19. User interface specification

## 19.1 Navigation

1.  Dashboard
2.  Certificates
3.  Certificate Profiles
4.  Domains
5.  IIS Websites
6.  Servers
7.  DNS Providers
8.  ACME Accounts
9.  Renewal Jobs
10. Monitoring
11. Notifications
12. Logs and Audit
13. Settings
14. About

## 19.2 Dashboard cards

- Total certificates.
- Healthy certificates.
- Expiring soon.
- Expired.
- Renewal failures.
- HTTPS failures.
- Managed servers.
- Pending operations.

## 19.3 Certificate list

Columns:

- Domain/common name.
- SAN count.
- Certificate type.
- Issuer.
- Expiry.
- Days remaining.
- Server.
- Store.
- Deployment status.
- Renewal status.

Actions:

- View details.
- Issue.
- Renew.
- Deploy.
- Inspect.
- Export (authorized only).
- Revoke.
- Disable profile.

## 19.4 Certificate wizard

### Step 1 — Authority

Choose the certificate authority and staging/production environment.

### Step 2 — Identifiers

Enter the root domain and additional SAN names. Provide clear wildcard guidance.

### Step 3 — Validation

Choose HTTP-01, DNS-01 or TLS-ALPN-01 only where supported.

### Step 4 — Provider

Select and test the DNS provider, or configure manual DNS.

### Step 5 — Deployment

Select IIS website(s), certificate store and binding behaviour.

### Step 6 — Renewal

Configure renewal threshold, notifications and automation.

### Step 7 — Review

Show a summary of domains, validation prerequisites, target websites and potential changes.

### Step 8 — Issue

Run the workflow with visible progress, safe cancellation and detailed error reporting.

## 19.5 Visual design

- Modern Windows desktop layout.
- Light and dark themes.
- Clear status indicators.
- Consistent spacing and typography.
- Search, filters and sorting.
- Accessible keyboard navigation.
- Confirmation for destructive actions.
- Copy buttons for DNS challenge records.
- Operation progress and logs.

------------------------------------------------------------------------

# 20. Logging, diagnostics and support

Use structured logs with:

- Timestamp.
- Severity.
- Component.
- Operation ID.
- Correlation ID.
- Server ID.
- Profile ID.
- Certificate ID.
- Error code.
- Safe diagnostic message.

Log categories:

- Application.
- Service.
- ACME.
- DNS.
- IIS.
- Certificate store.
- Renewal.
- Notifications.
- Remote agent.
- Security audit.

Do not log:

- Private keys.
- PFX passwords.
- DNS API tokens.
- ACME account private keys.
- Authorization headers.
- Full secret-bearing command lines.

Provide a support bundle containing sanitized logs and configuration metadata. Require explicit user approval before collecting or exporting diagnostics.

------------------------------------------------------------------------

# 21. Project structure

``` text
CertManager/
|
+-- src/
|   |
|   +-- CertManager.Desktop/
|   |   +-- Views/
|   |   +-- ViewModels/
|   |   +-- Controls/
|   |   +-- Themes/
|   |   +-- Resources/
|   |   +-- Services/
|   |
|   +-- CertManager.Service/
|   |   +-- Workers/
|   |   +-- Scheduling/
|   |   +-- JobExecution/
|   |   +-- Recovery/
|   |
|   +-- CertManager.Core/
|   |   +-- Entities/
|   |   +-- Interfaces/
|   |   +-- Enums/
|   |   +-- Exceptions/
|   |
|   +-- CertManager.Application/
|   |   +-- Certificates/
|   |   +-- Domains/
|   |   +-- Renewal/
|   |   +-- Deployment/
|   |   +-- Validation/
|   |
|   +-- CertManager.Infrastructure/
|   |   +-- Database/
|   |   +-- Repositories/
|   |   +-- Security/
|   |   +-- Logging/
|   |   +-- Ipc/
|   |
|   +-- CertManager.Acme/
|   |   +-- Accounts/
|   |   +-- Orders/
|   |   +-- Challenges/
|   |   +-- Certificates/
|   |
|   +-- CertManager.Integrations/
|       +-- IIS/
|       +-- DNS/
|       |   +-- Cloudflare/
|       |   +-- Route53/
|       +-- Notifications/
|       +-- Apache/
|       +-- Nginx/
|
+-- tests/
|   +-- UnitTests/
|   +-- IntegrationTests/
|   +-- SecurityTests/
|   +-- EndToEndTests/
|
+-- installer/
+-- docs/
+-- scripts/
+-- samples/
+-- CertManager.sln
```

## 21.1 Dependency direction

- Core must not depend on Desktop, IIS or a DNS vendor.
- Application depends on Core abstractions.
- Infrastructure implements persistence, security and IPC.
- Integrations implement provider and server interfaces.
- Desktop calls the application through service IPC.
- Service hosts application workflows and scheduling.

Avoid circular project references.

------------------------------------------------------------------------

# 22. Suggested interfaces

These are design starting points, not final production contracts.

``` csharp
public interface ICertificateAuthorityClient
{
    Task<AcmeAccountInfo> RegisterAccountAsync(
        AcmeAccountOptions options,
        CancellationToken cancellationToken);

    Task<CertificateOrderResult> RequestCertificateAsync(
        CertificateRequest request,
        CancellationToken cancellationToken);

    Task<CertificateStatus> GetOrderStatusAsync(
        string orderReference,
        CancellationToken cancellationToken);

    Task RevokeCertificateAsync(
        CertificateReference certificate,
        RevocationReason reason,
        CancellationToken cancellationToken);
}
```

``` csharp
public interface ICertificateInstaller
{
    Task<InstalledCertificate> InstallAsync(
        CertificateBundle certificate,
        InstallOptions options,
        CancellationToken cancellationToken);

    Task<bool> IsInstalledAsync(
        CertificateReference certificate,
        CancellationToken cancellationToken);
}
```

``` csharp
public interface IServerAdapter
{
    Task<IReadOnlyList<ManagedWebsite>> DiscoverWebsitesAsync(
        CancellationToken cancellationToken);

    Task<DeploymentResult> DeployCertificateAsync(
        CertificateReference certificate,
        DeploymentTarget target,
        CancellationToken cancellationToken);

    Task<HealthCheckResult> VerifyAsync(
        DeploymentTarget target,
        CancellationToken cancellationToken);
}
```

``` csharp
public interface INotificationProvider
{
    Task SendAsync(
        NotificationMessage message,
        CancellationToken cancellationToken);
}
```

The actual design should include provider capabilities, structured errors, operation IDs, timeouts and idempotency.

------------------------------------------------------------------------

# 23. Development roadmap and acceptance criteria

## 23.1 Phase, release and effort overview

Phases map to releases as follows. Effort is a **planning estimate in person-weeks** for a small team (two developers), accurate to roughly ±50%. Re-baseline after Phase 1, when real velocity is known.

| Phase | Name | Release | Depends on | Effort (person-weeks) |
|------:|:-----|:--------|:-----------|----------------------:|
| 0 | Decisions and spikes | Before MVP | none | 2–3 |
| 1 | Foundation (includes minimal installer) | MVP | 0 | 5–7 |
| 2 | IIS discovery | MVP | 1 | 2–3 |
| 3 | ACME staging issuance (HTTP-01) | MVP | 0 (ADR-001), 1, 2 | 4–6 |
| 4 | IIS deployment and rollback | MVP | 2, 3 | 3–4 |
| 5 | Renewal and MVP alerts | MVP | 3, 4 | 3–4 |
| 6 | DNS-01 and wildcard | Release 2 | 3, 5 | 5–7 |
| 7 | Monitoring, notifications, site auto-discovery | Release 2 | 5 | 4–5 |
| 8 | Remote servers | Release 3 | 5, 7 | 8–12 |
| 9 | Other server adapters | Release 3 | 8 | 8–10 |
| 10 | Packaging hardening and release readiness | Gate for every release | 1, 5 | 4–6 |

Totals: MVP including its Phase 10 gate about 23–33 person-weeks; Release 2 adds about 9–12; Release 3 adds about 16–22; overall about 48–67.

Phase 3 depends on Phase 2 because the HTTP-01 challenge responder must coexist with existing IIS sites without disturbing them.

## Phase 0 — Decisions and spikes

Work:

- ACME engine spike and ADR-001 (Section 6.3).
- ADR-002: final naming approach and rename procedure.
- ADR-003: licence confirmation (Apache-2.0) and DCO policy.
- ADR-004: secret storage approach (DPAPI machine scope versus Credential Manager).

Acceptance criteria:

- ADR-001 to ADR-004 are written, reviewed and merged.
- Requirements register (Appendix B) is reviewed and accepted as the MVP baseline.

## Phase 1 — Foundation

Work:

- Create solution and projects using the `CertManager.*` names.
- Configure dependency injection.
- Build WPF shell and navigation.
- Create Windows Worker Service.
- Configure SQLite and migrations (including the tables in Section 16).
- Add structured logging.
- Implement named-pipe communication with Windows-group authorization (Section 5.1).
- Implement service health/status display.
- Domain normalization library (IDN, Section 10.5) with unit tests.
- Minimal installer (WiX MSI or equivalent) that installs and registers the service, creates the data directory with restrictive permissions and the three Windows groups, and uninstalls cleanly.
- Repository governance files and CI pipeline with build, tests and dependency scanning (Appendix A).

Acceptance criteria:

- Desktop launches.
- Service installs and starts, and the installer works on a clean VM, including uninstall.
- Desktop can securely query service status; an unauthorized caller is rejected.
- Service restarts without losing persisted configuration.
- Database migrations work on a clean install and upgrade.
- CI is green and publishes the requirement-to-test coverage report.

## Phase 2 — IIS discovery

Work:

- Detect IIS availability.
- Enumerate websites and bindings, including IPv6 and wildcard IP forms.
- Read hostnames and ports.
- Inspect certificate store.
- Display current HTTPS status.

Acceptance criteria:

- Correctly lists test IIS websites.
- Handles missing IIS gracefully.
- Does not modify IIS during discovery.
- Handles malformed or unusual bindings safely.

## Phase 3 — ACME staging issuance

Work:

- Create ACME account.
- Configure staging directory.
- Integrate the engine chosen in ADR-001 behind `ICertificateAuthorityClient`.
- Implement HTTP-01 without disturbing existing IIS sites.
- CAA pre-flight and A/AAAA reachability checks (Section 10.5).
- Order idempotency key and workflow locks (Section 10.6).
- Request single-domain certificate.
- Validate certificate and chain.
- Import into Windows store.

Acceptance criteria:

- Test certificate successfully issued against staging.
- Challenge cleanup works.
- Private key remains protected.
- Failure states are understandable and logged.
- Repeated or concurrent requests do not create uncontrolled duplicate orders.

## Phase 4 — IIS deployment

Work:

- Configure HTTPS binding.
- Support SNI.
- Preserve existing settings.
- Verify endpoint.
- Implement rollback.

Acceptance criteria:

- Correct certificate assigned.
- Existing working binding preserved on failure.
- Multiple websites can be configured safely.
- No unrelated IIS settings are changed.
- Binding ownership conflicts are reported, not silently resolved.

## Phase 5 — Renewal

Work:

- Persist renewal schedules.
- Percentage-based renewal and alert thresholds, with ARI where the CA supports it (Section 9.3).
- Evaluate due certificates.
- Renew through ACME.
- Deploy replacement.
- MVP alerts: dashboard and Windows Event Log.

Acceptance criteria:

- Renewal occurs with the desktop closed.
- Service restart does not lose jobs.
- Previous certificate remains available during deployment.
- Failed deployment does not silently mark renewal successful.
- Threshold tests pass for 90-day, 45-day and 6-day lifetimes.
- Failures raise a dashboard alert and an Event Log entry.

## Phase 6 — DNS-01 and wildcard

Work:

- DNS provider abstraction.
- Cloudflare integration.
- Route 53 integration.
- Manual DNS fallback.
- Wildcard issuance.
- Apex plus wildcard identifiers.
- DNS owner-name locking and CNAME delegation detection.

Acceptance criteria:

- TXT record created and verified.
- Existing TXT values are preserved.
- Only owned challenge records are removed.
- Wildcard and root identifiers validate correctly.
- API credentials are protected.

## Phase 7 — Monitoring, notifications and site auto-discovery

Work:

- Expiry monitoring.
- HTTPS health checks.
- SMTP.
- Telegram.
- Notification history.
- Detection of new IIS websites with policy-gated auto-deployment (Section 10.3).

Acceptance criteria:

- Alerts are delivered for configured events.
- Duplicate alerts are controlled.
- Secrets are masked.
- Health check results are auditable.
- Auto-deployment never runs without an explicitly enabled policy.

## Phase 8 — Remote servers

Work:

- Agent identity.
- Secure enrollment.
- Encrypted communication.
- Remote job execution.
- Agent health and update process.

Acceptance criteria:

- Only enrolled agents accept authorized jobs.
- Replayed or unauthorized requests are rejected.
- Agent disconnection is handled.
- Remote actions are auditable.

## Phase 9 — Other server adapters

Work:

- Apache adapter.
- Nginx adapter.
- Linux agent.
- Deployment validation and rollback.

Acceptance criteria:

- Each supported server/version combination passes integration tests.
- Configuration is validated before reload.
- Failed deployment preserves the previous configuration.

## Phase 10 — Packaging hardening and release readiness

This phase is a gate that runs before **every** production release, starting with the MVP.

Work:

- Production MSI hardening, service installation, upgrade and uninstaller.
- Database backup and restore.
- Code-signing evaluation and signed release artifacts.
- SBOM generation, checksums and release notes (Appendix A).
- Update notification feature (Section 25.4).
- Documentation.
- Security and performance testing.

Acceptance criteria:

- Clean installation succeeds.
- Upgrade preserves settings and certificates.
- Uninstall does not unexpectedly remove customer certificates or DNS records.
- Recovery procedure is documented and tested.
- Release artifacts are signed and an SBOM is published.

------------------------------------------------------------------------

# 24. Testing strategy

## 24.0 Test identifiers and traceability

Every test has an ID with a prefix: `UT-` unit, `IT-` integration, `E2E-` end-to-end, `SEC-` security, `PERF-` performance. Every requirement in Appendix B must be verified by at least one test, and every test must cite at least one requirement ID. A requirement with no test is not considered implemented. Coverage of this mapping is reported in CI for each release.

## 24.1 Unit tests

Test:

- Domain normalization.
- Wildcard matching.
- SAN validation.
- Renewal due calculations.
- Retry/backoff calculations.
- Provider error mapping.
- Certificate expiry handling.
- Authorization decisions.
- IDN normalization, punycode round trips and invalid-label rejection.
- CAA evaluation including `issuewild`.
- Order idempotency key calculation.
- Lock ordering and lease expiry.
- Percentage-based renewal and alert thresholds for 90-day, 45-day and 6-day lifetimes.

## 24.2 Integration tests

Test:

- SQLite persistence.
- ACME staging.
- DNS provider sandbox or test zone.
- Windows certificate store.
- IIS test website.
- Named-pipe access control.
- Notification delivery using test destinations.
- Concurrent workflows contending for the same identifier, DNS owner name and IIS server.
- CAA lookups against a test zone.
- Windows Event Log alert delivery.

## 24.3 End-to-end tests

1.  Issue a single-domain staging certificate.
2.  Install it in the local machine store.
3.  Bind it to a test IIS site.
4.  Verify HTTPS and hostname.
5.  Renew using a test workflow.
6.  Update binding.
7.  Verify old certificate is retained according to policy.
8.  Simulate DNS provider failure.
9.  Simulate IIS deployment failure.
10. Restart service during a persisted job and verify recovery.

## 24.4 Security tests

- Unauthorized local IPC request.
- Unauthorized service command.
- DNS token disclosure checks.
- Private-key logging checks.
- Malformed domain and challenge input.
- Command injection attempts.
- Unsafe file path attempts.
- Certificate mismatch.
- Certificate chain validation failure.
- Remote agent replay and authentication tests.

## 24.5 Production safeguards

- Use staging during development.
- Use test domains and test IIS sites.
- Do not run destructive tests against production bindings.
- Require confirmation for revocation and removal.
- Maintain backups and rollback procedures.
- Monitor CA rate limits.

------------------------------------------------------------------------

# 25. Packaging, installation and upgrades

## 25.1 Installer responsibilities

- Check Windows version and prerequisites.
- Install application files.
- Install and configure Windows service.
- Create application data directory.
- Set restrictive directory permissions.
- Initialize or migrate SQLite database.
- Create shortcuts.
- Register uninstall information.
- Provide service status and diagnostic guidance.

## 25.2 Upgrade requirements

- Back up database before migration.
- Preserve ACME account references and protected secrets.
- Preserve renewal jobs.
- Stop service cleanly.
- Apply migrations.
- Restart service.
- Verify service health.
- Provide rollback guidance.

## 25.3 Uninstall requirements

Offer explicit choices for:

- Remove application files.
- Keep database and settings.
- Remove application-owned scheduled jobs.
- Keep installed certificates.
- Remove application-created certificates only after dependency checks and explicit confirmation.

Never silently revoke certificates or delete DNS zones during uninstall.

## 25.4 Application updates and telemetry

**Updates.**

- Updates are delivered as signed MSI packages using a major-upgrade path. Section 25.2 (backup, migrate, verify, rollback guidance) applies to every update.
- The service runs with elevated rights, so v1 has **no silent self-update**. Updates are always administrator-initiated.
- An optional, off-by-default "check for updates" feature may query the project's public release feed over HTTPS and only **notify**. Downloads are verified against the published SHA-256 checksum and the package signature before installation.
- Release channels: stable and preview. Preview builds are never installed by default.
- Release 2 adds winget and Chocolatey packaging. Remote-agent updates (Release 3) follow Section 14.2 (signed or authenticated updates).
- The previous MSI and a pre-migration database backup are kept so an update can be rolled back.

**Telemetry.**

- None by default. The software does not phone home.
- If usage or crash reporting is ever added, it must be opt-in, documented, anonymous, and show a preview of exactly what would be sent before the user agrees.
- Support bundles (Section 20) are created only with explicit approval and never include secrets.

------------------------------------------------------------------------

# 26. Operational requirements

## 26.1 Network prerequisites

Depending on the selected validation method:

- Outbound HTTPS to the ACME directory and certificate authority endpoints.
- DNS access to the configured provider API for DNS-01.
- Public DNS records for requested domains.
- Inbound port 80 for common HTTP-01 workflows.
- Inbound port 443 for normal HTTPS and TLS-ALPN-01 workflows.
- Correct routing, NAT and firewall configuration.

Network requirements vary by provider and deployment topology.

## 26.2 Time synchronization

Accurate system time is required for TLS and certificate validity. Detect large clock drift and provide an actionable warning.

## 26.3 Backups

Back up:

- SQLite database.
- Application configuration.
- Protected secret recovery information, where supported.
- Certificate metadata and deployment mappings.

Private-key backup must be separately protected. A database backup alone does not guarantee that certificates can be restored to another machine.

------------------------------------------------------------------------

# 27. Product quality metrics

Track the following during testing and later production use:

| Metric                          | Goal                                    |
|:--------------------------------|:----------------------------------------|
| Successful issuance rate        | Measure by CA and validation method     |
| Successful renewal rate         | Measure monthly                         |
| Failed deployment rollback rate | Verify all supported failure cases      |
| Missed renewal jobs             | Target zero                             |
| Duplicate issuance              | Minimize through idempotency            |
| Service recovery                | Resume persisted work safely            |
| Idle resource use               | Profile on reference hardware           |
| Dashboard responsiveness        | Measure with representative inventories |
| Alert delivery success          | Track retries and failures              |
| Security incidents              | No known unresolved critical issues     |

Do not claim a performance or reliability advantage over competitors until measured under comparable conditions.

------------------------------------------------------------------------

# 28. Risks and mitigations

| Risk                            | Mitigation                                   |
|:--------------------------------|:---------------------------------------------|
| CA rate limits                  | Staging, backoff, request throttling         |
| DNS propagation delay           | Authoritative/public DNS checks and timeout  |
| DNS API credential compromise   | Scoped tokens, DPAPI, rotation               |
| Private-key disclosure          | Windows key storage, ACLs, no secret logging |
| IIS binding outage              | Pre-checks, verification, rollback           |
| Service crash                   | Persisted jobs and recovery                  |
| ACME library abandonment        | Adapter abstraction and dependency review    |
| External executable changes     | Version pinning and compatibility tests      |
| Remote agent compromise         | Strong identity, least privilege, revocation |
| Unintended certificate deletion | Dependency checks and explicit confirmation  |
| Unsupported provider behaviour  | Capability checks and tested support matrix  |
| ACME engine decision slips | Time-boxed spike, scoring rubric and ADR-001 (Section 6.3) |
| Shortening certificate lifetimes | Percentage-based renewal and ARI (Section 9.3) |
| Supply-chain compromise | Pinned dependencies, scanning, SBOM and signed releases (Appendix A) |
| Maintainer bus factor | Governance, CODEOWNERS and multi-maintainer rules (Appendix A) |

------------------------------------------------------------------------

# 29. Documentation deliverables

The project should maintain:

- Product requirements document.
- Architecture decision records.
- Database schema documentation.
- Developer setup guide.
- Build and release guide.
- User guide.
- IIS integration guide.
- DNS provider setup guides.
- Security model.
- Backup and recovery guide.
- Troubleshooting guide.
- Supported platform matrix.
- Third-party licence notices.
- Release notes.
- Governance and contribution policy.
- Requirements register and traceability report.
- Software bill of materials (SBOM) per release.
- Competitor parity research notes.

------------------------------------------------------------------------

# 30. Definition of done for the first production release

The first production release is ready only when:

- Desktop and service install cleanly.
- Service runs independently of the UI.
- Local IPC is authenticated and access-controlled.
- IIS websites and bindings are discovered accurately.
- ACME staging and production are supported.
- Single-domain issuance works.
- HTTP-01 works in supported network topologies.
- DNS-01 and wildcard support are included if wildcard is part of the declared release scope.
- Certificates are installed securely.
- IIS bindings are configured and verified.
- Renewal is persisted and automatic.
- Service restart does not lose renewal schedules.
- Failed deployment does not silently replace a working configuration.
- Logs and audit records are available.
- Credentials and private keys are protected.
- Installer, upgrade and uninstall behaviour are tested.
- Security, integration and end-to-end tests pass.
- User and administrator documentation is complete.
- ADR-001 to ADR-004 are accepted.
- Every MVP requirement in Appendix B has at least one passing test.
- Governance files are published and releases are signed with an SBOM.
- Update and telemetry behaviour matches Section 25.4.

------------------------------------------------------------------------

# 31. Final product vision

Open-Source SSL/TLS Certificate Manager should become a dependable SSL/TLS lifecycle automation platform, starting with Windows and IIS and expanding through modular integrations.

Its defining capabilities should be:

1.  Fast and reliable certificate issuance.
2.  Root, SAN and wildcard certificate management.
3.  Automated DNS validation.
4.  Automated IIS installation and binding management.
5.  Renewal without an open desktop application.
6.  Multi-domain and multi-server management.
7.  Secure secret and private-key handling.
8.  Health monitoring and actionable notifications.
9.  Recovery and rollback.
10. Extensible support for additional certificate authorities and web servers.

**Core engineering principle: Build a secure, recoverable and testable certificate lifecycle engine first. Add convenience, scale and additional integrations around that foundation.**

------------------------------------------------------------------------

# 32. Official technical references

Use official documentation as the source of truth during implementation. Product features, supported runtimes, API behaviour, rate limits and licences can change.

- Let’s Encrypt: https://letsencrypt.org/
- Let’s Encrypt documentation: https://letsencrypt.org/docs/
- Let’s Encrypt rate limits: https://letsencrypt.org/docs/rate-limits/
- ACME RFC 8555: https://www.rfc-editor.org/rfc/rfc8555
- win-acme: https://www.win-acme.com/
- win-acme source repository: https://github.com/win-acme/win-acme
- Microsoft .NET: https://learn.microsoft.com/dotnet/
- WPF: https://learn.microsoft.com/dotnet/desktop/wpf/
- .NET Worker Services: https://learn.microsoft.com/dotnet/core/extensions/workers
- Microsoft IIS administration: https://learn.microsoft.com/iis/manage/
- Microsoft certificate stores: https://learn.microsoft.com/dotnet/api/system.security.cryptography.x509certificates
- SQLite: https://www.sqlite.org/docs.html
- Cloudflare API: https://developers.cloudflare.com/api/
- AWS Route 53 API: https://docs.aws.amazon.com/Route53/latest/APIReference/Welcome.html
- WiX Toolset: https://wixtoolset.org/
- ACME Renewal Information (ARI): RFC 9773 (verify the final RFC number and CA support)
- DNS Certification Authority Authorization (CAA): RFC 8659
- Internationalized domain names: RFC 5890 and Unicode UTS #46
- CycloneDX SBOM standard: https://cyclonedx.org/
- Developer Certificate of Origin: https://developercertificate.org/
- Semantic Versioning: https://semver.org/
- Sigstore: https://www.sigstore.dev/
- OpenSSF Scorecard: https://securityscorecards.dev/
- Certify The Web: https://certifytheweb.com/
- Posh-ACME: https://poshac.me/

------------------------------------------------------------------------

# Appendix A: Open-source governance and supply-chain policy

## A.1 Roles and decisions

| Role | Responsibility |
|:-----|:---------------|
| Contributor | Anyone who submits issues, pull requests or documentation |
| Committer | Trusted contributor with merge rights, nominated and approved by maintainers |
| Maintainer | Owns releases, security response and project direction |

- Day-to-day decisions use lazy consensus on pull requests and issues.
- Significant changes (architecture, public interfaces, licence, security model) require a written proposal and an Architecture Decision Record (ADR), with at least a 7-day comment period.
- Security-sensitive paths (secret storage, IPC, authorization, installer, agent protocol) are listed in `CODEOWNERS` and need review by two maintainers.
- Target: move to a neutral organization once there are at least three maintainers from at least two different employers, to support the vendor-neutral goal.

## A.2 Contribution policy

- Contributions are accepted under Apache-2.0 with **DCO sign-off** (`Signed-off-by`). No CLA unless a future legal review requires one.
- Pull requests need passing CI, tests for new behaviour, and an updated requirement or ADR reference when relevant.
- Adopt the Contributor Covenant as `CODE_OF_CONDUCT.md`.
- Issue and pull-request templates are required (see the community files list in the identity section).

## A.3 Security disclosure

- `SECURITY.md` describes private reporting through the repository's private security advisory feature.
- Targets: acknowledge within 3 business days, triage within 7 days, coordinated disclosure with a fix target of 90 days or sooner depending on severity.
- Security advisories and fixed versions are published publicly after a fix is available. Request CVE identifiers for confirmed vulnerabilities.

## A.4 Release engineering

- Semantic Versioning with tagged releases and public release notes.
- Release artifacts have SHA-256 checksums. Tags are signed. The MSI is code-signed when a certificate is available (evaluated in Phase 10); provenance attestation through Sigstore is recommended.
- A CycloneDX SBOM is generated and published for every release.
- Builds run in CI from a clean environment. Reproducible builds are a goal, not a day-one requirement.

## A.5 Dependency policy

- Pin dependency versions. Use automated update proposals (Dependabot or Renovate) and review every new dependency.
- Allowed licences by default: Apache-2.0, MIT, BSD, ISC. Anything else needs maintainer approval and is recorded in the third-party notices.
- CI fails on known critical vulnerabilities in dependencies.
- Keep an OpenSSF Scorecard check in CI and track the score over time.

## A.6 Telemetry and trademarks

- No telemetry by default (Section 25.4).
- Trademark and name usage policy is written after the final name is chosen (ADR-002).

---

# Appendix B: Requirements register and traceability

IDs are stable. Release: MVP, R2 (Release 2), R3 (Release 3). Phase refers to Section 23. Verification IDs follow Section 24.0 and are assigned when tests are written; the "Verification type" column states what kind of test is required.

| ID | Requirement | Section | Release | Phase | Verification type |
|:---|:------------|:--------|:--------|------:|:------------------|
| FR-001 | Issue a single-domain certificate through ACME using HTTP-01 | 9.1 | MVP | 3 | E2E |
| FR-002 | Support ACME staging and production directories | 8.3 | MVP | 3 | IT |
| FR-003 | Discover IIS sites and bindings read-only | 12.1 | MVP | 2 | IT |
| FR-004 | Install certificates in the Windows store with restricted key ACLs | 12.2 | MVP | 3 | IT |
| FR-005 | Update IIS bindings with SNI support and read-back verification | 12.3 | MVP | 4 | E2E |
| FR-006 | Roll back a failed binding update and keep the previous certificate active | 12.3 | MVP | 4 | E2E |
| FR-007 | Provide a manual renewal action | 8.6 | MVP | 5 | E2E |
| FR-008 | Renew automatically with the desktop UI closed | 7.1, 9.2 | MVP | 5 | E2E |
| FR-009 | Resume persisted workflows after a service restart | 9.1 | MVP | 5 | E2E |
| FR-010 | Apply percentage-based renewal and alert thresholds, with ARI where supported | 9.3 | MVP | 5 | UT |
| FR-011 | Show alerts in the dashboard and write them to the Windows Event Log | 15.2 | MVP | 5 | IT |
| FR-012 | Prevent duplicate orders using the idempotency key and locks | 10.6 | MVP | 3 | UT, IT |
| FR-013 | Check CAA records before ordering | 10.5 | MVP | 3 | UT, IT |
| FR-014 | Normalize and validate identifiers including IDN | 10.5 | MVP | 1 | UT |
| FR-015 | Provide logs, operation history and audit records | 17.5, 20 | MVP | 1 | IT |
| FR-016 | Support DNS-01 validation | 2.3, 11 | R2 | 6 | IT |
| FR-017 | Cloudflare DNS integration | 11.2 | R2 | 6 | IT |
| FR-018 | AWS Route 53 DNS integration | 11.2 | R2 | 6 | IT |
| FR-019 | Manual DNS fallback | 11.3 | R2 | 6 | IT |
| FR-020 | Wildcard and apex-plus-wildcard certificates | 10.1 | R2 | 6 | E2E |
| FR-021 | SAN certificates | 10 | R2 | 6 | E2E |
| FR-022 | Policy-gated auto-deployment for newly discovered IIS sites | 10.3 | R2 | 7 | IT |
| FR-023 | SMTP and Telegram notifications with duplicate suppression | 15 | R2 | 7 | IT |
| FR-024 | Secure certificate export | 8.6 | R2 | 7 | SEC |
| FR-025 | Remote agent enrollment and encrypted communication | 14 | R3 | 8 | SEC, IT |
| FR-026 | Apache and Nginx adapters with validated reload | 13 | R3 | 9 | IT |
| SEC-001 | Restrict named-pipe access with ACLs and authenticate the Windows identity | 7.3 | MVP | 1 | SEC |
| SEC-002 | Authorize every privileged operation in the service, per request | 5.2 | MVP | 1 | SEC |
| SEC-003 | Protect secrets with DPAPI or Credential Manager; no plain-text secrets | 17.2 | MVP | 3 | SEC |
| SEC-004 | Never log secrets, keys or authorization headers | 20 | MVP | 1 | SEC |
| SEC-005 | Prefer non-exportable private keys and narrow key ACLs | 17.3 | MVP | 3 | SEC |
| SEC-006 | Validate all external input; no command injection | 17.1 | MVP | 1 | SEC |
| SEC-007 | Audit every privileged operation | 17.5 | MVP | 1 | IT |
| SEC-008 | Replay protection and mutual authentication for agents | 14.2 | R3 | 8 | SEC |
| NFR-001 | Service idle memory target below 50 MB | 18 | MVP | 10 | PERF |
| NFR-002 | Desktop idle memory target below 150 MB | 18 | MVP | 10 | PERF |
| NFR-003 | Dashboard loads in under 2 seconds with 1,000 certificates | 18 | MVP | 10 | PERF |
| NFR-004 | No blocking network or certificate operations on the UI thread | 18 | MVP | 1 | UT, IT |
| NFR-005 | Clean install, upgrade and uninstall | 25 | MVP | 1, 10 | E2E |
| GOV-001 | Publish licence, community and security files | Identity, A.2 | MVP | 1 | Review |
| GOV-002 | Require DCO sign-off and CI gates | A.2 | MVP | 1 | CI |
| GOV-003 | Publish signed releases, checksums and an SBOM | A.4 | MVP | 10 | CI |
| GOV-004 | No telemetry by default; opt-in updates check only | 25.4 | MVP | 10 | Review |

**Traceability rules.** Each row must gain at least one verification ID before its release. CI reports the coverage percentage and fails a release build if any MVP row has no passing test.

---

# Appendix C: Glossary

| Term | Meaning |
|:-----|:--------|
| ACME | Automatic Certificate Management Environment, the protocol (RFC 8555) used to request certificates |
| ADR | Architecture Decision Record |
| ARI | ACME Renewal Information, a CA-provided hint about when to renew |
| A-label | The ASCII (punycode) form of an internationalized domain label |
| CA | Certificate authority |
| CAA | DNS record that states which CAs may issue for a domain |
| CSR | Certificate signing request |
| DCO | Developer Certificate of Origin, a sign-off on each commit |
| DNS-01 | Validation method using a TXT record under `_acme-challenge` |
| DPAPI | Windows Data Protection API for protecting local secrets |
| HTTP-01 | Validation method using a file served over HTTP on port 80 |
| IDN | Internationalized domain name |
| IPC | Inter-process communication (here, Windows named pipes) |
| PFX | Password-protected certificate and private-key file |
| SAN | Subject Alternative Name |
| SBOM | Software bill of materials |
| SNI | Server Name Indication, allows several HTTPS sites on one IP and port |
| TLS-ALPN-01 | Validation method using a special TLS handshake on port 443 |

---

# Appendix D: ADR index

| ADR | Topic | Status | Due |
|:----|:------|:-------|:----|
| ADR-001 | ACME engine selection (process in Section 6.3) | Proposed | End of Phase 0 |
| ADR-002 | Final product name, rename procedure and trademark policy | Proposed | Before first public release |
| ADR-003 | Licence confirmation (Apache-2.0) and DCO policy | Proposed | End of Phase 0 |
| ADR-004 | Secret storage approach (DPAPI machine scope or Credential Manager) | Proposed | End of Phase 0 |

---

**End of document**

**Document owner:** Open-Source SSL/TLS Certificate Manager project  
**Next recommended activity:** Run Phase 0 (ADR-001 to ADR-004), approve the MVP requirements in Appendix B, then create the `CertManager` solution and implement Phase 1.
