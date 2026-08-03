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
        /// Uygulama spesifik yetkiler. Ihtiyaca gore ekleyin.
        /// </summary>
        public static class App
        {
            // Ornek yetkiler:
            // [PermissionInfo("Dashboard goruntuleme yetkisi.")]
            // public const string ViewDashboard = "App.ViewDashboard";

            // [PermissionInfo("Kayitlari listeleme ve goruntuleme yetkisi.")]
            // public const string ViewRecords = "App.ViewRecords";

            // [PermissionInfo("Kayit ekleme yetkisi.")]
            // public const string AddRecords = "App.AddRecords";

            // [PermissionInfo("Kayit duzenleme yetkisi.")]
            // public const string EditRecords = "App.EditRecords";

            // [PermissionInfo("Kayit silme yetkisi.")]
            // public const string DeleteRecords = "App.DeleteRecords";
        }
    }
}
