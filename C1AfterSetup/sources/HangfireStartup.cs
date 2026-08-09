using System;
using Composite.Core.Application;
using Hangfire;
using Hangfire.CompositeC1;
using Hangfire.Dashboard;
using Hangfire.Logging;
using Owin;

/// <summary>
/// OWIN Startup: /hangfire dashboard'unu OWIN pipeline'ı üzerinden servis eder.
/// Web.config'teki "owin:AppStartup=Startup" anahtarı OWIN'e bu sınıfı bildirir
/// (referans sitedeki ForOwinStartup.dll yerine App_Code'da yerleşik olarak çalışır).
///
/// ÖNEMLİ: Microsoft.Owin.Host.SystemWeb, OwinHttpModule'u PreApplicationStartMethod ile kaydeder;
/// belirtilen bir Startup sınıfı yoksa app init'te EntryPointNotFoundException fırlatır ve uygulama
/// sonsuz recycle döngüsüne girer. Bu sınıf (owin:AppStartup ile işaret edilen) bu durumu engeller.
/// </summary>
public class Startup
{
    public void Configuration(IAppBuilder app)
    {
        var options = new DashboardOptions();
        options.Authorization = new IDashboardAuthorizationFilter[]
        {
            new LocalRequestsOnlyAuthorizationFilter()
        };
        app.UseHangfireDashboard("/hangfire", options);
    }
}

/// <summary>
/// Hangfire storage + arka plan sunucusu başlatma (C1 [ApplicationStartup]).
/// - Storage: Hangfire.CompositeC1.CompositeC1Storage (C1 XML veri deposu). Kurulurken gerekli C1
///   veri tiplerini (Hangfire.CompositeC1.Types.*) DynamicTypeManager.EnsureCreateStore ile otomatik oluşturur.
/// - Sunucu: BackgroundJobServer — kuyruktaki işleri ve recurring job'ları işler.
/// - Dashboard: OWIN Startup sınıfı (yukarıdaki "Startup") /hangfire'ı servis eder.
///
/// C# 5 uyumlu (C1 CMS 6.13 + Roslyn CodeDom ile derlenir).
/// </summary>
[ApplicationStartup]
public static class HangfireStartup
{
    private static readonly object _lock = new object();
    private static bool _initialized;
    private static BackgroundJobServer _server;

    public static void OnBeforeInitialize()
    {
    }

    public static void OnInitialized()
    {
        lock (_lock)
        {
            if (_initialized) return;

            try
            {
                // 1) Storage: Composite C1 (XML) veri deposu — tipleri otomatik oluşturur.
                GlobalConfiguration.Configuration.UseCompositeC1Storage();

                // 2) Log sağlayıcıyı sabitle. Hangfire'ın LibLog'u, bin'deki
                //    Microsoft.Practices.EnterpriseLibrary.Logging assembly'sini otomatik algılayıp
                //    kullanmaya çalışır; "Logging" config section'ı tanımlı olmadığı için
                //    BackgroundJobServer kurulumunda ConfigurationErrorsException fırlatır.
                Hangfire.Logging.LogProvider.SetCurrentLogProvider(
                    new Hangfire.Logging.LogProviders.ColouredConsoleLogProvider());

                // 3) Arka plan sunucusunu başlat.
                _server = new BackgroundJobServer();

                Composite.Core.Log.LogInformation("Hangfire",
                    "Hangfire sunucusu başlatıldı (CompositeC1Storage, /hangfire dashboard).");
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("Hangfire", "Hangfire başlatma hatası: " + ex, ex);
            }

            _initialized = true;
        }
    }
}
