# AuthKit Management Pages

This directory contains the ready-made management pages shipped with the AuthKit package.
All page templates are prefixed with `AuthKit.` so they appear grouped in the C1 CMS Layout
perspective.

## Changes from the Original Pages

| Original (Fleet Tracking) | AuthKit (Clean) |
|---|---|
| `Auth.Authentication.*` | `AuthKit.Authentication.*` |
| `Auth.Authorization.*` | `AuthKit.Authorization.*` |
| `Auth.Navigation.PageIds.Login` (hardcoded GUID) | `AuthKit.Authentication.AuthenticationManager.LoginPageId` |
| `C1.UrlHelper.GetUrlFromPageId` | `AuthKit.C1.C1UrlHelper.GetUrlFromPageId` |
| `PermissionKeys.LocaFleet.*` | REMOVED (fleet-tracking specific) |
| Hardcoded login GUIDs (`870ff503-...`) | Configurable via `KeyTreeStoreManager` |

## CSS/JS Dependencies

`AuthKit.PanelLayout.cshtml` is the main layout and pulls in Bootstrap 5, jQuery,
DataTables (Editor/Buttons/Select), SweetAlert2, and Tabler Icons from CDNs. This makes the
package self-contained; there is no dependency on external theme files.

## Page List

| Page | Purpose |
|---|---|
| `AuthKit.SetupPage.cshtml` | Setup page — "Create Pages" auto-creates the 9 pages |
| `AuthKit.PanelLayout.cshtml` | Main sidebar admin panel layout |
| `AuthKit.AuthLayout.cshtml` | Layout for Login/Register/Forgot Password/Reset Password/Logout pages |
| `AuthKit.UserManagementPage.cshtml` | User list, add/delete/edit, group assignment |
| `AuthKit.GroupManagementPage.cshtml` | Group list, add/delete/edit, member management |
| `AuthKit.GroupPermissionPage.cshtml` | Group-based Allow/Deny permission matrix |
| `AuthKit.UserPermissionPage.cshtml` | User-based Allow/Deny + inheritance view |
