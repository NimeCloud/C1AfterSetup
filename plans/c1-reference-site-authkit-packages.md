# Reference Site + Package Inventory + Migration Plan

> **Purpose:** Document the reference C1 CMS site that holds the **working precursor AuthKit**
> implementation (DataTables admin UI + C# API via Razor-based synthetic API) and its exact
> NuGet package inventory. This is the authoritative source for porting the admin pages/APIs
> and rebuilding our package tracking.
>
> **Last updated:** 2026-08-07

---

## 1. Reference Site Location (IMPORTANT)

| Item | Path |
|------|------|
| **Reference site root** | `E:\_CODE_\WebDev\SystemC1\Website` |
| **Package store** | `E:\_CODE_\WebDev\SystemC1\packages` |
| **Package pin (authoritative)** | `E:\_CODE_\WebDev\SystemC1\Website\packages.config` |
| **Solution** | `E:\_CODE_\WebDev\SystemC1\Website\LocaThor.sln` |
| **NuGet config** | `E:\_CODE_\WebDev\SystemC1\Website\NuGet.config` |

> ⚠️ The fresh install `E:\C1\dev\Website` (used by the deploy pipeline as `-site`) is **NOT** the
> reference site. The reference site is `E:\_CODE_\WebDev\SystemC1\Website`.

---

## 2. What the Reference Site Contains

- **Working AuthKit precursor** — the user/group/permission mechanism that our `AuthKit` (in
  `C1AfterSetup/sources/AuthKit`) is derived from.
- **Working DataTables admin pages** — Users, Groups, Permissions pages render correctly,
  **both** the JavaScript side (DataTables Editor) **and** the C# API side work.
- **Razor-based synthetic API system** — the reference site implements its admin API endpoints
  as Razor functions (a custom/synthetic API pattern), NOT the `ApiHandler`/`AuthApi` pattern we
  use in C1AfterSetup. This is the part we need to port into real HTTP handlers.
- This is a **location/MQTT application** (LocaThor) — it also contains MQTT, Hangfire,
  SignalR/WampSharp, MailKit, WebPush code that our AuthKit does **not** need. Only the
  AuthKit-related subset is relevant to port.

---

## 3. NuGet Package Inventory (from `packages.config`)

`packages.config` (101 packages, net48) is the **authoritative** source — it records the exact
versions the site was running with after the user hand-tested combinations. The `packages/`
folder contains BOTH old and new versions (the user's testing history); `packages.config` pins
the **working** one.

### 3a. AuthKit / admin-page relevant
| Package | Version (working) | Note |
|---------|-------------------|------|
| `BCrypt.Net-Next` | **4.1.0** | 4.0.3 also present in `packages/` (older, tested) |
| `Newtonsoft.Json` | **13.0.3** | JSON serialization (AuthApi uses it) |
| `Newtonsoft.Json.Bson` | **1.0.2** | |
| `Microsoft.CodeDom.Providers.DotNetCompilerPlatform` | **2.0.1** | Roslyn CodeDom for C1 Razor |
| `System.Memory` | **4.6.3** | 4.6.0 also present |
| `System.Buffers` | **4.6.1** | 4.6.0 also present |
| `System.Numerics.Vectors` | **4.6.1** | 4.6.0 also present |
| `System.Runtime.CompilerServices.Unsafe` | **6.1.2** | 6.1.0 also present |
| `System.ValueTuple` | **4.5.0** | |
| `System.Threading.Tasks.Extensions` | **4.6.0** | |
| `Microsoft.Extensions.DependencyInjection` + `.Abstractions` | **1.1.0** | |
| `Castle.Core` | **4.2.1** | |
| `Microsoft.AspNet.Razor` | **3.2.3** | Razor 3 (C1 Razor) |
| `Microsoft.AspNet.WebPages` | **3.2.3** | |
| `Microsoft.Web.Infrastructure` | **1.0.0.0** | |

### 3b. C1 CMS platform
| Package | Version |
|---------|---------|
| `CompositeC1.Core` | **6.13.0** |
| `CompositeC1.ScheduledTasks` | **0.5.1** |
| `CompositeC1Contrib.Core` | **0.9.0** |
| `Hangfire.CompositeC1` | **1.6.20** |
| `Hangfire.Core` | **1.8.14** |

### 3c. Google OAuth
| Package | Version |
|---------|---------|
| `Google.Apis` | **1.71.0** |
| `Google.Apis.Auth` | **1.71.0** |
| `Google.Apis.Core` | **1.71.0** |
| `Google.Apis.Oauth2.v2` | **1.68.0.1869** |

### 3d. Real-time / messaging (NOT needed for AuthKit, present in site)
| Package | Version |
|---------|---------|
| `Microsoft.AspNet.SignalR.Core` / `.JS` | **2.4.3** |
| `Microsoft.Owin` / `.Host.SystemWeb` / `.Hosting` | **4.2.3** |
| `Microsoft.Owin.Security` | **2.1.0** |
| `Owin` | **1.0** |
| `WampSharp` family (4) | **18.3.1** |
| `MQTTnet` | **4.3.6.1152** |

### 3e. E-mail / push (NOT needed for AuthKit, present in site)
| Package | Version |
|---------|---------|
| `MailKit` | **4.13.0** (4.15.1 also present — MimeKit is 4.15.1) |
| `MimeKit` | **4.15.1** |
| `BouncyCastle.Cryptography` | **2.6.2** (2.5.1 also present) |
| `Portable.BouncyCastle` | **1.8.1.3** |
| `SharpZipLib` | **1.4.2** (0.86.0 also present) |
| `WebPush` | **1.0.12** |

### 3f. .NET compatibility shims (NETStandard facades, transitive deps)
`System.*` packages at **4.3.0** (`System.Runtime`, `System.IO`, `System.Linq`, `System.Threading`,
`System.Security.Cryptography.*`, etc.), plus `NETStandard.Library 1.6.1`,
`Microsoft.NETCore.Platforms 1.1.0`, `System.Collections.Immutable 1.3.1`,
`System.Diagnostics.DiagnosticSource 4.3.0`, `System.Threading.Tasks.Dataflow 4.7.0`,
`System.Reactive* 3.0.0`, `System.Formats.Asn1 8.0.1`, `jQuery 3.7.1`, `jQuery 1.6.4` (old, in folder).

> **Full authoritative list:** read `E:\_CODE_\WebDev\SystemC1\Website\packages.config` directly.

---

## 4. Key Constraints & Lessons (from the user's hand-install experience)

1. **Latest versions BREAK C1.** The user installed packages one-by-one by trial-and-error.
   Do NOT blindly upgrade to latest.
2. **Version pinning is mandatory.** Every package must be pinned to the exact version from
   `packages.config` (the working combination).
3. **`packages/` folder contains duplicates** (old + new). Always resolve via `packages.config`,
   never by picking the newest folder.
4. **After any upgrade:** verify with `aspnet_compiler` + C1 log (AI_CONTEXT §4, §9).

---

## 5. Migration Plan

### Goal
Bring the **working DataTables admin UI + C# API** from the reference site into our
`C1AfterSetup` pipeline, and establish **trackable NuGet package management**.

### Phase A — Package management foundation (NEW helper project)
1. Create `C1SiteDependencies/` — a small .NET Framework 4.8 class library (old-style .csproj,
   C# 5-compatible) with a `packages.config`.
2. Populate `packages.config` from the reference site's (AuthKit-relevant subset), pinned exactly.
3. `nuget restore` → classic `packages/<id>.<version>/` folder appears (trackable).
4. Build copies package DLLs to output → tool copies to `sources/bin/`.
5. Replace the ad-hoc `sources/bin/` DLL list; `setup.manifest.json` `binDependencies` stays as
   the deployment manifest.

### Phase B — Port the admin API + DataTables UI
1. Extract the reference site's working DataTables page templates + Razor synthetic API logic.
2. Port the API logic into real HTTP handlers (`ApiHandler.cs` / `AuthApi.cs`) implementing:
   `GetRealUsers`, `GetTemplateUsers`, `AddUser`, `UpdateUser`, `DeleteUser`,
   `GetAllGroupIdsForUser`, `GetAllGroupsForUserManagement`, `UpdateUserGroupsDelta`,
   `AddGroup`, `UpdateGroup`, `DeleteGroup`, permission endpoints.
3. Fix the DataTables Editor CDN 404 (license) — either self-host the licensed JS, or replace
   with plain DataTables + custom modals.
4. Re-apply the XHTML/CDATA/UTF-8-BOM rules from AI_CONTEXT §15 to the ported templates.

### Phase C — Full package scan (optional, later)
- `nuget locals global-packages -list` / scan `%USERPROFILE%\.nuget\packages` to enumerate all
  packages the machine has; match against reference `packages.config` and `bin/*.dll` versions
  to add the complete set (including non-AuthKit packages if desired).

---

## 6. Open Questions / Unknowns

- **Transitive dependencies** of each top-level package are not yet mapped; start resolution
  from `packages.config` (it already lists them flat) — order of install is handled by NuGet
  during restore, so install order does not matter (it resolves the graph).
- Whether the reference site's DataTables Editor is licensed (self-hostable) vs. needing a
  replacement — determines Phase B step 3.

---

## 7. Related Docs

- [`AI_CONTEXT.md`](../AI_CONTEXT.md) — pipeline, Razor/XHTML/CDATA rules, build commands
- [`plans/deploy-admin-tools-to-new-site.md`](deploy-admin-tools-to-new-site.md) — new-task prompt (AdminTools)
- [`plans/new-task-prompt-port-authkit-admin-pages.md`](new-task-prompt-port-authkit-admin-pages.md) — short prompt for Phase A+B
