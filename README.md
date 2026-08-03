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
- **Header cleanup** — optionally removes `Server` and custom response headers via `Web.config`.

## Requirements

- .NET Framework 4.8
- A C1 CMS 6.x site (or a folder that will become one) — `Web.config` + `App_Data` must exist.
- C# 5-compatible sources for the tool itself (compiled with the framework `MSBuild.exe`).

## Usage

```powershell
# Offline (site stopped) — safest
C1AfterSetup.exe -site "E:\sites\MyC1Site"

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
| `-mode online\|offline` | `online` waits for C1 to compile between phases; default `offline` |
| `-url <url>` | Site URL used for health checks in online mode |
| `-dryrun` | Reports the planned steps without writing anything |
| `-force` | Skips verify and re-applies all steps from sources (takes a new backup) |
| `-manifest <path>` | Alternative manifest (default `Config\setup.manifest.json`) |

## How it works

The pipeline is defined in [`Program.cs`](C1AfterSetup/Program.cs) and each phase is an `ISetupStep`:

1. **Preflight** — validates the site, probes online mode, takes a backup (first run or `-force` only).
2. **Dependencies** — copies `sources/bin` + `sources/overrides` to `~/bin`.
3. **Data types** — deploys `DataMetaData` XMLs parent-first (grouped A→C), regenerating `Composite.Generated.dll` in online mode.
4. **App_Code** — AuthKit + KeyTreeStore + helper modules to `~/App_Code`.
5. **Page templates** — deploys templates master-first per manifest `order`.
6. **Razor** — deploys Razor functions to `~/App_Data/Razor`.
7. **Web.config** — applies `removeServerHeader`, `customHeaders clear/remove`, module registration (only if not already present).
8. **Verify** — reports every deployed file and the `Web.config` state.

Everything to deploy lives in `C1AfterSetup/sources/`, and what maps where is described in `Config/setup.manifest.json`.

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

## Building

```powershell
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" C1AfterSetup.sln /p:Configuration=Release
```

Or open [`C1AfterSetup.sln`](C1AfterSetup.sln) in Visual Studio and build.

> The tool's own sources are C# 5-compatible so they build with the .NET Framework `MSBuild.exe` without Roslyn. The deployed App_Code sources (AuthKit, KeyTreeStore) are compiled by C1 at runtime.

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
