using System;
using System.IO;

namespace C1AfterSetup.Steps
{
    public class PreflightStep : ISetupStep
    {
        public string Name
        {
            get { return "Ön Kontrol (Preflight)"; }
        }

        public bool Verify(SetupContext context)
        {
            // Ön kontrol her çalışmada yapılmalıdır (ortam + yedek durumu); atlanmaz.
            return false;
        }

        public string Fingerprint(SetupContext context)
        {
            return "";
        }

        public bool Execute(SetupContext context)
        {
            if (!File.Exists(context.ResolveSite("Web.config")))
            {
                context.Error("Web.config bulunamadı - geçerli bir C1 CMS sitesi değil.");
                return false;
            }

            if (!Directory.Exists(context.ResolveSite("App_Data")))
            {
                context.Error("App_Data klasörü bulunamadı.");
                return false;
            }

            // Hedef zaten başlatılmışsa (DataStores/Packages mevcut) ve -fresh VERİLMEMİŞSE:
            // bayat Composite.Generated.dll nedeniyle App_Code derlenemeyebilir, AMA
            // CompileGeneratedTypesStep (derleme adımı) siteyi bir kere başlatıp tipleri
            // DynamicTypeManager ile kaydeder ve DLL'i üretir. Mevcut site içeriği KORUNUR.
            // Yalnızca BİLGİ; hata değil.
            if (context.Manifest.DataTypes.Count > 0 &&
                context.Mode == RunMode.Offline &&
                !context.Fresh &&
                !context.IsTargetFresh())
            {
                context.Log("BİLGİ: Hedef site zaten başlatılmış (mevcut içerik, admin, sayfalar korunur).");
                context.Log("  Veri tipleri, derleme adımı ile eklenecek (IIS Express headless başlatma).");
                context.Log("  Bayat Composite.Generated.dll, derleme adımı tarafından yeniden üretilir.");
            }

            // Online modda site erişilebilir olmalı
            if (context.Mode == RunMode.Online)
            {
                var probe = new Detect.SiteProbe(context);
                string error;
                if (!probe.IsSiteReachable(out error))
                {
                    context.Warn("Online mod seçildi ama site erişilemedi (" + error + "). Offline gibi devam edilecek; fazlar arası derleme bekleme HTTP olmadan yapılacak.");
                }
                else
                {
                    context.Log("Site erişilebilir durumda.");
                }
            }

            // Yedek al (Web.config + DataMetaData + DataStores).
            // Yalnızca İLK çalışmada veya FORCE ile yeniden başlatmada alınır; sonraki
            // çalışmalarda orijinal temiz yedek korunur (yarım kalmış durumu yedeklemeyiz).
            if (!context.DryRun)
            {
                if (context.IsFirstRun || context.Force)
                {
                    Directory.CreateDirectory(context.BackupPath);
                    string[] items = new string[]
                    {
                        "Web.config",
                        Path.Combine("App_Data", "Composite", "DataMetaData"),
                        Path.Combine("App_Data", "Composite", "DataStores")
                    };
                    foreach (string item in items)
                    {
                        string src = context.ResolveSite(item);
                        if (File.Exists(src))
                        {
                            File.Copy(src, Path.Combine(context.BackupPath, Path.GetFileName(src)), true);
                        }
                        else if (Directory.Exists(src))
                        {
                            CopyDirectory(src, Path.Combine(context.BackupPath, "backup_" + Path.GetFileName(src)));
                        }
                    }
                    context.Log("Yedek oluşturuldu: " + context.BackupPath);
                }
                else
                {
                    context.Log("Önceki çalışmadan yedek mevcut; yeni yedek alınmadı (orijinal temiz yedek korunur).");
                }
            }

            return true;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - Web.config / App_Data varlığı doğrulanacak");
            context.Log("  - Online modda site erişim kontrolü");
            context.Log("  - Yedek alınacak (Web.config, DataMetaData, DataStores)");
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
