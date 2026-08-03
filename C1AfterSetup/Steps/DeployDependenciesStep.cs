using System;
using System.Collections.Generic;
using System.IO;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Bağımlılık DLL'lerini ~/bin klasörüne kopyalar.
    /// Kaynaklar: sources/bin (manifest'teki varsayılan bağımlılıklar) + sources/overrides
    /// (elle eklenen ve varsayılanları/mevcutları ÜZERİNE yazan dosyalar).
    ///
    /// Verify: tüm hedef DLL'ler kaynakla içerik olarak aynıysa true (atlanır).
    /// Execute: yalnızca farklı/eksik olanları yeniler (FileSyncUtil.CopyIfDifferent).
    /// </summary>
    public class DeployDependenciesStep : ISetupStep
    {
        public string Name
        {
            get { return "Bağımlılık DLL'leri (/bin + overrides)"; }
        }

        public bool Verify(SetupContext context)
        {
            string targetBin = context.ResolveSite("bin");

            // Manifest bağımlılıkları
            foreach (string dep in context.Manifest.BinDependencies)
            {
                string src = Path.Combine(context.BinSourcePath, dep);
                if (!File.Exists(src)) continue; // kaynak yoksa kontrol edilemez
                string dst = Path.Combine(targetBin, dep);
                if (!File.Exists(dst) || !FileSyncUtil.FilesEqual(src, dst)) return false;
            }

            // overrides
            if (Directory.Exists(context.OverridesSourcePath))
            {
                string[] overrideFiles = Directory.GetFiles(context.OverridesSourcePath, "*", SearchOption.TopDirectoryOnly);
                foreach (string src in overrideFiles)
                {
                    string dst = Path.Combine(targetBin, Path.GetFileName(src));
                    if (!File.Exists(dst) || !FileSyncUtil.FilesEqual(src, dst)) return false;
                }
            }

            return true;
        }

        public string Fingerprint(SetupContext context)
        {
            var paths = new List<string>();
            foreach (string dep in context.Manifest.BinDependencies)
            {
                string src = Path.Combine(context.BinSourcePath, dep);
                if (File.Exists(src)) paths.Add(src);
            }
            if (Directory.Exists(context.OverridesSourcePath))
                paths.Add(context.OverridesSourcePath);
            return FileSyncUtil.SourceFingerprint(paths.ToArray());
        }

        public bool Execute(SetupContext context)
        {
            string targetBin = context.ResolveSite("bin");
            Directory.CreateDirectory(targetBin);
            int updated = 0;

            // 1) Manifest'te listelenen varsayılan bağımlılıklar (sources/bin)
            foreach (string dep in context.Manifest.BinDependencies)
            {
                string src = Path.Combine(context.BinSourcePath, dep);
                if (!File.Exists(src))
                {
                    context.Warn("Bağımlılık kaynağı yok: sources/bin/" + dep);
                    continue;
                }
                string dst = Path.Combine(targetBin, dep);
                if (FileSyncUtil.CopyIfDifferent(src, dst))
                {
                    updated++;
                    context.Log("  + " + dep + " -> ~/bin/" + dep + " (güncellendi)");
                }
                else
                {
                    context.Log("  = " + dep + " zaten güncel");
                }
            }

            // 2) sources/overrides: kullanıcının elle koyduğu her dosya, mevcutları üzerine yazar
            if (Directory.Exists(context.OverridesSourcePath))
            {
                string[] overrideFiles = Directory.GetFiles(context.OverridesSourcePath, "*", SearchOption.TopDirectoryOnly);
                if (overrideFiles.Length == 0)
                {
                    context.Log("  sources/overrides boş - atlandı.");
                }
                else
                {
                    foreach (string src in overrideFiles)
                    {
                        string name = Path.GetFileName(src);
                        string dst = Path.Combine(targetBin, name);
                        if (FileSyncUtil.CopyIfDifferent(src, dst))
                        {
                            updated++;
                            context.Log("  (override) + " + name + " -> ~/bin/" + name + " (güncellendi)");
                        }
                        else
                        {
                            context.Log("  (override) = " + name + " zaten güncel");
                        }
                    }
                }
            }
            else
            {
                context.Log("  sources/overrides boş - atlandı.");
            }

            if (updated == 0) context.Log("  Tüm bağımlılıklar zaten güncel.");
            return true;
        }

        public void Plan(SetupContext context)
        {
            foreach (string dep in context.Manifest.BinDependencies)
                context.Log("  - ~/bin/" + dep + " (sources/bin)");
            context.Log("  - sources/overrides içindeki tüm dosyalar ~/bin'e yazılır (varsayılanları üzerine yazar)");
        }
    }
}
