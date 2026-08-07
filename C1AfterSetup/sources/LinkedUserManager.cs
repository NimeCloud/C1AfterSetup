using System;

public class LinkedUserManager
{
    public static bool EnsureShadowUser(string username, string email, string password, string providerName)
    {
        try
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
                email = username + "@" + providerName + ".internal";

            // CreateUser returns a tuple: (bool IsSuccess, string ErrorMessage, User NewUser)
            var result = AuthKit.Authentication.AuthenticationManager.CreateUser(
                username, email, password, true, false);

            // IsSuccess=true -> created. If the error message contains "already", the user
            // already exists (shadow account already present) - treat as OK.
            bool isExisting = !result.IsSuccess && (result.ErrorMessage ?? "").Contains("already");
            if (!result.IsSuccess && !isExisting)
            {
                // Real error: log it and return false so the login fails
                Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                    $"Shadow user could not be created: {username} ({providerName}) - {result.ErrorMessage}");
                return false;
            }

            var user = result.NewUser
                       ?? AuthKit.Authentication.AuthenticationManager.FindUserByUsername(username);
            if (user == null)
            {
                Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                    $"Shadow user not found after ensure: {username} ({providerName})");
                return false;
            }

            // Bootstrap: join the shadow user into System.Administrators when the current C1
            // user is a C1 Administrator, or when the admin group has no members yet (so the
            // system is never left without an administrator).
            try
            {
                AuthKit.Authorization.AuthorizationManager.EnsureAdministratorMembership(user.Id);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                    $"Administrator bootstrap failed for {username}: {ex.Message}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                $"Shadow user creation error: {username} ({providerName}) - {ex.Message}");
            return false;
        }
    }
}
