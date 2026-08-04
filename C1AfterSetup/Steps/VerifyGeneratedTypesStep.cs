using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using C1AfterSetup.Detect;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// İlk başlatma SONRASI (online) doğrulama: C1'in veri tiplerini gerçekten üretip
    /// üretmediğini ve App_Code'un bunlara karşı derlendiğini kontrol eder.
    ///
    /// Neden gerekli? Offline kopyada Composite.Generated.dll üretilemez; her şey ilk
    /// açılışta olur. Bu adım, o açılışın gerçekten başarılı olduğunu kapatır:
    ///   - Site HTTP 200 dönüyor (App_Code derlendi),
    ///   - bin\Composite.Generated.dll mevcut ve beklenen veri tiplerini içeriyor (yansıma),
    ///   - C1 log'da yeni hata yok,
    ///   - (bilgi) AuthKit DataStore dosyaları oluşmuş.
    ///
    /// Offline modda yapılacak bir şey yoktur; kullanıcıya ilk açılış sonrası
    /// nasıl doğrulayacağını anlatır ve başarılı sayılır.
    /// (C# 5 uyumlu.)
    /// </summary>
    public class VerifyGeneratedTypesStep : ISetupStep
    {
        public string Name
        {
            get { return "Üretilen Tipleri Doğrulama (Composite.Generated.dll)"; }
        }

        public bool Verify(SetupContext context)
        {
            // Bu adım her çalışmada rapor üretir; atlanmaz.
            return false;
        }

        public string Fingerprint(SetupContext context)
        {
            return "";
        }

        public bool Execute(SetupContext context)
        {
            if (context.Mode == RunMode.Offline)
            {
                context.Log("  Offline modda ilk açılış doğrulaması yapılamaz.");
                context.Log("  Siteyi başlattıktan sonra şununla doğrulayın:");
                context.Log("    C1AfterSetup.exe -site \"" + context.SitePath + "\" -mode online" +
                    (string.IsNullOrWhiteSpace(context.SiteUrl) ? "" : " -url \"" + context.SiteUrl + "\""));
                return true;
            }

            if (context.Manifest.GeneratedTypes == null || context.Manifest.GeneratedTypes.Count == 0)
            {
                context.Log("  Manifest'te generatedTypes tanımlı değil; tip doğrulaması atlandı.");
                return true;
            }

            int ok = 0, missing = 0;

            // 1) Site sağlığı (App_Code derlenmiş mi -> HTTP 200)
            var probe = new SiteProbe(context);
            string probeError;
            if (probe.IsHealthy(out probeError))
            {
                ok++;
                context.Log("  + Site sağlıklı (HTTP 200 / App_Code derlendi).");
            }
            else
            {
                context.Error("  Site sağlıklı değil: " + probeError);
                missing++;
            }

            // 2) Composite.Generated.dll + beklenen tipler (yansıma)
            string dllCheck;
            if (DllContainsExpectedTypes(context, out dllCheck))
            {
                ok++;
                context.Log("  + Composite.Generated.dll mevcut ve beklenen " + context.Manifest.GeneratedTypes.Count + " tipi içeriyor.");
            }
            else
            {
                context.Error("  " + dllCheck);
                missing++;
            }

            // 3) C1 log'da yeni hata yok
            string logError = GetNewLogError(context);
            if (string.IsNullOrEmpty(logError))
            {
                ok++;
                context.Log("  + C1 log'da yeni hata yok.");
            }
            else
            {
                context.Error("  C1 log'da hata: " + logError);
                missing++;
            }

            // 4) (Bilgi) AuthKit DataStore dosyaları — kritik değil, yalnızca rapor
            string storesMissing;
            int present = DataStoreFilesPresent(context, out storesMissing);
            context.Log("  DataStore dosyaları: " + present + "/" + context.Manifest.GeneratedTypes.Count + " mevcut"
                + (present == context.Manifest.GeneratedTypes.Count ? "." : " (" + storesMissing + ")"));

            context.Log("Doğrulama: " + ok + " kontrol tamam, " + missing + " eksik.");
            return missing == 0;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - Online: HTTP 200 + Composite.Generated.dll tipleri + C1 log kontrolü");
            context.Log("  - Offline: ilk açılış sonrası -mode online ile doğrulama önerisi");
        }

        /// <summary>Composite.Generated.dll'yi yükleyip manifest'teki tüm beklenen tipleri içerip içermediğini döndürür.</summary>
        private static bool DllContainsExpectedTypes(SetupContext context, out string missing)
        {
            missing = null;
            string dll = context.ResolveSite(Path.Combine("bin", "Composite.Generated.dll"));
            if (!File.Exists(dll))
            {
                missing = "bin/Composite.Generated.dll bulunamadı (C1 ilk açılışta üretmedi).";
                return false;
            }

            var missingTypes = new List<string>();
            try
            {
                string binDir = Path.GetDirectoryName(dll);
                ResolveEventHandler handler = delegate (object sender, ResolveEventArgs args)
                {
                    try
                    {
                        string name = new AssemblyName(args.Name).Name + ".dll";
                        string candidate = Path.Combine(binDir, name);
                        return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                    }
                    catch
                    {
                        return null;
                    }
                };

                AppDomain.CurrentDomain.AssemblyResolve += handler;
                try
                {
                    // LoadFrom dosyayı KİLİTLER (canlı sitede kötü); byte dizisinden yükle.
                    Assembly asm = Assembly.Load(File.ReadAllBytes(dll));
                    foreach (string t in context.Manifest.GeneratedTypes)
                    {
                        if (asm.GetType(t, false) == null) missingTypes.Add(t);
                    }
                }
                finally
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= handler;
                }
            }
            catch (Exception ex)
            {
                missing = "Composite.Generated.dll yansıma ile okunamadı: " + ex.Message;
                return false;
            }

            if (missingTypes.Count > 0)
            {
                missing = "Composite.Generated.dll'de eksik tipler: " + string.Join(", ", missingTypes.ToArray());
                return false;
            }
            return true;
        }

        /// <summary>Her beklenen tip için DataStore dosyasının varlığını sayar (bilgi amaçlı).</summary>
        private static int DataStoreFilesPresent(SetupContext context, out string missing)
        {
            missing = null;
            string dataStores = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataStores"));
            var absent = new List<string>();
            int present = 0;
            foreach (string t in context.Manifest.GeneratedTypes)
            {
                if (File.Exists(Path.Combine(dataStores, t + ".xml"))) present++;
                else absent.Add(t);
            }
            if (absent.Count > 0) missing = "eksik: " + string.Join(", ", absent.ToArray());
            return present;
        }

        /// <summary>En güncel C1 log dosyalarındaki en yeni hata/exception satırını döndürür (yoksa null).</summary>
        private static string GetNewLogError(SetupContext context)
        {
            string logDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "Log"));
            if (!Directory.Exists(logDir)) return null;

            var files = Directory.GetFiles(logDir, "*.log", SearchOption.TopDirectoryOnly)
                                 .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                                 .Take(3);
            foreach (string file in files)
            {
                try
                {
                    string[] lines = File.ReadAllLines(file);
                    foreach (string line in lines.Reverse().Take(30))
                    {
                        if (line.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            line.IndexOf("Exception", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return line;
                        }
                    }
                }
                catch
                {
                    // Log okunamadıysa yok say
                }
            }
            return null;
        }
    }
}
