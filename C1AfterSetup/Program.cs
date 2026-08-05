using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using C1AfterSetup.Steps;

namespace C1AfterSetup
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            // Türkçe karakterlerin konsolda düzgün görünmesi için UTF-8 çıktı
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // Bazı konsol yazı tipleri UTF-8'i desteklemez; yok sayılır.
            }

            string sitePath = null;
            string siteUrl = null;
            string outDir = null;
            RunMode mode = RunMode.Offline;
            bool dryRun = false;
            bool force = false;
            bool fresh = false;
            bool capture = false;
            string manifestPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "setup.manifest.json");

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLowerInvariant();
                switch (arg)
                {
                    case "-site":
                        if (i + 1 < args.Length) sitePath = args[++i];
                        break;
                    case "-url":
                        if (i + 1 < args.Length) siteUrl = args[++i];
                        break;
                    case "-out":
                        if (i + 1 < args.Length) outDir = args[++i];
                        break;
                    case "-mode":
                        if (i + 1 < args.Length)
                            mode = args[++i].ToLowerInvariant() == "online" ? RunMode.Online : RunMode.Offline;
                        break;
                    case "-dryrun":
                        dryRun = true;
                        break;
                    case "-force":
                        force = true;
                        break;
                    case "-fresh":
                        fresh = true;
                        break;
                    case "-capture":
                        capture = true;
                        break;
                    case "-manifest":
                        if (i + 1 < args.Length) manifestPath = args[++i];
                        break;
                    case "-h":
                    case "-help":
                        PrintHelp();
                        return 0;
                }
            }

            if (string.IsNullOrWhiteSpace(sitePath))
            {
                Console.WriteLine("Hedef C1 CMS site yolu verilmedi. -site <yol> kullanın.");
                PrintHelp();
                return 1;
            }

            if (!Directory.Exists(sitePath))
            {
                Console.WriteLine("Site yolu bulunamadı: " + sitePath);
                return 1;
            }

            // -out verildiyse: kaynak siteyi ayrı bir dağıtım klasörüne kopyala ve
            // pipeline'ı o klasöre uygula. Böylece çalışan Web.config/Website klasörü bozulmaz.
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                string srcRoot = Path.GetFullPath(sitePath).TrimEnd('\\');
                string dstRoot = Path.GetFullPath(outDir).TrimEnd('\\');
                if (string.Equals(srcRoot, dstRoot, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("HATA: -out hedefi, -site ile aynı klasör olamaz. In-place için -out vermeyin.");
                    return 1;
                }
                if (Directory.Exists(dstRoot) && Directory.GetFileSystemEntries(dstRoot).Length > 0)
                {
                    Console.WriteLine("HATA: -out hedefi boş değil: " + dstRoot + ". Lütfen boşaltın veya başka yol verin.");
                    return 1;
                }
                Console.WriteLine("Site dağıtım klasörüne kopyalanıyor: " + srcRoot + " -> " + dstRoot);
                CopyDirectoryRecursive(srcRoot, dstRoot);
                sitePath = dstRoot;
            }

            SetupManifest manifest;
            try
            {
                manifest = SetupManifest.Load(manifestPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Manifest yüklenemedi (" + manifestPath + "): " + ex.Message);
                return 1;
            }

            var context = new SetupContext(sitePath, mode, dryRun, siteUrl, manifest, force, fresh);

            // -capture: tipleri kurulmuş (başlatılmış) bir sitenin bin\Composite.Generated.dll dosyasını
            // sources\generated\ altına kopyalar; sonraki offline dağıtımlar bunu şip eder.
            if (capture)
            {
                string genSrc = context.ResolveSite(Path.Combine("bin", "Composite.Generated.dll"));
                if (!File.Exists(genSrc))
                {
                    Console.WriteLine("HATA: " + genSrc + " bulunamadı. Tipleri kurulmuş (başlatılmış) bir site gerekir.");
                    return 1;
                }
                string genDir = context.ResolveSource("generated");
                Directory.CreateDirectory(genDir);
                string genDst = Path.Combine(genDir, "Composite.Generated.dll");
                File.Copy(genSrc, genDst, true);
                Console.WriteLine("Yakalanan Composite.Generated.dll: " + genSrc + " -> " + genDst);
                Console.WriteLine("Kalıcı yapmak için bu dosyayı C1AfterSetup\\sources\\generated\\ altına kopyalayın.");
                return 0;
            }

            var steps = new List<ISetupStep>
            {
                new PreflightStep(),
                new PrepareFreshStep(),
                new DeployDependenciesStep(),
                new DeployDataTypesStep(),
                new DeployPackageStep(),
                new CompileGeneratedTypesStep(),
                new DeployAppCodeStep(),
                new DeployPageTemplatesStep(),
                new DeployAuthKitPagesStep(),
                new DeployRazorStep(),
                new ConfigureWebConfigStep(),
                new VerifyStep(),
                new VerifyGeneratedTypesStep()
            };

            context.Log("=== C1AfterSetup başlıyor ===");
            context.Log("Site     : " + context.SitePath);
            context.Log("Mod      : " + context.Mode.ToString() + " (" + (context.DryRun ? "DRY-RUN" : "gerçek") + ")");
            if (!string.IsNullOrWhiteSpace(context.SiteUrl)) context.Log("URL      : " + context.SiteUrl);
            context.Log("Manifest : " + manifestPath);
            if (context.DryRun) context.Log("DRY-RUN: hiçbir dosya yazılmayacak.");
            if (force) context.Log("FORCE    : verify atlanarak TÜM adımlar yeniden uygulanacak.");
            if (fresh) context.Log("FRESH    : hedef site 'hiç başlatılmamış' duruma getirilecek (runtime durumu temizlenir).");

            // Önceki çalışmanın (varsa) kayıtlı durumunu özetle
            if (context.State.Steps.Count > 0)
            {
                context.Log("Önceki çalışma durumu (" + context.State.StateFilePath + "):");
                foreach (var rec in context.State.Steps)
                {
                    string status = rec.Failed ? "YARIM/BAŞARISIZ" : (rec.Completed ? "tamamlandı" : "kayıtsız");
                    context.Log("  - " + rec.Name + ": " + status + (string.IsNullOrEmpty(rec.CompletedAt) ? "" : " (" + rec.CompletedAt + ")"));
                }
            }

            int exitCode = 0;
            foreach (var step in steps)
            {
                if (context.DryRun)
                {
                    context.Log("[DRY-RUN] Adım: " + step.Name);
                    step.Plan(context);
                    continue;
                }

                context.Log("--- Adım: " + step.Name + " ---");

                // 1) Önceki çalışmada bu adım BAŞARISIZ olduysa -> verify'a takılmadan yeniden dene
                if (context.State.IsFailed(step.Name))
                {
                    context.Log("  Önceki çalışmada BAŞARISIZ kayıtlı -> yeniden uygulanıyor.");
                }
                // 2) FORCE yoksa hedef durumu doğrula: zaten güncelse atla, değilse yenile
                else if (!force)
                {
                    bool verified = false;
                    try { verified = step.Verify(context); }
                    catch (Exception vx) { context.Warn("Adım verify edilirken hata: " + vx.Message); }

                    if (verified)
                    {
                        context.Log("  Zaten güncel durumda -> atlandı (verify OK).");
                        if (!context.State.IsCompleted(step.Name))
                            context.MarkStepCompleted(step.Name, SafeFingerprint(context, step));
                        continue;
                    }
                    if (context.State.IsCompleted(step.Name))
                        context.Log("  Önceki çalışmada tamamlanmıştı; yeniden çalıştırılıyor (hedef güncel değil ya da adım her seferinde çalışır).");
                }
                else
                {
                    context.Log("  FORCE: verify atlanıyor, yeniden uygulanıyor.");
                }

                // 3) Uygula ve state'e işle
                try
                {
                    if (!step.Execute(context))
                    {
                        context.Error("Adım BAŞARISIZ: " + step.Name);
                        context.MarkStepFailed(step.Name);
                        exitCode = 1;
                        break;
                    }
                    context.MarkStepCompleted(step.Name, SafeFingerprint(context, step));
                }
                catch (Exception ex)
                {
                    context.Error("Adım sırasında özel durum: " + step.Name + ": " + ex.Message);
                    context.Error(ex.ToString());
                    context.MarkStepFailed(step.Name);
                    exitCode = 1;
                    break;
                }
            }

            context.Log(exitCode == 0 ? "=== C1AfterSetup tamamlandı ===" : "=== C1AfterSetup HATA ile bitti ===");
            return exitCode;
        }

        /// <summary>Adımın imzasını güvenle hesaplar (istisna olursa boş döner).</summary>
        private static string SafeFingerprint(SetupContext context, ISetupStep step)
        {
            try { return step.Fingerprint(context); }
            catch { return ""; }
        }

        /// <summary>Bir dizin ağacını birebir kopyalar (-out dağıtım klasörü için).</summary>
        private static void CopyDirectoryRecursive(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectoryRecursive(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Kullanım:");
            Console.WriteLine("  C1AfterSetup.exe -site <C1-Site-Klasoru> [-out <dağıtım-klasörü>] [-mode online|offline] [-url <site-url>] [-dryrun] [-manifest <yol>] [-fresh]");
            Console.WriteLine("  -site     : Kaynak / hedef C1 CMS web sitesi kök klasörü (zorunlu)");
            Console.WriteLine("  -out      : Verilirse, -site'i bu klasöre kopyalar ve pipeline'ı oraya uygular;");
            Console.WriteLine("              kaynak klasör (çalışan siteniz) bozulmaz. Hedef boş olmalıdır.");
            Console.WriteLine("  -mode     : online (C1 çalışırken, fazlar arası derleme bekler) veya offline (varsayılan)");
            Console.WriteLine("  -url      : Online mod için site adresi (ör. https://localhost/site) - derleme sağlık kontrolünde kullanılır");
            Console.WriteLine("  -dryrun   : Hiçbir şey yazmaz, planlanan adımları raporlar");
            Console.WriteLine("  -force    : Verify'ı atlar; TÜM adımları kaynaklardan yeniden uygular (yeni yedek alır)");
            Console.WriteLine("  -fresh    : Hedefi 'hiç başlatılmamış' duruma getirir; C1 runtime durumunu (DataStores,");
            Console.WriteLine("              Packages işaretleri, bayat bin\\Composite.Generated.dll vb.) temizler, böylece");
            Console.WriteLine("              ilk açılışta AutoInstallPackages işlenir ve üretilen dll sıfırdan oluşur.");
            Console.WriteLine("  -capture  : Hedef sitedeki (tipleri kurulmuş) bin\\Composite.Generated.dll dosyasını");
            Console.WriteLine("              sources\\generated\\ altına kopyalar; sonraki offline dağıtımlar bunu şip eder.");
            Console.WriteLine("  -manifest : Alternatif manifest yolu (varsayılan Config\\setup.manifest.json)");
            Console.WriteLine();
            Console.WriteLine("Sıfır manuel adımlı fresh dağıtım:");
            Console.WriteLine("  C1AfterSetup.exe -site <yeni-site-kopysi> -fresh   (offline, site kapalıyken)");
            Console.WriteLine("  -> Klasörü sunucuya kopyala ve ilk kez başlat; C1 paketi kurar, tipleri üretir.");
            Console.WriteLine("  -> Sonra doğrulamak için: -mode online -url <adres> (ilk açılış sonrası).");
            Console.WriteLine();
            Console.WriteLine("Yeniden çalıştırılabilirlik:");
            Console.WriteLine("  Her adım önce hedef durumu verify eder; zaten güncelse atlar, farklıysa yeniler.");
            Console.WriteLine("  Önceki çalışmada BAŞARISIZ kalan adım, verify'a takılmadan yeniden denenir.");
            Console.WriteLine("  İlerleme <site>\\App_Data\\Composite\\C1AfterSetup\\state.json içinde saklanır.");
        }
    }
}
