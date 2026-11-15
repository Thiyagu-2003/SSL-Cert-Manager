# CertManager Phase 1 Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish the foundational .NET solution architecture, domain models, SQLite persistence with migrations, Windows Named Pipe IPC, background worker service, and initial WPF desktop shell with unit tests.

**Architecture:** Clean modular architecture with strict dependency direction: Core defines domain entities and interfaces without framework dependencies; Infrastructure handles SQLite persistence and Named Pipe IPC; Service hosts background scheduling and IPC endpoint; Desktop interacts exclusively through the service via authenticated IPC.

**Tech Stack:** C# .NET 8.0, WPF, Microsoft.Extensions.Hosting (Worker Service), Entity Framework Core SQLite, System.IO.Pipes, xUnit, FluentAssertions.

**Spec:** `D:/Cross Platform Applications/Ssl Cert Manager/CertManager_Project_Specification_v2.md`

## Global Constraints

- Working code name: `CertManager` for solution, projects, namespaces, and folder names (Spec Section 1.0, 21.0, ADR-002).
- Clean dependency direction: `CertManager.Core` must not depend on Desktop, IIS, or external vendor libraries (Spec Section 21.1).
- Authorization: Named Pipes must authenticate caller Windows Identity and enforce role-based permissions (Administrator, Operator, Auditor) on every request (Spec Section 5.1, 7.3).
- Persistence: SQLite schema matching the complete entity definitions in Spec Section 16 (AcmeOrders, AcmeChallenges, WorkflowSteps, WorkflowLocks, CertificateProfiles, ProfileDomains, IisWebsites, IisBindings).
- No hardcoded secrets, passwords, or plain-text private keys in logs or IPC payloads (Spec Section 8.3, 17.0).

## Review Focus

1. Domain normalization failure on internationalized domain names (IDN / Punycode A-labels) or malformed wildcards (`*.example.com` vs `test.*.example.com`).
2. Concurrent IPC requests exhausting Named Pipe server threads or failing to verify Windows group claims per call.
3. Database migration failure on fresh SQLite creation or schema drift with foreign key constraints enabled.
4. Workflow lock expiration / orphan lock recovery on service unexpected termination.
5. Safe deserialization and payload size limits on Named Pipe message framing to prevent memory exhaustion.

---

### Task 1: Git Repository & Solution Scaffolding

**Files:**
- Create: `.gitignore`
- Create: `CertManager.sln`
- Create: `src/CertManager.Core/CertManager.Core.csproj`
- Create: `src/CertManager.Application/CertManager.Application.csproj`
- Create: `src/CertManager.Infrastructure/CertManager.Infrastructure.csproj`
- Create: `src/CertManager.Acme/CertManager.Acme.csproj`
- Create: `src/CertManager.Integrations.Iis/CertManager.Integrations.Iis.csproj`
- Create: `src/CertManager.Service/CertManager.Service.csproj`
- Create: `src/CertManager.Desktop/CertManager.Desktop.csproj`
- Create: `tests/CertManager.UnitTests/CertManager.UnitTests.csproj`

**Interfaces:**
- Produces: Visual Studio Solution and projects targeting `net8.0` (with `net8.0-windows` for Desktop).

- [ ] **Step 1: Initialize Git repository and standard .gitignore**
Initialize git repo in workspace root and create standard Visual Studio `.gitignore`.
Command: `git init`

- [ ] **Step 2: Create .NET Solution and Projects**
Run CLI commands:
```powershell
dotnet new sln -n CertManager
dotnet new classlib -n CertManager.Core -o src/CertManager.Core
dotnet new classlib -n CertManager.Application -o src/CertManager.Application
dotnet new classlib -n CertManager.Infrastructure -o src/CertManager.Infrastructure
dotnet new classlib -n CertManager.Acme -o src/CertManager.Acme
dotnet new classlib -n CertManager.Integrations.Iis -o src/CertManager.Integrations.Iis
dotnet new worker -n CertManager.Service -o src/CertManager.Service
dotnet new wpf -n CertManager.Desktop -o src/CertManager.Desktop
dotnet new xunit -n CertManager.UnitTests -o tests/CertManager.UnitTests

dotnet sln add (Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object { $_.FullName })
```

- [ ] **Step 3: Setup Project References according to Section 21.1**
Configure dependencies:
- Application -> Core
- Infrastructure -> Core, Application
- Acme -> Core
- Integrations.Iis -> Core
- Service -> Core, Application, Infrastructure, Acme, Integrations.Iis
- Desktop -> Core
- UnitTests -> Core, Application, Infrastructure

- [ ] **Step 4: Verify build succeeds**
Run: `dotnet build`
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Commit**
`git add . && git commit -m "chore: scaffold CertManager solution and project structure"`

---

### Task 2: Core Domain Entities & IDN Domain Normalizer

**Files:**
- Create: `src/CertManager.Core/Enums/DomainType.cs`
- Create: `src/CertManager.Core/Enums/ValidationMethod.cs`
- Create: `src/CertManager.Core/Enums/CertificateStatus.cs`
- Create: `src/CertManager.Core/Enums/UserRole.cs`
- Create: `src/CertManager.Core/Entities/Domain.cs`
- Create: `src/CertManager.Core/Entities/CertificateProfile.cs`
- Create: `src/CertManager.Core/Entities/ProfileDomain.cs`
- Create: `src/CertManager.Core/Entities/CertificateRecord.cs`
- Create: `src/CertManager.Core/Entities/AcmeOrder.cs`
- Create: `src/CertManager.Core/Entities/AcmeChallenge.cs`
- Create: `src/CertManager.Core/Entities/WorkflowStep.cs`
- Create: `src/CertManager.Core/Entities/WorkflowLock.cs`
- Create: `src/CertManager.Core/Entities/IisWebsite.cs`
- Create: `src/CertManager.Core/Entities/IisBinding.cs`
- Create: `src/CertManager.Core/Common/DomainNormalizer.cs`
- Test: `tests/CertManager.UnitTests/Core/DomainNormalizerTests.cs`

**Interfaces:**
- Produces: `DomainNormalizer.Normalize(string domain) -> NormalizedDomainResult`
- Produces: Core domain entities reflecting Spec Section 16 database model.

- [ ] **Step 1: Write failing unit tests for DomainNormalizer**
In `tests/CertManager.UnitTests/Core/DomainNormalizerTests.cs`:
Test cases:
- Lowercase conversion: `EXAMPLE.COM` -> `example.com`
- Trim whitespace: `  example.com  ` -> `example.com`
- Trailing dot removal: `example.com.` -> `example.com`
- Wildcard identification: `*.example.com` -> isWildcard true, clean root `example.com`
- Invalid nested wildcard: `test.*.example.com` -> throws `ArgumentException`
- Internationalized domain (IDN / Punycode): `münchen.de` -> `xn--mnchen-3ya.de`

- [ ] **Step 2: Run test to verify it fails**
Run: `dotnet test --filter FullyQualifiedName~DomainNormalizerTests`
Expected: FAIL (type or method not found).

- [ ] **Step 3: Implement domain entities and DomainNormalizer**
Implement `DomainNormalizer` using `System.Globalization.IdnMapping` to produce canonical ASCII A-labels and Unicode display names. Implement core entities with required properties from Section 16.

- [ ] **Step 4: Run test to verify it passes**
Run: `dotnet test --filter FullyQualifiedName~DomainNormalizerTests`
Expected: PASS.

- [ ] **Step 5: Commit**
`git add src/CertManager.Core tests/CertManager.UnitTests && git commit -m "feat(core): implement domain entities and IDN domain normalizer"`

---

### Task 3: Infrastructure Persistence with EF Core SQLite

**Files:**
- Create: `src/CertManager.Infrastructure/Data/CertManagerDbContext.cs`
- Create: `src/CertManager.Infrastructure/Data/Configurations/*.cs`
- Create: `src/CertManager.Infrastructure/Data/DatabaseInitializer.cs`
- Test: `tests/CertManager.UnitTests/Infrastructure/DatabaseTests.cs`

**Interfaces:**
- Produces: `CertManagerDbContext : DbContext`
- Produces: `IDatabaseInitializer.InitializeAsync()` to apply migrations and ensure tables/indices.

- [ ] **Step 1: Add Microsoft.EntityFrameworkCore.Sqlite package to Infrastructure**
Command: `dotnet add src/CertManager.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite`

- [ ] **Step 2: Write failing unit test for Database initialization and entity persistence**
In `tests/CertManager.UnitTests/Infrastructure/DatabaseTests.cs`:
Test creating in-memory/temp SQLite database, applying schema, inserting a `Domain`, `CertificateProfile`, and `AcmeOrder`, asserting constraints and relationships.

- [ ] **Step 3: Run test to verify it fails**
Run: `dotnet test --filter FullyQualifiedName~DatabaseTests`
Expected: FAIL.

- [ ] **Step 4: Implement CertManagerDbContext & Entity Configurations**
Configure primary keys, foreign keys, unique indices (`DomainName`, `IdempotencyKey`, `LockKey`), and default values per Spec Section 16.

- [ ] **Step 5: Run test to verify it passes**
Run: `dotnet test --filter FullyQualifiedName~DatabaseTests`
Expected: PASS.

- [ ] **Step 6: Commit**
`git add src/CertManager.Infrastructure tests/CertManager.UnitTests && git commit -m "feat(infra): setup EF Core SQLite persistence and schema mappings"`

---

### Task 4: Named Pipe IPC & Role-Based Authorization

**Files:**
- Create: `src/CertManager.Core/Ipc/IpcMessages.cs`
- Create: `src/CertManager.Infrastructure/Ipc/NamedPipeServer.cs`
- Create: `src/CertManager.Infrastructure/Ipc/NamedPipeClient.cs`
- Create: `src/CertManager.Infrastructure/Ipc/WindowsIdentityAuthorizer.cs`
- Test: `tests/CertManager.UnitTests/Ipc/NamedPipeIpcTests.cs`

**Interfaces:**
- Produces: `IpcRequest`, `IpcResponse`, `ServiceStatusResponse`
- Produces: `INamedPipeServer`, `INamedPipeClient`
- Produces: `WindowsIdentityAuthorizer.AuthorizeCaller(PipeStream pipe, UserRole requiredRole) -> bool`

- [ ] **Step 1: Write failing unit tests for IPC message framing and serialization**
In `tests/CertManager.UnitTests/Ipc/NamedPipeIpcTests.cs`:
Test serialization/deserialization of `IpcRequest` and `IpcResponse` with length-prefixed binary/JSON framing, handling of payloads up to 1MB, rejection of oversized payloads.

- [ ] **Step 2: Run test to verify it fails**
Run: `dotnet test --filter FullyQualifiedName~NamedPipeIpcTests`
Expected: FAIL.

- [ ] **Step 3: Implement IPC protocol, NamedPipeServer, and NamedPipeClient**
Use `NamedPipeServerStream` and `NamedPipeClientStream` with `PipeOptions.Asynchronous`.
Implement message framing (4-byte length prefix + UTF8 JSON payload).
Implement `WindowsIdentityAuthorizer` checking `WindowsIdentity` of the connected client against specified Windows roles/groups with fallback for local dev mode.

- [ ] **Step 4: Run test to verify it passes**
Run: `dotnet test --filter FullyQualifiedName~NamedPipeIpcTests`
Expected: PASS.

- [ ] **Step 5: Commit**
`git add src/CertManager.Core src/CertManager.Infrastructure tests/CertManager.UnitTests && git commit -m "feat(ipc): implement length-prefixed Named Pipe server, client, and authorizer"`

---

### Task 5: Background Worker Service Skeleton

**Files:**
- Modify: `src/CertManager.Service/Program.cs`
- Create: `src/CertManager.Service/Worker.cs`
- Create: `src/CertManager.Service/Services/IpcHostService.cs`
- Create: `src/CertManager.Service/Services/RenewalSchedulerService.cs`
- Test: `tests/CertManager.UnitTests/Service/ServiceHostingTests.cs`

**Interfaces:**
- Produces: Windows Worker Service running IPC server and renewal scheduler background task loop.

- [ ] **Step 1: Write failing test for Service DI configuration**
Verify host builder resolves `CertManagerDbContext`, `INamedPipeServer`, and required hosted services without circular dependency or missing registrations.

- [ ] **Step 2: Run test to verify it fails**
Run: `dotnet test --filter FullyQualifiedName~ServiceHostingTests`
Expected: FAIL.

- [ ] **Step 3: Implement Worker Service and Dependency Injection in Program.cs**
Configure `Microsoft.Extensions.Hosting.WindowsServices`, SQLite connection string, Serilog console/file logging, and register background workers.

- [ ] **Step 4: Run test to verify it passes**
Run: `dotnet test --filter FullyQualifiedName~ServiceHostingTests`
Expected: PASS.

- [ ] **Step 5: Commit**
`git add src/CertManager.Service tests/CertManager.UnitTests && git commit -m "feat(service): configure background worker service and IPC hosting"`

---

### Task 6: WPF Desktop UI Shell & IPC Status Integration

**Files:**
- Create: `src/CertManager.Desktop/ViewModels/ViewModelBase.cs`
- Create: `src/CertManager.Desktop/ViewModels/MainViewModel.cs`
- Create: `src/CertManager.Desktop/ViewModels/DashboardViewModel.cs`
- Create: `src/CertManager.Desktop/Services/ServiceIpcClient.cs`
- Modify: `src/CertManager.Desktop/App.xaml.cs`
- Modify: `src/CertManager.Desktop/MainWindow.xaml`
- Create: `src/CertManager.Desktop/Views/DashboardView.xaml`

**Interfaces:**
- Produces: WPF Desktop application showing live connection state to the background service, dashboard summary counts (Certificates, Expiring Soon, Active Orders), and recent audit events.

- [ ] **Step 1: Implement MVVM base and ViewModels**
Implement `ViewModelBase` with `INotifyPropertyChanged`, `RelayCommand`, and `DashboardViewModel`.

- [ ] **Step 2: Implement ServiceIpcClient in Desktop**
Connects to `CertManager.Ipc` named pipe and requests `GetServiceStatusAsync`.

- [ ] **Step 3: Implement MainWindow layout and DashboardView**
Create modern clean dashboard layout with service connection indicator (Connected / Disconnected / Connecting), stats cards (Total Certs, Expiring Soon, Failed Renewals), and navigation tabs.

- [ ] **Step 4: Verify build and test suite**
Run: `dotnet build && dotnet test`
Expected: Build succeeded with 0 errors, all unit tests passing.

- [ ] **Step 5: Commit**
`git add src/CertManager.Desktop && git commit -m "feat(desktop): implement WPF dashboard shell and IPC client connection"`
