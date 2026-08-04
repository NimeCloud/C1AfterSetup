using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Fresh dağıtımda veri tiplerini C1'in RESMİ API'si üzerinden kaydedip
    /// ~/bin/Composite.Generated.dll dosyasını üretir.
    ///
    /// Neden gerekli? ASP.NET, App_Code'u C1'in Application_Start'ından ÖNCE derler; bu yüzden
    /// App_Code, üretilmiş tiplere ancak bin'deki Composite.Generated.dll zaten o tipleri
    /// içeriyorsa erişebilir. C1, elle atılan DataMetaData XML'lerini başlatılmış sitede
    /// "yetim" olarak siler; tipler C1'in DynamicTypeManager API'siyle KAYDEDİLİRSE derlenir.
    ///
    /// Bu adım (yalnızca offline modda):
    ///   1. DataTypeAutoInstaller.cs [ApplicationStartup] hook'unu ~/App_Code içine kopyalar.
    ///   2. IIS Express ile siteyi headless başlatır (Application_Start tetiklenir).
    ///   3. Hook, ~/App_Data/Composite/PendingDataTypes XML'lerini DynamicTypeManager.CreateStore
    ///      ile kaydeder (DataStores oluşur).
    ///   4. web.config touch → zarif kapanış (Application_End) → C1, Composite.Generated.dll'i
    ///      yeni tiplerle yeniden üretir.
    ///   5. Üretilen DLL'in beklenen tipleri içerdiği doğrulanır.
    ///
    /// App_Code'da yalnızca hook varken çalışır (AuthKit henüz konuşlandırılmamıştır; AuthKit,
    /// bu adımdan SONRA gelen DeployAppCodeStep ile eklenir). IIS Express yoksa uyarı + elle
    /// yapılacak adımları söyler. (C# 5 uyumlu.)
    /// </summary>
    public class CompileGeneratedTypesStep : ISetupStep
    {
        public string Name
        {
            get { return "Veri Tiplerini Derleme (Composite.Generated.dll)"; }
        }

        public bool Verify(SetupContext context)
        {
            // Online modda veya beklenen tip yoksa atla; hedef DLL zaten tipleri içeriyorsa gerek yok.
            if (context.Mode == RunMode.Online) return true;
            if (context.Manifest.GeneratedTypes == null || context.Manifest.GeneratedTypes.Count == 0) return true;
            return DllContainsExpectedTypes(context);
        }

        public string Fingerprint(SetupContext context)
        {
            return "";
        }

        public bool Execute(SetupContext context)
        {
            if (context.Mode == RunMode.Online)
            {
                context.Log("  Online modda derleme adımı atlandı (canlı sitede C1 zaten derler).");
                return true;
            }
            if (context.Manifest.GeneratedTypes == null || context.Manifest.GeneratedTypes.Count == 0)
            {
                context.Log("  Manifest'te generatedTypes yok; derleme adımı atlandı.");
                return true;
            }
            if (DllContainsExpectedTypes(context))
            {
                context.Log("  Composite.Generated.dll zaten beklenen tipleri içeriyor; derleme gerekmedi.");
                return true;
            }

            // 1) Hook'u App_Code'a kopyala
            string hookSrc = context.ResolveSource("DataTypeAutoInstaller.cs");
            if (!File.Exists(hookSrc))
            {
                context.Error("sources/DataTypeAutoInstaller.cs bulunamadı.");
                return false;
            }
            string hookDst = context.ResolveSite(Path.Combine("App_Code", "DataTypeAutoInstaller.cs"));
            Directory.CreateDirectory(Path.GetDirectoryName(hookDst));
            if (FileSyncUtil.CopyIfDifferent(hookSrc, hookDst))
            {
                context.Log("  + Hook konuşlandırıldı: ~/App_Code/DataTypeAutoInstaller.cs");
            }
            else
            {
                context.Log("  = Hook zaten güncel: ~/App_Code/DataTypeAutoInstaller.cs");
            }

            // 2) IIS Express bul
            string iis = FindIisExpress();
            if (iis == null)
            {
                context.Warn("IIS Express bulunamadı. Derleme adımı atlandı.");
                context.Warn("Elle yapmak için: siteyi başlatın (hook tipleri kaydeder), sonra web.config'e");
                context.Warn("dokunup recycle edin; Composite.Generated.dll yeniden üretilir. Ardından aracı");
                context.Warn("yeniden çalıştırın (App_Code + modül konuşlandırılır).");
                return true;
            }

            // 3) PowerShell derleme script'ini yaz ve çalıştır
            string psPath = Path.Combine(Path.GetTempPath(), "c1aftersetup-compile.ps1");
            File.WriteAllText(psPath, BuildCompileScript(), new UTF8Encoding(false));

            int port = 8090;
            string sitePath = context.SitePath;
            // Standart çıktıyı YÖNLENDİRME; child iisexpress stdout'u açık tuttuğu için ReadToEnd kilitlenirdi.
            // Bunun yerine script, durumunu bir log dosyasına yazar; biz de işlem bitince okuruz.
            string logFile = Path.Combine(Path.GetTempPath(), "c1aftersetup-compile-" + Guid.NewGuid().ToString("N") + ".log");

            context.Log("  IIS Express ile site headless başlatılıyor (port " + port + "), tipler kaydedilecek...");
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -File \"" + psPath + "\" -SitePath \"" + sitePath + "\" -Port " + port + " -LogFile \"" + logFile + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true
            };

            using (Process proc = Process.Start(psi))
            {
                if (!proc.WaitForExit(900000)) // 15 dk
                {
                    try { proc.Kill(); } catch { }
                    context.Error("Derleme zaman aşımı (15 dk).");
                    return false;
                }
                if (File.Exists(logFile))
                {
                    foreach (string line in File.ReadAllLines(logFile))
                    {
                        if (!string.IsNullOrEmpty(line.Trim())) context.Log("  " + line.Trim());
                    }
                }
                if (proc.ExitCode != 0)
                {
                    context.Error("Veri tipleri derlenemedi (exit " + proc.ExitCode + "). Log'u kontrol edin.");
                    return false;
                }
            }

            // 4) Doğrula
            if (DllContainsExpectedTypes(context))
            {
                context.Log("  TAMAM: Composite.Generated.dll beklenen tipleri içeriyor.");
                // 5) TAZE site (context.Fresh) ise derleme adımı yarım kurulum durumu bırakır;
                //    hedefi temiz-fresh'e döndür ki kullanıcının ilk gerçek açılışında kurulum
                //    sihirbazı düzgün çalışsın. BAŞLATILMIŞ sitede mevcut içerik korunur;
                //    sıfırlama YAPILMAZ (admin, sayfalar, demo içerik silinmez).
                if (context.Fresh)
                {
                    ResetRuntimeState(context);
                }
                else
                {
                    context.Log("  Hedef başlatılmış bir site; mevcut içerik korundu (DataStores/Packages sıfırlanmadı).");
                }
                return true;
            }
            context.Error("Composite.Generated.dll üretilemedi ya da beklenen tipleri içermiyor.");
            return false;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - Hook (~/App_Code/DataTypeAutoInstaller.cs) + PendingDataTypes XML'leri");
            context.Log("  - IIS Express headless başlatılır; tipler DynamicTypeManager ile kaydedilir");
            context.Log("  - Zarif kapanış ile Composite.Generated.dll yeniden üretilir");
        }

        /// <summary>IIS Express'in yürütülebilir yolunu bulur; yoksa null.</summary>
        private static string FindIisExpress()
        {
            string[] candidates = new string[]
            {
                @"C:\Program Files (x86)\IIS Express\iisexpress.exe",
                @"C:\Program Files\IIS Express\iisexpress.exe"
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            return null;
        }

        /// <summary>Hedef bin\Composite.Generated.dll dosyasının manifest'teki tüm beklenen tipleri içerip içermediği.</summary>
        private static bool DllContainsExpectedTypes(SetupContext context)
        {
            if (context.Manifest.GeneratedTypes == null || context.Manifest.GeneratedTypes.Count == 0) return true;
            string dll = context.ResolveSite(Path.Combine("bin", "Composite.Generated.dll"));
            if (!File.Exists(dll)) return false;
            try
            {
                string binDir = Path.GetDirectoryName(dll);
                ResolveEventHandler handler = delegate (object sender, ResolveEventArgs e)
                {
                    try
                    {
                        string name = new AssemblyName(e.Name).Name + ".dll";
                        string p = Path.Combine(binDir, name);
                        return File.Exists(p) ? Assembly.LoadFrom(p) : null;
                    }
                    catch { return null; }
                };
                AppDomain.CurrentDomain.AssemblyResolve += handler;
                try
                {
                    // LoadFrom dosyayı KİLİTLER; byte dizisinden yükle (dosya değişebilir).
                    Assembly asm = Assembly.Load(File.ReadAllBytes(dll));
                    foreach (string t in context.Manifest.GeneratedTypes)
                    {
                        if (asm.GetType(t, false) == null) return false;
                    }
                    return true;
                }
                finally
                {
                    AppDomain.CurrentDomain.AssemblyResolve -= handler;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Derleme için başlatılan sitenin geride bıraktığı yarım kurulum durumunu temizler:
        /// runtime durum klasörleri boşaltılır (DLL korunur) ve PendingDataTypes XML'leri
        /// yeniden kopyalanır. Böylece hedef, tipleri içeren DLL ile birlikte "hiç başlatılmamış"
        /// gibi görünür ve ilk gerçek açılışta kurulum sihirbazı normal çalışır.
        /// </summary>
        private static void ResetRuntimeState(SetupContext context)
        {
            string[] dirs = new string[]
            {
                Path.Combine("App_Data", "Composite", "DataStores"),
                Path.Combine("App_Data", "Composite", "Packages"),
                Path.Combine("App_Data", "Composite", "Cache"),
                Path.Combine("App_Data", "Composite", "Log"),
                Path.Combine("App_Data", "Composite", "LogFiles"),
                Path.Combine("App_Data", "Composite", "ApplicationState"),
                Path.Combine("App_Data", "Composite", "Temp"),
                Path.Combine("App_Data", "Media"),
                Path.Combine("App_Data", "Composite", "C1AfterSetup")
            };
            foreach (string rel in dirs)
            {
                string p = context.ResolveSite(rel);
                if (Directory.Exists(p))
                {
                    try { Directory.Delete(p, true); } catch { }
                }
            }

            // C1'in başlangıçta beklediği boş klasörler
            Directory.CreateDirectory(context.ResolveSite(Path.Combine("App_Data", "Composite", "DataStores")));
            Directory.CreateDirectory(context.ResolveSite(Path.Combine("App_Data", "Composite", "Packages")));
            Directory.CreateDirectory(context.ResolveSite(Path.Combine("App_Data", "Composite", "Log")));
            Directory.CreateDirectory(context.ResolveSite(Path.Combine("App_Data", "Composite", "LogFiles")));

            // Hook tarafından tüketilen PendingDataTypes XML'lerini yeniden kopyala
            string src = context.ResolveSource("DataMetaData");
            string pending = context.ResolveSite(Path.Combine("App_Data", "Composite", "PendingDataTypes"));
            Directory.CreateDirectory(pending);
            if (Directory.Exists(src))
            {
                foreach (string f in Directory.GetFiles(src, "*.xml", SearchOption.TopDirectoryOnly))
                    File.Copy(f, Path.Combine(pending, Path.GetFileName(f)), true);
            }

            // C1, derleme IIS Express oturumu sırasında AutoInstallPackages'taki .c1pac'i TÜKETİR
            // (kurar ve Packages/installed altına taşır). Packages dizini yukarıda silinip
            // yeniden oluşturulduğu için .c1pac'i YENİDEN ÜRETMEK GEREKİR; aksi halde
            // kullanıcının ilk gerçek açılışında tipler kaybolur -> "Site under construction".
            RegenerateC1Pac(context);

            context.Log("  Derleme sonrası runtime durumu sıfırlandı (temiz-fresh). İlk gerçek açılışta kurulum sihirbazı normal çalışır;");
            context.Log("  hook, AuthKit store'larını oluşturur ve App_Code, DLL'deki tipler sayesinde derlenir.");
        }

        /// <summary>
        /// DataMetaData XML'lerinden .c1pac paketini yeniden üretir ve AutoInstallPackages'a yazar.
        /// Derleme IIS Express oturumu sırasında C1 paketi tüketmiş olabilir; bu yüzden reset sonrası
        /// paketin yeniden oluşturulması şarttır.
        /// </summary>
        private static void RegenerateC1Pac(SetupContext context)
        {
            string srcDir = context.ResolveSource("DataMetaData");
            if (!Directory.Exists(srcDir)) return;
            string[] files = Directory.GetFiles(srcDir, "*.xml", SearchOption.TopDirectoryOnly);
            if (files.Length == 0) return;

            string autoDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "AutoInstallPackages"));
            Directory.CreateDirectory(autoDir);

            // DeployPackageStep ile aynı install.xml üretimi
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<mi:PackageInstaller xmlns:mi=\"http://www.composite.net/ns/management/packageinstaller/1.0\">");
            sb.Append("<mi:PackageRequirements minimumCompositeVersion=\"2.0.0.0\" maximumCompositeVersion=\"9.9999.9999.9999\"/>");
            sb.Append("<mi:PackageInformation id=\"C1AFTERSETUP-DATATYPES-000000000001\" name=\"C1AfterSetup Data Types\" groupName=\"C1AfterSetup\" version=\"1.0.0\" author=\"C1AfterSetup\" website=\"\" canBeUninstalled=\"true\" systemLocking=\"none\">");
            sb.Append("<Description>Data types deployed by C1AfterSetup.</Description>");
            sb.Append("</mi:PackageInformation>");
            sb.Append("<mi:PackageFragmentInstallerBinaries/>");
            sb.Append("<mi:PackageFragmentInstallers>");
            sb.Append("<mi:Add installerType=\"Composite.Core.PackageSystem.PackageFragmentInstallers.DynamicDataTypePackageFragmentInstaller, Composite\" ");
            sb.Append("uninstallerType=\"Composite.Core.PackageSystem.PackageFragmentInstallers.DynamicDataTypePackageFragmentUninstaller, Composite\">");
            sb.Append("<Types>");
            foreach (string file in files)
            {
                string descriptor = File.ReadAllText(file);
                string escaped = SecurityElement.Escape(descriptor);
                sb.Append("<Type providerName=\"GeneratedDataTypesElementProvider\" dataTypeDescriptor=\"")
                  .Append(escaped)
                  .Append("\"/>");
            }
            sb.Append("</Types>");
            sb.Append("</mi:Add>");
            sb.Append("</mi:PackageFragmentInstallers>");
            sb.Append("</mi:PackageInstaller>");

            string tmpDir = Path.Combine(Path.GetTempPath(), "c1aftersetup-pkg-reset-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "install.xml"), sb.ToString(), new UTF8Encoding(false));
            string c1pac = Path.Combine(autoDir, "C1AfterSetup-DataTypes.c1pac");
            if (File.Exists(c1pac)) File.Delete(c1pac);
            ZipFile.CreateFromDirectory(tmpDir, c1pac);
            Directory.Delete(tmpDir, true);

            context.Log("  .c1pac yeniden üretildi: " + c1pac + " (" + files.Length + " tip)");
        }

        /// <summary>Headless derleme script'i: siteyi başlatır, tiplerin kaydını ve DLL yazımını bekler.</summary>
        private static string BuildCompileScript()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("param([string]$SitePath, [int]$Port = 8090, [int]$TimeoutSeconds = 420, [string]$LogFile = '')");
            sb.AppendLine("function Log($m) { Write-Host $m; if ($LogFile) { try { Add-Content -Path $LogFile -Value $m -Encoding UTF8 } catch { } } }");
            sb.AppendLine("$ErrorActionPreference = 'Continue'");
            sb.AppendLine("$iis = 'C:\\Program Files (x86)\\IIS Express\\iisexpress.exe'");
            sb.AppendLine("if (-not (Test-Path $iis)) { $iis = 'C:\\Program Files\\IIS Express\\iisexpress.exe' }");
            sb.AppendLine("if (-not (Test-Path $iis)) { Log 'HATA: IIS Express yok'; exit 2 }");
            sb.AppendLine("$dll = Join-Path $SitePath 'bin\\Composite.Generated.dll'");
            sb.AppendLine("$before = if (Test-Path $dll) { (Get-Item $dll).LastWriteTimeUtc } else { [DateTime]::MinValue }");
            sb.AppendLine("$dsDir = Join-Path $SitePath 'App_Data\\Composite\\DataStores'");
            sb.AppendLine("");
            sb.AppendLine("# ===== FAZ 1: Tipleri kaydet (IIS Express baslat -> hook calisir -> store'lar olusur) =====");
            sb.AppendLine("$proc = Start-Process -FilePath $iis -ArgumentList \"/path:`\"$SitePath`\"\",\"/port:$Port\" -WindowStyle Hidden -PassThru");
            sb.AppendLine("try {");
            sb.AppendLine("  $deadline = (Get-Date).AddSeconds(180)");
            sb.AppendLine("  while ((Get-Date) -lt $deadline) { try { $null = Invoke-WebRequest -UseBasicParsing -Uri \"http://localhost:$Port/\" -TimeoutSec 5; break } catch { } ; Start-Sleep -Seconds 3 }");
            sb.AppendLine("  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)");
            sb.AppendLine("  $registered = $false");
            sb.AppendLine("  while ((Get-Date) -lt $deadline) {");
            sb.AppendLine("    $authKit = if (Test-Path $dsDir) { (Get-ChildItem $dsDir -Filter 'AuthKit*.xml' -ErrorAction SilentlyContinue | Measure-Object).Count } else { 0 }");
            sb.AppendLine("    if ($authKit -ge 1) { $registered = $true; Log ('Tipler kaydedildi: ' + $authKit + ' store'); break }");
            sb.AppendLine("    Start-Sleep -Seconds 5");
            sb.AppendLine("  }");
            sb.AppendLine("  if (-not $registered) { Log 'HATA: tipler kaydedilmedi'; exit 1 }");
            sb.AppendLine("}");
            sb.AppendLine("finally { if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } }");
            sb.AppendLine("");
            sb.AppendLine("# ===== FAZ 2: DLL yenile (yeni IIS Express -> recycle -> C1 DLL'i gunceller) =====");
            sb.AppendLine("# (Ayni oturumda IIS Express, yukledigi DLL'i kilitleyip yeniden yazamaz; ayri oturum gerekir.)");
            sb.AppendLine("$proc = Start-Process -FilePath $iis -ArgumentList \"/path:`\"$SitePath`\"\",\"/port:$Port\" -WindowStyle Hidden -PassThru");
            sb.AppendLine("try {");
            sb.AppendLine("  try { $null = Invoke-WebRequest -UseBasicParsing -Uri \"http://localhost:$Port/\" -TimeoutSec 10 } catch { }");
            sb.AppendLine("  Add-Content -Path (Join-Path $SitePath 'Web.config') -Value \"`n<!-- C1AfterSetup recycle -->\" -Encoding UTF8");
            sb.AppendLine("  $deadline = (Get-Date).AddSeconds($TimeoutSeconds)");
            sb.AppendLine("  $dllOk = $false");
            sb.AppendLine("  while ((Get-Date) -lt $deadline) {");
            sb.AppendLine("    if (Test-Path $dll) {");
            sb.AppendLine("      $t = (Get-Item $dll).LastWriteTimeUtc");
            sb.AppendLine("      if ($t -gt $before) { $dllOk = $true; Log ('DLL yazildi: ' + $t + ' (' + (Get-Item $dll).Length + ' bytes)'); break }");
            sb.AppendLine("    }");
            sb.AppendLine("    Start-Sleep -Seconds 4");
            sb.AppendLine("  }");
            sb.AppendLine("  if (-not $dllOk) { Log 'HATA: DLL yazilmadi'; exit 1 }");
            sb.AppendLine("  Log 'TAMAM'");
            sb.AppendLine("  exit 0");
            sb.AppendLine("}");
            sb.AppendLine("finally { if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } }");
            return sb.ToString();
        }
    }
}
