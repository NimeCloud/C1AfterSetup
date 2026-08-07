# C1AfterSetup — AI Context Memory

> **Purpose:** Restore accumulated knowledge when a `new task` starts a fresh AI agent / context.
> Debugging gotchas, command patterns, constants, and methods are collected here.
> A new agent should treat this file as the **FIRST file to read**.
>
> **Last updated:** 2026-08-07 (required + optional NuGet packages documented; binding-redirect + Verify fixes; all docs in English)

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

### Avoid slow VS 2022 "validating directories" on output:
When you open the deploy output folder in VS 2022 as a Web Site and press Build, VS runs the ASP.NET precompiler (`aspnet_compiler.exe`) which walks the entire directory tree and compiles every template — this is painfully slow on C1 sites (tens of thousands of files). **The build is unnecessary** — C1 runtime-compiles everything anyway.

**Fix:** In VS 2022, open the Web Site **Property Pages → Build** (or MSBuild Options) and **uncheck "Build this project"**. Then F5 / Ctrl+F5 runs without recompilation. If you need a precompile check, run `aspnet_compiler` once from the CLI as a one-time verification.

**If you forget and VS starts validating:** the tree size can be reduced by deleting regenerable runtime folders first (`Cache`, `GeneratedRazorHost`, `LogFiles`, `TreeDefinition`, `App_Data_Composite_*`).

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
> "input as E:\C1\dev\Website, output as r:\deploy3"

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
| 5 | **Hybrid Data Store** | `ConfigureSqlDataProviderStep` |
| 6 | C1 Package | `DeployPackageStep` |
| 7 | Compile DLL | `CompileGeneratedTypesStep` |
| 8 | App_Code | `DeployAppCodeStep` |
| 9 | **Page Templates** | `DeployPageTemplatesStep` |
| 10 | **AuthKit Pages** | `DeployAuthKitPagesStep` |
| 11 | **Admin Tools** | `DeployAdminToolPagesStep` |
| 12 | Razor | `DeployRazorStep` |
| 13 | Web.config | `ConfigureWebConfigStep` |
| 14 | Verify | `VerifyStep` |
| 15 | Gen. Verify | `VerifyGeneratedTypesStep` |

**IMPORTANT:** `ConfigureSqlDataProviderStep` runs AFTER data types (needs the manifest) and BEFORE C1 package/compile (provider infra must be in place before types are registered). `DeployAuthKitPagesStep` and `DeployAdminToolPagesStep` must run AFTER `DeployPageTemplatesStep` (need template GUIDs) and BEFORE `DeployRazorStep`. Also AFTER `CompileGeneratedTypesStep` because the templates reference generated types.

`DeployAdminToolPagesStep` appends 2 page records (IPage + IPage_Unpublished + IPageStructure + PlaceholderContent) to the DataStore XMLs — mirroring the `DeployAuthKitPagesStep` pattern. The `.cshtml` templates are deployed separately by `DeployPageTemplatesStep` (manifest `order: 2`).

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
- [`AI_CONTEXT.md`](AI_CONTEXT.md) — this file, debugging gotchas, context memory
- [`C1AfterSetup/C1AfterSetup.csproj`](C1AfterSetup/C1AfterSetup.csproj) — explicit file listing
- [`C1AfterSetup/Program.cs`](C1AfterSetup/Program.cs) — step pipeline registration
- [`C1AfterSetup/Config/setup.manifest.json`](C1AfterSetup/Config/setup.manifest.json) — what gets deployed
- [`plans/c1-cms-hybrid-sql-xml-datastore.md`](plans/c1-cms-hybrid-sql-xml-datastore.md) — hybrid XML+SQL datastore setup guide, provider routing, cross-provider references
- [`plans/deploy-admin-tools-to-new-site.md`](plans/deploy-admin-tools-to-new-site.md) — new-task prompt: step-by-step instructions for deploying hybrid datastore + admin tool pages to a new C1 CMS site
- [`plans/c1-reference-site-authkit-packages.md`](plans/c1-reference-site-authkit-packages.md) — **reference site** (working AuthKit + DataTables admin) + exact NuGet package inventory + migration plan
- [`plans/new-task-prompt-port-authkit-admin-pages.md`](plans/new-task-prompt-port-authkit-admin-pages.md) — short new-task prompt: port AuthKit admin pages/APIs + NuGet package management

**REFERENCE SITE (IMPORTANT):** `E:\_CODE_\WebDev\SystemC1\Website` — working precursor AuthKit
with DataTables admin UI + C# API (Razor synthetic API). Authoritative package versions:
`E:\_CODE_\WebDev\SystemC1\Website\packages.config`. Package store:
`E:\_CODE_\WebDev\SystemC1\packages`. ⚠️ The fresh install `E:\C1\dev\Website` is NOT the reference site.

---

## 13. Git Commit Method

Characters like `<`, `"`, `>` (or Turkish characters) in a commit message may be mis-parsed by the
shell. **Always use the `-F` (file) method:**

```cmd
:: 1) Write the message to a temp file (via write_to_file)
:: 2) Commit:
git add <files>
git commit -F "path/to/commit-message.txt"

:: 3) Delete the temp file:
del "path/to/commit-message.txt"
```

**Never use `-m "..."`** — `&`, `<`, `>` break in cmd.exe.

---

## 14. Admin Tools Template GUIDs (deploy20)

### Templates

| Template File | TemplateId |
|---------------|------------|
| `AdminTools.DataProviderSelector.cshtml` | `A1100000-0000-0000-0000-A110A110A110` |
| `AdminTools.DatatypeMigrator.cshtml` | `A1200000-0000-0000-0000-A120A120A120` |

### AdminTools Deterministic Page IDs

| Page | PageId |
|------|--------|
| Data Provider Default | `A1110000-0000-0000-0000-A111A111A111` |
| Datatype Migrator | `A1210000-0000-0000-0000-A121A121A121` |

Pages are top-level (ParentId = zero GUID) with LocalOrdering 100/101.

### AdminTools VersionId Gotcha

**Symptom:** `System.FormatException: GUID must contain 32 digits with 4 dashes` at `WritePageElements`.

**Root cause:** Trying to generate a deterministic VersionId via `page.PageId.ToString().Substring(0, 28) + "0001"` produces an invalid GUID string (cuts mid-dash).

**Fix:** Use `Guid.NewGuid().ToString()` — same pattern as `DeployAuthKitPagesStep`. The `WritePlaceholderContent` helper reads VersionIds back from the IPage XML, so any unique GUID works; they don't need to be deterministic.

---

## 15. C1 Razor Parser Gotchas (CSHTML Page Templates)

C1 CMS 6.13 uses an older ASP.NET Web Pages Razor parser with strict limitations:

1. **No single-statement `if` in `@{ }` blocks.** Every `if`, `else if`, `foreach` body MUST be enclosed in `{ }`.
   ```csharp
   // BROKEN:
   if (cond) DoSomething();
   // FIXED:
   if (cond) { DoSomething(); }
   ```

2. **No `@if`/`@foreach` nesting inside `@if` bodies after HTML.** Inside a top-level `@if (cond) { }`:
   - First statement(s) can be bare `if` (no `@`).
   - After any HTML markup, nested `@if`/`@foreach` may fail with "Unexpected 'if' keyword after '@' character".
   - **Workaround:** Move ALL HTML rendering to `@functions` helpers returning `IHtmlString`, and call via `@RenderPage(...)` as a single call in markup. This avoids all Razor nesting ambiguities.

3. **`@if/else` WHERE the `else` block starts with HTML then has nested control flow** also breaks similarly.

4. **VS Code Razor linter shows false errors** — it uses ASP.NET Core Razor rules. Trust the C1 compiler output (log file), not VS Code squiggles.

5. **Strict XHTML in preview mode (`/c1mode(unpublished)`): TWO mandatory rules.**

   **5a. `<style>` and `<script>` MUST use CDATA wrappers WITH NEWLINES.** C1 CMS parses the rendered template output as XHTML. Inline CSS with `@keyframes`, CSS variables, or any `;` in `<style>` — and JavaScript with `;`, `&`, `<`, or `>` in `<script>` — cause `System.Xml.XmlException: The ';' character cannot be included in a name`.

   **CRITICAL:** `// <![CDATA[` is a JavaScript single-line comment. Since `StringBuilder.Append()` does NOT add newlines, all JS after it lands on the SAME LINE and is COMMENTED OUT. You MUST add `\n` after the CDATA opening and before the closing.

   ```csharp
   // CSS (block comment, but newlines keep it readable and safe)
   sb.Append("<style>\n/* <![CDATA[ */\n");
   sb.Append("... CSS ...\n");
   sb.Append("/* ]]> */</style>");

   // JavaScript (SINGLE-LINE COMMENT — newlines are MANDATORY)
   sb.Append("<script>\n// <![CDATA[\n");
   sb.Append("... JS ...\n");
   sb.Append("// ]]></script>");
   ```

   **5b. ALL `&` in HTML text MUST be escaped as `&`.** Any bare `&` in HTML text nodes (button labels, headings, paragraph text) causes `System.Xml.XmlException: Error parsing EntityName`. This is XHTML 101 — `&` is a special character that starts XML entity references. In C# string literals inside `StringBuilder.Append()`, write `&` instead of `&` for all text content outside CDATA blocks.

   ```csharp
   // BROKEN:
   sb.Append("<button>Save & Restart</button>");
   // FIXED:
   sb.Append("<button>Save & Restart</button>");
   ```

**RULE: When creating or modifying ANY .cshtml page template in this project:**
- **(a)** ALWAYS wrap `<style>` with `/* <![CDATA[ */ ... /* ]]> */` and `<script>` with `// <![CDATA[ ... // ]]>` — WITH newlines after/before CDATA markers (JS single-line comment would comment out everything otherwise).
- **(b)** NEVER use bare `&` in HTML text — use "and". `&` is unreliable (write tools silently revert it).
- **(c)** NEVER use bare boolean attributes in XHTML: `disabled`, `selected`, `checked`, `readonly`, `multiple`, `required` — MUST be `disabled="disabled"`, `selected="selected"`, etc.
- **(d)** ALL `.cshtml` files MUST be saved as **UTF-8 with BOM**. PowerShell `WriteAllText` default is UTF-8 WITHOUT BOM, which causes Turkish character corruption in C1's Razor parser. Use `new System.Text.UTF8Encoding($true)` (BOM) when saving via PowerShell, or use `write_to_file` tool (BOM-aware). If Turkish characters appear garbled (e.g., `Å` instead of `Ş`), re-save with BOM.
- **(e)** AuthKit management pages MUST use **auth-only** checks (`currentUser == null`), NOT `HasPermission(...)`. The AuthKit Group/PermissionInGroup stores are EMPTY on fresh deploys (the permission system isn't seeded), so `HasPermission` always returns false and every page redirects to login → infinite login loop. The C1 admin's shadow user (`LinkedUserManager.EnsureShadowUser`) is never added to an AuthKit group. ALL redirects MUST include `?redirect=<urlencode(currentUrl)>` so after login the user returns to where they were.
- All rules are NOT optional — they prevent runtime errors in C1 preview mode.

---

## 16. Quick-Fix Workflow (Patch deployed output without full redeploy)

When an error is found in `r:\deployXY` (e.g., XHTML parsing error in a template, missing file, wrong config), and the fix is small/targeted:

**Apply the fix to BOTH places — the source AND the deployed output:**
1. Fix the source file in `C1AfterSetup/sources/` (or `C1AfterSetup/Steps/`, etc.) so it's permanent.
2. Copy the fixed file directly to the corresponding path in `r:\deployXY\` overwriting the broken one.

**When to do this vs. full redeploy:**
| Scenario | Action |
|----------|--------|
| Fixing a single `.cshtml` template | Quick-fix: copy source → deployXY |
| Fixing a step's C# logic | Quick-fix: rebuild tool, then copy EXE + sources → deployXY (but re-run is safer) |
| Adding new files / changing pipeline order | Full redeploy to new deployXZ |
| VS 2022 has deployXY open (locked `.vs` folder) | Quick-fix the data files (PageTemplates, DataStores, Razor, configs) — these are not locked |

**Important:** When VS 2022 has the output folder open, the `.vs` subfolder is locked. You can still overwrite `App_Data/PageTemplates/*.cshtml`, `App_Data/Composite/DataStores/*.xml`, `Web.config`, `App_Data/Razor/*.cshtml`, etc. — these are NOT locked by VS. Only the `.vs\` directory is. If the `-out` target fails because `rmdir` can't delete the directory, the fix is to deploy to a new number (`deployXZ`) OR manually delete everything except `.vs`.

**Example (XHTML `&` fix):**
```cmd
:: Fix source:
::   edit C1AfterSetup\sources\PageTemplates\AdminTools.DataProviderSelector.cshtml
::   change "Save & Restart" -> "Save & Restart"

:: Quick-fix deployed output (VS 2022 has r:\deploy24 open):
copy /y "C1AfterSetup\sources\PageTemplates\AdminTools.DataProviderSelector.cshtml" "r:\deploy24\App_Data\PageTemplates\AdminTools.DataProviderSelector.cshtml"
:: -> VS/IIS Express picks it up on next request; no full redeploy needed.
```

---

## 17. NuGet Package Management (AuthKit-relevant, fresh-install order)

### Authoritative source
- Reference site: `E:\_CODE_\WebDev\SystemC1\Website` (working precursor AuthKit)
- **Package pin (authoritative):** `E:\_CODE_\WebDev\SystemC1\Website\packages.config`
- Package store: `E:\_CODE_\WebDev\SystemC1\packages` — contains OLD + NEW versions (test history).
  **Always resolve via `packages.config`, never by the newest folder.**
- Deploy source for DLLs: `C1AfterSetup/sources/bin` (listed in `setup.manifest.json` `binDependencies`).

### Install order (VS 2022 method: leaf/dependency FIRST, then target package)
When adding a package, VS 2022 lists its dependencies. **Cancel the install**, manually install the
newest possible versions of those dependencies FIRST (they exist in `packages/`), then the target.

| Order | Package | Pin | Target DLL path in store | Notes |
|-------|---------|-----|--------------------------|-------|
| 1 | `System.Buffers` | 4.6.1 | `lib/net462/System.Buffers.dll` | leaf, no deps |
| 2 | `System.Numerics.Vectors` | 4.6.1 | `lib/net462/System.Numerics.Vectors.dll` | leaf, no deps |
| 3 | `System.Runtime.CompilerServices.Unsafe` | 6.1.2 | `lib/net462/System.Runtime.CompilerServices.Unsafe.dll` | leaf, no deps |
| 4 | `System.Threading.Tasks.Extensions` | 4.6.0 | `lib/net462/System.Threading.Tasks.Extensions.dll` | depends on Unsafe |
| 5 | `System.Memory` | 4.6.3 | `lib/net462/System.Memory.dll` | depends on 1,2,3,4 |
| 6 | `Newtonsoft.Json` | 13.0.3 | `lib/net45/Newtonsoft.Json.dll` | **REQUIRED** by `ApiHandler.cs`/`AuthApi.cs` |
| 7 | `BCrypt.Net-Next` | 4.1.0 | `lib/net48/BCrypt.Net-Next.dll` | leaf |
| 8 | `Microsoft.CodeDom.Providers.DotNetCompilerPlatform` | 2.0.1 | `lib/...` + `tools/roslynlatest/*` | brings roslyn + ValueTuple |

### Gotcha fixed (2026-08-07)
- `Newtonsoft.Json.dll` was missing from `sources/bin` + `binDependencies` while
  `ApiHandler.cs`/`AuthApi.cs` call `Newtonsoft.Json.JsonConvert` → runtime failure.
  Copied `Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll` into `sources/bin` and added to manifest.
- The reference site's bin DOES contain `Newtonsoft.Json.dll` (check `bin\Newtonsoft.Json.dll.refresh`).
- `sources/bin/BCrypt.Net-Next.dll` was 4.2.0.0 (wrong) — replaced with pinned `4.1.0` net48.
- Added `System.Threading.Tasks.Extensions.dll` (4.6.0) + `System.ValueTuple.dll` (4.5.0) to sources/bin + manifest.

### ConfigureWebConfigStep binding-redirect bugs fixed (2026-08-07)
Two bugs meant the AuthKit redirects were logged but NEVER written to disk:
1. **`doc.Save` ran BEFORE the redirect block** — the redirect loop appended nodes and set
   `changed = true`, but nothing saved them. **Fix:** re-save the doc after the redirect loop.
2. **`RemoveDependentAssembly`/`HasDependentAssembly` used `SelectNodes("dependentAssembly")`**
   which is namespace-agnostic-failing: C1's `assemblyBinding` lives in
   `xmlns="urn:schemas-microsoft-com:asm.v1"`, so the existing `Newtonsoft.Json → 6.0.0.0` entry
   was never removed → two conflicting redirects for the same assembly. **Fix:** iterate
   `ChildNodes` filtering on `LocalName == "dependentAssembly"` (namespace-independent).
- VerifyStep DataMetaData check now matches by **file name (GUID)** in both `PendingDataTypes` and
  `DataMetaData` (C1 rewrites the XML content after `CompileGeneratedTypesStep` runs, so content
  equality produced false "EKSİK/ESKİ" errors).

### After any package change
- `dotnet build C1AfterSetup/C1AfterSetup.csproj -c Release`
- `aspnet_compiler.exe -v / -p "r:\deployNN"` + C1 log (§9 for known false positives).

---

## 18. Optional C1 CMS / NuGet Packages (NOT auto-deployed)

The pipeline ships ONLY the AuthKit-required DLLs (`sources/bin` + manifest `binDependencies`).
The reference site (`E:\_CODE_\WebDev\SystemC1\Website`) has ~73 more DLLs that this tool
deliberately does NOT deploy — they are C1 CMS packages installable/upgradeable later from
**C1 Console → Packages** (or NuGet) when a feature needs them.

**Inventory (exact pins):** [`C1AfterSetup/Config/optional.packages.json`](C1AfterSetup/Config/optional.packages.json)

| Group | Packages (pinned) |
|---|---|
| Google OAuth | `Google.Apis 1.71.0`, `Google.Apis.Auth 1.71.0`, `Google.Apis.Core 1.71.0`, `Google.Apis.Oauth2.v2 1.68.0.1869` |
| E-mail (SMTP) | `MailKit 4.13.0`, `MimeKit 4.15.1`, `BouncyCastle.Cryptography 2.6.2`, `Portable.BouncyCastle 1.8.1.3`, `SharpZipLib 1.4.2` |
| Scheduled Tasks | `Hangfire.CompositeC1 1.6.20`, `Hangfire.Core 1.8.14`, `CompositeC1.ScheduledTasks 0.5.1`, `Common.Logging` |
| C1 Contributions | `CompositeC1Contrib.Core 0.9.0` |
| Real-time / Messaging | `MQTTnet 4.3.6.1152`, `SignalR.Core 2.4.3`, `Microsoft.Owin* 4.2.3`/`2.1.0`, `Owin 1.0`, `WampSharp 18.3.1` family |
| C1 Search | `Orckestra.Search`, `Orckestra.Search.LuceneNET`, `Lucene.Net*`, `BoboBrowse.Net`, `C5` |
| JSON Bson | `Newtonsoft.Json.Bson 1.0.2` |
| .NET Standard facades | `NETStandard.Library 1.6.1` family (`System.*` 4.3.0 shims, auto-resolved by NuGet) |

**Rules:**
- **Do NOT copy these DLLs into `sources/bin` blindly.** Several are C1 packages that must be
  installed via C1's package system (they register types/functions/UI).
- Version pinning source of truth: `E:\_CODE_\WebDev\SystemC1\Website\packages.config`.
- To make one mandatory later: copy its DLL(s) to `sources/bin` + add to `binDependencies` +
  add any binding redirect (see §17) + verify (`aspnet_compiler` + C1 log).

---

## 19. AuthKit Admin Page Assets (self-contained, no CDN / no commercial Editor)

The AuthKit management pages must be **independent** of the Mdrnz/reference template. They load all
CSS/JS from a local `~/assets/authkit/` folder (deployed via manifest `assets` → `DeployPageTemplatesStep`).

**Local assets (`sources/assets/authkit/`):**
- `jquery-3.7.1.min.js`
- `bootstrap/bootstrap.min.css` + `bootstrap.bundle.min.js`
- `datatables/jquery.dataTables.min.js` (1.13.5) + `dataTables.buttons.min.js` + `buttons.bootstrap5.*` + `dataTables.select.min.js` + `select.bootstrap5.min.css`
- `datatables/altEditor/dataTables.altEditor.free.js` + `tr.json` — **free altEditor**, replaces commercial DataTables Editor
- `sweetalert2/sweetalert2.all.min.js` + `sweetalert2.min.css`

**Key fixes (2026-08-07):**
- **`$ is not defined`:** PanelLayout loaded jQuery at the END of `<body>`, so child-page scripts
  (inside `@RenderBody`) ran BEFORE jQuery. **Fix:** load jQuery + DataTables + altEditor + SweetAlert2
  in `<head>` (same as Mdrnz.PanelLayout) — child scripts always see `$`.
- **`dataTables.editor.min.js` CDN 404:** DataTables Editor is commercial and not on the CDN.
  **Fix:** replaced with free **altEditor** (`altEditor: true` + `onAddRow`/`onEditRow`/`onDeleteRow`
  callbacks) in `UserManagementPage` and `GroupManagementPage`.
- All CSS/JS now points to `~/assets/authkit/...` (only Tabler Icons remains a CDN `<link>`, which works).

**Manifest:** `"assets": [{ "source": "assets/authkit", "target": "~/assets/authkit" }]` — handled by
`DeployPageTemplatesStep` (Verify checks content equality; Execute uses `FileSyncUtil.SyncDirectory`).

**Test (localhost:2681 = `R:\deploy_pkg_v6`):** `/AuthKit/Users` redirects to login when unauthenticated
(auth-only ✓); all `~/assets/authkit/*` return HTTP 200; login page loads local bootstrap/jquery/sweetalert2.
