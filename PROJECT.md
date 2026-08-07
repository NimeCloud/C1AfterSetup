r:\deploy5 klasörüne çıkart# C1AfterSetup — Project Knowledge Base

> **Purpose:** Automate post-installation additions to a C1 CMS Website: AuthKit data types, App_Code modules, page templates, Razor functions, KeyTreeStore, Web.config hardening — with zero manual steps.
>
> **Last updated:** 2026-08-07 (deploy21: AdminTools pages now deployed via DeployAdminToolPagesStep in pipeline; docs synchronized)

---

## 1. Project Overview

| Item | Detail |
|------|--------|
| **Solution** | [`C1AfterSetup.sln`](C1AfterSetup.sln) |
| **Project** | [`C1AfterSetup/C1AfterSetup.csproj`](C1AfterSetup/C1AfterSetup.csproj) |
| **Framework** | .NET Framework 4.8 (C# 5 compatible) |
| **Output** | `C1AfterSetup/bin/Release/C1AfterSetup.exe` |
| **Source site** | [`Website/`](Website/) — a fresh, never-started C1 CMS 6.x install |
| **Payload** | [`C1AfterSetup/sources/`](C1AfterSetup/sources/) — App_Code, DataMetaData, templates, Razor, DLLs |
| **Manifest** | [`C1AfterSetup/Config/setup.manifest.json`](C1AfterSetup/Config/setup.manifest.json) |
| **Test tools** | [`testdata/`](testdata/) — PowerShell diagnostics, fake site fixture |

---

## 2. Core Problem: `Composite.Generated.dll`

C1 CMS compiles C# classes for all `isCodeGenerated` data types into `bin/Composite.Generated.dll` at runtime.

**Why offline deploy fails:**
1. ASP.NET compiles `App_Code` during `HostingEnvironment.Initialize` — **before** C1's `Application_Start`.
2. On an already-initialized site, C1 **deletes** hand-dropped `DataMetaData` XMLs as orphans.
3. Types only get compiled when registered via C1's official API (`DynamicTypeManager.CreateStore`).

**Solution:** The tool generates the type-containing DLL BEFORE deploying App_Code, using a headless IIS Express session with a [`[ApplicationStartup]` hook](C1AfterSetup/sources/DataTypeAutoInstaller.cs).

---

## 3. Pipeline (Step Order)

Defined in [`Program.cs`](C1AfterSetup/Program.cs:11) `Main()`:

| # | Step | File | Key Role |
|---|------|------|----------|
|  1 | **Preflight** | [`PreflightStep.cs`](C1AfterSetup/Steps/PreflightStep.cs) | Validate site, take backup, gate `-fresh` |
|  2 | **Fresh Prep** | [`PrepareFreshStep.cs`](C1AfterSetup/Steps/PrepareFreshStep.cs) | Strip runtime state (DataStores/Packages/Cache/Log/AppState/Media) to factory-fresh; preserves DLL if it has expected types |
|  3 | **Dependencies** | [`DeployDependenciesStep.cs`](C1AfterSetup/Steps/DeployDependenciesStep.cs) | Copy `sources/bin/` + `sources/overrides/` to `~/bin`; ships `sources/generated/Composite.Generated.dll` if present |
|  4 | **Data Types** | [`DeployDataTypesStep.cs`](C1AfterSetup/Steps/DeployDataTypesStep.cs) | Copy DataMetaData XMLs to `~/App_Data/Composite/PendingDataTypes` (NOT DataMetaData — C1 deletes orphans there) |
|  5 | **Hybrid Data Store** | [`ConfigureSqlDataProviderStep.cs`](C1AfterSetup/Steps/ConfigureSqlDataProviderStep.cs) | Inject `c1` connection string in `Web.config`, register `DynamicSqlDataProvider` plugin in `Composite.config`, create empty `DynamicSqlDataProvider.config` |
|  6 | **C1 Package** | [`DeployPackageStep.cs`](C1AfterSetup/Steps/DeployPackageStep.cs) | Build `.c1pac` from XMLs → `~/App_Data/Composite/AutoInstallPackages/` |
|  7 | **Compile DLL** | [`CompileGeneratedTypesStep.cs`](C1AfterSetup/Steps/CompileGeneratedTypesStep.cs) | Deploy hook → IIS Express headless → register types → recycle → DLL generated |
|  8 | **App_Code** | [`DeployAppCodeStep.cs`](C1AfterSetup/Steps/DeployAppCodeStep.cs) | Deploy AuthKit + KeyTreeStore + HeaderCleanupModule to `~/App_Code` |
|  9 | **Templates** | [`DeployPageTemplatesStep.cs`](C1AfterSetup/Steps/DeployPageTemplatesStep.cs) | Deploy `.cshtml` page templates (master-first per manifest `order`) |
| 10 | **AuthKit Pages** | [`DeployAuthKitPagesStep.cs`](C1AfterSetup/Steps/DeployAuthKitPagesStep.cs) | Generate 10 AuthKit pages programmatically in DataStores XMLs (Home + 9 sub-pages) |
| 11 | **Admin Tools** | [`DeployAdminToolPagesStep.cs`](C1AfterSetup/Steps/DeployAdminToolPagesStep.cs) | Generate 2 AdminTools pages in DataStores XMLs (Data Provider Default + Datatype Migrator, top-level, idempotent) |
| 12 | **Razor** | [`DeployRazorStep.cs`](C1AfterSetup/Steps/DeployRazorStep.cs) | Deploy Razor functions to `~/App_Data/Razor` |
| 13 | **Web.config** | [`ConfigureWebConfigStep.cs`](C1AfterSetup/Steps/ConfigureWebConfigStep.cs) | Remove headers, register modules, add assembly refs |
| 14 | **Verify** | [`VerifyStep.cs`](C1AfterSetup/Steps/VerifyStep.cs) | Report deployed files |
| 15 | **Gen. Verify** | [`VerifyGeneratedTypesStep.cs`](C1AfterSetup/Steps/VerifyGeneratedTypesStep.cs) | Online: check DLL types, HTTP 200, DataStore files, C1 log errors |

---

## 4. CLI Reference

```
C1AfterSetup.exe -site <path> [-out <path>] [-mode online|offline] [-url <url>]
                 [-fresh] [-capture] [-dryrun] [-force] [-manifest <path>]
```

| Flag | Description |
|------|-------------|
| `-site` | Target C1 site root (required) |
| `-out` | Copy `-site` → output dir, run pipeline there (target must be empty) |
| `-mode` | `online` waits for C1 recompile; default `offline` |
| `-url` | Site URL for online health checks |
| `-fresh` | Reset target to never-started state (strip runtime, keep type-DLL) |
| `-capture` | Copy target's `Composite.Generated.dll` → `sources/generated/` for fast-path shipping |
| `-dryrun` | Plan only, write nothing |
| `-force` | Skip verify, re-apply all steps |
| `-manifest` | Alternative manifest path |

### Deploy to initialized site (preserve content):
```powershell
C1AfterSetup.exe -site "E:\dev\Website" -out "r:\deploy"   # no -fresh!
```

### Deploy to fresh site (zero manual steps):
```powershell
C1AfterSetup.exe -site "E:\dev\Website" -out "r:\deploy" -fresh
```

---

## 5. The Compile Step In Detail

[`CompileGeneratedTypesStep.cs`](C1AfterSetup/Steps/CompileGeneratedTypesStep.cs) is the most complex step.

### Flow:
1. Deploy [`DataTypeAutoInstaller.cs`](C1AfterSetup/sources/DataTypeAutoInstaller.cs) hook to `~/App_Code/`
2. Write PowerShell compile script to temp
3. **Phase 1:** Start IIS Express → hook fires on `Application_Start` → reads `PendingDataTypes` XMLs → calls `DynamicTypeManager.CreateStore(descriptor, true)` → types registered in DataStores → kill IIS Express
4. **Phase 2:** Start IIS Express again (new session, no DLL lock) → touch `Web.config` for recycle → C1 regenerates `Composite.Generated.dll` → kill IIS Express
5. Verify DLL contains all `generatedTypes` from manifest
6. If `-fresh`: call `ResetRuntimeState()` to return to clean-fresh state

### `ResetRuntimeState()`:
- Deletes: DataStores, Packages, Cache, Log, LogFiles, ApplicationState, Temp, Media, C1AfterSetup
- Recreates empty: DataStores, Packages, Log, LogFiles
- Re-copies PendingDataTypes XMLs from source
- **CRITICAL: Regenerates `.c1pac`** — C1 consumed it during Phase 1; without this, real first start has no types → "Site under construction"

### Key details:
- Uses **two separate IIS Express sessions** because one session locks the loaded DLL
- Writes output to a log file (not stdout redirect — avoids deadlock)
- Uses `Assembly.Load(byte[])` not `Assembly.LoadFrom()` — avoids file lock on DLL
- DllContainsExpectedTypes() resolves dependencies via `AppDomain.AssemblyResolve`

---

## 6. Data Type Hierarchy (Group Order)

From [`setup.manifest.json`](C1AfterSetup/Config/setup.manifest.json:5):

| Group | Types | Dependencies |
|-------|-------|--------------|
| **A** (3) | User, Group, Module | none |
| **B** (3) | Permission, Token, KeyTreeItem | User, Module |
| **C** (4) | PermissionInGroup, PermissionInUser, UserInGroup, UserModuleState | User, Group, Module, Permission |

### Generated Types (`generatedTypes` in manifest):
```
KeyTreeStoreKit.Data.KeyTreeItem
AuthKit.Data.Authentication.User
AuthKit.Data.Authentication.Token
AuthKit.Data.Authorization.Group
AuthKit.Data.Authorization.Module
AuthKit.Data.Authorization.Permission
AuthKit.Data.Authorization.PermissionInGroup
AuthKit.Data.Authorization.PermissionInUser
AuthKit.Data.Authorization.UserInGroup
AuthKit.Data.Authorization.UserModuleState
```

**Note:** `KeyTreeItem` uses namespace `KeyTreeStoreKit.Data` (renamed from `AuthKit.KeyTreeStore.Data` to make it independent from AuthKit).

---

## 7. Namespace Architecture

### `KeyTreeStoreKit` (independent key/value store)
- **Namespace:** `KeyTreeStoreKit`
- **File:** [`sources/KeyTreeStore/KeyTreeStoreManager.cs`](C1AfterSetup/sources/KeyTreeStore/KeyTreeStoreManager.cs)
- **Generated type:** `KeyTreeStoreKit.Data.KeyTreeItem` (type ID: `7e43385d-2a82-4221-867e-dddcaeb3f883`)
- **DataMetaData XML:** [`sources/DataMetaData/KeyTreeItem 7e43385d-2a82-4221-867e-dddcaeb3f883.xml`](C1AfterSetup/sources/DataMetaData/KeyTreeItem%207e43385d-2a82-4221-867e-dddcaeb3f883.xml)
- **Deploy target:** `~/App_Code/KeyTreeStore/KeyTreeStoreManager.cs`

### `AuthKit` (authentication & authorization framework)
- **Namespaces:**
  - `AuthKit.Authentication` — [`AuthenticationManager.cs`](C1AfterSetup/sources/AuthKit/Authentication/AuthenticationManager.cs), [`OAuthHelper.cs`](C1AfterSetup/sources/AuthKit/Authentication/OAuthHelper.cs)
  - `AuthKit.Authorization` — [`AuthorizationManager.cs`](C1AfterSetup/sources/AuthKit/Authorization/AuthorizationManager.cs), `GroupManagement.cs`, `KeyInitializer.cs`, `PermissionManagement.cs`, `PermissionSyncService.cs`, `Models/`
  - `AuthKit.C1` — [`C1Security.cs`](C1AfterSetup/sources/AuthKit/C1/C1Security.cs), [`C1UrlHelper.cs`](C1AfterSetup/sources/AuthKit/C1/C1UrlHelper.cs)
  - `AuthKit.Startup` — [`AuthStartupHandler.cs`](C1AfterSetup/sources/AuthKit/Startup/AuthStartupHandler.cs)
- **References to KeyTreeStoreKit:** Uses `global::KeyTreeStoreKit.KeyTreeStoreManager` to avoid namespace collision (AuthKit.KeyTreeStore would resolve ambiguously)

### Cross-reference pattern:
```csharp
// In AuthKit.Startup.AuthStartupHandler:
global::KeyTreeStoreKit.KeyTreeStoreManager.EnsureRoot();

// In AuthKit.Authentication.AuthenticationManager:
global::KeyTreeStoreKit.KeyTreeStoreManager.Get("Auth.OAuth.Google.ClientId", "");
```

### Page Templates & Razor:
```csharp
// In PageTemplates/AuthKit.SetupPage.cshtml and Razor/AuthKit/SetupPages.cshtml:
KeyTreeStoreKit.KeyTreeStoreManager.Set("Auth.LoginPageId", ...);
KeyTreeStoreKit.KeyTreeStoreManager.Set("Auth.OAuth.Google.ClientId", ...);
```

---

## 8. Source File Inventory

```
C1AfterSetup/sources/
├── DataTypeAutoInstaller.cs              # [ApplicationStartup] hook — registers PendingDataTypes
├── HeaderCleanupModule.cs                # IHttpModule to strip response headers
├── bin/
│   └── BCrypt.Net-Next.dll               # Password hashing dependency
├── overrides/
│   └── README.txt                        # Optional bin overrides
├── generated/
│   └── (Composite.Generated.dll)         # Created by -capture; shipped as fast path
├── DataMetaData/                         # 10 XMLs → .c1pac + PendingDataTypes
│   ├── Group 75d09216-*.xml
│   ├── Module 155b11ff-*.xml
│   ├── User 8029145b-*.xml
│   ├── KeyTreeItem 7e43385d-*.xml        # namespace="KeyTreeStoreKit.Data"
│   ├── Permission 3cb96a1a-*.xml
│   ├── Token 1f86b8d5-*.xml
│   ├── PermissionInGroup 74c07d7d-*.xml
│   ├── PermissionInUser 52035a5f-*.xml
│   ├── UserInGroup 8bc2b2d6-*.xml
│   └── UserModuleState ac33a998-*.xml
├── KeyTreeStore/
│   └── KeyTreeStoreManager.cs            # namespace KeyTreeStoreKit
├── AuthKit/
│   ├── Authentication/
│   │   ├── AuthenticationManager.cs      # Login, CRUD, OAuth, tokens
│   │   └── OAuthHelper.cs
│   ├── Authorization/
│   │   ├── AuthorizationManager.cs
│   │   ├── GroupManagement.cs
│   │   ├── KeyInitializer.cs
│   │   ├── PermissionManagement.cs
│   │   ├── PermissionSyncService.cs
│   │   └── Models/
│   │       ├── ErrorCodes.cs
│   │       ├── GroupKeys.cs
│   │       ├── ModuleKeys.cs
│   │       ├── PermissionInfoAttribute.cs
│   │       ├── PermissionKeys.App.cs
│   │       ├── PermissionKeys.cs
│   │       └── PermissionModels.cs
│   ├── C1/
│   │   ├── C1Security.cs
│   │   └── C1UrlHelper.cs
│   └── Startup/
│       └── AuthStartupHandler.cs         # Initialize() — keys, permissions, KeyTreeStoreKit root
├── PageTemplates/
│   ├── AuthKit.PanelLayout.cshtml
│   ├── AuthKit.AuthLayout.cshtml
│   ├── AuthKit.SetupPage.cshtml          # OAuth settings, login/register page config
│   ├── AuthKit.UserManagementPage.cshtml
│   ├── AuthKit.GroupManagementPage.cshtml
│   ├── AuthKit.GroupPermissionPage.cshtml
│   └── AuthKit.UserPermissionPage.cshtml
└── Razor/AuthKit/
    ├── ForgotPasswordForm.cshtml
    ├── LoginForm.cshtml
    ├── LogoutForm.cshtml
    ├── RegisterForm.cshtml
    ├── ResetPasswordForm.cshtml
    └── SetupPages.cshtml
```

---

## 9. Key Design Decisions & Gotchas

### Why `PendingDataTypes` not `DataMetaData`?
C1 deletes unrecognized XMLs from `DataMetaData` on initialized sites. The hook reads from `PendingDataTypes` instead. After the hook processes them, it deletes the XMLs (`File.Delete`). `ResetRuntimeState` re-copies them for the real first start.

### Why two IIS Express sessions?
A single IIS Express session loads `Composite.Generated.dll` and **locks it**. The second session (after killing the first) allows C1 to write the updated DLL. Without this, the DLL is never regenerated → App_Code fails to compile.

### Why `Assembly.Load(byte[])` not `Assembly.LoadFrom()`?
`Assembly.LoadFrom()` locks the file. When `DllContainsExpectedTypes()` checks the DLL, it loads from bytes to avoid locking. Same for `PrepareFreshStep.DllContainsExpectedTypes()`.

### Why `global::` prefix for KeyTreeStoreKit references?
Inside the `AuthKit` namespace, `KeyTreeStore` would resolve to `AuthKit.KeyTreeStore` (ambiguous). Using `global::KeyTreeStoreKit.KeyTreeStoreManager` forces resolution from the global namespace.

### CS1628: `out` parameter in lambda
In [`KeyTreeStoreManager.cs`](C1AfterSetup/sources/KeyTreeStore/KeyTreeStoreManager.cs:64), `parentId` is an `out` parameter used inside a lambda. C# 5 doesn't allow this. Fixed by copying to a local: `string currentParentId = parentId;`

### `.c1pac` regeneration in `ResetRuntimeState()`
During the compile IIS Express session, C1 **consumes** the `.c1pac` from `AutoInstallPackages` (installs it, moves to `Packages/installed`). `ResetRuntimeState` deletes `Packages` and recreates it empty. Without regenerating the `.c1pac`, the real first start has no types → "Site under construction". Fixed by [`RegenerateC1Pac()`](C1AfterSetup/Steps/CompileGeneratedTypesStep.cs:281).

---

## 10. Common Error Scenarios

| Symptom | Cause | Fix |
|---------|-------|-----|
| **"Site under construction"** | `.c1pac` missing from `AutoInstallPackages` after compile reset, or cshtml references stale namespace | `RegenerateC1Pac()` + verify all `KeyTreeStoreKit.` refs |
| **CS0234: 'Data' not in namespace 'AuthKit'** | `Composite.Generated.dll` doesn't contain AuthKit types | Run compile step (IIS Express headless) |
| **CS0234: 'KeyTreeStore' not found** | Stale `KeyTreeStore.` reference instead of `KeyTreeStoreKit.` | Rename all occurrences |
| **File access denied on DLL delete** | `Assembly.LoadFrom()` locks the file | Use `Assembly.Load(File.ReadAllBytes(path))` |
| **Foreign keys integrity: admin** | `-fresh` destroyed initialized site content | Deploy without `-fresh` to preserve existing content |
| **IIS Express stdout deadlock** | `RedirectStandardOutput` + `WaitForExit` hangs | Write compile log to file instead |
| **DataStores not cleared after reset** | IIS Express file locks still active | Kill IIS Express before reset; add retry/delay |

---

## 11. Build & Deploy

### Build:
```powershell
dotnet build C1AfterSetup/C1AfterSetup.csproj -c Release
# Output: C1AfterSetup/bin/Release/C1AfterSetup.exe
```

### Deploy (to ramdisk):
```powershell
# Initialize target WITH content preservation:
rmdir /s /q "r:\deploy" 2>nul
C1AfterSetup.exe -site .\Website -out r:\deploy

# Fresh (zero manual steps, destroys existing content):
rmdir /s /q "r:\deploy" 2>nul
C1AfterSetup.exe -site .\Website -out r:\deploy -fresh
```

### Verify DLL types:
```powershell
# Use diag script (requires Composite.dll in GAC or bin path)
powershell -File testdata/diag_generated.ps1 -dllPath "r:\deploy\bin\Composite.Generated.dll" -binPath "r:\deploy\bin"
```

---

## 12. Test Tools

| Tool | Purpose |
|------|---------|
| [`testdata/diag_generated.ps1`](testdata/diag_generated.ps1) | Inspect types in Composite.Generated.dll |
| [`testdata/build_generated.ps1`](testdata/build_generated.ps1) | Bootstrap IIS Express headless compile |
| [`testdata/reflect_api.ps1`](testdata/reflect_api.ps1) | Reflect on C1 API surface |
| [`testdata/reflect_api2.ps1`](testdata/reflect_api2.ps1) | Extended C1 API reflection |
| [`testdata/fakesite/`](testdata/fakesite/) | Minimal never-started C1 fixture for testing |

---

## 13. State & Resumability

- Progress stored at `~/App_Data/Composite/C1AfterSetup/state.json`
- Each step: `Verify()` checks current state → skip if correct; re-apply if stale
- Failed steps automatically retried on next run
- `-force` skips verify and re-applies everything
- Backups saved to `r:\backups\YYYYMMDD-HHMMSS\` before first mutation

---

## 14. Debugging Experience

See [`DEBUG-XP.md`](DEBUG-XP.md) for accumulated debugging knowledge:
- Template GUIDs & page ID mapping
- C1 DataStores XML format reference
- Build commands & shell quirks
- Step registration checklist
- Known aspnet_compiler false positives
- `-fresh` behavior details
