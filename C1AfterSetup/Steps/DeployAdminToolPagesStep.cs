using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// Admin tool sayfalarini (Data Provider Default, Datatype Migrator) programmatic olarak
    /// hedef sitenin DataStores XML'lerine ekler.
    /// 
    /// Mevcut sayfalar korunur; yalnizca eksik AdminTools sayfalari eklenir.
    /// Sayfalar root seviyesinde (ParentId=zero) LocalOrdering 100/101 olarak eklenir.
    /// </summary>
    public class DeployAdminToolPagesStep : ISetupStep
    {
        // --- Template GUIDs (sayfa sablon .cshtml'lerindeki TemplateId degerleriyle ayni) ---
        private static readonly Guid TemplateDataProviderSelector = new Guid("A1100000-0000-0000-0000-A110A110A110");
        private static readonly Guid TemplateDatatypeMigrator = new Guid("A1200000-0000-0000-0000-A120A120A120");

        // --- PageType GUIDs (C1 CMS varsayilanlari) ---
        private static readonly Guid PageTypePage = new Guid("f7869eb2-7369-4eb2-af47-e3be261e92c7");

        // --- Root parent ---
        private static readonly Guid RootParentId = new Guid("00000000-0000-0000-0000-000000000000");

        /// <summary>
        /// AdminTools page definition: ID, template, title, ordering.
        /// These pages render themselves via their templates — no Razor function in placeholder content.
        /// </summary>
        private struct AdminToolPageDef
        {
            public Guid PageId;
            public Guid TemplateId;
            public Guid PageTypeId;
            public string Title;
            public string MenuTitle;
            public string UrlTitle;
            public int LocalOrdering;
        }

        public string Name
        {
            get { return "Admin Tools Sayfalari (DataStores)"; }
        }

        public bool Verify(SetupContext context)
        {
            string ipage = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataStores",
                "Composite.Data.Types.IPage_tr-TR.xml"));
            if (!File.Exists(ipage)) return false;

            var doc = XDocument.Load(ipage);
            var root = doc.Root;
            if (root == null) return false;

            string[] allIds = AllAdminToolPageIds();
            foreach (string id in allIds)
            {
                bool found = false;
                foreach (var el in root.Elements("PageElements"))
                {
                    if ((string)el.Attribute("Id") == id) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }

        public string Fingerprint(SetupContext context)
        {
            return ""; // Her zaman kontrol et
        }

        public bool Execute(SetupContext context)
        {
            string dstDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataStores"));
            Directory.CreateDirectory(dstDir);

            var pages = GetAdminToolPageDefs();

            // 1) IPage_tr-TR.xml
            WritePageElements(context, dstDir,
                "Composite.Data.Types.IPage_tr-TR.xml",
                "PageElementsElements", "PageElements",
                pages, "IPage");

            // 2) IPage_Unpublished_tr-TR.xml
            WritePageElements(context, dstDir,
                "Composite.Data.Types.IPage_Unpublished_tr-TR.xml",
                "PageElementsElements", "PageElements",
                pages, "IPage_Unpublished");

            // 3) IPageStructure.xml
            WritePageStructure(context, dstDir, pages);

            // 4) IPagePlaceholderContent_tr-TR.xml — empty content (templates render themselves)
            WritePlaceholderContent(context, dstDir, pages);

            return true;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - 2 Admin Tools sayfasi programmatic olarak DataStores XML'lerine eklenir:");
            context.Log("    Data Provider Default (root, LocalOrdering=100) + Datatype Migrator (root, LocalOrdering=101)");
            context.Log("  - Sayfa yapisi (IPageStructure) ve bos PlaceholderContent otomatik olusturulur.");
        }

        // ------------------------------------------------------------------------
        //  Page definitions
        // ------------------------------------------------------------------------

        private static AdminToolPageDef[] GetAdminToolPageDefs()
        {
            return new AdminToolPageDef[]
            {
                // Data Provider Default
                new AdminToolPageDef
                {
                    PageId = new Guid("A1110000-0000-0000-0000-A111A111A111"),
                    TemplateId = TemplateDataProviderSelector,
                    PageTypeId = PageTypePage,
                    Title = "Data Provider Default",
                    MenuTitle = "Data Provider Default",
                    UrlTitle = "Data-Provider-Default",
                    LocalOrdering = 100,
                },
                // Datatype Migrator
                new AdminToolPageDef
                {
                    PageId = new Guid("A1210000-0000-0000-0000-A121A121A121"),
                    TemplateId = TemplateDatatypeMigrator,
                    PageTypeId = PageTypePage,
                    Title = "Datatype Migrator",
                    MenuTitle = "Datatype Migrator",
                    UrlTitle = "Datatype-Migrator",
                    LocalOrdering = 101,
                },
            };
        }

        private static string[] AllAdminToolPageIds()
        {
            var pages = GetAdminToolPageDefs();
            var ids = new string[pages.Length];
            for (int i = 0; i < pages.Length; i++)
                ids[i] = pages[i].PageId.ToString();
            return ids;
        }

        // ------------------------------------------------------------------------
        //  Write IPage / IPage_Unpublished
        // ------------------------------------------------------------------------

        private static void WritePageElements(SetupContext context, string dstDir,
            string fileName, string rootName, string elementName,
            AdminToolPageDef[] pages, string label)
        {
            string dstPath = Path.Combine(dstDir, fileName);

            XDocument dstDoc;
            if (File.Exists(dstPath))
            {
                try { dstDoc = XDocument.Load(dstPath); }
                catch { dstDoc = new XDocument(new XElement(rootName)); }
            }
            else
            {
                dstDoc = new XDocument(new XElement(rootName));
            }

            XElement dstRoot = dstDoc.Root;
            if (dstRoot == null)
            {
                dstRoot = new XElement(rootName);
                dstDoc.Add(dstRoot);
            }

            var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");

            int added = 0, skipped = 0;
            foreach (var page in pages)
            {
                string id = page.PageId.ToString();

                bool exists = false;
                foreach (XElement dstEl in dstRoot.Elements(elementName))
                {
                    if ((string)dstEl.Attribute("Id") == id) { exists = true; break; }
                }
                if (exists) { skipped++; continue; }

                var el = new XElement(elementName,
                    new XAttribute("PublicationStatus", "published"),
                    new XAttribute("ChangeDate", now),
                    new XAttribute("CreationDate", now),
                    new XAttribute("ChangedBy", "admin"),
                    new XAttribute("CreatedBy", "admin"),
                    new XAttribute("Id", id),
                    new XAttribute("TemplateId", page.TemplateId.ToString()),
                    new XAttribute("PageTypeId", page.PageTypeId.ToString()),
                    new XAttribute("Title", page.Title),
                    new XAttribute("MenuTitle", page.MenuTitle),
                    new XAttribute("UrlTitle", page.UrlTitle),
                    new XAttribute("FriendlyUrl", ""),
                    new XAttribute("Description", ""),
                    new XAttribute("SourceCultureName", "tr-TR"),
                    new XAttribute("VersionId", Guid.NewGuid().ToString())
                );

                dstRoot.Add(el);
                added++;
            }

            dstDoc.Save(dstPath);
            context.Log(string.Format("  {0}: +{1} eklendi, {2} atlandi (zaten var) -> {3}",
                label, added, skipped, fileName));
        }

        // ------------------------------------------------------------------------
        //  Write IPageStructure
        // ------------------------------------------------------------------------

        private static void WritePageStructure(SetupContext context, string dstDir,
            AdminToolPageDef[] pages)
        {
            string label = "IPageStructure";
            string fileName = "Composite.Data.Types.IPageStructure.xml";
            string dstPath = Path.Combine(dstDir, fileName);
            string rootName = "PageStructureElementsElements";
            string elementName = "PageStructureElements";

            XDocument dstDoc;
            if (File.Exists(dstPath))
            {
                try { dstDoc = XDocument.Load(dstPath); }
                catch { dstDoc = new XDocument(new XElement(rootName)); }
            }
            else
            {
                dstDoc = new XDocument(new XElement(rootName));
            }

            XElement dstRoot = dstDoc.Root;
            if (dstRoot == null)
            {
                dstRoot = new XElement(rootName);
                dstDoc.Add(dstRoot);
            }

            int added = 0, skipped = 0;
            foreach (var page in pages)
            {
                string id = page.PageId.ToString();

                bool exists = false;
                foreach (XElement dstEl in dstRoot.Elements(elementName))
                {
                    if ((string)dstEl.Attribute("Id") == id) { exists = true; break; }
                }
                if (exists) { skipped++; continue; }

                // Both pages are top-level under root
                var el = new XElement(elementName,
                    new XAttribute("Id", id),
                    new XAttribute("ParentId", RootParentId.ToString()),
                    new XAttribute("LocalOrdering", page.LocalOrdering)
                );

                dstRoot.Add(el);
                added++;
            }

            dstDoc.Save(dstPath);
            context.Log(string.Format("  {0}: +{1} eklendi, {2} atlandi (zaten var) -> {3}",
                label, added, skipped, fileName));
        }

        // ------------------------------------------------------------------------
        //  Write IPagePlaceholderContent — empty content (templates render themselves)
        // ------------------------------------------------------------------------

        private static void WritePlaceholderContent(SetupContext context, string dstDir,
            AdminToolPageDef[] pages)
        {
            string label = "IPagePlaceholderContent";
            string fileName = "Composite.Data.Types.IPagePlaceholderContent_tr-TR.xml";
            string dstPath = Path.Combine(dstDir, fileName);
            string rootName = "PagePlaceholderContentElementsElements";
            string elementName = "PagePlaceholderContentElements";

            // IPage_tr-TR.xml'den her sayfanin VersionId'sini oku
            var pageVersionIds = new Dictionary<string, string>();
            string ipagePath = Path.Combine(dstDir, "Composite.Data.Types.IPage_tr-TR.xml");
            if (File.Exists(ipagePath))
            {
                try
                {
                    var ipageDoc = XDocument.Load(ipagePath);
                    if (ipageDoc.Root != null)
                    {
                        foreach (var el in ipageDoc.Root.Elements("PageElements"))
                        {
                            string id = (string)el.Attribute("Id");
                            string ver = (string)el.Attribute("VersionId");
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(ver))
                                pageVersionIds[id] = ver;
                        }
                    }
                }
                catch { }
            }

            XDocument dstDoc;
            if (File.Exists(dstPath))
            {
                try { dstDoc = XDocument.Load(dstPath); }
                catch { dstDoc = new XDocument(new XElement(rootName)); }
            }
            else
            {
                dstDoc = new XDocument(new XElement(rootName));
            }

            XElement dstRoot = dstDoc.Root;
            if (dstRoot == null)
            {
                dstRoot = new XElement(rootName);
                dstDoc.Add(dstRoot);
            }

            // AdminTools PageId seti - eski kayitlari temizle
            var adminToolPageIdSet = new HashSet<string>(AllAdminToolPageIds());

            // Eski AdminTools placeholder kayitlarini kaldir
            int removed = 0;
            var toRemove = new List<XElement>();
            foreach (XElement dstEl in dstRoot.Elements(elementName))
            {
                if (adminToolPageIdSet.Contains((string)dstEl.Attribute("PageId"))
                    && (string)dstEl.Attribute("PlaceHolderId") == "content")
                {
                    toRemove.Add(dstEl);
                }
            }
            foreach (var el in toRemove) { el.Remove(); removed++; }

            // Yeni placeholder content ekle (bos — template kendini render eder)
            int added = 0;
            var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");

            foreach (var page in pages)
            {
                string pageId = page.PageId.ToString();

                // Bu pageId icin zaten varsa atla
                bool alreadyExists = false;
                foreach (XElement dstEl in dstRoot.Elements(elementName))
                {
                    if ((string)dstEl.Attribute("PageId") == pageId
                        && (string)dstEl.Attribute("PlaceHolderId") == "content")
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                if (alreadyExists) continue;

                string pageVersionId;
                if (!pageVersionIds.TryGetValue(pageId, out pageVersionId))
                    pageVersionId = Guid.NewGuid().ToString();

                string contentHtml =
                    "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
                    "\t<head>\n\t</head>\n" +
                    "\t<body>\n\n" +
                    "\t</body>\n" +
                    "</html>";

                var el = new XElement(elementName,
                    new XAttribute("PublicationStatus", "published"),
                    new XAttribute("ChangeDate", now),
                    new XAttribute("CreationDate", now),
                    new XAttribute("ChangedBy", "admin"),
                    new XAttribute("CreatedBy", "admin"),
                    new XAttribute("PageId", pageId),
                    new XAttribute("PlaceHolderId", "content"),
                    new XAttribute("Content", contentHtml),
                    new XAttribute("SourceCultureName", "tr-TR"),
                    new XAttribute("VersionId", pageVersionId)
                );

                dstRoot.Add(el);
                added++;
            }

            dstDoc.Save(dstPath);
            context.Log(string.Format("  {0}: +{1} eklendi, {2} kaldirildi -> {3}",
                label, added, removed, fileName));
        }
    }
}
