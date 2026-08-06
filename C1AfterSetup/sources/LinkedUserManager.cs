using System;

public class LinkedUserManager
{
    public static bool EnsureShadowUser(string username, string email, string password, string providerName)
    {
        try
        {
            if (string.IsNullOrEmpty(email) || !email.Contains("@"))
                email = username + "@" + providerName + ".internal";

            // deploy6'da CreateUser tuple döner: (bool IsSuccess, string ErrorMessage, User NewUser)
            var result = AuthKit.Authentication.AuthenticationManager.CreateUser(
                username, email, password, true, false);

            // IsSuccess=true → olusturuldu. Hata mesaji "zaten" iceriyorsa → zaten var, OK.
            if (result.IsSuccess || (result.ErrorMessage ?? "").Contains("zaten"))
                return true;

            // Gercek hata: logla ve false dön ki login basarisiz olsun
            Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                $"Shadow user olusturulamadi: {username} ({providerName}) - {result.ErrorMessage}");
            return false;
        }
        catch (Exception ex)
        {
            Composite.Core.Log.LogError("LinkedUserManager.EnsureShadowUser",
                $"Shadow user olusturma hatasi: {username} ({providerName}) - {ex.Message}");
            return false;
        }
    }
}
