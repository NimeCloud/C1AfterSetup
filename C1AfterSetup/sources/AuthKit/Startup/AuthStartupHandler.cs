using System;

namespace AuthKit.Startup
{
    /// <summary>
    /// Global.asax Application_Start'ta cagrilmasi gereken baslangic islemleri.
    /// 
    /// Kullanim:
    /// Global.asax icinde Application_Start metoduna su satiri ekleyin:
    ///   AuthStartupHandler.Initialize();
    /// </summary>
    public static class AuthStartupHandler
    {
        private static bool _initialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Auth sisteminin baslatilip baslatilmadigini kontrol eder.
        /// SetupPages.cshtml tarafindan otomatik kontrol edilir.
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// Auth sistemini baslatir:
        /// 1. GroupKeys ve ModuleKeys alanlarini doldurur
        /// 2. PermissionSyncService ile tum yetkileri veritabanina senkronize eder
        ///
        /// NOT: Global.asax'a manuel eklemeye gerek YOKTUR.
        /// AuthKit Admin sayfasi ilk ziyaret edildiginde otomatik cagrilir.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            lock (_lock)
            {
                if (_initialized) return;

                try
                {
                    // 0. KeyTreeStore root sentinel'ini hazirla (C1 Data sekmesinde tek kok)
                    global::KeyTreeStoreKit.KeyTreeStoreManager.EnsureRoot();

                    // 1. Key alanlarini doldur
                    Authorization.KeyInitializer.InitializeAllKeys(typeof(Authorization.GroupKeys));
                    Authorization.KeyInitializer.InitializeAllKeys(typeof(Authorization.ModuleKeys));

                    // 2. Yetkileri senkronize et
                    var syncService = new Authorization.PermissionSyncService();
                    syncService.SynchronizePermissions();

                    _initialized = true;
                }
                catch (Exception ex)
                {
                    Composite.Core.Log.LogError("AuthStartupHandler", "Auth baslatma hatasi: " + ex.Message);
                }
            }
        }
    }
}

