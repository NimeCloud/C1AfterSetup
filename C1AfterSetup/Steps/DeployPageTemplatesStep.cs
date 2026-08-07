using System;
using System.IO;
using System.Linq;
using C1AfterSetup.Detect;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Sayfa şablonlarını master-first sırayla (manifest Order alanı) ~/App_Data/PageTemplates klasörüne kopyalar.
    /// Örn: PanelLayout (master) önce, sonra AuthLayout/SetupPage, en son yönetim sayfaları.
    ///
    /// Verify: manifest'teki tüm şablonlar hedefte, kaynakla aynıysa true (atlanır).
    /// Execute: yalnızca eksik/farklı şablonları yeniler.
    /// </summary>
    public class DeployPageTemplatesStep : ISetupStep
    {
        public string Name
        {
            get { return "Page Templates (master-first)"; }
        }

        public bool Verify(SetupContext context)
        {
            string srcDir = context.ResolveSource("PageTemplates");
            if (!Directory.Exists(srcDir)) return true; // kaynak yoksa kontrol edilemez

            string targetDir = context.ResolveSite(Path.Combine("App_Data", "PageTemplates"));
            foreach (var t in context.Manifest.Templates)
            {
                string[] matches = Directory.GetFiles(srcDir, Path.GetFileName(t.File), SearchOption.TopDirectoryOnly);
                foreach (string src in matches)
                {
                    string dst = Path.Combine(targetDir, Path.GetFileName(src));
                    if (!File.Exists(dst) || !FileSyncUtil.FilesEqual(src, dst)) return false;
                }
            }

            // Statik assets (CSS/JS): manifest Assets eşlemelerini doğrula
            foreach (var a in context.Manifest.Assets)
            {
                string srcA = context.ResolveSource(a.Source);
                string dstA = context.ResolveSite(a.Target);
                if (!Directory.Exists(srcA)) continue;
                if (!Directory.Exists(dstA)) return false;
                foreach (string file in Directory.GetFiles(srcA, "*", SearchOption.AllDirectories))
                {
                    string rel = file.Substring(Path.GetFullPath(srcA).Length).TrimStart('\\', '/');
                    string dstF = Path.Combine(dstA, rel);
                    if (!File.Exists(dstF) || !FileSyncUtil.FilesEqual(file, dstF)) return false;
                }
            }
            return true;
        }

        public string Fingerprint(SetupContext context)
        {
            string srcDir = context.ResolveSource("PageTemplates");
            if (!Directory.Exists(srcDir)) return "";
            return FileSyncUtil.SourceFingerprint(srcDir);
        }

        public bool Execute(SetupContext context)
        {
            string targetDir = context.ResolveSite(Path.Combine("App_Data", "PageTemplates"));
            Directory.CreateDirectory(targetDir);

            string srcDir = context.ResolveSource("PageTemplates");
            if (!Directory.Exists(srcDir))
            {
                context.Warn("sources/PageTemplates klasörü bulunamadı.");
                return true;
            }

            int updated = 0;

            var templates = context.Manifest.Templates.OrderBy(t => t.Order).ToList();
            foreach (var t in templates)
            {
                string[] matches = Directory.GetFiles(srcDir, Path.GetFileName(t.File), SearchOption.TopDirectoryOnly);
                if (matches.Length == 0)
                {
                    context.Warn("Template bulunamadı: " + t.File);
                    continue;
                }
                foreach (string src in matches)
                {
                    string dst = Path.Combine(targetDir, Path.GetFileName(src));
                    if (FileSyncUtil.CopyIfDifferent(src, dst))
                    {
                        updated++;
                        context.Log("  + " + Path.GetFileName(src) + " (order " + t.Order + ") (güncellendi)");
                    }
                    else
                    {
                        context.Log("  = " + Path.GetFileName(src) + " (order " + t.Order + ") zaten güncel");
                    }
                }
            }

            // 2) Statik assets (CSS/JS): manifest Assets eşlemelerini ~/ altına kopyala
            foreach (var a in context.Manifest.Assets)
            {
                string srcA = context.ResolveSource(a.Source);
                string dstA = context.ResolveSite(a.Target);
                if (!Directory.Exists(srcA))
                {
                    context.Warn("  Assets kaynağı yok: " + a.Source);
                    continue;
                }
                int n = FileSyncUtil.SyncDirectory(srcA, dstA);
                updated += n;
                if (n > 0)
                    context.Log("  + assets: " + a.Source + " -> " + a.Target + " (" + n + " dosya güncellendi)");
                else
                    context.Log("  = assets: " + a.Source + " zaten güncel");
            }

            if (context.Mode == RunMode.Online)
            {
                var monitor = new CompilationMonitor(context);
                string error;
                if (!monitor.WaitUntilStable(false, out error))
                {
                    context.Error("Template derlemesi beklenemedi: " + error);
                    return false;
                }
            }
            if (updated == 0) context.Log("  Tüm şablonlar zaten güncel.");
            return true;
        }

        public void Plan(SetupContext context)
        {
            foreach (var t in context.Manifest.Templates.OrderBy(t => t.Order))
                context.Log("  - order " + t.Order + ": " + t.File);
            foreach (var a in context.Manifest.Assets)
                context.Log("  - assets: " + a.Source + " -> " + a.Target);
        }
    }
}
