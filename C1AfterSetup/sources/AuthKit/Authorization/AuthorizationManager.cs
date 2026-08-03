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
                return (false, "Grup üyeleri güncellenirken bir hata oluştu.");
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
                        var relationsToDelete = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                            .Where(ug => ug.RefUserId == userId && groupIdsToRemove.Contains(ug.RefGroupId)).ToList();
                        connection.Delete<AuthKit.Data.Authorization.UserInGroup>(relationsToDelete);
                    }

                    if (groupIdsToAdd != null && groupIdsToAdd.Any())
                    {
                        var relationsToAdd = groupIdsToAdd.Select(groupId =>
                        {
                            var newMember = connection.CreateNew<AuthKit.Data.Authorization.UserInGroup>();
                            newMember.RefUserId = userId;
                            newMember.RefGroupId = groupId;
                            return newMember;
                        }).ToList();

                        foreach (var relation in relationsToAdd)
                            connection.Add(relation);
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("UpdateUserGroupsDelta", ex.Message);
                return (false, "Kullanıcının grup üyelikleri güncellenirken bir hata oluştu.");
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

            if (IsUserInGroup(user.Id, GroupKeys.System.Administrators))
                return true;

            if (HasDeny(permissionId, user.Id, PermissionScope.User)) return false;
            if (HasDeny(permissionId, user.Id, PermissionScope.Group)) return false;
            if (HasAllow(permissionId, user.Id, PermissionScope.User)) return true;
            if (HasAllow(permissionId, user.Id, PermissionScope.Group)) return true;
            if (HasDeny(permissionId, user.Id, PermissionScope.Everyone)) return false;
            if (HasAllow(permissionId, user.Id, PermissionScope.Everyone)) return true;

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
