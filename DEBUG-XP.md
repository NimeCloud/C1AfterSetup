# C1AfterSetup — Debugging Experience Log

> **Purpose:** Accumulated debugging knowledge, gotchas, and fix recipes from real sessions.
> New AI agents / fresh contexts should read this file FIRST when starting a C1AfterSetup task.
>
> **Last updated:** 2026-08-05 (deploy5: AuthKit pages fix + pipeline registration)

---

## 1. Template GUIDs (Hardcoded in .cshtml)

Each `.cshtml` page template defines its own `TemplateId` in the `Configure()` method.
These are the **source of truth** — when referencing a template via `IPage.TemplateId`, use exactly these GUIDs:

| Template File | TemplateId |
|---------------|------------|
| `AuthKit.PanelLayout.cshtml` | `34df3c70-3c58-454b-8aab-f2259c9b9ca5` |
| `AuthKit.AuthLayout.cshtml` | `24e07eb6-17c9-424e-9c16-219f57a85900` |
| `AuthKit.SetupPage.cshtml` | `a1b0a1b0-0000-0000-0000-a1b0a1b0a1b0` |
| `AuthKit.UserManagementPage.cshtml` | `f9a2e1d7-8c3b-4b2a-9e1f-1a2b3c4d5e6f` |
| `AuthKit.GroupManagementPage.cshtml` | `50562763-9421-40b6-8897-5214ab170051` |
| `AuthKit.GroupPermissionPage.cshtml` | `ff5a0c90-7f64-4e56-b9b9-1c078276f4c2` |
| `AuthKit.UserPermissionPage.cshtml` | `70d2e0d8-d2d1-4209-a5ae-a0bc7d283a9d` |

### C1 PageType IDs (built-in, always the same):
| PageType | GUID |
|----------|------|
| Home | `de22fed1-0729-4ad3-aa1c-6047e54bf429` |
| Page (sub-page) | `f7869eb2-7369-4eb2-af47-e3be261e92c7` |

### AuthKit Deterministic Page IDs:
| Page | PageId |
|------|--------|
| AuthKit Home | `e1e01000-0000-0000-0000-e1e0e1e0e1e0` |
| Login | `f6f06000-0000-0000-0000-f6f0f6f0f6f0` |
| Register | `a7a07000-0000-0000-0000-a7a0a7a0a7a0` |
| Forgot Password | `b8b08000-0000-0000-0000-b8b0b8b0b8b0` |
| Reset Password | `c9c09000-0000-0000-0000-c9c0c9c0c9c0` |
| Logout | `d0d0a000-0000-0000-0000-d0d0d0d0d0d0` |
| Users | `b2b02000-0000-0000-0000-b2b0b2b0b2b0` |
| Groups | `c3c03000-0000-0000-0000-c3c0c3c0c3c0` |
| Group Permissions | `d4d04000-0000-0000-0000-d4d0d4d0d4d0` |
| User Permissions | `e5e05000-0000-0000-0000-e5e0e5e0e5e0` |

---

## 2. DeployAuthKitPagesStep: Registration GOTCHA

**Symptom:** Step compiles but never appears in pipeline output.

**Root cause:** Two things must be done when adding a new `ISetupStep`:

1. **Add `<Compile Include>` to [`.csproj`](C1AfterSetup/C1AfterSetup.csproj)** — the project uses explicit file listing, NOT wildcard auto-include.
2. **Add `new DeployAuthKitPagesStep()` to the `steps` list in [`Program.cs`](C1AfterSetup/Program.cs:139).**

If only one is done, you get either:
- CS0246 (missing Compile Include) → step class not found
- Step exists but silently skipped (missing from steps list)

**Checklist for adding a new step:**
- [ ] Create `C1AfterSetup/Steps/NewStep.cs` implementing `ISetupStep`
- [ ] Add `<Compile Include="Steps\NewStep.cs" />` to `.csproj`
- [ ] Add `new NewStep()` to the `steps` list in `Program.cs`

---

## 3. DeployAuthKitPagesStep: The "Silent Skip" Bug (FIXED)

**Original bug:** The step tried to merge AuthKit pages from `sources/DataStores/` XML files that **didn't exist**. The code checked `if (!Directory.Exists(srcDir))` and `return true` (silent skip). Pages were never created.

**Fix (2026-08-05):** Rewrote the step to generate all 10 AuthKit pages **programmatically** — no source XML dependency. The step directly writes to these DataStore XMLs:
- `Composite.Data.Types.IPage_tr-TR.xml`
- `Composite.Data.Types.IPage_Unpublished_tr-TR.xml`
- `Composite.Data.Types.IPageStructure.xml`
- `Composite.Data.Types.IPagePlaceholderContent_tr-TR.xml`

**Page hierarchy:** AuthKit Home (root) → 9 sub-pages as children.

**Placeholder content:**
- Auth pages (Login, Register, Forgot, Reset, Logout): embed `<f:function name="AuthKit.XxxForm" />` in content
- Management pages (Users, Groups, Permissions): empty content (template renders itself)
- AuthKit Home: empty content (SetupPage template renders itself)

---

## 4. Build Commands

### Build the tool:
```cmd
dotnet build C1AfterSetup/C1AfterSetup.csproj -c Release
```
Output: `C1AfterSetup/bin/Release/C1AfterSetup.exe`

### Build the output website (precompile check):
```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\aspnet_compiler.exe -v / -p "r:\deploy5"
```
**Known false positive:** `CS0433: httpheaderscontrol.ascx` duplicated type — this is a C1 CMS pre-existing issue, NOT caused by our changes. The site works fine on IIS/IIS Express.

### Run the tool:
```cmd
C1AfterSetup\bin\Release\C1AfterSetup.exe -site "E:\C1\dev\Website" -out "r:\deploy5" -mode offline -force
```
`-out` (not `--output`!) — copies source to target, then applies pipeline there.

---

## 5. Shell Environment

The default shell on this system is **CMD.EXE** (`C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` — but commands run in cmd context).

**Do NOT use:**
- PowerShell cmdlets (`Remove-Item`, `Start-Sleep`, `Get-Content`)
- Unix utilities (`rm`, `grep`, `sed`)
- `&&` chaining (use `&` for cmd)

**Use:**
- `rmdir /s /q <dir>` — delete directory
- `findstr` — search in files
- `dir` — list files
- `ping -n N 127.0.0.1 >nul` — sleep N seconds
- `&` — command chaining in cmd

---

## 6. C1 CMS DataStores XML Format

When writing to DataStore XML files, the format is specific:

### IPage / IPage_Unpublished:
```xml
<PageElementsElements>
  <PageElements PublicationStatus="published" ChangeDate="..." CreationDate="..."
    ChangedBy="admin" CreatedBy="admin"
    Id="guid" TemplateId="guid" PageTypeId="guid"
    Title="..." MenuTitle="..." UrlTitle="..."
    FriendlyUrl="" Description="" SourceCultureName="tr-TR"
    VersionId="guid" />
</PageElementsElements>
```

### IPageStructure:
```xml
<PageStructureElementsElements>
  <PageStructureElements Id="page-guid" ParentId="parent-or-zero-guid" LocalOrdering="0" />
</PageStructureElementsElements>
```
Root pages use `ParentId="00000000-0000-0000-0000-000000000000"`.

### IPagePlaceholderContent:
```xml
<PagePlaceholderContentElementsElements>
  <PagePlaceholderContentElements PublicationStatus="published"
    ChangeDate="..." CreationDate="..." ChangedBy="admin" CreatedBy="admin"
    PageId="page-guid" PlaceHolderId="content"
    Content="<html>...</html>"
    SourceCultureName="tr-TR" VersionId="page-version-guid" />
</PagePlaceholderContentElementsElements>
```
**CRITICAL:** `VersionId` MUST match the `VersionId` in `IPage_tr-TR.xml` for that page, otherwise C1 shows Content == null.

---

## 7. Input/Output Convention

When the user says:
> "input olarak E:\C1\dev\Website output olarak r:\deploy3"

This means:
- `-site "E:\C1\dev\Website"` — source site to copy from
- `-out "r:\deploy3"` — output deployment folder

The `-out` logic:
1. Copies `-site` → `-out` (recursive directory copy)
2. Applies pipeline to `-out`
3. Source site is untouched

---

## 8. The `-fresh` Flag Behavior

| Flag | DataStores | Packages | Existing Pages | Composite.Generated.dll |
|------|-----------|----------|---------------|------------------------|
| **No `-fresh`** | Preserved | Preserved | Preserved | Regenerated if types present |
| **`-fresh`** | Cleared | Cleared | Destroyed | Regenerated from scratch |

**Without `-fresh`:** The site keeps its existing C1 state (SystemInitialized.xml, admin user, pages, installed packages). This is what you want for adding AuthKit to an existing site.

**With `-fresh`:** Everything is reset. The site will show "Site under construction" on first launch until the `.c1pac` is auto-installed.

---

## 9. Known aspnet_compiler False Positives

When building with `aspnet_compiler.exe`, these errors are **pre-existing C1 CMS issues** and can be ignored:

| Error | Cause |
|-------|-------|
| `CS0433: httpheaderscontrol.ascx` duplicate | C1's Composite/ controls clash with ASP.NET temp cache |
| `CS0168: variable 'ex' declared but never used` | Pre-existing template code, not our change |

**Our changes are clean if:**
- Zero errors mention `AuthKit`, `KeyTreeStore`, or our page IDs
- The DLL contains all expected types (`KeyTreeStoreKit.Data.KeyTreeItem`, `AuthKit.Data.*`)

---

## 10. Step Execution Order (Current Pipeline)

From [`Program.cs`](C1AfterSetup/Program.cs:139):

| # | Step | Class |
|---|------|-------|
| 1 | Preflight | `PreflightStep` |
| 2 | Fresh Prep | `PrepareFreshStep` |
| 3 | Dependencies | `DeployDependenciesStep` |
| 4 | Data Types | `DeployDataTypesStep` |
| 5 | C1 Package | `DeployPackageStep` |
| 6 | Compile DLL | `CompileGeneratedTypesStep` |
| 7 | App_Code | `DeployAppCodeStep` |
| 8 | **Page Templates** | `DeployPageTemplatesStep` |
| 9 | **AuthKit Pages** | `DeployAuthKitPagesStep` |
| 10 | Razor | `DeployRazorStep` |
| 11 | Web.config | `ConfigureWebConfigStep` |
| 12 | Verify | `VerifyStep` |
| 13 | Gen. Verify | `VerifyGeneratedTypesStep` |

**IMPORTANT:** `DeployAuthKitPagesStep` must run AFTER `DeployPageTemplatesStep` (needs template GUIDs) and BEFORE `DeployRazorStep` (Razor functions referenced in placeholder content). Also AFTER `CompileGeneratedTypesStep` because the AuthKit templates reference generated types.

---

## 11. AuthKit Page → Razor Function Mappings

The 5 auth pages need their Razor function embedded in placeholder content:

| Page | Razor Function |
|------|---------------|
| Login (`f6f06000...`) | `AuthKit.LoginForm` |
| Register (`a7a07000...`) | `AuthKit.RegisterForm` |
| Forgot Password (`b8b08000...`) | `AuthKit.ForgotPasswordForm` |
| Reset Password (`c9c09000...`) | `AuthKit.ResetPasswordForm` |
| Logout (`d0d0a000...`) | `AuthKit.LogoutForm` |

Management pages (Users, Groups, Permissions) and AuthKit Home have **empty** placeholder content — their templates render themselves.

---

## 12. File Checklist for New C1AfterSetup Tasks

When starting fresh, check these files FIRST:
- [`PROJECT.md`](PROJECT.md) — architecture, pipeline, data types
- [`DEBUG-XP.md`](DEBUG-XP.md) — this file, debugging gotchas
- [`C1AfterSetup/C1AfterSetup.csproj`](C1AfterSetup/C1AfterSetup.csproj) — explicit file listing
- [`C1AfterSetup/Program.cs`](C1AfterSetup/Program.cs) — step pipeline registration
- [`C1AfterSetup/Config/setup.manifest.json`](C1AfterSetup/Config/setup.manifest.json) — what gets deployed
