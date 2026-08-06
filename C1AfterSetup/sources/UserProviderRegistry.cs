using System;
using System.Collections.Generic;

public class UserProviderRegistry
{
    private static List<IUserProvider> _providers;
    private static readonly object _lock = new object();

    static UserProviderRegistry()
    {
        _providers = new List<IUserProvider>
        {
            new AuthKitUserProvider(),
            new C1UserProvider(),
        };
    }

    public static IUserProvider FindProvider(string username, string password)
    {
        foreach (var provider in _providers)
        {
            try
            {
                if (provider.ValidateCredentials(username, password))
                    return provider;
            }
            catch
            {
                // Provider failed — try next one
            }
        }
        return null;
    }

    public static IUserProvider FindProviderForUser(string username)
    {
        foreach (var provider in _providers)
        {
            try
            {
                if (provider.CanHandle(username))
                    return provider;
            }
            catch { }
        }
        return null;
    }
}
