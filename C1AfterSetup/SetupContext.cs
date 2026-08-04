using System;
using System.Collections.Generic;
using System.IO;

namespace C1AfterSetup
{
    /// <summary>Çalışma modu: site açıkken (online, derleme beklenir) veya kapalıyken (offline).</summary>
    public enum RunMode
    {
        Offline,
        Online
    }

    /// <summary>
    /// Kurulum boyunca taşınan ortak durum. Hedef site yolu, mod, manifest ve yardımcılar.
    /// (C# 5 uyumlu - .NET Framework msbuild ile derlenebilmesi için.)
    /// </summary>
    public sealed class SetupContext
    {
        public string SitePath { get; private set; }
        public RunMode Mode { get; private set; }
        public bool DryRun { get; private set; }
        public string SiteUrl { get; private set; }
        public string SourcesPath { get; private set; }
        public string BackupPath { get; private set; }
        public SetupManifest Manifest { get; private set; }
        public SetupState State { get; private set; }
        public bool Force { get; private set; }

        /// <summary>
        /// True ise hedef site "hiç başlatılmamış" (fresh) duruma getirilir:
        /// C1'in runtime durumu (DataStores, Packages işaretleri, bayat Composite.Generated.dll vb.)
        /// temizlenir, böylece ilk açılışta AutoInstallPackages işlenir ve
        /// Composite.Generated.dll sıfırdan üretilir.
        /// </summary>
        public bool Fresh { get; private set; }

        public List<string> LogLines { get; private set; }

        public SetupContext(string sitePath, RunMode mode, bool dryRun, string siteUrl, SetupManifest manifest, bool force, bool fresh)
        {
            SitePath = Path.GetFullPath(sitePath);
            Mode = mode;
            DryRun = dryRun;
            SiteUrl = siteUrl;
            Manifest = manifest;
            Force = force;
            Fresh = fresh;
            LogLines = new List<string>();
            SourcesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sources");
            BackupPath = dryRun
                ? null
                : Path.Combine(Path.GetDirectoryName(SitePath), "backups", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            State = SetupState.LoadOrCreate(SitePath);
        }

        /// <summary>
        /// Hedef sitenin "hiç başlatılmamış" (fresh) olup olmadığını C1 runtime işaretlerine
        /// bakarak belirler. Başlatılmış bir C1 sitesinde DataStores içinde veri dosyaları ve
        /// Packages altında "installed" işaretleri bulunur; ikisi de yoksa site fresh'tir.
        /// </summary>
        public bool IsTargetFresh()
        {
            string dataStores = ResolveSite(Path.Combine("App_Data", "Composite", "DataStores"));
            if (Directory.Exists(dataStores) &&
                Directory.GetFiles(dataStores, "*.xml", SearchOption.TopDirectoryOnly).Length > 0)
            {
                return false;
            }

            string packages = ResolveSite(Path.Combine("App_Data", "Composite", "Packages"));
            if (Directory.Exists(packages))
            {
                foreach (string dir in Directory.GetDirectories(packages))
                {
                    if (File.Exists(Path.Combine(dir, "installed"))) return false;
                }
            }

            return true;
        }

        /// <summary>Bu site için state dosyası hiç oluşturulmamış mı? (İlk çalışma mı?)</summary>
        public bool IsFirstRun
        {
            get { return State == null || State.StateFilePath == null || !File.Exists(State.StateFilePath); }
        }

        /// <summary>Bir adımın başarıyla tamamlandığını state'e işler ve diske yazar.</summary>
        public void MarkStepCompleted(string name, string fingerprint)
        {
            if (State != null) State.MarkCompleted(name, fingerprint);
        }

        /// <summary>Bir adımın başarısız olduğunu state'e işler ve diske yazar.</summary>
        public void MarkStepFailed(string name)
        {
            if (State != null) State.MarkFailed(name);
        }

        /// <summary>Hedef siteye göreli (veya ~/ ile başlayan) yolu mutlak yola çevirir.</summary>
        public string ResolveSite(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return SitePath;
            if (relativePath.StartsWith("~/")) relativePath = relativePath.Substring(2);
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(SitePath, relativePath);
        }

        /// <summary>sources/ klasörüne göreli yolu mutlak yola çevirir.</summary>
        public string ResolveSource(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return SourcesPath;
            relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(SourcesPath, relativePath);
        }

        /// <summary>Varsayılan bağımlılık DLL'lerinin kaynağı (sources/bin).</summary>
        public string BinSourcePath
        {
            get { return Path.Combine(SourcesPath, "bin"); }
        }

        /// <summary>Elle eklenen üzerine-yazma DLL'lerinin kaynağı (sources/overrides).</summary>
        public string OverridesSourcePath
        {
            get { return Path.Combine(SourcesPath, "overrides"); }
        }

        public void Log(string message)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message;
            LogLines.Add(line);
            Console.WriteLine(line);
        }

        public void Warn(string message) { Log("UYARI: " + message); }
        public void Error(string message) { Log("HATA: " + message); }
    }
}
