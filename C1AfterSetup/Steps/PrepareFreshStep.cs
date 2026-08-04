using System;
using System.IO;
using System.Reflection;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Hedef siteyi "hiç başlatılmamış" (fresh) duruma getirir: C1'in ilk çalıştırmada
    /// ürettiği runtime durumunu (DataStores, Packages işaretleri, Cache, Log, ApplicationState,
    /// Media ve bayat bin\Composite.Generated.dll) kaldırır.
    ///
    /// Neden gerekli? C1, veri tipi paketlerini (AutoInstallPackages) YALNIZCA hiç başlatılmamış
    /// bir sitede işler ve Composite.Generated.dll'i yalnızca o zaman sıfırdan üretir.
    /// Offline kopyalanan bir sitede bayat Composite.Generated.dll (içinde yeni tipler yok)
    /// kalırsa, App_Code o tiplere referans verdiği için ilk açılışta derleme hatası oluşur.
    ///
    /// Bu adım yalnızca -fresh bayrağı ile çalışır; bayrak verilmemişse no-op'tur.
    /// (C# 5 uyumlu.)
    /// </summary>
    public class PrepareFreshStep : ISetupStep
    {
        public string Name
        {
            get { return "Fresh Hazırlık (runtime durumu temizleme)"; }
        }

        /// <summary>Silinecek dizinler (site köküne göreli).</summary>
        private static readonly string[] DirsToStrip = new string[]
        {
            Path.Combine("App_Data", "Composite", "DataStores"),
            Path.Combine("App_Data", "Composite", "Packages"),
            Path.Combine("App_Data", "Composite", "Cache"),
            Path.Combine("App_Data", "Composite", "Log"),
            Path.Combine("App_Data", "Composite", "ApplicationState"),
            Path.Combine("App_Data", "Composite", "Temp"),
            Path.Combine("App_Data", "Media"),
            Path.Combine("App_Data", "Composite", "C1AfterSetup") // kendi ilerleme state'imiz
        };

        /// <summary>Silinecek dosyalar (site köküne göreli). Composite.Generated.dll ayrıca ele alınır:
        /// yalnızca beklenen tipleri içermiyorsa (bayatsa) silinir; içeriyorsa korunur.</summary>
        private static readonly string[] FilesToStrip = new string[]
        {
            Path.Combine("bin", "Composite.Generated.pdb")
        };

        public bool Verify(SetupContext context)
        {
            // -fresh yoksa veya site online (çalışıyor) ise adım gerekmez/uygulanmaz.
            if (!context.Fresh) return true;
            if (context.Mode == RunMode.Online) return true;
            return context.IsTargetFresh();
        }

        public string Fingerprint(SetupContext context)
        {
            return "";
        }

        public bool Execute(SetupContext context)
        {
            if (!context.Fresh)
            {
                context.Log("  -fresh verilmedi; Fresh hazırlık atlandı.");
                return true;
            }

            if (context.Mode == RunMode.Online)
            {
                context.Warn("-fresh yalnızca offline modda anlamlıdır (site kapalıyken). Online modda atlanıyor;");
                context.Warn("çalışan bir sitede veri tiplerini güncellemek için normal '-mode online' akışını kullanın.");
                return true;
            }

            if (context.IsTargetFresh())
            {
                context.Log("  Hedef zaten 'hiç başlatılmamış' durumda; temizlik gerekmedi.");
                return true;
            }

            context.Log("  Hedef başlatılmış bir site; fresh duruma getiriliyor...");

            // 1) Yedekle (sadece geri getirilmesi zor olan runtime durumu; dll zaten yeniden üretilir).
            if (!string.IsNullOrEmpty(context.BackupPath))
            {
                string backupRoot = Path.Combine(context.BackupPath, "fresh_reset");
                int backed = 0;
                foreach (string rel in DirsToStrip)
                {
                    string src = context.ResolveSite(rel);
                    if (Directory.Exists(src))
                    {
                        CopyDirectory(src, Path.Combine(backupRoot, rel.Replace('\\', '_').Replace('/', '_')));
                        backed++;
                    }
                }
                context.Log("  Fresh öncesi runtime durumu yedeğe alındı: " + backupRoot + " (" + backed + " klasör)");
            }

            // 2) Dizinleri kaldır ve BOŞ olarak yeniden oluştur.
            //    C1, başlangıçta DataStores gibi klasörlerin VAR olmasını bekler (eksikse HttpException).
            foreach (string rel in DirsToStrip)
            {
                string path = context.ResolveSite(rel);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                    context.Log("  - Temizlendi (dizin): " + rel);
                }
                else
                {
                    context.Log("  = Yok (dizin): " + rel);
                }
                Directory.CreateDirectory(path);
            }

            // 3) Composite.Generated.dll: beklenen tipleri içeriyorsa KORU (paket zaten kurulmuş demektir),
            //    içermiyorsa/bayat ise SİL -> C1 ilk açılışta yeniden üretsin.
            string generatedDll = context.ResolveSite(Path.Combine("bin", "Composite.Generated.dll"));
            if (File.Exists(generatedDll))
            {
                if (DllContainsExpectedTypes(context))
                {
                    context.Log("  = Korundu (dosya): bin\\Composite.Generated.dll (beklenen tipleri içeriyor)");
                }
                else
                {
                    File.Delete(generatedDll);
                    context.Log("  - Kaldırıldı (dosya): bin\\Composite.Generated.dll (bayat / beklenen tipler eksik)");
                }
            }
            else
            {
                context.Log("  = Yok (dosya): bin\\Composite.Generated.dll");
            }

            // 4) Geriye kalan silinecek dosyalar (pdb vb.)
            foreach (string rel in FilesToStrip)
            {
                string path = context.ResolveSite(rel);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    context.Log("  - Kaldırıldı (dosya): " + rel);
                }
                else
                {
                    context.Log("  = Yok (dosya): " + rel);
                }
            }

            context.Log("  Fresh hazırlık tamam. İlk açılışta C1: AutoInstallPackages'i işler, veri tiplerini üretir,");
            context.Log("  Composite.Generated.dll'i sıfırdan oluşturur ve App_Code'u bu yeni dll'e göre derler.");
            return true;
        }

        public void Plan(SetupContext context)
        {
            if (!context.Fresh)
            {
                context.Log("  -fresh verilmedi; Fresh hazırlık uygulanmayacak.");
                return;
            }
            if (context.IsTargetFresh())
            {
                context.Log("  Hedef zaten 'hiç başlatılmamış' durumda; temizlik gerekmez.");
                return;
            }
            context.Log("  -fresh: aşağıdaki C1 runtime durumu temizlenip boş olarak yeniden oluşturulacak (yedek alınarak):");
            foreach (string rel in DirsToStrip) context.Log("    - " + rel);
            context.Log("    - bin\\Composite.Generated.dll (yalnızca beklenen tipleri içermiyorsa silinir)");
            foreach (string rel in FilesToStrip) context.Log("    - " + rel);
        }

        /// <summary>
        /// bin\Composite.Generated.dll dosyasının manifest'teki TÜM beklenen tipleri içerip
        /// içermediğini yansıma ile denetler (en iyi çaba). Doğrulanamazsa false döner.
        /// </summary>
        private static bool DllContainsExpectedTypes(SetupContext context)
        {
            if (context.Manifest.GeneratedTypes == null || context.Manifest.GeneratedTypes.Count == 0)
                return true;
            string dll = context.ResolveSite(Path.Combine("bin", "Composite.Generated.dll"));
            if (!File.Exists(dll)) return false;
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
                    // LoadFrom dosyayı KİLİTLER; silmek gerekebileceğinden byte dizisinden yükle.
                    Assembly asm = Assembly.Load(File.ReadAllBytes(dll));
                    foreach (string t in context.Manifest.GeneratedTypes)
                    {
                        if (asm.GetType(t, false) == null) return false;
                    }
                    return true;
                }
                finally
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= handler;
                }
            }
            catch
            {
                return false;
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
