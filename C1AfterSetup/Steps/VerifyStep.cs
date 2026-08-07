using System;
using System.IO;
using System.Linq;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Kurulum sonrası dağıtılan dosyaların varlığını ve Web.config değişikliğini doğrular.
    /// İçerik-temelli kontrol yapar (FileSyncUtil.FilesEqual): eksik ya da kaynakla farklı
    /// olan her dosya raporlanır. Dizin eşlemeleri (ör. ~/App_Code/AuthKit) dizin + içerik olarak
    /// doğrulanır.
    /// </summary>
    public class VerifyStep : ISetupStep
    {
        public string Name
        {
            get { return "Doğrulama (Verify)"; }
        }

        public bool Verify(SetupContext context)
        {
            // Son doğrulama raporu her çalışmada gösterilir; atlanmaz.
            return false;
        }

        public string Fingerprint(SetupContext context)
        {
            return "";
        }

        public bool Execute(SetupContext context)
        {
            int ok = 0, missing = 0;

            // bin bağımlılıkları (içerik eşitliği)
            foreach (string dep in context.Manifest.BinDependencies)
            {
                string src = Path.Combine(context.BinSourcePath, dep);
                string dst = context.ResolveSite(Path.Combine("bin", dep));
                if (File.Exists(src) && File.Exists(dst) && FileSyncUtil.FilesEqual(src, dst)) ok++;
                else { context.Error("  EKSİK/ESKİ: ~/bin/" + dep); missing++; }
            }

            // overrides (sadece kullanıcı koymuşsa kontrol et)
            if (Directory.Exists(context.OverridesSourcePath))
            {
                foreach (string src in Directory.GetFiles(context.OverridesSourcePath, "*", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(src);
                    string dst = context.ResolveSite(Path.Combine("bin", name));
                    if (File.Exists(dst) && FileSyncUtil.FilesEqual(src, dst)) ok++;
                    else { context.Error("  EKSİK/ESKİ override: ~/bin/" + name); missing++; }
                }
            }

            // App_Code (dosya ya da dizin eşlemesi)
            foreach (var m in context.Manifest.AppCode.Items)
            {
                string src = context.ResolveSource(m.Source);
                string dst = context.ResolveSite(m.Target);

                if (File.Exists(src))
                {
                    if (File.Exists(dst) && FileSyncUtil.FilesEqual(src, dst)) ok++;
                    else { context.Error("  EKSİK/ESKİ: " + m.Target); missing++; }
                }
                else if (Directory.Exists(src))
                {
                    if (SyncDirMatches(src, dst)) ok++;
                    else { context.Error("  EKSİK/ESKİ (dizin): " + m.Target); missing++; }
                }
            }

            // DataMetaData — GUID tabanlı varlık kontrolü (içerik eşitliği DEĞİL).
            // CompileGeneratedTypesStep siteyi headless başlatıp tipleri işlediğinde C1, DataMetaData
            // XML'lerini kendi formatında yeniden yazar; bu yüzden içerik karşılaştırması her zaman
            // yanlış-pozitif "EKSİK/ESKİ" üretir. Dosya adı (TipAdı <GUID>.xml) kaynakla aynı kalır.
            // İki konum kontrol edilir:
            //   - PendingDataTypes: DeployDataTypesStep tarafından konuşlandırılır (henüz işlenmemiş)
            //   - DataMetaData:     CompileGeneratedTypesStep tipleri kaydettikten sonra taşınır
            string srcDataMeta = context.ResolveSource("DataMetaData");
            if (Directory.Exists(srcDataMeta))
            {
                foreach (var t in context.Manifest.DataTypes)
                {
                    foreach (string src in Directory.GetFiles(srcDataMeta, Path.GetFileName(t.File), SearchOption.TopDirectoryOnly))
                    {
                        string name = Path.GetFileName(src);
                        string dstPending = context.ResolveSite(Path.Combine("App_Data", "Composite", "PendingDataTypes", name));
                        string dstRegistered = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataMetaData", name));
                        bool okPending = File.Exists(dstPending);
                        bool okRegistered = File.Exists(dstRegistered);
                        if (okPending || okRegistered) ok++;
                        else { context.Error("  EKSİK DataMetaData: " + name); missing++; }
                    }
                }
            }

            // Templates (içerik eşitliği)
            string srcTemplates = context.ResolveSource("PageTemplates");
            foreach (var t in context.Manifest.Templates)
            {
                string dst = context.ResolveSite(Path.Combine("App_Data", "PageTemplates", Path.GetFileName(t.File)));
                string src = Path.Combine(srcTemplates, Path.GetFileName(t.File));
                if (File.Exists(src) && File.Exists(dst) && FileSyncUtil.FilesEqual(src, dst)) ok++;
                else { context.Error("  EKSİK/ESKİ Template: " + Path.GetFileName(t.File)); missing++; }
            }

            // Razor (dosya ya da dizin eşlemesi)
            foreach (var m in context.Manifest.Razor)
            {
                string src = context.ResolveSource(m.Source);
                string dst = context.ResolveSite(m.Target);

                if (File.Exists(src))
                {
                    if (File.Exists(dst) && FileSyncUtil.FilesEqual(src, dst)) ok++;
                    else { context.Error("  EKSİK/ESKİ Razor: " + m.Target); missing++; }
                }
                else if (Directory.Exists(src))
                {
                    if (SyncDirMatches(src, dst)) ok++;
                    else { context.Error("  EKSİK/ESKİ Razor (dizin): " + m.Target); missing++; }
                }
            }

            // Web.config header temizliği
            string wc = context.ResolveSite("Web.config");
            if (File.Exists(wc))
            {
                string text = File.ReadAllText(wc);
                bool hasModule = text.Contains("HeaderCleanupModule");
                bool hasServerHeader = text.Contains("removeServerHeader");
                bool hasCustomHeaders = text.Contains("<customHeaders>");
                if (hasModule && hasServerHeader && hasCustomHeaders) ok++;
                else { context.Error("  Web.config header temizliği eksik uygulanmış."); missing++; }
            }
            else
            {
                context.Error("  Web.config bulunamadı (doğrulama yapılamadı).");
                missing++;
            }

            context.Log("Doğrulama tamam: " + ok + " mevcut, " + missing + " eksik.");
            return missing == 0;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - Kurulan dosyaların varlığı/içeriği ve Web.config değişikliği doğrulanacak");
        }

        /// <summary>Kaynak dizin ağacının her dosyası hedefte aynı içerikle var mı?</summary>
        private static bool SyncDirMatches(string srcDir, string dstDir)
        {
            if (!Directory.Exists(dstDir)) return false;
            foreach (string file in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(Path.GetFullPath(srcDir).Length).TrimStart('\\', '/');
                string dst = Path.Combine(dstDir, rel);
                if (!File.Exists(dst) || !FileSyncUtil.FilesEqual(file, dst)) return false;
            }
            return true;
        }
    }
}
