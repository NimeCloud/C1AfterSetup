using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// C1 CMS hibrit XML + SQL data provider altyapi yapilandirmasi:
    ///   1. web.config'e 'c1' adinda connection string ekler.
    ///   2. Composite.config'e DynamicSqlDataProvider plugin'i kaydeder.
    ///   3. App_Data/Composite/Configuration/DynamicSqlDataProvider.config dosyasini
    ///      BOS (Interfaces'siz) olarak olusturur.
    ///
    /// NOT: Tipler simdilik DynamicSqlDataProvider.config'te listelenmez.
    /// C1'in SQL provider'i boot sirasinda LoadDataTypes() ile descriptor arar;
    /// descriptor henuz kaydedilmemis oldugu icin (PendingDataTypes'ta bekler) boot kirilir.
    ///
    /// SQL'e gecis icin: tipleri once XML'de olustur (normal akis), sonra
    /// Composite.Data.DataProviderCopier ile SQL'e kopyala.
    /// Bkz: plans/c1-cms-hybrid-sql-xml-datastore.md §7
    ///
    /// (C# 5 uyumlu.)
    /// </summary>
    public class ConfigureSqlDataProviderStep : ISetupStep
    {
        public string Name
        {
            get { return "SQL Data Provider Altyapisi (web.config + Composite.config + DynamicSqlDataProvider.config)"; }
        }

        public bool Verify(SetupContext context)
        {
            var sqlCfg = context.Manifest.SqlDataProvider;
            if (sqlCfg == null || !sqlCfg.Enabled) return true;

            // web.config connection string kontrolu
            string webConfigPath = context.ResolveSite("Web.config");
            if (!File.Exists(webConfigPath)) return false;
            try
            {
                var webDoc = new XmlDocument();
                webDoc.PreserveWhitespace = true;
                webDoc.Load(webConfigPath);
                if (!HasConnectionString(webDoc)) return false;
            }
            catch { return false; }

            // Composite.config plugin kaydi
            string compositeConfigPath = context.ResolveSite(Path.Combine("App_Data", "Composite", "Composite.config"));
            if (!File.Exists(compositeConfigPath)) return false;
            try
            {
                var compDoc = new XmlDocument();
                compDoc.PreserveWhitespace = true;
                compDoc.Load(compositeConfigPath);
                if (!HasSqlProviderPlugin(compDoc)) return false;
            }
            catch { return false; }

            // DynamicSqlDataProvider.config (bos olmali)
            string sqlConfigPath = context.ResolveSite(Path.Combine("App_Data", "Composite", "Configuration", "DynamicSqlDataProvider.config"));
            return File.Exists(sqlConfigPath);
        }

        public string Fingerprint(SetupContext context)
        {
            var sqlCfg = context.Manifest.SqlDataProvider;
            if (sqlCfg == null || !sqlCfg.Enabled) return "";
            StringBuilder sb = new StringBuilder();
            sb.Append(sqlCfg.ConnectionString).Append('|');
            if (sqlCfg.SqlTypes != null)
                foreach (var t in sqlCfg.SqlTypes)
                    sb.Append(t.File).Append('=').Append(t.TableName).Append(';');
            return FileSyncUtil.HashText(sb.ToString());
        }

        public bool Execute(SetupContext context)
        {
            var sqlCfg = context.Manifest.SqlDataProvider;
            if (sqlCfg == null || !sqlCfg.Enabled)
            {
                context.Log("  SQL Data Provider devre disi; atlaniyor.");
                return true;
            }

            bool changed = false;

            // 1) web.config connection string
            changed |= ConfigureWebConfig(context, sqlCfg);

            // 2) Composite.config plugin kaydi
            changed |= ConfigureCompositeConfig(context);

            // 3) DynamicSqlDataProvider.config (bos, interfaces yok)
            changed |= ConfigureSqlProviderConfig(context, sqlCfg);

            if (!changed)
                context.Log("  SQL Data Provider altyapisi zaten guncel.");
            return true;
        }

        public void Plan(SetupContext context)
        {
            var sqlCfg = context.Manifest.SqlDataProvider;
            if (sqlCfg == null || !sqlCfg.Enabled) { context.Log("  SQL Data Provider devre disi."); return; }
            context.Log("  - web.config: connection string (name=c1)");
            context.Log("  - Composite.config: DynamicSqlDataProvider plugin kaydi");
            context.Log("  - DynamicSqlDataProvider.config: bos (interfaces yok)");
            if (sqlCfg.SqlTypes != null && sqlCfg.SqlTypes.Count > 0)
            {
                context.Log("  - SQL tipleri XML'de olusturulur, sonra DataProviderCopier ile tasinir");
                context.Log("    Bkz: plans/c1-cms-hybrid-sql-xml-datastore.md §7");
            }
        }

        // ======================== web.config ========================

        private static bool ConfigureWebConfig(SetupContext context, SqlDataProviderSettings sqlCfg)
        {
            string webConfigPath = context.ResolveSite("Web.config");
            if (!File.Exists(webConfigPath))
            {
                context.Error("Web.config bulunamadi.");
                return false;
            }
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(webConfigPath);

            bool changed = false;

            XmlElement connStrings = doc.DocumentElement["connectionStrings"];
            if (connStrings == null)
            {
                connStrings = doc.CreateElement("connectionStrings");
                doc.DocumentElement.InsertAfter(connStrings,
                    doc.DocumentElement.SelectSingleNode("configSections") ?? doc.DocumentElement.FirstChild);
                changed = true;
            }

            bool hasC1 = false;
            foreach (XmlNode n in connStrings.SelectNodes("add"))
            {
                if (n.Attributes != null && n.Attributes["name"] != null && n.Attributes["name"].Value == "c1")
                {
                    hasC1 = true;
                    if (n.Attributes["connectionString"] != null && n.Attributes["connectionString"].Value != sqlCfg.ConnectionString)
                    {
                        n.Attributes["connectionString"].Value = sqlCfg.ConnectionString;
                        changed = true;
                        context.Log("  + web.config connection string (c1) guncellendi.");
                    }
                    else
                    {
                        context.Log("  = web.config connection string (c1) zaten guncel.");
                    }
                    break;
                }
            }

            if (!hasC1)
            {
                XmlElement addEl = doc.CreateElement("add");
                addEl.SetAttribute("name", "c1");
                addEl.SetAttribute("connectionString", sqlCfg.ConnectionString);
                addEl.SetAttribute("providerName", "System.Data.SqlClient");
                connStrings.AppendChild(addEl);
                changed = true;
                context.Log("  + web.config connection string (c1) eklendi.");
            }

            if (changed) doc.Save(webConfigPath);
            return changed;
        }

        // ======================== Composite.config ========================

        private static bool ConfigureCompositeConfig(SetupContext context)
        {
            string path = context.ResolveSite(Path.Combine("App_Data", "Composite", "Composite.config"));
            if (!File.Exists(path))
            {
                context.Error("Composite.config bulunamadi.");
                return false;
            }
            var doc = new XmlDocument();
            doc.PreserveWhitespace = true;
            doc.Load(path);

            bool changed = false;

            XmlElement plugins = doc.DocumentElement["Composite.Data.Plugins.DataProviderConfiguration"];
            if (plugins == null)
            {
                context.Error("Composite.config'te DataProviderConfiguration bulunamadi.");
                return false;
            }
            XmlElement list = plugins["DataProviderPlugins"];
            if (list == null)
            {
                context.Error("Composite.config'te DataProviderPlugins bulunamadi.");
                return false;
            }

            bool hasSql = false;
            foreach (XmlNode n in list.SelectNodes("add"))
            {
                if (n.Attributes != null && n.Attributes["name"] != null && n.Attributes["name"].Value == "DynamicSqlDataProvider")
                {
                    hasSql = true;
                    break;
                }
            }

            if (!hasSql)
            {
                XmlElement sqlAdd = doc.CreateElement("add");
                sqlAdd.SetAttribute("connectionStringName", "c1");
                sqlAdd.SetAttribute("sqlQueryLoggingEnabled", "false");
                sqlAdd.SetAttribute("sqlQueryLoggingIncludeStack", "false");
                sqlAdd.SetAttribute("type", "Composite.Plugins.Data.DataProviders.MSSqlServerDataProvider.SqlDataProvider, Composite");
                sqlAdd.SetAttribute("name", "DynamicSqlDataProvider");

                XmlNode afterNode = null;
                foreach (XmlNode n in list.SelectNodes("add"))
                {
                    if (n.Attributes != null && n.Attributes["name"] != null && n.Attributes["name"].Value == "DynamicXmlDataProvider")
                    {
                        afterNode = n;
                        break;
                    }
                }
                if (afterNode != null)
                    list.InsertAfter(sqlAdd, afterNode);
                else
                    list.AppendChild(sqlAdd);

                changed = true;
                context.Log("  + Composite.config: DynamicSqlDataProvider plugin eklendi.");
            }
            else
            {
                context.Log("  = Composite.config: DynamicSqlDataProvider zaten kayitli.");
            }

            if (changed) doc.Save(path);
            return changed;
        }

        // ======================== DynamicSqlDataProvider.config (BOS) ========================

        private static bool ConfigureSqlProviderConfig(SetupContext context, SqlDataProviderSettings sqlCfg)
        {
            string configDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "Configuration"));
            Directory.CreateDirectory(configDir);
            string path = Path.Combine(configDir, "DynamicSqlDataProvider.config");

            if (File.Exists(path))
            {
                context.Log("  = DynamicSqlDataProvider.config zaten mevcut.");
                return false;
            }

            var doc = CreateEmptySqlProviderConfig();
            doc.Save(path);
            context.Log("  + DynamicSqlDataProvider.config olusturuldu (bos Interfaces).");
            return true;
        }

        private static XmlDocument CreateEmptySqlProviderConfig()
        {
            var doc = new XmlDocument();
            XmlDeclaration decl = doc.CreateXmlDeclaration("1.0", "utf-8", null);
            doc.AppendChild(decl);

            XmlElement root = doc.CreateElement("configuration");

            XmlElement configSections = doc.CreateElement("configSections");
            XmlElement section = doc.CreateElement("section");
            section.SetAttribute("name", "Composite.Data.Plugins.SqlDataProviderConfiguration");
            section.SetAttribute("type",
                "Composite.Plugins.Data.DataProviders.MSSqlServerDataProvider.SqlDataProviderConfigurationSection, Composite, Version=6.13.9280.21599, Culture=neutral, PublicKeyToken=null");
            configSections.AppendChild(section);
            root.AppendChild(configSections);

            XmlElement sqlSection = doc.CreateElement("Composite.Data.Plugins.SqlDataProviderConfiguration");
            XmlElement interfaces = doc.CreateElement("Interfaces");
            sqlSection.AppendChild(interfaces);
            root.AppendChild(sqlSection);

            doc.AppendChild(root);
            return doc;
        }

        // ======================== Yardimcilar ========================

        private static bool HasConnectionString(XmlDocument doc)
        {
            var connStrings = doc.DocumentElement["connectionStrings"];
            if (connStrings == null) return false;
            foreach (XmlNode n in connStrings.SelectNodes("add"))
            {
                if (n.Attributes != null && n.Attributes["name"] != null && n.Attributes["name"].Value == "c1")
                    return true;
            }
            return false;
        }

        private static bool HasSqlProviderPlugin(XmlDocument doc)
        {
            var plugins = doc.DocumentElement["Composite.Data.Plugins.DataProviderConfiguration"];
            if (plugins == null) return false;
            var list = plugins["DataProviderPlugins"];
            if (list == null) return false;
            foreach (XmlNode n in list.SelectNodes("add"))
            {
                if (n.Attributes != null && n.Attributes["name"] != null && n.Attributes["name"].Value == "DynamicSqlDataProvider")
                    return true;
            }
            return false;
        }
    }
}
