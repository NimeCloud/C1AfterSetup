public class ExternalUserInfo
{
    public string Username { get; set; }
    public string Email { get; set; }
    public string ExternalUserId { get; set; }
}

public interface IUserProvider
{
    string Name { get; }
    bool CanHandle(string username);
    bool ValidateCredentials(string username, string password);
    ExternalUserInfo GetUser(string username);
}
