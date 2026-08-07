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
            if (result.IsSuccess || (result.ErrorMessage ?? "").Contains("already"))
                return true;

            // Real error: log it and return false so the login fails
            Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                $"Shadow user could not be created: {username} ({providerName}) - {result.ErrorMessage}");
            return false;
        }
        catch (Exception ex)
        {
            Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                $"Shadow user creation error: {username} ({providerName}) - {ex.Message}");
            return false;
        }
    }
}
