# New-Task Prompt: Hybrid XML+SQL Deploy + Admin Tool Pages

> Copy this prompt into a **new C1AfterSetup task** to deploy the hybrid datastore
> configuration and the two admin tool pages (Data Provider Default + Datatype Migrator)
> into a fresh C1 CMS site.
>
> **Status:** ✅ Implementation complete in this workspace (deploy21). All steps, templates,
> and configurations are built and verified. This prompt documents the state so a fresh AI
> context can understand, verify, and extend.

---

## 📋 Instructions for the AI agent

### Step 1 — Read these files FIRST (in order)

1. [`AI_CONTEXT.md`](AI_CONTEXT.md) — template GUIDs, page IDs, pipeline order, Razor gotchas, git conventions
2. [`PROJECT.md`](PROJECT.md) — architecture, data type hierarchy, namespace design, gotchas
3. [`plans/c1-cms-hybrid-sql-xml-datastore.md`](plans/c1-cms-hybrid-sql-xml-datastore.md) — hybrid datastore setup: routing, provider config, DataProviderCopier, cross-provider references, troubleshooting
4. [`C1AfterSetup/C1AfterSetup.csproj`](C1AfterSetup/C1AfterSetup.csproj) — explicit file listing (must add new files here)
5. [`C1AfterSetup/Program.cs`](C1AfterSetup/Program.cs) — step pipeline registration (15 steps)
6. [`C1AfterSetup/Config/setup.manifest.json`](C1AfterSetup/Config/setup.manifest.json) — what gets deployed (templates, dependencies, data types, appCode, razor, webConfig)

### Step 2 — Understand what is already built

The pipeline now has **15 steps**. The hybrid XML+SQL capability and admin tool pages
are fully implemented:

| Step # | Step | File | Status |
|--------|------|------|--------|
| 5 | **Hybrid Data Store** | [`ConfigureSqlDataProviderStep.cs`](C1AfterSetup/Steps/ConfigureSqlDataProviderStep.cs) | ✅ Inject `c1` connection string into `Web.config`, register `DynamicSqlDataProvider` plugin in `Composite.config` (after `DynamicXmlDataProvider`), create empty `DynamicSqlDataProvider.config`. `DynamicXmlDataProvider` stays default. |
| 9 | **Page Templates** | [`DeployPageTemplatesStep.cs`](C1AfterSetup/Steps/DeployPageTemplatesStep.cs) | ✅ Copies all templates master-first per manifest `order` — now includes `AdminTools.DataProviderSelector.cshtml` and `AdminTools.DatatypeMigrator.cshtml` (order 2) |
| 11 | **Admin Tools** | [`DeployAdminToolPagesStep.cs`](C1AfterSetup/Steps/DeployAdminToolPagesStep.cs) | ✅ Generates 2 pages in DataStores XMLs (IPage, IPage_Unpublished, IPageStructure, PlaceholderContent). Top-level root pages, LocalOrdering 100/101, idempotent |

#### Source files in place:
| File | TemplateId |
|------|-----------|
| [`C1AfterSetup/sources/PageTemplates/AdminTools.DataProviderSelector.cshtml`](C1AfterSetup/sources/PageTemplates/AdminTools.DataProviderSelector.cshtml) | `A1100000-0000-0000-0000-A110A110A110` |
| [`C1AfterSetup/sources/PageTemplates/AdminTools.DatatypeMigrator.cshtml`](C1AfterSetup/sources/PageTemplates/AdminTools.DatatypeMigrator.cshtml) | `A1200000-0000-0000-0000-A120A120A120` |

#### Page IDs (deterministic):
| Page | PageId |
|------|--------|
| Data Provider Default | `A1110000-0000-0000-0000-A111A111A111` |
| Datatype Migrator | `A1210000-0000-0000-0000-A121A121A121` |

### Step 3 — What the AdminTools pages do

#### Data Provider Default (`/Data-Provider-Default`)
- Lists all registered data providers (XML + SQL) with claimed type counts
- Shows current `defaultDynamicTypeDataProviderName` with a badge
- Allows changing the default provider → saves `Composite.config` → `HttpRuntime.UnloadAppDomain()` (recycle)
- Idempotent page installer: `?action=create` creates both pages if missing (checks via `IPage.UrlTitle`)
- Quick links to Datatype Migrator + C1 Console

#### Datatype Migrator (`/Datatype-Migrator`)
- Lists all `isCodeGenerated` types from `DataMetaData` with current provider + record count
- Per-type `<select>` listbox: current provider (label) vs. alternate provider
- **Apply** triggers migration:
  1. Backs up source AND target provider configs (`.bak_YYYYMMDD_HHmmss`)
  2. `new DataProviderCopier(src, tgt)` with `IgnorePrimaryKeyViolation=true`, `UseTransaction=true` copies data
  3. Removes the `dataTypeId` entry from source provider's `<Interfaces>`
  4. Adds the entry to target provider's `<Interfaces>` (with correct store format: `tableName` for SQL, `filename`/`elementName` for XML)
  5. **XML→SQL only:** renames old XML DataStore file to `.xml.migrated` (keeps backup)
  6. `HttpRuntime.UnloadAppDomain()` recycle
- Both directions work: XML→SQL and SQL→XML

Both pages are admin-gated: `AuthKit.C1.C1Security.IsCurrentUserInAdministratorsGroup()`.

### Step 4 — What you may need to do for a NEW site

If you're adapting this pipeline for a **different C1 CMS site** (not `E:\C1\dev\Website`):

1. **Verify the SQL connection string** in [`setup.manifest.json`](C1AfterSetup/Config/setup.manifest.json:4) — update the `Data Source`, `User ID`, `Password` for the target SQL Server.
2. **Ensure the SQL database exists**: `CREATE DATABASE C1SqlDataStore; ALTER DATABASE C1SqlDataStore SET AUTO_CLOSE OFF;`
3. **Run the pipeline**: `C1AfterSetup.exe -site "<source>" -out "r:\deploy" -mode offline -force`
4. **Verify via `aspnet_compiler`** (see `AI_CONTEXT.md` §4): known false positives are `CS0433` and `CS0168`
5. **Check C1 log**: `App_Data/Composite/LogFiles/YYYYMMDD.txt` for `Failed to compile razor file` — treat as real errors
6. **Browse both pages** as admin:
   - `http://{site}/Data-Provider-Default` — "Yetkisiz Erisim" when not logged in (confirms auth works)
   - `http://{site}/Datatype-Migrator` — same

### Step 5 — If adding a NEW admin-tool page

Follow the pattern:

1. Create `.cshtml` in `C1AfterSetup/sources/PageTemplates/` with:
   - `Configure()` setting `TemplateId` (deterministic GUID)
   - All HTML in `@functions` via `StringBuilder` → `IHtmlString` (see `AI_CONTEXT.md` §15 Razor gotchas)
   - No nested `@if`/`@foreach` after HTML in markup — use `@RenderPage(…)` pattern
2. Add the template GUID + page ID to `AI_CONTEXT.md` §14
3. Add to [`setup.manifest.json`](C1AfterSetup/Config/setup.manifest.json) → `templates` array
4. Add a `AdminToolPageDef` entry in [`DeployAdminToolPagesStep.cs`](C1AfterSetup/Steps/DeployAdminToolPagesStep.cs) → `GetAdminToolPageDefs()`
5. If the new page needs placeholder content (Razor function embed), add content in `WritePlaceholderContent()`; if self-rendering, empty `<html>` body is fine

### Step 6 — Critical gotchas

1. **Razor templates must use `IHtmlString` pattern.** The C1 6.13 Razor parser cannot handle `@if`/`@foreach` nested inside `@if` bodies after HTML. All HTML rendering is done in `@functions` via `StringBuilder`, returned as `IHtmlString`, and called with a single `@RenderPage(...)` in markup. See `AI_CONTEXT.md` §15.

2. **No single-statement `if` in `@{ }` blocks.** Every `if`, `else if`, `foreach` body MUST be enclosed in `{ }`.

3. **Strict XHTML in preview mode (`/c1mode(unpublished)`).** All attributes need values: `selected="selected"` not bare `selected`. Self-closing tags: `<br />`, `<hr />` with space before `/`.

4. **Inline CSS/JS MUST use CDATA wrappers.** C1's preview mode parses the page output as XHTML. Inline `<style>` blocks with `@keyframes`, CSS variables, or any `;` — and `<script>` blocks with `;`, `&`, `<`, `>` — cause `XmlException: The ';' character cannot be included in a name`. Always wrap: `<style>/* <![CDATA[ */ ... /* ]]> */</style>` and `<script>// <![CDATA[ ... // ]]></script>`. See `AI_CONTEXT.md` §15.5.

5. **`Dictionary.Contains()` → `ContainsKey()`.** `Contains()` resolves to LINQ extension which doesn't work in C1 Razor — use `ContainsKey()`.

5. **Page installation is idempotent.** Both the pipeline step and the runtime `?action=create` skip existing pages.

6. **VersionId: use `Guid.NewGuid()`, not deterministic.** Trying to build a VersionId via `Substring` on the page GUID produces invalid GUID strings (cuts mid-dash). See `AI_CONTEXT.md` §14.

7. **The admin tool pages' `.cshtml` templates are deployed by `DeployPageTemplatesStep`** (via manifest), NOT by `DeployAdminToolPagesStep` (which only creates the DataStore page records). Make sure both are in sync — the template file must exist in `sources/PageTemplates/` AND the manifest must list it.

8. **`ConfigureSqlDataProviderStep` runs AFTER data types but BEFORE C1 package/compile.** The provider infra (connection string, plugin registration, empty config) must be in place before types are registered, otherwise C1 can't create SQL-bound types at runtime.

### Step 7 — Test the migration flow

After deployment:
1. Login to C1 Console as admin
2. `Data-Provider-Default` → verify `DynamicXmlDataProvider` is default
3. `Datatype-Migrator` → verify generated types appear with current provider
4. Pick `SqlProvider.Test` (or any XML-stored generated type) → target SQL → click **Apply**
5. Verify: config moved between provider configs, old XML store renamed to `.xml.migrated`, data exists in SQL table
6. Optionally migrate back (SQL → XML) to verify reverse direction

---

## ⚡ Quick reference (copy for new-task message)

```
Read these files first:
1. AI_CONTEXT.md
2. PROJECT.md
3. plans/c1-cms-hybrid-sql-xml-datastore.md
4. C1AfterSetup/C1AfterSetup.csproj
5. C1AfterSetup/Program.cs

Task: Deploy the hybrid XML+SQL datastore configuration and two admin-tool
Content-perspective pages (Data Provider Default + Datatype Migrator) to a
C1 CMS site using this pipeline.

Already built in this workspace (deploy21):
- ConfigureSqlDataProviderStep.cs — injects c1 connection string, registers
  DynamicSqlDataProvider plugin, creates empty DynamicSqlDataProvider.config
- AdminTools.DataProviderSelector.cshtml + AdminTools.DatatypeMigrator.cshtml
  in sources/PageTemplates/ (TemplateId A110.../A120...)
- DeployAdminToolPagesStep.cs — generates page records in DataStores XMLs
- setup.manifest.json — lists both AdminTools templates (order 2)
- .csproj + Program.cs — step is registered

Template GUIDs and page IDs are in AI_CONTEXT.md section 14.
Migration logic uses DataProviderCopier — see plans/c1-cms-hybrid-sql-xml-datastore.md section 7.
Razor gotchas (IHtmlString pattern, no single-statement if) in AI_CONTEXT.md section 15.
```
