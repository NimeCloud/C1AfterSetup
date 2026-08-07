# New-Task Prompt: Port AuthKit Admin Pages + NuGet Package Management

> Copy this short block into a **new C1AfterSetup task**. Read the referenced docs FIRST.

---

## ⚡ Quick reference (copy for new-task message)

```
Read these first:
1. AI_CONTEXT.md  (pipeline, Razor/XHTML/CDATA/UTF8-BOM rules, build commands)
2. plans/c1-reference-site-authkit-packages.md  (reference site, package inventory, plan)
3. plans/c1-cms-hybrid-sql-xml-datastore.md     (hybrid datastore rules)
4. C1AfterSetup/C1AfterSetup.csproj + Program.cs + Config/setup.manifest.json

Reference site (WORKING AuthKit + DataTables UI + C# API):
- E:\_CODE_\WebDev\SystemC1\Website            (site with working admin pages)
- E:\_CODE_\WebDev\SystemC1\Website\packages.config  (AUTHORITATIVE package versions)
- E:\_CODE_\WebDev\SystemC1\packages           (actual .nupkg store; contains old+new)
- The reference uses a Razor-based SYNTHETIC API - port it to real ApiHandler/AuthApi handlers.
- Fresh install E:\C1\dev\Website is NOT the reference site.

Task (Phase A): NuGet package management
- Create C1SiteDependencies/ helper project (.NET 4.8, old-style csproj, C#5) + packages.config
- Pin AuthKit-relevant packages EXACTLY as in reference packages.config (BCrypt.Net-Next 4.1.0,
  Newtonsoft.Json 13.0.3, System.Memory 4.6.3, Microsoft.CodeDom.Providers.DotNetCompilerPlatform 2.0.1, ...)
- nuget restore -> packages/<id>.<version>/ folder (trackable). Build -> sources/bin/.
- Never use latest versions - they break C1. Pin + verify via aspnet_compiler + C1 log.

Task (Phase B): Port working admin API + DataTables UI
- Extract reference site DataTables page templates + synthetic API logic.
- Implement real handlers: GetRealUsers, GetTemplateUsers, AddUser, UpdateUser, DeleteUser,
  GetAllGroupIdsForUser, GetAllGroupsForUserManagement, UpdateUserGroupsDelta, AddGroup,
  UpdateGroup, DeleteGroup, permission endpoints.
- Fix DataTables Editor CDN 404 (commercial JS) - self-host or replace with plain DataTables + custom modals.
- Apply AI_CONTEXT §15 rules (XHTML strict, CDATA, UTF-8 BOM, auth-only checks, ?redirect=).

Verify: aspnet_compiler clean, C1 log clean, browse each admin page logged-in as admin.
```

---

## Rules to always follow
1. **Pin exact package versions** from `packages.config` — do NOT upgrade to latest (breaks C1).
2. **AuthKit admin pages use auth-only checks** (`currentUser == null`) + `?redirect=` param.
3. **All `.cshtml`: strict XHTML** — CDATA-wrapped CSS/JS with newlines, no bare `&`, no bare
   boolean attributes, **UTF-8 with BOM**.
4. **Quick-fix workflow**: for small fixes, patch BOTH `sources/` and the deployed output;
   full redeploy only for new files/pipeline changes. VS 2022 may lock `.vs` only.
