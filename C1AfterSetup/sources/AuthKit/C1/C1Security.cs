using System;
using Composite.C1Console.Security;
using Composite.Data;
using Composite.Data.Types;
using System.Linq;

namespace AuthKit.C1
{
    /// <summary>
    /// C1 CMS guvenlik yardimci metotlari.
    /// </summary>
    public static class C1Security
    {
        /// <summary>
        /// C1 CMS'in kendi Administrator grubunda olup olmadigini kontrol eder.
        /// Bu, C1 admin paneli yetkileri icindir; AuthKit yetkilerinden bagimsizdir.
        /// </summary>
        public static bool IsCurrentUserInAdministratorsGroup()
        {
            string currentUsername = UserValidationFacade.GetUsername();

            if (string.IsNullOrEmpty(currentUsername))
                return false;

            IUser user = DataFacade.GetData<IUser>()
                .FirstOrDefault(u => u.Username == currentUsername);

            if (user == null) return false;

            IUserGroup administratorsGroup = DataFacade.GetData<IUserGroup>()
                .FirstOrDefault(g => g.Name == "Administrator");

            if (administratorsGroup == null) return false;

            bool isInGroup = DataFacade.GetData<IUserUserGroupRelation>()
                .Any(r => r.UserId == user.Id && r.UserGroupId == administratorsGroup.Id);

            return isInGroup;
        }

        /// <summary>
        /// Verilen username'in C1 CMS kullanicisi (Composite.Data.Types.IUser) olup olmadigini
        /// kontrol eder. AuthKit Razor panelinde "C1 kullanicisi + DENY yok = otomatik erisim"
        /// kurali icin kullanilir — C1'in kendi Administrator grubundan bagimsizdir.
        /// </summary>
        public static bool IsC1User(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return false;

            try
            {
                return DataFacade.GetData<IUser>()
                    .Any(u => u.Username != null && u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}
