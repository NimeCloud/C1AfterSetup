using System;
using System.Collections.Generic;
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
            //    Her zaman gunceller (versiyon uyusmazligi durumunda eski kayitlari temizler).
            if (context.Manifest.RoslynEnabled)
            {
                XmlElement codedom = GetOrCreate(doc.DocumentElement, "system.codedom");
                XmlElement compilers = GetOrCreate(codedom, "compilers");
                const string roslynTypePrefix =
                    "Microsoft.CodeDom.Providers.DotNetCompilerPlatform.";
                const string roslynAsm =
                    "Microsoft.CodeDom.Providers.DotNetCompilerPlatform, " +
                    "Version=2.0.1.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35";

                // Eski (yanlis versiyonlu) compiler kayitlarini temizle
                var oldCompilers = new List<XmlNode>();
                foreach (XmlNode n in compilers.SelectNodes("compiler"))
                {
                    if (n.Attributes != null && n.Attributes["type"] != null
                        && n.Attributes["type"].Value.Contains("DotNetCompilerPlatform"))
                    {
                        oldCompilers.Add(n);
                    }
                }
                foreach (var n in oldCompilers) { compilers.RemoveChild(n); changed = true; }

                // C# compiler ekle
                XmlElement csEl2 = doc.CreateElement("compiler");
                csEl2.SetAttribute("language", "c#;cs;csharp");
                csEl2.SetAttribute("extension", ".cs");
                csEl2.SetAttribute("type", roslynTypePrefix + "CSharpCodeProvider, " + roslynAsm);
                csEl2.SetAttribute("warningLevel", "4");
                csEl2.SetAttribute("compilerOptions", "/langversion:default /nowarn:1659;1699;1701");
                compilers.AppendChild(csEl2);
                context.Log("  + system.codedom compiler C# (Roslyn)");
                changed = true;

                // VB compiler ekle
                XmlElement vbEl2 = doc.CreateElement("compiler");
                vbEl2.SetAttribute("language", "vb;vbs;visualbasic;vbscript");
                vbEl2.SetAttribute("extension", ".vb");
                vbEl2.SetAttribute("type", roslynTypePrefix + "VBCodeProvider, " + roslynAsm);
                vbEl2.SetAttribute("warningLevel", "4");
                vbEl2.SetAttribute("compilerOptions",
                    "/langversion:default /nowarn:41008 /define:_MYTYPE=\\\"Web\\\" " +
                    "/imports:Microsoft.VisualBasic,System,System.Collections," +
                    "System.Collections.Specialized,System.Configuration,System.Text," +
                    "System.Text.RegularExpressions,System.Web,System.Web.Caching," +
                    "System.Web.SessionState,System.Web.Security,System.Web.Profile," +
                    "System.Web.UI,System.Web.UI.WebControls,System.Web.UI.WebControls.WebParts," +
                    "System.Web.UI.HtmlControls");
                compilers.AppendChild(vbEl2);
                context.Log("  + system.codedom compiler VB (Roslyn)");
                changed = true;
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

            // 6) AuthKit-relevant NuGet package binding redirects.
            //    Reference site (SystemC1) pins these exact versions in its Web.config. C1's own
            //    components (e.g. WampRouter) were built against OLD versions (Newtonsoft.Json 6.0.0.0),
            //    so without redirects a FileLoadException occurs at startup. Redirect 0.0.0.0 → newVersion.
            {
                XmlElement runtime = GetOrCreate(doc.DocumentElement, "runtime");
                XmlElement assemblyBinding = GetOrCreate(runtime, "assemblyBinding",
                    "urn:schemas-microsoft-com:asm.v1");

                var redirects = new[]
                {
                    new { Name = "Newtonsoft.Json", Token = "30ad4fe6b2a6aeed", New = "13.0.0.0" },
                    new { Name = "System.Memory", Token = "cc7b13ffcd2ddd51", New = "4.0.5.0" },
                    new { Name = "System.ValueTuple", Token = "cc7b13ffcd2ddd51", New = "4.0.3.0" },
                    new { Name = "System.Buffers", Token = "cc7b13ffcd2ddd51", New = "4.0.5.0" },
                    new { Name = "System.Runtime.CompilerServices.Unsafe", Token = "b03f5f7f11d50a3a", New = "6.0.3.0" },
                    new { Name = "System.Threading.Tasks.Extensions", Token = "cc7b13ffcd2ddd51", New = "4.2.1.0" },
                    new { Name = "Owin", Token = "f0ebd12fd5e55cc5", New = "1.0.0.0" },
                    new { Name = "Microsoft.Owin", Token = "31bf3856ad364e35", New = "4.2.3.0" },
                    new { Name = "Microsoft.Owin.Host.SystemWeb", Token = "31bf3856ad364e35", New = "4.2.3.0" },
                    new { Name = "Microsoft.Owin.Hosting", Token = "31bf3856ad364e35", New = "4.2.3.0" }
                };

                foreach (var r in redirects)
                {
                    RemoveDependentAssembly(assemblyBinding, r.Name);

                    XmlElement da = doc.CreateElement("dependentAssembly", "urn:schemas-microsoft-com:asm.v1");
                    XmlElement ai = doc.CreateElement("assemblyIdentity", "urn:schemas-microsoft-com:asm.v1");
                    ai.SetAttribute("name", r.Name);
                    ai.SetAttribute("publicKeyToken", r.Token);
                    ai.SetAttribute("culture", "neutral");
                    da.AppendChild(ai);

                    XmlElement br = doc.CreateElement("bindingRedirect", "urn:schemas-microsoft-com:asm.v1");
                    br.SetAttribute("oldVersion", "0.0.0.0-" + r.New);
                    br.SetAttribute("newVersion", r.New);
                    da.AppendChild(br);

                    assemblyBinding.AppendChild(da);
                    context.Log("  + bindingRedirect " + r.Name + " 0.0.0.0-" + r.New + " → " + r.New);
                    changed = true;
                }
            }

            // Redirect'ler eklenmiş olabilir; değişiklikleri diske yaz.
            if (changed)
            {
                doc.Save(webConfigPath);
                context.Log("  Web.config güncellendi (binding redirects).");
            }

            // 6b) OWIN başlatma sınıfı açıkça belirtilir.
            //     Microsoft.Owin.Host.SystemWeb, OwinHttpModule'u PreApplicationStartMethod ile kaydeder;
            //     belirtilen bir Startup sınıfı yoksa app init'te EntryPointNotFoundException fırlatır ve
            //     uygulama sonsuz recycle döngüsüne girer (referans sitede ForOwinStartup.dll vardı).
            //     App_Code'a dağıtılan global "Startup" sınıfını owin:AppStartup ile sabitliyoruz.
            {
                XmlElement appSettings = GetOrCreate(doc.DocumentElement, "appSettings");
                bool hasOwinKey = false;
                foreach (XmlNode n in appSettings.ChildNodes)
                {
                    if (n is XmlElement && n.LocalName == "add")
                    {
                        XmlElement addEl = (XmlElement)n;
                        if (addEl.Attributes["key"] != null
                            && addEl.Attributes["key"].Value == "owin:AppStartup")
                        {
                            hasOwinKey = true;
                            if (addEl.Attributes["value"] == null
                                || addEl.Attributes["value"].Value != "Startup")
                            {
                                addEl.SetAttribute("value", "Startup");
                                changed = true;
                                context.Log("  = appSettings owin:AppStartup=Startup (güncellendi)");
                            }
                        }
                    }
                }
                if (!hasOwinKey)
                {
                    XmlElement addEl = doc.CreateElement("add");
                    addEl.SetAttribute("key", "owin:AppStartup");
                    addEl.SetAttribute("value", "Startup");
                    appSettings.AppendChild(addEl);
                    context.Log("  + appSettings owin:AppStartup=Startup");
                    changed = true;
                }
            }

            // OWIN anahtarı (ve önceki bölümlerdeki değişiklikler) dahil son durumu diske yaz.
            if (changed)
            {
                doc.Save(webConfigPath);
                context.Log("  Web.config güncellendi (OWIN appSettings + diğer değişiklikler).");
            }

            // 7) Global.asax: API route'larini RegisterRoutes'a ekle
            //    (auth/login, api/time vb. endpoint'lerin çalışması için gerekli)
            PatchGlobalAsaxRoutes(context);

            return true;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - requestFiltering removeServerHeader=true");
            context.Log("  - httpProtocol/customHeaders clear/remove");
            context.Log("  - modules HeaderCleanupModule kaydı");
        }

        /// <summary>
        /// Global.asax RegisterRoutes metoduna API route'larini ekler.
        /// Idempotent: "AuthApi" route'u zaten varsa tekrar eklemez.
        /// </summary>
        private static void PatchGlobalAsaxRoutes(SetupContext context)
        {
            string globalAsaxPath = context.ResolveSite("Global.asax");
            if (!File.Exists(globalAsaxPath))
            {
                context.Warn("  Global.asax bulunamadı, API route'ları eklenemedi.");
                return;
            }

            string content = File.ReadAllText(globalAsaxPath, Encoding.UTF8);

            // Idempotent: zaten AuthApi route'u varsa atla
            if (content.Contains("AuthApi"))
            {
                context.Log("  = Global.asax API route'ları zaten mevcut.");
                return;
            }

            // Routes.RegisterPageRoute(routes); satırından ÖNCE 3 route'u ekle
            string marker = "Routes.RegisterPageRoute(routes);";
            int idx = content.IndexOf(marker);
            if (idx < 0)
            {
                context.Warn("  Global.asax'ta RegisterPageRoute bulunamadı, API route'ları eklenemedi.");
                return;
            }

            string routeBlock = @"
        // Modern REST API routes — registered BEFORE C1 page routes
        // Auth route FIRST — more specific, must match before generic api/{action}
        routes.Add(""AuthApi"", new Route(""api/auth/{action}"", new RouteValueDictionary(), new RouteValueDictionary(), new AuthRouteHandler()));
        routes.Add(""Api"", new Route(""api/{action}"", new RouteValueDictionary(), new RouteValueDictionary(), new ApiRouteHandler()));
        routes.Add(""ApiWithName"", new Route(""api/{action}/{name}"", new RouteValueDictionary(), new RouteValueDictionary(), new ApiRouteHandler()));

        ";

            content = content.Insert(idx, routeBlock);
            File.WriteAllText(globalAsaxPath, content, Encoding.UTF8);
            context.Log("  + Global.asax API route'ları eklendi (api/auth/*, api/*).");
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

        private static XmlElement GetOrCreate(XmlElement parent, string name, string ns)
        {
            XmlElement el = parent[name];
            if (el == null)
            {
                foreach (XmlNode n in parent.ChildNodes)
                {
                    if (n is XmlElement && n.LocalName == name && n.NamespaceURI == ns)
                    {
                        el = (XmlElement)n;
                        break;
                    }
                }
                if (el == null)
                {
                    el = parent.OwnerDocument.CreateElement(name, ns);
                    parent.AppendChild(el);
                }
            }
            return el;
        }

        private static bool HasDependentAssembly(XmlElement assemblyBinding, string name)
        {
            foreach (XmlNode n in assemblyBinding.ChildNodes)
            {
                if (n is XmlElement && n.LocalName == "dependentAssembly")
                {
                    var ai = ((XmlElement)n)["assemblyIdentity"];
                    if (ai != null && ai.Attributes["name"] != null && ai.Attributes["name"].Value == name)
                        return true;
                }
            }
            return false;
        }

        private static void RemoveDependentAssembly(XmlElement assemblyBinding, string name)
        {
            var toRemove = new List<XmlNode>();
            foreach (XmlNode n in assemblyBinding.ChildNodes)
            {
                if (n is XmlElement && n.LocalName == "dependentAssembly")
                {
                    var ai = ((XmlElement)n)["assemblyIdentity"];
                    if (ai != null && ai.Attributes["name"] != null && ai.Attributes["name"].Value == name)
                        toRemove.Add(n);
                }
            }
            foreach (var n in toRemove)
                assemblyBinding.RemoveChild(n);
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
