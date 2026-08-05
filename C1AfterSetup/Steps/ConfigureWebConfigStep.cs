using System;
using System.IO;
using System.Text;
using System.Xml;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Web.config'e gereksiz HTTP response header'larını temizleyen ayarları güvenle ekler:
    ///   - requestFiltering removeServerHeader="true"
    ///   - httpProtocol/customHeaders: clear + istenen header'ları remove
    ///   - modules: HeaderCleanupModule kaydı
    /// XmlDocument (PreserveWhitespace) kullanır; C1'in yorumlarına ve mevcut ayarlara dokunmaz.
    /// (C# 5 uyumlu.)
    /// </summary>
    public class ConfigureWebConfigStep : ISetupStep
    {
        public string Name
        {
            get { return "Web.config Header Temizliği"; }
        }

        public bool Verify(SetupContext context)
        {
            string webConfigPath = context.ResolveSite("Web.config");
            if (!File.Exists(webConfigPath)) return false;
            try
            {
                var doc = new XmlDocument();
                doc.PreserveWhitespace = true;
                doc.Load(webConfigPath);
                return IsConfigured(doc, context);
            }
            catch
            {
                return false;
            }
        }

        public string Fingerprint(SetupContext context)
        {
            var cfg = context.Manifest.WebConfig;
            StringBuilder sb = new StringBuilder();
            sb.Append(cfg.RemoveServerHeader).Append('|');
            if (cfg.RemoveCustomHeaders != null)
                sb.Append(string.Join(",", cfg.RemoveCustomHeaders.ToArray()));
            sb.Append('|');
            if (cfg.AddModules != null)
            {
                foreach (var m in cfg.AddModules)
                    sb.Append(m.Name).Append('=').Append(m.Type).Append(';');
            }
            return FileSyncUtil.HashText(sb.ToString());
        }

        public bool Execute(SetupContext context)
        {
            var cfg = context.Manifest.WebConfig;
            string webConfigPath = context.ResolveSite("Web.config");
            if (!File.Exists(webConfigPath))
            {
                context.Error("Web.config bulunamadı.");
                return false;
            }

            bool changed = false;
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(webConfigPath);

            XmlElement sysWebServer = GetOrCreate(doc.DocumentElement, "system.webServer");

            // 1) removeServerHeader
            if (cfg.RemoveServerHeader)
            {
                XmlElement security = GetOrCreate(sysWebServer, "security");
                XmlElement requestFiltering = GetOrCreate(security, "requestFiltering");
                if (SetAttribute(requestFiltering, "removeServerHeader", "true"))
                {
                    context.Log("  + requestFiltering removeServerHeader=true");
                    changed = true;
                }
            }

            // 2) httpProtocol/customHeaders: clear + remove belirtilen header'lar
            if (cfg.RemoveCustomHeaders != null && cfg.RemoveCustomHeaders.Count > 0)
            {
                XmlElement httpProtocol = GetOrCreate(sysWebServer, "httpProtocol");
                XmlElement customHeaders = GetOrCreate(httpProtocol, "customHeaders");

                if (customHeaders.SelectNodes("clear").Count == 0)
                {
                    XmlElement clearEl = doc.CreateElement("clear");
                    customHeaders.PrependChild(clearEl);
                    changed = true;
                }

                foreach (string header in cfg.RemoveCustomHeaders)
                {
                    bool exists = HasRemove(customHeaders, header);
                    if (!exists)
                    {
                        XmlElement removeEl = doc.CreateElement("remove");
                        removeEl.SetAttribute("name", header);
                        customHeaders.AppendChild(removeEl);
                        changed = true;
                    }
                }
                if (changed)
                    context.Log("  + customHeaders clear/remove " + string.Join(", ", cfg.RemoveCustomHeaders.ToArray()));
            }

            // 3) modules: HeaderCleanupModule ekle
            if (cfg.AddModules != null && cfg.AddModules.Count > 0)
            {
                XmlElement modules = GetOrCreate(sysWebServer, "modules");
                foreach (var m in cfg.AddModules)
                {
                    if (!HasAdd(modules, m.Name))
                    {
                        XmlElement addEl = doc.CreateElement("add");
                        addEl.SetAttribute("name", m.Name);
                        if (!string.IsNullOrWhiteSpace(m.Type)) addEl.SetAttribute("type", m.Type);
                        modules.AppendChild(addEl);
                        context.Log("  + modules add " + m.Name);
                        changed = true;
                    }
                }
            }

            // 4) system.codedom: Roslyn compiler (Microsoft.CodeDom.Providers.DotNetCompilerPlatform)
            //    C1 CMS eski Roslyn ile gelir (C# 5). Yeni compiler ile C# 6+ destegi saglanir.
            if (context.Manifest.RoslynEnabled)
            {
                XmlElement codedom = GetOrCreate(doc.DocumentElement, "system.codedom");
                XmlElement compilers = GetOrCreate(codedom, "compilers");
                const string roslynTypePrefix =
                    "Microsoft.CodeDom.Providers.DotNetCompilerPlatform.";

                if (!HasCompiler(compilers, "c#"))
                {
                    XmlElement csEl = doc.CreateElement("compiler");
                    csEl.SetAttribute("language", "c#;cs;csharp");
                    csEl.SetAttribute("extension", ".cs");
                    csEl.SetAttribute("type", roslynTypePrefix +
                        "CSharpCodeProvider, Microsoft.CodeDom.Providers.DotNetCompilerPlatform, " +
                        "Version=2.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    csEl.SetAttribute("warningLevel", "4");
                    csEl.SetAttribute("compilerOptions", "/langversion:default /nowarn:1659;1699;1701");
                    compilers.AppendChild(csEl);
                    context.Log("  + system.codedom compiler C# (Roslyn)");
                    changed = true;
                }

                if (!HasCompiler(compilers, "vb"))
                {
                    XmlElement vbEl = doc.CreateElement("compiler");
                    vbEl.SetAttribute("language", "vb;vbs;visualbasic;vbscript");
                    vbEl.SetAttribute("extension", ".vb");
                    vbEl.SetAttribute("type", roslynTypePrefix +
                        "VBCodeProvider, Microsoft.CodeDom.Providers.DotNetCompilerPlatform, " +
                        "Version=2.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                    vbEl.SetAttribute("warningLevel", "4");
                    vbEl.SetAttribute("compilerOptions",
                        "/langversion:default /nowarn:41008 /define:_MYTYPE=\\\"Web\\\" " +
                        "/imports:Microsoft.VisualBasic,System,System.Collections," +
                        "System.Collections.Specialized,System.Configuration,System.Text," +
                        "System.Text.RegularExpressions,System.Web,System.Web.Caching," +
                        "System.Web.SessionState,System.Web.Security,System.Web.Profile," +
                        "System.Web.UI,System.Web.UI.WebControls,System.Web.UI.WebControls.WebParts," +
                        "System.Web.UI.HtmlControls");
                    compilers.AppendChild(vbEl);
                    context.Log("  + system.codedom compiler VB (Roslyn)");
                    changed = true;
                }
            }

            // 5) compilation/assemblies: App_Code'un ihtiyaç duyduğu framework referansları
            //    (örn. System.Net.Http) Web.config'te eksikse eklenir.
            if (cfg.AssemblyReferences != null && cfg.AssemblyReferences.Count > 0)
            {
                XmlElement sysWeb = GetOrCreate(doc.DocumentElement, "system.web");
                XmlElement compilation = GetOrCreate(sysWeb, "compilation");
                XmlElement assemblies = GetOrCreate(compilation, "assemblies");
                foreach (string asm in cfg.AssemblyReferences)
                {
                    if (!HasAssembly(assemblies, asm))
                    {
                        XmlElement addEl = doc.CreateElement("add");
                        addEl.SetAttribute("assembly", asm);
                        assemblies.AppendChild(addEl);
                        context.Log("  + compilation assemblies add " + asm);
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                doc.Save(webConfigPath);
                context.Log("  Web.config güncellendi.");
            }
            else
            {
                context.Log("  Web.config zaten istenen durumda (değişiklik gerekmedi).");
            }
            return true;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - requestFiltering removeServerHeader=true");
            context.Log("  - httpProtocol/customHeaders clear/remove");
            context.Log("  - modules HeaderCleanupModule kaydı");
        }

        private static XmlElement GetOrCreate(XmlElement parent, string name)
        {
            XmlElement el = parent[name];
            if (el == null)
            {
                el = parent.OwnerDocument.CreateElement(name);
                parent.AppendChild(el);
            }
            return el;
        }

        private static bool SetAttribute(XmlElement el, string name, string value)
        {
            XmlAttribute attr = el.Attributes[name];
            if (attr == null)
            {
                el.SetAttribute(name, value);
                return true;
            }
            if (attr.Value != value)
            {
                attr.Value = value;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Web.config'in manifest'teki TÜM istenen ayarları zaten içerip içermediğini kontrol eder.
        /// True ise değişiklik gerekmez (Verify için).
        /// </summary>
        private static bool IsConfigured(XmlDocument doc, SetupContext context)
        {
            var cfg = context.Manifest.WebConfig;
            var sysWebServer = doc.DocumentElement["system.webServer"];
            if (sysWebServer == null) return false;

            if (cfg.RemoveServerHeader)
            {
                var security = sysWebServer["security"];
                var rf = security != null ? security["requestFiltering"] : null;
                if (rf == null || rf.Attributes["removeServerHeader"] == null || rf.Attributes["removeServerHeader"].Value != "true")
                    return false;
            }

            if (cfg.RemoveCustomHeaders != null && cfg.RemoveCustomHeaders.Count > 0)
            {
                var httpProtocol = sysWebServer["httpProtocol"];
                var customHeaders = httpProtocol != null ? httpProtocol["customHeaders"] : null;
                if (customHeaders == null || customHeaders.SelectNodes("clear").Count == 0) return false;
                foreach (string header in cfg.RemoveCustomHeaders)
                {
                    if (!HasRemove(customHeaders, header)) return false;
                }
            }

            if (cfg.AddModules != null && cfg.AddModules.Count > 0)
            {
                var modules = sysWebServer["modules"];
                if (modules == null) return false;
                foreach (var m in cfg.AddModules)
                {
                    if (!HasAdd(modules, m.Name)) return false;
                }
            }

            if (cfg.AssemblyReferences != null && cfg.AssemblyReferences.Count > 0)
            {
                var sysWeb = doc.DocumentElement["system.web"];
                var compilation = sysWeb != null ? sysWeb["compilation"] : null;
                var assemblies = compilation != null ? compilation["assemblies"] : null;
                if (assemblies == null) return false;
                foreach (string asm in cfg.AssemblyReferences)
                {
                    if (!HasAssembly(assemblies, asm)) return false;
                }
            }

            return true;
        }

        private static bool HasAssembly(XmlElement assemblies, string assemblyName)
        {
            foreach (XmlNode n in assemblies.SelectNodes("add"))
            {
                if (n.Attributes != null && n.Attributes["assembly"] != null && n.Attributes["assembly"].Value == assemblyName)
                    return true;
            }
            return false;
        }

        private static bool HasRemove(XmlElement customHeaders, string name)
        {
            foreach (XmlNode n in customHeaders.SelectNodes("remove"))
            {
                if (n.Attributes != null && n.Attributes["name"] != null && n.Attributes["name"].Value == name)
                    return true;
            }
            return false;
        }

        private static bool HasAdd(XmlElement modules, string name)
        {
            foreach (XmlNode n in modules.SelectNodes("add"))
            {
                if (n.Attributes != null && n.Attributes["name"] != null && n.Attributes["name"].Value == name)
                    return true;
            }
            return false;
        }

        private static bool HasCompiler(XmlElement compilers, string languagePrefix)
        {
            foreach (XmlNode n in compilers.SelectNodes("compiler"))
            {
                if (n.Attributes != null && n.Attributes["language"] != null
                    && n.Attributes["language"].Value.StartsWith(languagePrefix))
                    return true;
            }
            return false;
        }
    }
}
