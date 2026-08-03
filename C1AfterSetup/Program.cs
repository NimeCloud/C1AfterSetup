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
            RunMode mode = RunMode.Offline;
            bool dryRun = false;
            bool force = false;
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

            var context = new SetupContext(sitePath, mode, dryRun, siteUrl, manifest, force);

            var steps = new List<ISetupStep>
            {
                new PreflightStep(),
                new DeployDependenciesStep(),
                new DeployDataTypesStep(),
                new DeployPackageStep(),
                new DeployAppCodeStep(),
                new DeployPageTemplatesStep(),
                new DeployRazorStep(),
                new ConfigureWebConfigStep(),
                new VerifyStep()
            };

            context.Log("=== C1AfterSetup başlıyor ===");
            context.Log("Site     : " + context.SitePath);
            context.Log("Mod      : " + context.Mode.ToString() + " (" + (context.DryRun ? "DRY-RUN" : "gerçek") + ")");
            if (!string.IsNullOrWhiteSpace(context.SiteUrl)) context.Log("URL      : " + context.SiteUrl);
            context.Log("Manifest : " + manifestPath);
            if (context.DryRun) context.Log("DRY-RUN: hiçbir dosya yazılmayacak.");
            if (force) context.Log("FORCE    : verify atlanarak TÜM adımlar yeniden uygulanacak.");

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

        private static void PrintHelp()
        {
            Console.WriteLine("Kullanım:");
            Console.WriteLine("  C1AfterSetup.exe -site <C1-Site-Klasoru> [-mode online|offline] [-url <site-url>] [-dryrun] [-manifest <yol>]");
            Console.WriteLine("  -site     : Hedef C1 CMS web sitesi kök klasörü (zorunlu)");
            Console.WriteLine("  -mode     : online (C1 çalışırken, fazlar arası derleme bekler) veya offline (varsayılan)");
            Console.WriteLine("  -url      : Online mod için site adresi (ör. https://localhost/site) - derleme sağlık kontrolünde kullanılır");
            Console.WriteLine("  -dryrun   : Hiçbir şey yazmaz, planlanan adımları raporlar");
            Console.WriteLine("  -force    : Verify'ı atlar; TÜM adımları kaynaklardan yeniden uygular (yeni yedek alır)");
            Console.WriteLine("  -manifest : Alternatif manifest yolu (varsayılan Config\\setup.manifest.json)");
            Console.WriteLine();
            Console.WriteLine("Yeniden çalıştırılabilirlik:");
            Console.WriteLine("  Her adım önce hedef durumu verify eder; zaten güncelse atlar, farklıysa yeniler.");
            Console.WriteLine("  Önceki çalışmada BAŞARISIZ kalan adım, verify'a takılmadan yeniden denenir.");
            Console.WriteLine("  İlerleme <site>\\App_Data\\Composite\\C1AfterSetup\\state.json içinde saklanır.");
        }
    }
}
