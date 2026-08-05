using System;

namespace AuthKit.Settings
{
    /// <summary>
    /// AuthKit ayarlari icin KeyTreeStoreKit.KeyTreeStoreManager uzerinde
    /// ince bir sarmalayici. SetupPage ve SetupPages tarafindan kullanilir.
    /// </summary>
    public static class SettingsManager
    {
        public static string Get(string key, string defaultValue)
        {
            return KeyTreeStoreKit.KeyTreeStoreManager.Get(key, defaultValue);
        }

        public static void Set(string key, string value)
        {
            KeyTreeStoreKit.KeyTreeStoreManager.Set(key, value);
        }
    }
}
