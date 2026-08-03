using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using C1AfterSetup.Detect;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// DataMetaData XML'lerini parent-first gruplar halinde ~/App_Data/Composite/DataMetaData klasörüne kopyalar.
    /// Online modda her grup sonrası C1'in Composite.Generated.dll'i yeniden üretmesi beklenir.
    ///
    /// Verify: manifest'teki tüm eşleşen DataMetaData dosyaları hedefte, kaynakla aynıysa true.
    /// Execute: yalnızca eksik/farklı dosyaları yeniler.
    /// </summary>
    public class DeployDataTypesStep : ISetupStep
    {
        public string Name
        {
            get { return "DataMetaData (parent-first)"; }
        }

        public bool Verify(SetupContext context)
        {
            string srcDir = context.ResolveSource("DataMetaData");
            if (!Directory.Exists(srcDir)) return true; // kaynak yoksa kontrol edilemez
            if (context.Manifest.DataTypes.Count == 0) return true;

            string targetDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataMetaData"));
            foreach (var entry in context.Manifest.DataTypes)
            {
                string pattern = Path.GetFileName(entry.File);
                foreach (string src in Directory.GetFiles(srcDir, pattern, SearchOption.TopDirectoryOnly))
                {
                    string dst = Path.Combine(targetDir, Path.GetFileName(src));
                    if (!File.Exists(dst) || !FileSyncUtil.FilesEqual(src, dst)) return false;
                }
            }
            return true;
        }

        public string Fingerprint(SetupContext context)
        {
            string srcDir = context.ResolveSource("DataMetaData");
            if (!Directory.Exists(srcDir)) return "";
            return FileSyncUtil.SourceFingerprint(srcDir);
        }

        public bool Execute(SetupContext context)
        {
            string targetDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataMetaData"));
            Directory.CreateDirectory(targetDir);

            List<DataTypeEntry> entries = context.Manifest.DataTypes;
            if (entries.Count == 0)
            {
                context.Log("Manifest'te DataMetaData yok, atlanıyor.");
                return true;
            }

            // Grup sırasına göre sırala (A < B < C ...), grup içinde dependsOn adedine göre
            var ordered = entries
                .OrderBy(e => e.Group ?? "Z", StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.DependsOn != null ? e.DependsOn.Count : 0)
                .ToList();

            int updated = 0;

            foreach (var group in ordered.GroupBy(e => e.Group ?? "Z"))
            {
                context.Log("Data tip grubu '" + group.Key + "' işleniyor (" + group.Count() + " tip)...");
                foreach (var entry in group)
                {
                    string srcDir = context.ResolveSource("DataMetaData");
                    if (!Directory.Exists(srcDir))
                    {
                        context.Warn("sources/DataMetaData klasörü bulunamadı.");
                        continue;
                    }
                    string pattern = Path.GetFileName(entry.File);
                    string[] matches = Directory.GetFiles(srcDir, pattern, SearchOption.TopDirectoryOnly);
                    if (matches.Length == 0)
                    {
                        context.Warn("Eşleşen DataMetaData bulunamadı: " + entry.File);
                        continue;
                    }
                    foreach (string src in matches)
                    {
                        string dst = Path.Combine(targetDir, Path.GetFileName(src));
                        if (FileSyncUtil.CopyIfDifferent(src, dst))
                        {
                            updated++;
                            context.Log("  + " + Path.GetFileName(src) + " (güncellendi)");
                        }
                        else
                        {
                            context.Log("  = " + Path.GetFileName(src) + " zaten güncel");
                        }
                    }
                }

                // Online modda grup sonrası C1'in sindirmesini bekle
                if (context.Mode == RunMode.Online)
                {
                    var monitor = new CompilationMonitor(context);
                    string error;
                    if (!monitor.WaitUntilStable(true, out error))
                    {
                        context.Error("Grup '" + group.Key + "' sonrası C1 derleme beklenemedi: " + error);
                        return false;
                    }
                }
            }

            if (updated == 0) context.Log("  Tüm DataMetaData dosyaları zaten güncel.");
            return true;
        }

        public void Plan(SetupContext context)
        {
            foreach (var group in context.Manifest.DataTypes.GroupBy(e => e.Group ?? "Z"))
                context.Log("  - Data grubu " + group.Key + ": " + string.Join(", ", group.Select(e => e.File)));
        }
    }
}
