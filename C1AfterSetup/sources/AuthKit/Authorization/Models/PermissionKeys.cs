namespace AuthKit.Authorization
{
    /// <summary>
    /// Temel (built-in) yetki anahtarları.
    /// Uygulamaya özel anahtarlar PermissionKeys.App.cs icinde tanimlanmalidir.
    /// </summary>
    public static partial class PermissionKeys
    {
        public static class Test
        {
            public const string View = "Test.View";
            public const string Add = "Test.Add";
            public const string Delete = "Test.Delete";
        }

        public static class Pages
        {
            public const string View = "Pages.View";
            public const string Edit = "Pages.Edit";
            public const string Publish = "Pages.Publish";
        }

        public static class Auth
        {
            public static class Groups
            {
                [PermissionInfo("Tüm grup yönetimi yetkilerini kapsar.")]
                public const string Manage = "Auth.Groups.Manage";

                [PermissionInfo("Grupları listeleme ve görüntüleme yetkisi.")]
                public const string View = "Auth.Groups.View";

                [PermissionInfo("Yeni bir kullanıcı grubu ekleme yetkisi.")]
                public const string Add = "Auth.Groups.Add";

                [PermissionInfo("Mevcut bir grubun bilgilerini düzenleme yetkisi.")]
                public const string Edit = "Auth.Groups.Edit";

                [PermissionInfo("Bir kullanıcı grubunu silme yetkisi.")]
                public const string Delete = "Auth.Groups.Delete";
            }

            public static class Users
            {
                public const string Manage = "Auth.Users.Manage";
                public const string View = "Auth.Users.View";
                public const string Add = "Auth.Users.Add";
                public const string Edit = "Auth.Users.Edit";
                public const string Delete = "Auth.Users.Delete";
            }

            public static class KeyTreeStore
            {
                public const string Manage = "Auth.KeyTreeStore.Manage";
                public const string View = "Auth.KeyTreeStore.View";
                public const string Add = "Auth.KeyTreeStore.Add";
                public const string Edit = "Auth.KeyTreeStore.Edit";
                public const string Delete = "Auth.KeyTreeStore.Delete";
            }

            public static class Permissions
            {
                [PermissionInfo("Gruplara yetki atama yetkisi.")]
                public const string Assign = "Auth.Permissions.Assign";

                [PermissionInfo("Kullanıcılara doğrudan yetki atama yetkisi.")]
                public const string AssignToUser = "Auth.Permissions.AssignToUser";
            }
        }
    }
}
