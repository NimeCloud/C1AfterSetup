using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace C1AfterSetup.Steps
{
    /// <summary>
    /// AuthKit sayfalarini (AuthKit, Login, Register, Forgot Password, Reset Password,
    /// Logout, Users, Groups, Group Permissions, User Permissions) programmatic olarak
    /// hedef sitenin DataStores XML'lerine ekler.
    /// 
    /// Mevcut sayfalar korunur; yalnizca eksik AuthKit sayfalari eklenir.
    /// Tum sayfalar "AuthKit" sayfasinin altina child olarak eklenir.
    /// </summary>
    public class DeployAuthKitPagesStep : ISetupStep
    {
        // --- Template GUIDs (sayfa sablon .cshtml'lerindeki TemplateId degerleriyle ayni) ---
        private static readonly Guid TemplateSetupPage = new Guid("a1b0a1b0-0000-0000-0000-a1b0a1b0a1b0"); // AuthKit.SetupPage
        private static readonly Guid TemplateAuthLayout = new Guid("24e07eb6-17c9-424e-9c16-219f57a85900"); // AuthKit.AuthLayout
        private static readonly Guid TemplateUserMgmt = new Guid("f9a2e1d7-8c3b-4b2a-9e1f-1a2b3c4d5e6f"); // AuthKit.UserManagementPage
        private static readonly Guid TemplateGroupMgmt = new Guid("50562763-9421-40b6-8897-5214ab170051"); // AuthKit.GroupManagementPage
        private static readonly Guid TemplateGroupPerm = new Guid("ff5a0c90-7f64-4e56-b9b9-1c078276f4c2"); // AuthKit.GroupPermissionPage
        private static readonly Guid TemplateUserPerm = new Guid("70d2e0d8-d2d1-4209-a5ae-a0bc7d283a9d"); // AuthKit.UserPermissionPage

        // --- PageType GUIDs (C1 CMS varsayilanlari) ---
        private static readonly Guid PageTypeHome = new Guid("de22fed1-0729-4ad3-aa1c-6047e54bf429");
        private static readonly Guid PageTypePage = new Guid("f7869eb2-7369-4eb2-af47-e3be261e92c7");

        // --- Root parent ---
        private static readonly Guid RootParentId = new Guid("00000000-0000-0000-0000-000000000000");

        // --- AuthKit page ID ---
        private static readonly Guid AuthKitHomePageId = new Guid("e1e01000-0000-0000-0000-e1e0e1e0e1e0");

        /// <summary>
        /// AuthKit page definition: ID, template, title, Razor function for placeholder content.
        /// </summary>
        private struct AuthKitPageDef
        {
            public Guid PageId;
            public Guid TemplateId;
            public Guid PageTypeId;
            public string Title;
            public string MenuTitle;
            public string UrlTitle;
            public string RazorFunction; // empty = no function (page renders itself)
            public int LocalOrdering;
        }

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

            string[] allIds = AllAuthKitPageIds();
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

            var pages = GetAuthKitPageDefs();

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

            // 4) IPagePlaceholderContent_tr-TR.xml
            WritePlaceholderContent(context, dstDir, pages);

            return true;
        }

        public void Plan(SetupContext context)
        {
            context.Log("  - 10 AuthKit sayfasi programmatic olarak DataStores XML'lerine eklenir:");
            context.Log("    AuthKit (root) + 9 alt sayfa: Login, Register, Forgot, Reset, Logout,");
            context.Log("    Users, Groups, Group Permissions, User Permissions");
            context.Log("  - Sayfa yapisi (IPageStructure) ve PlaceholderContent otomatik olusturulur.");
        }

        // ------------------------------------------------------------------------
        //  Page definitions
        // ------------------------------------------------------------------------

        private static AuthKitPageDef[] GetAuthKitPageDefs()
        {
            return new AuthKitPageDef[]
            {
                // AuthKit – uses SetupPage template, Home page type, root level
                new AuthKitPageDef
                {
                    PageId = new Guid("e1e01000-0000-0000-0000-e1e0e1e0e1e0"),
                    TemplateId = TemplateSetupPage,
                    PageTypeId = PageTypeHome,
                    Title = "AuthKit",
                    MenuTitle = "AuthKit",
                    UrlTitle = "AuthKit",
                    RazorFunction = "",  // SetupPage renders itself
                    LocalOrdering = 1,
                },
                // Login
                new AuthKitPageDef
                {
                    PageId = new Guid("f6f06000-0000-0000-0000-f6f0f6f0f6f0"),
                    TemplateId = TemplateAuthLayout,
                    PageTypeId = PageTypePage,
                    Title = "Login",
                    MenuTitle = "Login",
                    UrlTitle = "Login",
                    RazorFunction = "AuthKit.LoginForm",
                    LocalOrdering = 0,
                },
                // Register
                new AuthKitPageDef
                {
                    PageId = new Guid("a7a07000-0000-0000-0000-a7a0a7a0a7a0"),
                    TemplateId = TemplateAuthLayout,
                    PageTypeId = PageTypePage,
                    Title = "Register",
                    MenuTitle = "Register",
                    UrlTitle = "Register",
                    RazorFunction = "AuthKit.RegisterForm",
                    LocalOrdering = 1,
                },
                // Forgot Password
                new AuthKitPageDef
                {
                    PageId = new Guid("b8b08000-0000-0000-0000-b8b0b8b0b8b0"),
                    TemplateId = TemplateAuthLayout,
                    PageTypeId = PageTypePage,
                    Title = "Forgot Password",
                    MenuTitle = "Forgot Password",
                    UrlTitle = "Forgot-Password",
                    RazorFunction = "AuthKit.ForgotPasswordForm",
                    LocalOrdering = 2,
                },
                // Reset Password
                new AuthKitPageDef
                {
                    PageId = new Guid("c9c09000-0000-0000-0000-c9c0c9c0c9c0"),
                    TemplateId = TemplateAuthLayout,
                    PageTypeId = PageTypePage,
                    Title = "Reset Password",
                    MenuTitle = "Reset Password",
                    UrlTitle = "Reset-Password",
                    RazorFunction = "AuthKit.ResetPasswordForm",
                    LocalOrdering = 3,
                },
                // Logout
                new AuthKitPageDef
                {
                    PageId = new Guid("d0d0a000-0000-0000-0000-d0d0d0d0d0d0"),
                    TemplateId = TemplateAuthLayout,
                    PageTypeId = PageTypePage,
                    Title = "Logout",
                    MenuTitle = "Logout",
                    UrlTitle = "Logout",
                    RazorFunction = "AuthKit.LogoutForm",
                    LocalOrdering = 4,
                },
                // Users
                new AuthKitPageDef
                {
                    PageId = new Guid("b2b02000-0000-0000-0000-b2b0b2b0b2b0"),
                    TemplateId = TemplateUserMgmt,
                    PageTypeId = PageTypePage,
                    Title = "Users",
                    MenuTitle = "Kullanicilar",
                    UrlTitle = "Users",
                    RazorFunction = "",  // renders itself via PanelLayout
                    LocalOrdering = 5,
                },
                // Groups
                new AuthKitPageDef
                {
                    PageId = new Guid("c3c03000-0000-0000-0000-c3c0c3c0c3c0"),
                    TemplateId = TemplateGroupMgmt,
                    PageTypeId = PageTypePage,
                    Title = "Groups",
                    MenuTitle = "Gruplar",
                    UrlTitle = "Groups",
                    RazorFunction = "",
                    LocalOrdering = 6,
                },
                // Group Permissions
                new AuthKitPageDef
                {
                    PageId = new Guid("d4d04000-0000-0000-0000-d4d0d4d0d4d0"),
                    TemplateId = TemplateGroupPerm,
                    PageTypeId = PageTypePage,
                    Title = "Group Permissions",
                    MenuTitle = "Grup Yetkileri",
                    UrlTitle = "Group-Permissions",
                    RazorFunction = "",
                    LocalOrdering = 7,
                },
                // User Permissions
                new AuthKitPageDef
                {
                    PageId = new Guid("e5e05000-0000-0000-0000-e5e0e5e0e5e0"),
                    TemplateId = TemplateUserPerm,
                    PageTypeId = PageTypePage,
                    Title = "User Permissions",
                    MenuTitle = "Kullanici Yetkileri",
                    UrlTitle = "User-Permissions",
                    RazorFunction = "",
                    LocalOrdering = 8,
                },
            };
        }

        private static string[] AllAuthKitPageIds()
        {
            var pages = GetAuthKitPageDefs();
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
            AuthKitPageDef[] pages, string label)
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
            AuthKitPageDef[] pages)
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

                Guid parentId = (page.PageId == AuthKitHomePageId)
                    ? RootParentId
                    : AuthKitHomePageId;

                var el = new XElement(elementName,
                    new XAttribute("Id", id),
                    new XAttribute("ParentId", parentId.ToString()),
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
        //  Write IPagePlaceholderContent
        // ------------------------------------------------------------------------

        private static void WritePlaceholderContent(SetupContext context, string dstDir,
            AuthKitPageDef[] pages)
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

            // AuthKit PageId seti - eski kayitlari temizle
            var authKitPageIdSet = new HashSet<string>(AllAuthKitPageIds());

            // Eski AuthKit placeholder kayitlarini kaldir
            int removed = 0;
            var toRemove = new List<XElement>();
            foreach (XElement dstEl in dstRoot.Elements(elementName))
            {
                if (authKitPageIdSet.Contains((string)dstEl.Attribute("PageId"))
                    && (string)dstEl.Attribute("PlaceHolderId") == "content")
                {
                    toRemove.Add(dstEl);
                }
            }
            foreach (var el in toRemove) { el.Remove(); removed++; }

            // Yeni placeholder content ekle
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

                string contentHtml;
                if (!string.IsNullOrEmpty(page.RazorFunction))
                {
                    // Auth sayfalari: Razor fonksiyonunu gom
                    contentHtml =
                        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
                        "\t<head>\n\t</head>\n" +
                        "\t<body>\n\n" +
                        "<f:function name=\"" + page.RazorFunction + "\" " +
                        "xmlns:f=\"http://www.composite.net/ns/function/1.0\" />\n\n" +
                        "\t</body>\n" +
                        "</html>";
                }
                else
                {
                    // Yonetim sayfalari / AuthKit: bos content (template kendini render eder)
                    contentHtml =
                        "<html xmlns=\"http://www.w3.org/1999/xhtml\">\n" +
                        "\t<head>\n\t</head>\n" +
                        "\t<body>\n\n" +
                        "\t</body>\n" +
                        "</html>";
                }

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
