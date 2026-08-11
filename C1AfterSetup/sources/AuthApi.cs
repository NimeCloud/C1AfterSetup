using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

public class AuthApi : IHttpHandler
{
    public void ProcessRequest(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        context.Response.Cache.SetCacheability(HttpCacheability.NoCache);

        var action = (context.Items["RouteAction"] as string)
                     ?? context.Request.QueryString["action"]
                     ?? "status";

        object resp;

        try
        {
            switch (action)
            {
                case "status": resp = GetStatus(context); break;
                case "login": resp = DoLogin(context); break;
                case "register": resp = DoRegister(context); break;
                case "logout": resp = DoLogout(context); break;
                case "forgot": resp = DoForgotPassword(context); break;
                case "reset": resp = DoResetPassword(context); break;
                default: resp = new { success = false, error = $"Unknown action: '{action}'" }; break;
            }
        }
        catch (Exception ex)
        {
            resp = new { success = false, error = ex.Message };
        }

        context.Response.Write(Newtonsoft.Json.JsonConvert.SerializeObject(resp));
    }

    private object GetStatus(HttpContext ctx)
    {
        var c1LoggedIn = false; var c1Username = "?";
        try
        {
            c1Username = Composite.C1Console.Security.UserValidationFacade.GetUsername();
            c1LoggedIn = Composite.C1Console.Security.UserValidationFacade.IsLoggedIn();
        }
        catch { }

        // AuthKit uses its own "authToken" cookie (NOT FormsAuthentication), so read the
        // AuthKit user from the cookie. React pages look ONLY at DB permissions/groups.
        AuthKit.Data.Authentication.User authKitUser = null;
        try { authKitUser = AuthKit.Authentication.AuthenticationManager.GetCurrentUser(); }
        catch { }
        bool ak = authKitUser != null;

        var permissions = new List<string>();
        var groups = new List<string>();
        bool isAdmin = false;
        if (ak)
        {
            permissions = AuthKit.Authorization.AuthorizationManager.GetEffectivePermissions(authKitUser);
            groups = AuthKit.Authorization.AuthorizationManager.GetUserGroupNames(authKitUser);
            isAdmin = permissions.Contains(AuthKit.Authorization.PermissionKeys.App.Purchases.Manage)
                   || permissions.Contains(AuthKit.Authorization.PermissionKeys.App.Payments.Manage)
                   || permissions.Contains(AuthKit.Authorization.PermissionKeys.Auth.Users.View)
                   || permissions.Contains(AuthKit.Authorization.PermissionKeys.Auth.Groups.View)
                   || AuthKit.Authorization.AuthorizationManager.IsUserInGroup(authKitUser.Id, "System.Administrators");
        }

        return new
        {
            success = true,
            authenticated = ak || c1LoggedIn,
            username = ak ? authKitUser.UserName : (c1LoggedIn ? c1Username : null),
            c1LoggedIn = c1LoggedIn,
            isAdmin = isAdmin,
            tier = "free",
            permissions = permissions,
            groups = groups
        };
    }

    private object DoLogin(HttpContext ctx)
    {
        var u = ctx.Request.Form["username"] ?? ctx.Request.QueryString["username"];
        var p = ctx.Request.Form["password"] ?? ctx.Request.QueryString["password"];
        var r = ctx.Request.Form["rememberMe"] == "true";

        if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(p)) return GF();

        // 1. Provider zincirinde credentials dogrula
        var provider = UserProviderRegistry.FindProvider(u, p);
        if (provider == null) return GF();

        // 2. Kullanici bilgisini al
        var info = provider.GetUser(u);
        if (info == null) return GF();

        // 3. Shadow user olustur (C1 kullanicisiysa AuthKit'e kopyala)
        LinkedUserManager.EnsureShadowUser(info.Username, info.Email, p, provider.Name);

        // 4. AuthKit ile gercek login (cookie basar)
        var lr = AuthKit.Authentication.AuthenticationManager.Login(u, p, r);
        bool ok = string.IsNullOrEmpty(lr) || lr.Contains("\"ok\":true");
        if (ok) return new { success = true, username = u, provider = provider.Name };

        return GF();
    }
    private static object GF() => new { success = false, error = "Invalid username or password." };

    private object DoRegister(HttpContext ctx)
    {
        var u = ctx.Request.Form["username"]; var e = ctx.Request.Form["email"];
        var p = ctx.Request.Form["password"]; var c = ctx.Request.Form["confirm"];
        if (string.IsNullOrEmpty(u) || string.IsNullOrEmpty(e) || string.IsNullOrEmpty(p))
            return new { success = false, error = "All fields are required." };
        if (p != c) return new { success = false, error = "Passwords do not match." };
        var r = AuthKit.Authentication.AuthenticationManager.CreateUser(u, e, p, true, false);
        if (r.IsSuccess) { AuthKit.Authentication.AuthenticationManager.Login(u, p, false); return new { success = true, username = u }; }
        return new { success = false, error = r.ErrorMessage ?? "Registration failed." };
    }
    private object DoLogout(HttpContext ctx) { AuthKit.Authentication.AuthenticationManager.Logout(); System.Web.Security.FormsAuthentication.SignOut(); return new { success = true }; }
    private object DoForgotPassword(HttpContext ctx) { var e = ctx.Request.Form["email"] ?? ctx.Request.QueryString["email"]; if (string.IsNullOrEmpty(e)) return new { success = false, error = "Email is required." }; AuthKit.Authentication.AuthenticationManager.ForgotPassword(e); return new { success = true, message = "If this email is registered, a reset link has been sent." }; }
    private object DoResetPassword(HttpContext ctx) { try { var t = ctx.Request.Form["token"] ?? ctx.Request.QueryString["token"]; var p = ctx.Request.Form["password"]; var c = ctx.Request.Form["confirm"]; if (string.IsNullOrEmpty(t) || string.IsNullOrEmpty(p)) return new { success = false, error = "Token and password are required." }; if (p != c) return new { success = false, error = "Passwords do not match." }; AuthKit.Authentication.AuthenticationManager.ResetPassword(t, p); return new { success = true }; } catch (Exception ex) { return new { success = false, error = ex.Message }; } }

    public bool IsReusable => false;
}
