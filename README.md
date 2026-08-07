# C1AfterSetup

Automates the manual post-installation steps for a **fresh [C1 CMS](https://www.c1cms.com/) site**: deploys custom data types, App_Code modules, page templates, Razor functions, dependency DLLs and `Web.config` hardening — all from a single, resumable pipeline.

Built for C1 CMS 6.x on .NET Framework 4.8 (Windows). No third-party NuGet dependencies in the tool itself.

## Features

- **Step pipeline** — preflight → DLLs → data types → App_Code → templates → Razor → `Web.config` → verify.
- **Online / offline modes** — `online` waits for C1 to recompile between phases; `offline` does not.
- **Resumable & idempotent** — every step *verifies* the target state first: skips what is already correct, refreshes what is stale, and automatically retries steps that failed on a previous run. Progress is stored in the target site at `~/App_Data/Composite/C1AfterSetup/state.json`.
- **Content-aware copies** — only changed files are rewritten (MD5 compare), so re-runs are fast and non-destructive.
- **KeyTreeStore** — a built-in hierarchical key/value store (`KeyTreeItem` data type) shared across the whole site, with both path-based and flat key/value APIs. Auto-creates a `Root` container when the path starts with `Root/`, and works with or without a Root sentinel.
- **AuthKit integration** — deploys the AuthKit authorization framework (users, groups, permissions) as App_Code + data types.
- **Hybrid XML + SQL data store** — dual-provider setup (`DynamicXmlDataProvider` + `DynamicSqlDataProvider`). Most types stay in XML for file-sync portability; selected types live in SQL Server. Includes two admin-tool pages in Content perspective:
  - **Default Data Provider Selector** — scan providers, view/change the default dynamic-type provider.
  - **Datatype Migrator** — list generated types with their current provider, selectively migrate types (and data) between XML and SQL using `DataProviderCopier`.
- **Header cleanup** — optionally removes `Server` and custom response headers via `Web.config`.

## Requirements

- .NET Framework 4.8
- A C1 CMS 6.x site (or a folder that will become one) — `Web.config` + `App_Data` must exist.
- C# 5-compatible sources for the tool itself (compiled with the framework `MSBuild.exe`).

## Usage

```powershell
# Fresh deploy — ZERO manual steps (target is a brand-new, never-started C1 site)
C1AfterSetup.exe -site "E:\sites\MyC1Site" -fresh

# Build a deployable folder from an existing started site, without touching it
# (copies Website -> .\deploy, then applies -fresh + all additions offline)
C1AfterSetup.exe -site "E:\dev\Website" -out "E:\deploy\MyC1Site" -fresh

# Offline (site stopped) — safest, on an already-fresh folder
C1AfterSetup.exe -site "E:\sites\MyC1Site"

# Capture the type-containing Composite.Generated.dll from a site where the
# data-type package is already installed (one-time). Later offline deploys ship it.
C1AfterSetup.exe -site "E:\C1\dev\Website" -capture

# Online (C1 running) — waits for recompilation between phases
C1AfterSetup.exe -site "E:\sites\MyC1Site" -mode online -url "https://localhost/mysite"

# Plan only — writes nothing
C1AfterSetup.exe -site "E:\sites\MyC1Site" -dryrun

# Force re-apply every step (bypasses verify, takes a fresh backup)
C1AfterSetup.exe -site "E:\sites\MyC1Site" -force
```

### Arguments

| Argument | Description |
|---|---|
| `-site <path>` | Target C1 site root folder (required) |
| `-out <path>` | If given, copies `-site` into this folder and runs the pipeline there, leaving the source folder untouched (target must be empty) |
| `-mode online\|offline` | `online` waits for C1 to compile between phases; default `offline` |
| `-url <url>` | Site URL used for health checks in online mode |
| `-dryrun` | Reports the planned steps without writing anything |
| `-force` | Skips verify and re-applies all steps from sources (takes a new backup) |
| `-fresh` | Resets the target to a **never-started** C1 site: strips C1 runtime state (`DataStores`, `Packages` markers, `Cache`, `Log`, `ApplicationState`, `Media`) and rebuilds them empty (C1 requires these folders to exist). `bin\Composite.Generated.dll` is removed **only if** it lacks the expected generated types |
| `-capture` | Copies the target site's `bin\Composite.Generated.dll` into `sources\generated\` so later offline deploys ship the type-containing DLL |
| `-manifest <path>` | Alternative manifest (default `Config\setup.manifest.json`) |

## How it works

The pipeline is defined in [`Program.cs`](C1AfterSetup/Program.cs) and each phase is an `ISetupStep`:

1. **Preflight** — validates the site, probes online mode, takes a backup (first run or `-force` only), and gates fresh deploys (an already-initialized target requires `-fresh` or `-mode online`).
2. **Fresh prep** (`-fresh` only) — resets the target to a never-started site: strips C1 runtime state and rebuilds it empty (C1 requires these folders to exist). `bin\Composite.Generated.dll` is removed only if it lacks the expected types.
3. **Dependencies** — copies `sources/bin` + `sources/overrides` to `~/bin`; also ships a captured `sources/generated/Composite.Generated.dll` if present.
4. **Data types** — deploys the `DataMetaData` XMLs to `~/App_Data/Composite/PendingDataTypes`, and builds a C1 package (`.c1pac`) into `~/App_Data/Composite/AutoInstallPackages`.
5. **Hybrid datastore (SQL provider infra)** — configures the dual XML+SQL provider setup: injects the `c1` connection string into `Web.config`, registers `DynamicSqlDataProvider` plugin in `Composite.config` (right after `DynamicXmlDataProvider`), and creates `DynamicSqlDataProvider.config` with an empty `<Interfaces />`. `DynamicXmlDataProvider` remains the default — new types default to XML; existing types stay where they are.
6. **C1 package** — builds a validated `.c1pac` from the DataMetaData XMLs and places it in `~/App_Data/Composite/AutoInstallPackages` (consumed by C1 on first start).
7. **Compile generated types** — deploys the `DataTypeAutoInstaller` `[ApplicationStartup]` hook, starts the site headlessly (IIS Express), the hook registers the pending types via `DynamicTypeManager.CreateStore`, and a graceful recycle makes C1 write `Composite.Generated.dll` with the types. Skips if the DLL already contains them.
8. **App_Code** — AuthKit + KeyTreeStore + startup sync (initializes permission keys, KeyTreeStore `Root`, and DB↔C# records on first load) to `~/App_Code`.
9. **Page templates** — deploys templates master-first per manifest `order` (AuthKit + AdminTools).
10. **AuthKit pages** — generates the 10 AuthKit Content-perspective pages (Home + 9 sub-pages: Login, Register, Forgot, Reset, Logout, Users, Groups, Permissions) directly in DataStore XMLs (idempotent — skips existing).
11. **Admin tool pages** — generates the 2 admin-tool Content-perspective pages (Data Provider Default + Datatype Migrator) in the DataStore XMLs (top-level, idempotent).
12. **Razor** — deploys Razor functions to `~/App_Data/Razor` (Login, Register, Forgot, Reset, Logout, Setup forms).
13. **Web.config** — applies `removeServerHeader`, `customHeaders clear/remove`, module registration, and required assembly references (`System.Net.Http`, `System.Web.Extensions`).
14. **Verify** — reports every deployed file and the `Web.config` state.
15. **Generated types verify** — after the first start (online), confirms `Composite.Generated.dll` contains the expected data types, the site is healthy, and C1 logs are clean.

Everything to deploy lives in `C1AfterSetup/sources/`, and what maps where is described in `Config/setup.manifest.json`.

### Data types & `Composite.Generated.dll`

C1 CMS compiles the C# classes for every `isCodeGenerated` data type (interfaces + data wrappers) into `Composite.Generated.dll` via `Composite.Core.Types.CodeGenerationManager`. The `App_Code` sources reference those generated types directly (e.g. `AuthKit.KeyTreeStore.Data.KeyTreeItem`), so `App_Code` can only compile against a `Composite.Generated.dll` that already contains them.

**Why the DLL must be present before the first start:** ASP.NET compiles the `App_Code` directory during `HostingEnvironment.Initialize`, **before** C1's `Application_Start` runs. So a site whose `App_Code` references generated types needs `Composite.Generated.dll` (with those types) already in `~/bin`. C1 deletes hand-dropped `DataMetaData` XMLs on an already-initialized site as orphans — types only get compiled when they are **registered via C1's own API**.

**How the tool produces the DLL (no manual steps):**
1. The `DataMetaData` XMLs are deployed to `~/App_Data/Composite/PendingDataTypes`.
2. A `[ApplicationStartup]` hook ([`sources/DataTypeAutoInstaller.cs`](C1AfterSetup/sources/DataTypeAutoInstaller.cs)) is deployed to `~/App_Code` and, on the first start, reads those XMLs and calls `DynamicTypeManager.CreateStore(descriptor, true)` — C1's official registration API — which creates the stores and queues compilation.
3. The tool starts the site headlessly (IIS Express), waits for the types to register, then triggers a graceful recycle so C1 writes `Composite.Generated.dll` with the types.
4. Only then is the full `App_Code` (AuthKit) deployed — it compiles against the now type-containing DLL.

This works on both fresh (first-time setup) and already-initialized sites, verified with a real IIS Express run (all 10 AuthKit types ended up in the DLL). If IIS Express is unavailable the step warns and tells you how to trigger it manually.

As a fast path, `-capture` saves a type-containing `Composite.Generated.dll` into `sources\generated\` and later deploys ship it straight into `~/bin` (skipping the compile step).

## Resumability

- Each step implements `Verify(context)` and `Fingerprint(context)`.
- `Verify` returns `true` when the target is already correct → the step is **skipped**.
- `Verify` returns `false` → the step re-applies (only changed files).
- A step recorded as **failed** on a previous run is always retried.
- `~/App_Data/Composite/C1AfterSetup/state.json` persists per-step completion, failure state, and source fingerprints.

## KeyTreeStore

A hierarchical key/value store built on the `AuthKit.KeyTreeStore.Data.KeyTreeItem` data type.

```csharp
// Path-based (groups + keys)
var pwd  = KeyTreeStore.KeyTreeStoreManager.GetValue<string>("SMTP Settings/Password");
KeyTreeStore.KeyTreeStoreManager.SetValue("SMTP Settings/Password", "secret");

// Flat convenience API
var cid  = KeyTreeStore.KeyTreeStoreManager.Get("Auth.OAuth.Google.ClientId", "");
KeyTreeStore.KeyTreeStoreManager.Set("Auth.LoginPageId", "…");

// Lists (multiple values under one key)
KeyTreeStore.KeyTreeStoreManager.AddValue("Guvenlik/IzinVerilenIPler", "1.1.1.1");
```

Paths use `/` as the separator. A leading `/` is treated as "start from Root"; `Root/…`, `/…` and `…` are equivalent. The `Root` container is auto-created when the path starts with `Root/`, and the system works with or without a Root sentinel (manual parent items from the C1 Data perspective are supported).

## Hybrid Data Store (XML + SQL Server)

C1 CMS 6.x ships with a SQL data provider in the core (`Composite.dll`) — no package needed. This tool configures a hybrid dual-provider setup:

- `DynamicXmlDataProvider` (default) — most types stay in XML `DataStores` for file-sync portability.
- `DynamicSqlDataProvider` — selected types live in SQL Server. Per-type routing is via each provider's own `<Interfaces>` config file.

See [`plans/c1-cms-hybrid-sql-xml-datastore.md`](plans/c1-cms-hybrid-sql-xml-datastore.md) for the authoritative reference on provider routing, `DataProviderCopier`, and cross-provider references.

### Admin Tool Pages

Two self-rendering page templates are deployed as Content-perspective pages:

| Page | URL | Function |
|---|---|---|
| Data Provider Default | `/Data-Provider-Default` | Scan providers, see current default, change default dynamic-type provider |
| Datatype Migrator | `/Datatype-Migrator` | List generated types with per-type provider listbox; Apply triggers `DataProviderCopier` + config move + `.xml.migrated` rename + recycle |

Both pages are admin-gated (C1 Administrator group). Migration backs up provider configs and XML DataStores before any mutation.

### Extending

The hybrid datastore is extensible:
- Add more SQL-bound types by temporarily flipping the default provider, creating the type, then flipping back.
- Migrate existing types at runtime via the Datatype Migrator page.
- The admin tool pages can be extended with new functionality by adding C# logic to the `@functions` `RenderPage()` method.

## Building

```powershell
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" C1AfterSetup.sln /p:Configuration=Release
```

Or open [`C1AfterSetup.sln`](C1AfterSetup.sln) in Visual Studio and build.

> The tool's own sources are C# 5-compatible so they build with the .NET Framework `MSBuild.exe` without Roslyn. The deployed App_Code sources (AuthKit, KeyTreeStore) are compiled by C1 at runtime.

### Avoid slow VS 2022 build on the output website

The deployed output folder is a C1 CMS Web Site (not a Web Application). When opened in VS 2022 and built, VS runs the ASP.NET precompiler which walks the entire tree and compiles every template — extremely slow on C1 sites. **This build is unnecessary** — C1 compiles everything at runtime.

**Fix:** In VS 2022, open the Web Site **Property Pages → Build** and **uncheck "Build this project"**. F5/Ctrl+F5 will then run without recompilation. For a one-time precompile check, use `aspnet_compiler.exe` from the CLI (see [`AI_CONTEXT.md`](AI_CONTEXT.md) §4).

## Project structure

```
C1AfterSetup.sln
README.md
C1AfterSetup/
├─ Program.cs                # CLI entry point + step pipeline
├─ SetupContext.cs           # shared context (site path, mode, state, manifest)
├─ SetupManifest.cs          # setup.manifest.json model
├─ SetupState.cs             # per-site progress checkpoint (state.json)
├─ FileSyncUtil.cs           # MD5 copy-if-different + fingerprints
├─ Config/setup.manifest.json# what to deploy and where
├─ Steps/                    # ISetupStep implementations
├─ Detect/                   # site probe + compilation monitor
└─ sources/                  # deployed payload (AuthKit, KeyTreeStore, DataMetaData, …)
```

## License

This project is provided as-is. See the repository for license details.
