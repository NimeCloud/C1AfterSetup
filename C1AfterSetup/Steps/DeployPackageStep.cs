using System;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// DataMetaData tiplerini bir C1 paketine (.c1pac) sarar ve ~/App_Data/Composite/AutoInstallPackages
    /// dizinine yazar.
    ///
    /// Neden paket? C1 CMS, elle atılan DataMetaData XML'lerinden başlangıçta Composite.Generated.dll'i
    /// yeniden ÜRETMEZ; kod üretimi bir C1 paketi kurulurken tetiklenir.
    ///
    /// Davranış:
    ///  - TAZE site (henüz hiç açılmamış): C1 ilk açılışında AutoInstallPackages'taki paketi kurar
    ///    ve tipleri üretir — sıfır manuel adım.
    ///  - KURULU site: paket kurulumu AutoInstallPackages'tan tetiklenmez; .c1pac dosyası
    ///    C1 Console → Packages → Install üzerinden bir kez kurulur (C1 mimarisinin tek manuel adımı).
    /// </summary>
    public class DeployPackageStep : ISetupStep
    {
        private const string PackageC1PacName = "C1AfterSetup-DataTypes.c1pac";
        private const string PackageId = "C1AFTERSETUP-DATATYPES-000000000001";

        public string Name
        {
            get { return "C1 Veri Tipi Paketi (.c1pac)"; }
        }

        public bool Verify(SetupContext context)
        {
            // .c1pac mevcut VE yapısal olarak geçerliyse güncel say; değilse yeniden yaz.
            string c1pac = Path.Combine(
                context.ResolveSite(Path.Combine("App_Data", "Composite", "AutoInstallPackages")),
                PackageC1PacName);
            string error;
            return ValidateC1Pac(c1pac, out error);
        }

        public string Fingerprint(SetupContext context)
        {
            return FileSyncUtil.SourceFingerprint(context.ResolveSource("DataMetaData"));
        }

        public bool Execute(SetupContext context)
        {
            string srcDir = context.ResolveSource("DataMetaData");
            if (!Directory.Exists(srcDir))
            {
                context.Log("sources/DataMetaData yok, paket atlanıyor.");
                return true;
            }

            string[] files = Directory.GetFiles(srcDir, "*.xml", SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                context.Log("sources/DataMetaData boş, paket atlanıyor.");
                return true;
            }

            // 1) install.xml içeriğini üret
            string installXml = BuildInstallXml(files);

            // 2) Geçici klasöre install.xml yaz, .c1pac (zip) üret, AutoInstallPackages'a koy
            string autoDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "AutoInstallPackages"));
            Directory.CreateDirectory(autoDir);

            string tmpDir = Path.Combine(Path.GetTempPath(), "c1aftersetup-pkg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "install.xml"), installXml, new UTF8Encoding(false));

            string c1pac = Path.Combine(autoDir, PackageC1PacName);
            if (File.Exists(c1pac)) File.Delete(c1pac);
            ZipFile.CreateFromDirectory(tmpDir, c1pac);
            Directory.Delete(tmpDir, true);

            // Üretilen paketi doğrula (yapı + install.xml + tip tanımları).
            string validateError;
            if (!ValidateC1Pac(c1pac, out validateError))
            {
                context.Error("Üretilen .c1pac geçersiz: " + validateError);
                return false;
            }

            context.Log("C1 veri tipi paketi yazıldı ve doğrulandı: " + c1pac + " (" + files.Length + " tip)");
            context.Log("  - TAZE site (hiç açılmamış / -fresh): C1 ilk açılışında bu paketi AutoInstallPackages'tan kurar, tipleri üretir.");
            context.Log("  - KURULU site: C1 Console → Packages → Install → " + c1pac + " (tek manuel adım).");
            return true;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - DataMetaData tipleri bir C1 paketine (.c1pac) sarılıp AutoInstallPackages dizinine yazılacak");
            context.Log("  - Taze sitede C1 ilk açılışta kurar; kurulu sitede Console'dan bir kez kurulur");
        }

        /// <summary>
        /// .c1pac dosyasının geçerli olduğunu doğrular: dosya mevcut, zip içinde install.xml
        /// var, install.xml DynamicDataTypePackageFragmentInstaller fragment'ı ve en az bir
        /// <Type> tanımı içeriyor.
        /// </summary>
        private static bool ValidateC1Pac(string c1pacPath, out string error)
        {
            error = null;
            if (!File.Exists(c1pacPath))
            {
                error = "Paket dosyası yok: " + c1pacPath;
                return false;
            }
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(c1pacPath))
                {
                    ZipArchiveEntry entry = zip.GetEntry("install.xml");
                    if (entry == null)
                    {
                        error = "install.xml bulunamadı (bozuk paket).";
                        return false;
                    }
                    using (StreamReader reader = new StreamReader(entry.Open()))
                    {
                        string content = reader.ReadToEnd();
                        if (content.IndexOf("DynamicDataTypePackageFragmentInstaller", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            error = "install.xml'de DynamicDataTypePackageFragmentInstaller yok.";
                            return false;
                        }
                        if (content.IndexOf("<Type ", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            error = "install.xml'de hiç <Type> tanımı yok.";
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "Paket okunamadı: " + ex.Message;
                return false;
            }
        }

        private static string BuildInstallXml(string[] dataTypeFiles)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            sb.Append("<mi:PackageInstaller xmlns:mi=\"http://www.composite.net/ns/management/packageinstaller/1.0\">");
            sb.Append("<mi:PackageRequirements minimumCompositeVersion=\"2.0.0.0\" maximumCompositeVersion=\"9.9999.9999.9999\"/>");
            sb.Append("<mi:PackageInformation id=\"").Append(PackageId)
              .Append("\" name=\"C1AfterSetup Data Types\" groupName=\"C1AfterSetup\" version=\"1.0.0\" author=\"C1AfterSetup\" website=\"\" canBeUninstalled=\"true\" systemLocking=\"none\">")
              .Append("<Description>Data types deployed by C1AfterSetup.</Description>")
              .Append("</mi:PackageInformation>");
            sb.Append("<mi:PackageFragmentInstallerBinaries/>");
            sb.Append("<mi:PackageFragmentInstallers>");
            sb.Append("<mi:Add installerType=\"Composite.Core.PackageSystem.PackageFragmentInstallers.DynamicDataTypePackageFragmentInstaller, Composite\" ")
              .Append("uninstallerType=\"Composite.Core.PackageSystem.PackageFragmentInstallers.DynamicDataTypePackageFragmentUninstaller, Composite\">");
            sb.Append("<Types>");

            foreach (string file in dataTypeFiles)
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
            return sb.ToString();
        }
    }
}
