using System;
using System.Linq;
using Composite.Data;
using Composite.Data.Types;
using Composite.C1Console.Security;

public class C1UserProvider : IUserProvider
{
    public string Name => "c1";

    public bool CanHandle(string username)
    {
        try
        {
            var c1User = DataFacade.GetData<IUser>()
                .FirstOrDefault(u => u.Username == username);
            return c1User != null;
        }
        catch { return false; }
    }

    public bool ValidateCredentials(string username, string password)
    {
        try
        {
            var loginResult = UserValidationFacade.FormValidateUser(username, password);

            switch (loginResult)
            {
                case LoginResult.Success:
                case LoginResult.PasswordUpdateRequired:
                    return true;
                default:
                    return false;
            }
        }
        catch { return false; }
    }

    public ExternalUserInfo GetUser(string username)
    {
        var c1User = DataFacade.GetData<IUser>()
            .FirstOrDefault(u => u.Username == username);
        if (c1User == null) return null;

        return new ExternalUserInfo
        {
            Username = c1User.Username,
            Email = c1User.Username + "@c1.internal",
            ExternalUserId = c1User.Id.ToString()
        };
    }
}
