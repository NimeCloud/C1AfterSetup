using System;
using System.Collections.Generic;
using System.IO;
using C1AfterSetup.Detect;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Razor Function dosyalarını ~/App_Data/Razor altına kopyalar.
    ///
    /// Verify: tüm eşleme hedefleri kaynakla içerik olarak aynıysa true (atlanır).
    /// Execute: yalnızca eksik/farklı dosyaları yeniler.
    /// </summary>
    public class DeployRazorStep : ISetupStep
    {
        public string Name
        {
            get { return "Razor Functions"; }
        }

        public bool Verify(SetupContext context)
        {
            var mappings = context.Manifest.Razor;
            if (mappings == null || mappings.Count == 0) return true;

            foreach (var m in mappings)
            {
                string src = context.ResolveSource(m.Source);
                string dst = context.ResolveSite(m.Target);

                if (File.Exists(src))
                {
                    if (!File.Exists(dst) || !FileSyncUtil.FilesEqual(src, dst)) return false;
                }
                else if (Directory.Exists(src))
                {
                    if (!SyncDirEquals(src, dst)) return false;
                }
                // Kaynak yoksa kontrol edilemez -> pas geç
            }
            return true;
        }

        public string Fingerprint(SetupContext context)
        {
            var paths = new List<string>();
            foreach (var m in context.Manifest.Razor)
            {
                string src = context.ResolveSource(m.Source);
                if (File.Exists(src) || Directory.Exists(src)) paths.Add(src);
            }
            return FileSyncUtil.SourceFingerprint(paths.ToArray());
        }

        public bool Execute(SetupContext context)
        {
            var mappings = context.Manifest.Razor;
            if (mappings == null || mappings.Count == 0)
            {
                context.Log("Manifest'te Razor eşlemesi yok, atlanıyor.");
                return true;
            }

            int updated = 0;

            foreach (var m in mappings)
            {
                string src = context.ResolveSource(m.Source);
                string dst = context.ResolveSite(m.Target);

                if (File.Exists(src))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (FileSyncUtil.CopyIfDifferent(src, dst))
                    {
                        updated++;
                        context.Log("  + " + m.Source + " -> " + m.Target + " (güncellendi)");
                    }
                    else
                    {
                        context.Log("  = " + m.Source + " -> " + m.Target + " zaten güncel");
                    }
                }
                else if (Directory.Exists(src))
                {
                    int changed = FileSyncUtil.SyncDirectory(src, dst);
                    updated += changed;
                    context.Log("  " + (changed > 0 ? "+ " : "= ") + m.Source + " (dizin) -> " + m.Target
                        + (changed > 0 ? " (" + changed + " dosya güncellendi)" : " zaten güncel"));
                }
                else
                {
                    context.Warn("Razor kaynağı yok: " + src);
                }
            }

            if (context.Mode == RunMode.Online)
            {
                var monitor = new CompilationMonitor(context);
                string error;
                if (!monitor.WaitUntilStable(false, out error))
                {
                    context.Error("Razor derlemesi beklenemedi: " + error);
                    return false;
                }
            }
            if (updated == 0) context.Log("  Tüm Razor dosyaları zaten güncel.");
            return true;
        }

        public void Plan(SetupContext context)
        {
            foreach (var m in context.Manifest.Razor)
                context.Log("  - " + m.Source + " -> " + m.Target);
        }

        /// <summary>Kaynak dizin ağacının her dosyası hedefte aynı içerikle var mı?</summary>
        private static bool SyncDirEquals(string srcDir, string dstDir)
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
