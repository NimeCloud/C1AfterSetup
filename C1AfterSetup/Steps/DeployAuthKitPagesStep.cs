using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// AuthKit sayfalarini (Login, Register, Forgot Password, Reset Password, Logout,
    /// Users, Groups, Group Permissions, User Permissions) hedef sitenin DataStores
    /// XML'lerine ekler. Mevcut sayfalar korunur; yalnizca eksik AuthKit sayfalari
    /// eklenir (URL cakismasi durumunda atlanir).
    /// </summary>
    public class DeployAuthKitPagesStep : ISetupStep
    {
        public string Name
        {
            get { return "AuthKit Sayfalari (DataStores)"; }
        }

        public bool Verify(SetupContext context)
        {
            string ipage = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataStores",
                "Composite.Data.Types.IPage_tr-TR.xml"));
            if (!File.Exists(ipage)) return false;

            var doc = XDocument.Load(ipage);
            var root = doc.Root;
            if (root == null) return false;

            string[] authKitIds = AuthKitPageIds();
            foreach (string id in authKitIds)
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
            string srcDir = context.ResolveSource("DataStores");
            if (!Directory.Exists(srcDir))
            {
                context.Log("  sources/DataStores yok, AuthKit sayfalari atlaniyor.");
                return true;
            }

            string dstDir = context.ResolveSite(Path.Combine("App_Data", "Composite", "DataStores"));
            Directory.CreateDirectory(dstDir);

            // IPage_tr-TR.xml
            string srcIpage = Path.Combine(srcDir, "Composite.Data.Types.IPage_tr-TR.xml");
            string dstIpage = Path.Combine(dstDir, "Composite.Data.Types.IPage_tr-TR.xml");

            if (File.Exists(srcIpage))
            {
                MergeXml(srcIpage, dstIpage, "PageElementsElements", "PageElements", "Id",
                    "Composite.Data.Types.IPage_tr-TR",
                    context, "IPage");
            }

            // IPageStructure.xml
            string srcStruct = Path.Combine(srcDir, "Composite.Data.Types.IPageStructure.xml");
            string dstStruct = Path.Combine(dstDir, "Composite.Data.Types.IPageStructure.xml");

            if (File.Exists(srcStruct))
            {
                MergeXml(srcStruct, dstStruct, "PageStructureElementsElements", "PageStructureElements", "Id",
                    "Composite.Data.Types.IPageStructure",
                    context, "IPageStructure");
            }

            // IPage_Unpublished_tr-TR.xml (C1 requires unpublished versions too)
            string srcUnpub = Path.Combine(srcDir, "Composite.Data.Types.IPage_Unpublished_tr-TR.xml");
            string dstUnpub = Path.Combine(dstDir, "Composite.Data.Types.IPage_Unpublished_tr-TR.xml");

            if (File.Exists(srcUnpub))
            {
                MergeXml(srcUnpub, dstUnpub, "PageElementsElements", "PageElements", "Id",
                    "Composite.Data.Types.IPage_Unpublished_tr-TR",
                    context, "IPage_Unpublished");
            }

            // IPagePlaceholderContent_tr-TR.xml - AuthKit sayfalarina Razor fonksiyonlarini baglar.
            // C1 CMS'de placeholder content VersionId'si IPage VersionId'si ile eslesmek ZORUNDA.
            WritePlaceholderContent(dstDir, context);

            // KeyTreeStoreKit DataStore XML'ini merge et
            string srcKeyTree = Path.Combine(srcDir, "KeyTreeStoreKit.Data.KeyTreeItem.xml");
            string dstKeyTree = Path.Combine(dstDir, "KeyTreeStoreKit.Data.KeyTreeItem.xml");
            if (File.Exists(srcKeyTree))
            {
                MergeXml(srcKeyTree, dstKeyTree, "KeyTreeItemElementsElements", "KeyTreeItemElements", "Id",
                    "KeyTreeStoreKit.Data.KeyTreeItem",
                    context, "KeyTreeItem");
            }

            return true;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - sources/DataStores XML'leri hedef DataStores'a merge edilir (mevcut sayfalar korunur)");
            context.Log("  - 9 AuthKit sayfasi: Login, Register, Forgot, Reset, Logout, Users, Groups, GroupPerms, UserPerms");
            context.Log("  - PlaceholderContent: AuthKit Razor fonksiyonlari sayfa placeholder'larina baglanir (programmatic)");
        }

        private static void MergeXml(string srcPath, string dstPath, string rootName, string elementName,
            string idAttr, string typeName, SetupContext context, string label)
        {
            XDocument srcDoc;
            try { srcDoc = XDocument.Load(srcPath); }
            catch (Exception ex) { context.Error(label + " kaynak XML okunamadi: " + ex.Message); return; }

            XElement srcRoot = srcDoc.Root;
            if (srcRoot == null || srcRoot.Name != rootName)
            {
                context.Warn(label + " kaynak XML kok elemani gecersiz, atlaniyor.");
                return;
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
            if (dstRoot == null) dstRoot = new XElement(rootName);

            int added = 0, skipped = 0;
            foreach (XElement srcEl in srcRoot.Elements(elementName))
            {
                string id = (string)srcEl.Attribute(idAttr);
                if (string.IsNullOrEmpty(id)) continue;

                bool exists = false;
                foreach (XElement dstEl in dstRoot.Elements(elementName))
                {
                    if ((string)dstEl.Attribute(idAttr) == id) { exists = true; break; }
                }
                if (exists) { skipped++; continue; }

                dstRoot.Add(new XElement(srcEl));
                added++;
            }

            dstDoc.Save(dstPath);
            context.Log(string.Format("  {0}: +{1} eklendi, {2} atlandi (zaten var) -> {3}", label, added, skipped,
                Path.GetFileName(dstPath)));
        }

        /// <summary>
        /// AuthKit auth sayfalari icin IPagePlaceholderContent DataStore XML'ine
        /// placeholder content ekler. Her sayfanin "content" placeholder'ina ilgili
        /// Razor fonksiyonu baglanir.
        ///
        /// KRITIK: C1 CMS, placeholder content'i sayfaya VersionId ile baglar.
        /// IPagePlaceholderContent.VersionId == IPage.VersionId olmak ZORUNDADIR.
        /// Eslesmezse Content == null olur ve form goruntulenmez.
        /// </summary>
        private static void WritePlaceholderContent(string dstDir, SetupContext context)
        {
            string label = "IPagePlaceholderContent";
            string dstPath = Path.Combine(dstDir, "Composite.Data.Types.IPagePlaceholderContent_tr-TR.xml");

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

            // Auth sayfalari PageId -> RazorFunctionName
            var authPageMappings = new[]
            {
                new { PageId = "f6f06000-0000-0000-0000-f6f0f6f0f6f0", Function = "AuthKit.LoginForm" },
                new { PageId = "a7a07000-0000-0000-0000-a7a0a7a0a7a0", Function = "AuthKit.RegisterForm" },
                new { PageId = "b8b08000-0000-0000-0000-b8b0b8b0b8b0", Function = "AuthKit.ForgotPasswordForm" },
                new { PageId = "c9c09000-0000-0000-0000-c9c0c9c0c9c0", Function = "AuthKit.ResetPasswordForm" },
                new { PageId = "d0d0a000-0000-0000-0000-d0d0d0d0d0d0", Function = "AuthKit.LogoutForm" },
            };

            string rootName = "PagePlaceholderContentElementsElements";
            string elementName = "PagePlaceholderContentElements";

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
            if (dstRoot == null) dstRoot = new XElement(rootName);

            // AuthKit PageId seti - eski (yanlis VersionId'li) kayitlari temizlemek icin
            var authKitPageIds = new HashSet<string>(new[]
            {
                "f6f06000-0000-0000-0000-f6f0f6f0f6f0",
                "a7a07000-0000-0000-0000-a7a0a7a0a7a0",
                "b8b08000-0000-0000-0000-b8b0b8b0b8b0",
                "c9c09000-0000-0000-0000-c9c0c9c0c9c0",
                "d0d0a000-0000-0000-0000-d0d0d0d0d0d0",
            });

            // Eski AuthKit placeholder kayitlarini kaldir
            int removed = 0;
            var toRemove = new List<XElement>();
            foreach (XElement dstEl in dstRoot.Elements(elementName))
            {
                if (authKitPageIds.Contains((string)dstEl.Attribute("PageId"))
                    && (string)dstEl.Attribute("PlaceHolderId") == "content")
                {
                    toRemove.Add(dstEl);
                }
            }
            foreach (var el in toRemove) { el.Remove(); removed++; }
            if (removed > 0)
                context.Log(string.Format("  {0}: {1} eski AuthKit kaydi kaldirildi", label, removed));

            // Yeni placeholder content ekle (dogru VersionId ile)
            int added = 0;
            foreach (var mapping in authPageMappings)
            {
                string pageVersionId;
                if (!pageVersionIds.TryGetValue(mapping.PageId, out pageVersionId))
                    pageVersionId = Guid.NewGuid().ToString();

                // C1 CMS placeholder content formati (working ref: WebcamRecorder)
                string contentHtml =
                    "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
                    "\t<head>\n\t</head>\n" +
                    "\t<body>\n\n" +
                    "<f:function name=\"" + mapping.Function + "\" " +
                    "xmlns:f=\"http://www.composite.net/ns/function/1.0\" />\n\n" +
                    "\t</body>\n" +
                    "</html>";

                var now = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");

                var el = new XElement(elementName,
                    new XAttribute("PublicationStatus", "published"),
                    new XAttribute("ChangeDate", now),
                    new XAttribute("CreationDate", now),
                    new XAttribute("ChangedBy", "admin"),
                    new XAttribute("CreatedBy", "admin"),
                    new XAttribute("PageId", mapping.PageId),
                    new XAttribute("PlaceHolderId", "content"),
                    new XAttribute("Content", contentHtml),
                    new XAttribute("SourceCultureName", "tr-TR"),
                    new XAttribute("VersionId", pageVersionId)
                );

                dstRoot.Add(el);
                added++;
            }

            dstDoc.Save(dstPath);
            context.Log(string.Format("  {0}: +{1} eklendi, {2} kaldirildi -> {3}", label, added, removed,
                Path.GetFileName(dstPath)));
        }

        private static string[] AuthKitPageIds()
        {
            return new string[]
            {
                "f6f06000-0000-0000-0000-f6f0f6f0f6f0", // Login
                "d4d04000-0000-0000-0000-d4d0d4d0d4d0", // Group Permissions
                "e5e05000-0000-0000-0000-e5e0e5e0e5e0", // User Permissions
                "b8b08000-0000-0000-0000-b8b0b8b0b8b0", // Forgot Password
                "c9c09000-0000-0000-0000-c9c0c9c0c9c0", // Reset Password
                "d0d0a000-0000-0000-0000-d0d0d0d0d0d0", // Logout
                "a7a07000-0000-0000-0000-a7a0a7a0a7a0", // Register
                "b2b02000-0000-0000-0000-b2b0b2b0b2b0", // Users
                "c3c03000-0000-0000-0000-c3c0c3c0c3c0", // Groups
            };
        }
    }
}
