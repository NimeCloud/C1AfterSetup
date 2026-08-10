# Apply AuthKit Core Fixes to C1AfterSetup Sources (2026-08-10)

> **Purpose:** The AuthKit implementation was debugged & fixed in the **WebcamRecorder / WebsiteKit**
> project (`E:\_CODE_\WebcamRecorder\src\WebsiteKit`). This plan tells you **exactly how to port
> those fixes into C1AfterSetup's `sources/`** so a fresh deploy ships the corrected AuthKit.
>
> **Rule:** Do NOT re-invent. Copy the verified fix from WebcamRecorder's files into the matching
> C1AfterSetup source file, then verify (`aspnet_compiler` + C1 log, see AI_CONTEXT §4/§9).

---

## ⚡ Quick reference (copy for new-task message)

```
Port the verified AuthKit fixes from:
  E:\_CODE_\WebcamRecorder\src\WebsiteKit
into C1AfterSetup sources (C1AfterSetup/C1AfterSetup/sources/...).

Read FIRST:
1. AI_CONTEXT.md (esp. §15 Razor gotchas, §16 live-fix workflow, §19 assets)
2. This file (fix-by-fix mapping below)

After each port: dotnet build + aspnet_compiler + C1 log (§9 false positives).
Then browse each admin page as admin AND as a non-admin (must show Access Denied).
```

---

## Fix-by-fix mapping (WebcamRecorder → C1AfterSetup sources)

### FIX 1 — `HasPermission` evaluation order (DB DENY/ALLOW before admin membership)

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Code/AuthKit/Authorization/AuthorizationManager.cs`

**Problem fixed:** Previously `IsUserInGroup(System.Administrators)` returned `true` **before**
checking any DB DENY/ALLOW — so an admin could never be revoked from a single permission via the DB.
Now the order is:
1. DENY (User / Group / Everyone) → `false` (even for admins)
2. ALLOW (User / Group / Everyone) → `true`
3. **lastly** System.Administrators membership → `true`

**Apply to:** `C1AfterSetup/C1AfterSetup/sources/AuthKit/Authorization/AuthorizationManager.cs`
→ method `HasPermission(...)`. Copy the full method body from the WebcamRecorder file.

---

### FIX 2 — `UpdateUserGroupsDelta` dedup (one record per userId+groupId)

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Code/AuthKit/Authorization/AuthorizationManager.cs`

**Problem fixed:** Adding a group a user already belongs to created a **second** `UserInGroup`
row (one `IsAllowed=true`, one `false`) → duplicated memberships in the Group Memberships table.
Now:
- read existing `UserInGroup` rows for the user once;
- for each group to add: if a row exists → **update** it (`IsAllowed=true`, keep single row);
  if multiple rows exist → keep first, delete duplicates;
- else insert a fresh row.

**Apply to:** `C1AfterSetup/C1AfterSetup/sources/AuthKit/Authorization/AuthorizationManager.cs`
→ method `UpdateUserGroupsDelta(...)`.

---

### FIX 3 — `EnsureAdministratorMembership` dedup + bootstrap-only auto-add

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Code/AuthKit/Authorization/AuthorizationManager.cs`

**Problems fixed:**
- (a) A user could end up with **two** `UserInGroup` rows for System.Administrators (true + false).
- (b) Auto-grant ran on **every** page visit when the C1 user is a C1 administrator — so an admin
  you manually removed got re-added by the next request. Now auto-grant only happens as a
  **bootstrap** (administrators group is still empty) or for a C1 Administrator, and it also
  **dedupes** existing rows (keep one, set `IsAllowed=true`, delete the rest).

**Apply to:** `C1AfterSetup/C1AfterSetup/sources/AuthKit/Authorization/AuthorizationManager.cs`
→ method `EnsureAdministratorMembership(...)`.

---

### FIX 4 — PanelLayout: hardcoded admin gate + Access Denied (no navbar)

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Data/PageTemplates/AuthKit.PanelLayout.cshtml`

**Problem fixed:** Any signed-in user (e.g. a customer) could open `/AuthKit/Users` etc. and the
panel was fully functional. Now PanelLayout:
- computes `isAdmin = HasPermission(currentUser, Auth.Users.View)` (DB-aware);
- if NOT admin → renders a standalone **Access Denied** card (shows username badge + "Sign in with a
  different account" → `/Logout`), **no navbar / no `@RenderBody()` content**;
- if admin → normal navbar + `@RenderBody()`.

Also fixed on this template:
- Navbar now shows the username as a **Tabler icon** (`<i class="ti ti-user me-1">`) instead of an
  emoji (emoji became mojibake in C1 encoding);
- username expression uses `@(currentUser != null ? currentUser.UserName : "")` (older Razor can't
  parse `@currentUser?.UserName`).

**Apply to:** `C1AfterSetup/C1AfterSetup/sources/PageTemplates/AuthKit.PanelLayout.cshtml`
→ copy the whole `<body>` (the `@if (isAdmin) { … } else { … }` structure) + the navbar username span.

> ⚠️ AI_CONTEXT §15(e) currently says management pages "MUST use auth-only checks". This fix
> intentionally adds an admin gate on top. Keep BOTH: `currentUser == null` → redirect to login
> (`?redirect=` preserved), then `isAdmin == false` → Access Denied. Update §15(e) accordingly.

---

### FIX 5 — Four management pages: `CheckPagePermission`

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Data/PageTemplates/`
- `AuthKit.UserManagementPage.cshtml` → `CheckPagePermission(PermissionKeys.Auth.Users.View)`
- `AuthKit.GroupManagementPage.cshtml` → `CheckPagePermission(PermissionKeys.Auth.Groups.View)`
- `AuthKit.GroupPermissionPage.cshtml` → `CheckPagePermission(PermissionKeys.Auth.Permissions.Assign)`
- `AuthKit.UserPermissionPage.cshtml` → `CheckPagePermission(PermissionKeys.Auth.Permissions.AssignToUser)`

**Problem fixed:** pages only checked `currentUser == null`; a signed-in non-admin saw the full UI
(APIs were already protected, pages were not). Add the `CheckPagePermission(...)` call after the
existing login-redirect block in each template.

**Apply to:** the four `C1AfterSetup/C1AfterSetup/sources/PageTemplates/AuthKit.*Page.cshtml`
→ add the matching `AuthKit.Authorization.AuthorizationManager.CheckPagePermission(...)` line in the
`@{ }` block.

---

### FIX 6 — SetupPage (`/AuthKit`): admin gate + hidden navbar for non-admin

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Data/PageTemplates/AuthKit.SetupPage.cshtml`

**Problem fixed:** the AuthKit home/setup page rendered its management UI (Create Pages, OAuth) to
anyone. Now:
- `isAdmin` computed same way (DB-aware);
- navbar + management content only for admin; non-admin gets the **Access Denied** card;
- `action=create` / `action=saveoauth` also guarded with `&& isAdmin` (bootstrap
  `AuthStartupHandler.Initialize()` still runs for everyone).

**Apply to:** `C1AfterSetup/C1AfterSetup/sources/PageTemplates/AuthKit.SetupPage.cshtml`
→ copy the `@{ }` block (add `isAdmin`) + wrap navbar/content with `@if (isAdmin)`, add Access
Denied `else` branch.

> Note: SetupPage's Tabler icons CDN `<link>` must use a C# variable (`var tablerIconsUrl = "…@tabler/…@latest…"` then `href="@tablerIconsUrl"`) — bare `@tabler` / `@latest` in the URL
> breaks the old Razor parser (error CS0103). This was fixed in WebcamRecorder; copy it.

---

### FIX 7 — LoginForm: Access Denied when a non-admin is already signed in

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Data/Razor/AuthKit/LoginForm.cshtml`

**Problem fixed:** after login, `/AuthKit/Login` showed "Welcome back, X!" + "Continue to Panel" even
for a non-admin. Now three branches:
1. `currentUser != null && isAdmin` → "Welcome back" + Continue to Panel;
2. `currentUser != null && !isAdmin` → **Access Denied** (username badge + "Sign in with a different
   account" → `/Logout`);
3. else → login form.

**Apply to:** `C1AfterSetup/C1AfterSetup/sources/Razor/AuthKit/LoginForm.cshtml`
→ copy the `@{ }` (add `isAdmin`) + the three-branch body.

---

### FIX 8 — LogoutForm: confirmation page (username + Log Out button)

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Data/Razor/AuthKit/LogoutForm.cshtml`

**Problem fixed:** logout must not silently redirect (user wanted a fixed logout page). It now shows
the current username badge + "Log Out" button → POST `/Api/Logout` → redirect to login.

**Apply to:** `C1AfterSetup/C1AfterSetup/sources/Razor/AuthKit/LogoutForm.cshtml`
→ copy the whole file.

---

### FIX 9 — Data: dedupe `UserInGroup` rows (admin, not ea)

**Verified in:** `WebcamRecorder/src/WebsiteKit/App_Data/Composite/DataStores/AuthKit.Data.Authorization.UserInGroup.xml`

**Problem fixed:** two rows for the same `(userId, groupId)` (one `IsAllowed=true`, one `false`), and
a customer (`ea`) was a System.Administrators member. Corrected to a single row for `admin` only.

**Apply to:** after deploy, verify/repair the runtime `App_Data/Composite/DataStores/AuthKit.Data.Authorization.UserInGroup.xml`
in the **output** site (Fixes 2+3 prevent future duplicates). Do not ship user-specific data in
`sources/` — this is a per-deploy data check.

---

## Sequence & verification

1. Port Fixes 1–3 (C#) → `dotnet build C1AfterSetup/C1AfterSetup.csproj -c Release`.
2. Port Fixes 4–6 (page templates) → `aspnet_compiler -v / -p "<out>"` (ignore §9 false positives).
3. Port Fixes 7–8 (Razor functions).
4. Deploy to a fresh output folder (e.g. `R:\deploy_fix`), boot IIS Express port 2681.
5. Verify:
   - As **admin**: all panel pages load; navbar shows username icon; logout → confirmation page.
   - As **customer** (non-admin): panel pages + setup + login all show **Access Denied**;
     "Sign in with a different account" → `/Logout` → login.
   - `App_Data/.../UserInGroup.xml` has exactly ONE row per (user, group).

## Known edge cases / gotchas
- Update **AI_CONTEXT.md §15(e)**: auth-only check is still the base, but an admin gate
  (`isAdmin` via `HasPermission(Auth.Users.View)`) now sits on top. Fresh deploys seed permissions
  via `AuthStartupHandler.Initialize()` + `PermissionSyncService`, so `HasPermission` works.
- Keep every `.cshtml` **UTF-8 with BOM** (AI_CONTEXT §15-d); Turkish chars + emoji garble otherwise.
- Tabler icons URL must be in a C# variable (FIX 6 note) — bare `@` breaks the old Razor parser.
