namespace AuthKit.Authorization
{
    /// <summary>
    /// Sistem gruplari icin anahtar sabitleri.
    /// KeyInitializer.InitializeAllKeys(typeof(GroupKeys)) cagrisiyla
    /// alanlar otomatik olarak "System.Administrators" gibi dot-notation degerlerle doldurulur.
    /// </summary>
    public static class GroupKeys
    {
        public static class System
        {
            public static string Administrators;
        }

        public static class Content
        {
            public static string Editors;
            public static string Managers;
        }

        public static class App
        {
            /// <summary>
            /// Kayitli (registered) tum kullanicilarin otomatik uye oldugu grup.
            /// React dashboard temel yetkilerini verir; admin paneli vermez.
            /// </summary>
            public static string Customers;
        }
    }
}
