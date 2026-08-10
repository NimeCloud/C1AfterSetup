using Composite.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace AuthKit.Authorization
{
    [Flags]
    public enum PermissionScope
    {
        User = 1,
        Group = 2,
        Everyone = 4
    }

    public enum PermissionWeight
    {
        Deny = 0,
        Allow = 1
    }

    public enum PermissionDirection
    {
        UserToEveryone = 0,
        EveryoneToUser = 1
    }

    public static partial class AuthorizationManager
    {
        private static readonly string EVERYONE_GROUP_ID = "00000001";

        #region Permission Tanımlama

        public static string AddPermission(string description)
        {
            var perm = DataFacade.GetData<AuthKit.Data.Authorization.Permission>()
                        .FirstOrDefault(p => p.Description == description);

            if (perm == null)
            {
                perm = DataFacade.BuildNew<AuthKit.Data.Authorization.Permission>();
                perm.Description = description;
                perm = DataFacade.AddNew(perm);
            }

            return perm.Id;
        }
        #endregion

        #region Basit Allow/Deny Kontrolleri

        public static bool HasAllow(string permissionId, string userId, PermissionScope scopes = PermissionScope.User | PermissionScope.Group | PermissionScope.Everyone)
        {
            return HasAllowOrDeny(permissionId, userId, scopes, PermissionWeight.Allow);
        }

        public static bool HasDeny(string permissionId, string userId, PermissionScope scopes = PermissionScope.User | PermissionScope.Group | PermissionScope.Everyone)
        {
            return HasAllowOrDeny(permissionId, userId, scopes, PermissionWeight.Deny);
        }

        private static bool HasAllowOrDeny(string permissionId, string userId, PermissionScope scopes, PermissionWeight weight)
        {
            // User
            if (scopes.HasFlag(PermissionScope.User))
            {
                bool userHas = DataFacade.GetData<AuthKit.Data.Authorization.PermissionInUser>()
                    .Any(p => p.RefPermissionId == permissionId && p.RefUserId == userId && p.IsAllowed == (weight == PermissionWeight.Allow));
                if (userHas) return true;
            }

            // Group
            if (scopes.HasFlag(PermissionScope.Group))
            {
                var groupIds = DataFacade.GetData<AuthKit.Data.Authorization.UserInGroup>()
                                .Where(u => u.RefUserId == userId)
                                .Select(u => u.RefGroupId);

                bool groupHas = DataFacade.GetData<AuthKit.Data.Authorization.PermissionInGroup>()
                    .Any(p => groupIds.Contains(p.RefGroupId) && p.RefPermissionId == permissionId && p.IsAllowed == (weight == PermissionWeight.Allow));
                if (groupHas) return true;
            }

            // Everyone
            if (scopes.HasFlag(PermissionScope.Everyone))
            {
                bool everyoneHas = DataFacade.GetData<AuthKit.Data.Authorization.PermissionInGroup>()
                    .Any(p => p.RefGroupId == EVERYONE_GROUP_ID && p.RefPermissionId == permissionId && p.IsAllowed == (weight == PermissionWeight.Allow));
                if (everyoneHas) return true;
            }

            return false;
        }
        #endregion

        #region Permission Atama (User / Group)

        public static void SetUserPermission(string permissionId, string userId, bool allow)
        {
            var existing = DataFacade.GetData<AuthKit.Data.Authorization.PermissionInUser>()
                            .FirstOrDefault(p => p.RefUserId == userId && p.RefPermissionId == permissionId);

            if (existing != null)
            {
                existing.IsAllowed = allow;
                DataFacade.Update(existing);
            }
            else
            {
                var p = DataFacade.BuildNew<AuthKit.Data.Authorization.PermissionInUser>();
                p.RefUserId = userId;
                p.RefPermissionId = permissionId;
                p.IsAllowed = allow;
                DataFacade.AddNew(p);
            }
        }

        public static void SetGroupPermission(string permissionId, string groupId, bool allow)
        {
            var existing = DataFacade.GetData<AuthKit.Data.Authorization.PermissionInGroup>()
                            .FirstOrDefault(p => p.RefGroupId == groupId && p.RefPermissionId == permissionId);

            if (existing != null)
            {
                existing.IsAllowed = allow;
                DataFacade.Update(existing);
            }
            else
            {
                var p = DataFacade.BuildNew<AuthKit.Data.Authorization.PermissionInGroup>();
                p.RefGroupId = groupId;
                p.RefPermissionId = permissionId;
                p.IsAllowed = allow;
                DataFacade.AddNew(p);
            }
        }
        #endregion

        #region Reflection / Attribute Yardımcısı

        private static PermissionInfoAttribute GetPermissionAttribute(string permissionKey)
        {
            var nestedTypes = typeof(PermissionKeys).GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
            foreach (var type in nestedTypes.Concat(new[] { typeof(PermissionKeys) }))
            {
                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
                foreach (var field in fields)
                {
                    if (field.GetValue(null)?.ToString() == permissionKey)
                    {
                        return field.GetCustomAttribute<PermissionInfoAttribute>();
                    }
                }
            }
            return null;
        }
        #endregion

        #region Grup Üyelik Delta Güncelleme

        public static (bool IsSuccess, string ErrorMessage) UpdateGroupMembersDelta(string groupId, List<string> userIdsToAdd, List<string> userIdsToRemove)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    if (userIdsToRemove != null && userIdsToRemove.Any())
                    {
                        var relationsToDelete = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                            .Where(ug => ug.RefGroupId == groupId && userIdsToRemove.Contains(ug.RefUserId)).ToList();
                        connection.Delete<AuthKit.Data.Authorization.UserInGroup>(relationsToDelete);
                    }

                    if (userIdsToAdd != null && userIdsToAdd.Any())
                    {
                        foreach (var userId in userIdsToAdd)
                        {
                            var newMember = connection.CreateNew<AuthKit.Data.Authorization.UserInGroup>();
                            newMember.RefGroupId = groupId;
                            newMember.RefUserId = userId;
                            connection.Add(newMember);
                        }
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("UpdateGroupMembersDelta", ex.Message);
                return (false, "An error occurred while updating group members.");
            }
        }

        public static (bool IsSuccess, string ErrorMessage) UpdateUserGroupsDelta(string userId, List<string> groupIdsToAdd, List<string> groupIdsToRemove)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    if (groupIdsToRemove != null && groupIdsToRemove.Any())
                    {
                        // Aynı (userId, groupId) çifti için birden fazla kayıt varsa hepsini sil.
                        var relationsToDelete = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                            .Where(ug => ug.RefUserId == userId && groupIdsToRemove.Contains(ug.RefGroupId)).ToList();
                        connection.Delete<AuthKit.Data.Authorization.UserInGroup>(relationsToDelete);
                    }

                    if (groupIdsToAdd != null && groupIdsToAdd.Any())
                    {
                        // DUPLICATE KORUMASI: her (userId, groupId) çifti için TEK kayıt olmalı.
                        // Mevcut üyelikleri bir kere oku; aynı çiftten birden fazla kayıt varsa
                        // tekilleştir (ilkini true yap, fazlalıkları sil); yoksa yeni kayıt ekle.
                        var existingMemberships = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                            .Where(ug => ug.RefUserId == userId)
                            .ToList();

                        foreach (var groupId in groupIdsToAdd)
                        {
                            if (string.IsNullOrEmpty(groupId)) continue;

                            var duplicates = existingMemberships.Where(ug => ug.RefGroupId == groupId).ToList();
                            if (duplicates.Any())
                            {
                                // Tekilleştir: ilk kaydı true yap, fazla/false kayıtları sil.
                                var keep = duplicates[0];
                                if (!keep.IsAllowed)
                                {
                                    keep.IsAllowed = true;
                                    connection.Update(keep);
                                }
                                if (duplicates.Count > 1)
                                {
                                    connection.Delete<AuthKit.Data.Authorization.UserInGroup>(duplicates.Skip(1).ToList());
                                }
                                continue;
                            }

                            var newMember = connection.CreateNew<AuthKit.Data.Authorization.UserInGroup>();
                            newMember.RefUserId = userId;
                            newMember.RefGroupId = groupId;
                            newMember.IsAllowed = true;
                            connection.Add(newMember);
                        }
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("UpdateUserGroupsDelta", ex.Message);
                return (false, "An error occurred while updating the user's group memberships.");
            }
        }
        #endregion

        #region Yetki Kontrolü

        /// <summary>
        /// Bir kullanıcının belirli bir yetkiye sahip olup olmadığını DENY > ALLOW hiyerarşisine göre kontrol eder.
        /// </summary>
        public static bool HasPermission(AuthKit.Data.Authentication.User user, string permissionName)
        {
            if (user == null || string.IsNullOrWhiteSpace(permissionName))
                return false;

            var permission = DataFacade.GetData<AuthKit.Data.Authorization.Permission>()
                                       .FirstOrDefault(p => p.Name == permissionName);
            if (permission == null)
            {
                Composite.Core.Log.LogWarning("Authorization", $"Permission with key '{permissionName}' not found.");
                return false;
            }
            string permissionId = permission.Id;

            // Kural önceliği (kullanıcı isteği):
            // 1) Önce DB'den zorlamalı ENGEL (DENY) kontrol edilir — admin olsa bile geçerli.
            // 2) Zorlamalı engel yoksa ve DB'den ALLOW verilmişse izin verilir.
            // 3) DB'de ne engel ne de yetki varsa, System.Administrators üyeliğine bakılır.
            //    Böylece eskiden admin olan bir kullanıcı, admin üyeliği kaldırılmadan
            //    tek tek yetkilerden engellenebilir.
            if (HasDeny(permissionId, user.Id, PermissionScope.User)) return false;
            if (HasDeny(permissionId, user.Id, PermissionScope.Group)) return false;
            if (HasDeny(permissionId, user.Id, PermissionScope.Everyone)) return false;

            if (HasAllow(permissionId, user.Id, PermissionScope.User)) return true;
            if (HasAllow(permissionId, user.Id, PermissionScope.Group)) return true;
            if (HasAllow(permissionId, user.Id, PermissionScope.Everyone)) return true;

            if (IsUserInGroup(user.Id, GroupKeys.System.Administrators))
                return true;

            return false;
        }

        public static bool IsUserInGroup(string userId, string groupName)
        {
            var group = DataFacade.GetData<AuthKit.Data.Authorization.Group>()
                                  .FirstOrDefault(g => g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase));

            if (group == null) return false;

            return DataFacade.GetData<AuthKit.Data.Authorization.UserInGroup>()
                             .Any(u => u.RefUserId == userId && u.RefGroupId == group.Id);
        }

        /// <summary>
        /// Ensures the "System.Administrators" group exists in the database (idempotent).
        /// </summary>
        public static void EnsureAdministratorsGroup()
        {
            string name = GroupKeyName(GroupKeys.System.Administrators, "System.Administrators");
            using (var connection = new DataConnection())
            {
                bool exists = connection.Get<AuthKit.Data.Authorization.Group>()
                    .Any(g => g.GroupName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (exists) return;

                var group = connection.CreateNew<AuthKit.Data.Authorization.Group>();
                group.GroupName = name;
                group.Description = "System administrators - full access.";
                connection.Add(group);
            }
        }

        /// <summary>
        /// Ensures a user is a member of "System.Administrators" when they should be, so the
        /// system is never left without an administrator. A user is granted the membership if
        /// the current C1 user is a C1 Administrator, OR if the administrators group currently
        /// has no members yet (first-user bootstrap). Idempotent.
        /// </summary>
        public static void EnsureAdministratorMembership(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;

            string name = GroupKeyName(GroupKeys.System.Administrators, "System.Administrators");
            string adminGroupId;
            using (var connection = new DataConnection())
            {
                var adminGroup = connection.Get<AuthKit.Data.Authorization.Group>()
                    .FirstOrDefault(g => g.GroupName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (adminGroup == null)
                {
                    var group = connection.CreateNew<AuthKit.Data.Authorization.Group>();
                    group.GroupName = name;
                    group.Description = "System administrators - full access.";
                    adminGroup = connection.Add(group);
                }
                adminGroupId = adminGroup.Id;
            }

            using (var connection = new DataConnection())
            {
                // DUPLICATE KORUMASI: aynı (userId, adminGroupId) çifti için tek kayıt olmalı.
                // Birden fazla kayıt varsa tekilleştir: true olanı koru, fazla/false kayıtları sil.
                var existingRelations = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                    .Where(u => u.RefUserId == userId && u.RefGroupId == adminGroupId)
                    .ToList();

                if (existingRelations.Count > 0)
                {
                    // İlk kaydı true yap (mevcut kaydı update et), fazlalıkları sil.
                    var keep = existingRelations[0];
                    if (!keep.IsAllowed)
                    {
                        keep.IsAllowed = true;
                        connection.Update(keep);
                    }
                    if (existingRelations.Count > 1)
                    {
                        var duplicates = existingRelations.Skip(1).ToList();
                        connection.Delete<AuthKit.Data.Authorization.UserInGroup>(duplicates);
                    }
                    return;
                }

                // Bootstrap: admin grubu tamamen boşken (ilk kullanıcı) veya C1 Administrator iken
                // ekle — böylece sistem asla adminsiz kalmaz. Ancak grup zaten üyeli doluysa
                // otomatik ekleme YAPILMAZ; üyelikler el ile yönetilir (kullanıcı çıkardığında geri gelmez).
                bool groupHasMembers = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                    .Any(u => u.RefGroupId == adminGroupId);

                if (groupHasMembers) return;

                bool isC1Admin = false;
                try { isC1Admin = AuthKit.C1.C1Security.IsCurrentUserInAdministratorsGroup(); } catch { }

                if (!isC1Admin) return;

                var relation = connection.CreateNew<AuthKit.Data.Authorization.UserInGroup>();
                relation.RefUserId = userId;
                relation.RefGroupId = adminGroupId;
                relation.IsAllowed = true;
                connection.Add(relation);
            }
        }

        private static string GroupKeyName(string keyValue, string fallback)
        {
            return string.IsNullOrWhiteSpace(keyValue) ? fallback : keyValue;
        }

        public static IEnumerable<AuthKit.Data.Authorization.Module> GetUserVisibleModules(AuthKit.Data.Authentication.User user)
        {
            if (user == null) return Enumerable.Empty<AuthKit.Data.Authorization.Module>();

            if (IsUserInGroup(user.Id, GroupKeys.System.Administrators))
                return DataFacade.GetData<AuthKit.Data.Authorization.Module>().OrderBy(m => m.SortOrder);

            var userPermissionIds = new HashSet<String>();

            var userPermissions = DataFacade.GetData<AuthKit.Data.Authorization.PermissionInUser>()
                .Where(p => p.RefUserId == user.Id && p.IsAllowed);
            foreach (var p in userPermissions) userPermissionIds.Add(p.RefPermissionId);

            var groupIds = DataFacade.GetData<AuthKit.Data.Authorization.UserInGroup>()
                .Where(u => u.RefUserId == user.Id).Select(g => g.RefGroupId);
            var groupPermissions = DataFacade.GetData<AuthKit.Data.Authorization.PermissionInGroup>()
                .Where(p => groupIds.Contains(p.RefGroupId) && p.IsAllowed);
            foreach (var p in groupPermissions) userPermissionIds.Add(p.RefPermissionId);

            var visibleModuleIds = DataFacade.GetData<AuthKit.Data.Authorization.Permission>()
                .Where(p => userPermissionIds.Contains(p.Id))
                .Select(p => p.RefModuleId)
                .Distinct();

            return DataFacade.GetData<AuthKit.Data.Authorization.Module>()
                .Where(m => visibleModuleIds.Contains(m.Id))
                .OrderBy(m => m.SortOrder);
        }
        #endregion
    }
}
