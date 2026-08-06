public class AuthKitUserProvider : IUserProvider
{
    public string Name => "authkit";

    public bool CanHandle(string username)
    {
        return true;
    }

    public bool ValidateCredentials(string username, string password)
    {
        try
        {
            // deploy6'da Login() cookie basar! O yuzden ValidateCredentialsOnly kullan.
            return AuthKit.Authentication.AuthenticationManager.ValidateCredentialsOnly(username, password);
        }
        catch
        {
            return false;
        }
    }

    public ExternalUserInfo GetUser(string username)
    {
        return new ExternalUserInfo
        {
            Username = username,
            Email = username + "@authkit.internal",
            ExternalUserId = username
        };
    }
}
