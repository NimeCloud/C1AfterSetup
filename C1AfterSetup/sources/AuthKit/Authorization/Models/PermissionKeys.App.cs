namespace AuthKit.Authorization
{
    /// <summary>
    /// Uygulamaya özel yetki anahtarlari burada tanimlanir.
    /// Ornek: Webcam Recorder, Fleet Management vb.
    /// PermissionSyncService bu siniftaki tum const string alanlarini
    /// otomatik olarak veritabanina senkronize eder.
    /// </summary>
    public static partial class PermissionKeys
    {
        /// <summary>
        /// Uygulama spesifik yetkiler.
        /// FIX 11 (unified role-based dashboard): kayitli musteri kullanicilari "Customers"
        /// grubu uzerinden bu anahtarlarin alt kumesini alir; Purchases/Payments.Manage
        /// admin bolumunu tanimlar (AuthApi.GetStatus isAdmin hesabinda kullanilir).
        /// </summary>
        public static class App
        {
            /// <summary>
            /// React dashboard ana sayfasi (musteri).
            /// </summary>
            [PermissionInfo("Dashboard goruntuleme yetkisi.")]
            public const string ViewDashboard = "App.ViewDashboard";

            public static class Licenses
            {
                /// <summary>
                /// Lisanslari listeleme/goruntuleme yetkisi.
                /// </summary>
                [PermissionInfo("Lisanslari listeleme/goruntuleme yetkisi.")]
                public const string View = "App.Licenses.View";
            }

            public static class Purchases
            {
                /// <summary>
                /// Satin alma kaydi olusturma yetkisi (musteri).
                /// </summary>
                [PermissionInfo("Satin alma kaydi olusturma yetkisi.")]
                public const string Create = "App.Purchases.Create";

                /// <summary>
                /// Satin alma kayitlarini listeleme/goruntuleme yetkisi (musteri).
                /// </summary>
                [PermissionInfo("Satin alma kayitlarini listeleme/goruntuleme yetkisi.")]
                public const string View = "App.Purchases.View";

                /// <summary>
                /// Satin alma yonetimi (admin bolumu).
                /// </summary>
                [PermissionInfo("Satin alma yonetimi (admin).")]
                public const string Manage = "App.Purchases.Manage";
            }

            public static class Payments
            {
                /// <summary>
                /// Odeme yonetimi (admin bolumu).
                /// </summary>
                [PermissionInfo("Odeme yonetimi (admin).")]
                public const string Manage = "App.Payments.Manage";
            }
        }
    }
}
