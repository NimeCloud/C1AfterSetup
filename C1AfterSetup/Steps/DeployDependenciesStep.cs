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

            // sources/generated: yakalanmış (tipleri içeren) Composite.Generated.dll varsa hedefte olmalı
            string generatedSrc = context.ResolveSource(Path.Combine("generated", "Composite.Generated.dll"));
            if (File.Exists(generatedSrc))
            {
                string generatedDst = Path.Combine(targetBin, "Composite.Generated.dll");
                if (!File.Exists(generatedDst) || !FileSyncUtil.FilesEqual(generatedSrc, generatedDst)) return false;
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

            // 3) sources/roslyn: Roslyn compiler dosyalari ~/bin/roslyn altina kopyalanir
            //    (Microsoft.CodeDom.Providers.DotNetCompilerPlatform icin gerekli).
            if (context.Manifest.RoslynEnabled)
            {
                string roslynSrc = context.ResolveSource("roslyn");
                if (Directory.Exists(roslynSrc))
                {
                    string roslynDst = Path.Combine(targetBin, "roslyn");
                    int roslynChanged = FileSyncUtil.SyncDirectory(roslynSrc, roslynDst);
                    updated += roslynChanged;
                    if (roslynChanged > 0)
                        context.Log("  + roslyn/ (dizin) -> ~/bin/roslyn/ (" + roslynChanged + " dosya güncellendi)");
                    else
                        context.Log("  = roslyn/ (dizin) zaten güncel");
                }
                else
                {
                    context.Warn("  sources/roslyn yok, Roslyn compiler kopyalanamadi.");
                }
            }

            // 4) sources/generated: yakalanmış (tipleri içeren) Composite.Generated.dll varsa ~/bin'e kopyala.
            //    Böylece App_Code, derleme anında tipleri bulur (ASP.NET, C1 Application_Start'tan ÖNCE App_Code derler).
            string generatedSrc = context.ResolveSource(Path.Combine("generated", "Composite.Generated.dll"));
            if (File.Exists(generatedSrc))
            {
                string generatedDst = Path.Combine(targetBin, "Composite.Generated.dll");
                if (FileSyncUtil.CopyIfDifferent(generatedSrc, generatedDst))
                {
                    updated++;
                    context.Log("  (generated) + Composite.Generated.dll -> ~/bin/Composite.Generated.dll (güncellendi)");
                }
                else
                {
                    context.Log("  (generated) = Composite.Generated.dll zaten güncel");
                }
            }
            else
            {
                context.Log("  sources/generated yok - Composite.Generated.dll şip edilmedi (ilk açılışta C1 üretir).");
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
