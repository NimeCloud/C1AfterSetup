using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace C1AfterSetup
{
    /// <summary>Manifest içindeki tek bir DataMetaData tipi.</summary>
    public class DataTypeEntry
    {
        /// <summary>Kaynak dosya adı / glob örneği: "Group *.xml".</summary>
        public string File { get; set; }

        /// <summary>Bağımlılık grubu. Önce eklenmesi gereken parent tipler düşük gruplarda (A, B, C...).</summary>
        public string Group { get; set; }

        /// <summary>Bu tipin FK ile bağlı olduğu parent tip adları (yönetici için bilgi amaçlı).</summary>
        public List<string> DependsOn { get; set; }

        public DataTypeEntry()
        {
            Group = "Z";
            DependsOn = new List<string>();
        }
    }

    /// <summary>Manifest içindeki tek bir sayfa şablonu.</summary>
    public class TemplateEntry
    {
        public string File { get; set; }
        public int Order { get; set; }
    }

    /// <summary>Kaynak -> hedef eşlemesi.</summary>
    public class FileMapping
    {
        public string Source { get; set; }
        public string Target { get; set; }
    }

    public class AppCodeSettings
    {
        public List<FileMapping> Items { get; set; }

        public AppCodeSettings()
        {
            Items = new List<FileMapping>();
        }
    }

    public class ModuleEntry
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class WebConfigSettings
    {
        public bool RemoveServerHeader { get; set; }
        public List<string> RemoveCustomHeaders { get; set; }
        public List<ModuleEntry> AddModules { get; set; }

        /// <summary>
        /// App_Code derlemesinin referans vermesi gereken framework assembly'leri (tam nitelikli adlar).
        /// Web.config'in <system.web>/<compilation>/<assemblies> bölümüne eksikse eklenir.
        /// Örnek: "System.Net.Http, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
        /// </summary>
        public List<string> AssemblyReferences { get; set; }

        public WebConfigSettings()
        {
            RemoveCustomHeaders = new List<string>();
            AddModules = new List<ModuleEntry>();
            AssemblyReferences = new List<string>();
        }
    }

    /// <summary>
    /// setup.manifest.json modeli. Yeni bileşenler buraya eklenerek pipeline'a girer.
    /// (Bağımlılıksız: System.Web.Script.Serialization.JavaScriptSerializer kullanır.)
    /// </summary>
    public class SetupManifest
    {
        public List<DataTypeEntry> DataTypes { get; set; }
        public AppCodeSettings AppCode { get; set; }
        public List<TemplateEntry> Templates { get; set; }
        public List<FileMapping> Razor { get; set; }
        public List<string> BinDependencies { get; set; }
        public WebConfigSettings WebConfig { get; set; }

        public SetupManifest()
        {
            DataTypes = new List<DataTypeEntry>();
            AppCode = new AppCodeSettings();
            Templates = new List<TemplateEntry>();
            Razor = new List<FileMapping>();
            BinDependencies = new List<string>();
            WebConfig = new WebConfigSettings();
        }

        public static SetupManifest Load(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Manifest bulunamadı: " + path);
            string json = File.ReadAllText(path);
            var serializer = new JavaScriptSerializer();
            return serializer.Deserialize<SetupManifest>(json);
        }
    }
}
