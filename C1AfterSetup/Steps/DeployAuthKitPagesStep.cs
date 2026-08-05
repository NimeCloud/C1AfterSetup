using System;
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

            // IPagePlaceholderContent_tr-TR.xml – AuthKit sayfalarina Razor fonksiyonlarini
            // (LoginForm, RegisterForm vb.) baglar. PlaceholderContent XElement'larini
            // programmatic olarak olusturur (XML entity escaping sorunu olmamasi icin).
            WritePlaceholderContent(dstDir, context);

            // KeyTreeStoreKit DataStore XML'ini merge et (Root + Auth.LoginPageId + Auth.ResetPasswordPageId).
            // Bu sayede PanelLayout, UserManagementPage vb. sayfalar page-ID tabanlı yönlendirme yapabilir
            // (hardcoded ~/login yerine AuthKit Login sayfasına yönlendirir).
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
        /// AuthKit auth sayfalari (Login, Register, ForgotPassword, ResetPassword, Logout) icin
        /// IPagePlaceholderContent DataStore XML'ine programmatic olarak placeholder content
        /// ekler. Her sayfanin "content" placeholder'ina ilgili Razor fonksiyonu baglanir.
        ///
        /// C1 CMS, placeholder content'i su HTML/XHTML yapisinda saklar:
        ///   <html xmlns="http://www.w3.org/1999/xhtml"><head></head><body>
        ///   <f:function name="AuthKit.LoginForm" xmlns:f="http://www.composite.net/ns/function/1.0" />
        ///   </body></html>
        ///
        /// Content degeri, XAttribute icine yerlestirildiginde .NET otomatik olarak
        /// < > " gibi XML entity'lerini dogru sekilde escape eder.
        /// </summary>
        private static void WritePlaceholderContent(string dstDir, SetupContext context)
        {
            string label = "IPagePlaceholderContent";
            string dstPath = Path.Combine(dstDir, "Composite.Data.Types.IPagePlaceholderContent_tr-TR.xml");

            // Auth sayfalari icin PageId -> RazorFunctionName eslesmesi
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

            // Hedef XML'i yukle veya olustur
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
            foreach (var mapping in authPageMappings)
            {
                // Bu (PageId, PlaceHolderId) icin zaten kayit var mi?
                bool exists = false;
                foreach (XElement dstEl in dstRoot.Elements(elementName))
                {
                    if ((string)dstEl.Attribute("PageId") == mapping.PageId
                        && (string)dstEl.Attribute("PlaceHolderId") == "content")
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists) { skipped++; continue; }

                // Content: C1'in bekledigi HTML/XHTML formati.
                // <html xmlns="http://www.w3.org/1999/xhtml"><head></head><body>
                // <f:function name="AuthKit.Xyz" xmlns:f="http://www.composite.net/ns/function/1.0" />
                // </body></html>
                // XAttribute icine konuldugunda .NET otomatik escape eder.
                string contentHtml =
                    "<html xmlns=\"http://www.w3.org/1999/xhtml\"><head></head><body>\n" +
                    "<f:function name=\"" + mapping.Function + "\" " +
                    "xmlns:f=\"http://www.composite.net/ns/function/1.0\" />\n" +
                    "\t</body></html>";

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
                    new XAttribute("VersionId", Guid.NewGuid().ToString())
                );

                dstRoot.Add(el);
                added++;
            }

            dstDoc.Save(dstPath);
            context.Log(string.Format("  {0}: +{1} eklendi, {2} atlandi (zaten var) -> {3}", label, added, skipped,
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
