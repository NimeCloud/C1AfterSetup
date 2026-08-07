using Composite.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web;

namespace AuthKit.Authorization
{
    public static partial class AuthorizationManager
    {
        /// <summary>
        /// Login sayfasının C1 page GUID'i. Uygulama tarafında set edilmelidir.
        /// </summary>
        public static Guid LoginPageId { get; set; } = Guid.Empty;

        /// <summary>
        /// RAZOR SAYFALARI İÇİN MERKEZİ YETKİ KONTROLÜ
        /// Yetkisi olmayan kullanıcıları login sayfasına yönlendirir.
        /// </summary>
        public static void CheckPagePermission(string permissionKey)
        {
            var currentUser = AuthKit.Authentication.AuthenticationManager.GetCurrentUser();
            if (currentUser == null || !HasPermission(currentUser, permissionKey))
            {
                if (LoginPageId != Guid.Empty)
                    HttpContext.Current.Response.Redirect($"~/page({LoginPageId})");
                else
                    HttpContext.Current.Response.Redirect(
                        AuthKit.C1.C1UrlHelper.GetUrlFromPageId(new Guid("f6f06000-0000-0000-0000-f6f0f6f0f6f0"), "~/login"));
            }
        }

        public static string CheckApiPermission(string permissionKey)
        {
            var currentUser = AuthKit.Authentication.AuthenticationManager.GetCurrentUser();

            if (currentUser == null)
            {
                HttpContext.Current.Response.StatusCode = 401;
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { error = "You must be logged in to perform this operation." });
            }

            if (!HasPermission(currentUser, permissionKey))
            {
                HttpContext.Current.Response.StatusCode = 403;

                var permissionInfo = GetPermissionAttribute(permissionKey);
                string errorMessage = "You do not have the required permission to perform this operation.";

                if (permissionInfo != null && !string.IsNullOrEmpty(permissionInfo.ErrorMessage))
                    errorMessage = permissionInfo.ErrorMessage;

                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { error = errorMessage });
            }

            return null;
        }

        /// <summary>
        /// Veritabanındaki tüm modülleri ve onlara bağlı tüm yetkileri hiyerarşik bir yapıda döndürür.
        /// </summary>
        public static object GetAllModulesAndPermissions()
        {
            using (var conn = new DataConnection())
            {
                var modules = conn.Get<AuthKit.Data.Authorization.Module>().OrderBy(m => m.SortOrder).ToList();
                var permissions = conn.Get<AuthKit.Data.Authorization.Permission>().ToList();

                var result = modules.Select(m => new
                {
                    m.Id,
                    m.ModuleName,
                    Permissions = permissions
                        .Where(p => p.RefModuleId == m.Id)
                        .Select(p => new { p.Id, p.Name, p.Description })
                        .OrderBy(p => p.Name)
                });

                return result.ToList();
            }
        }

        /// <summary>
        /// Bir kullanıcıya doğrudan atanan yetkileri ve modül durumlarını günceller.
        /// </summary>
        public static (bool IsSuccess, string ErrorMessage) UpdateDirectUserPermissions(string userId, List<UserPermissionUpdate> permissions, List<ModuleStateUpdate> moduleStates)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    foreach (var pUpdate in permissions)
                    {
                        var existingPermission = connection.Get<AuthKit.Data.Authorization.PermissionInUser>()
                            .FirstOrDefault(p => p.RefUserId == userId && p.RefPermissionId == pUpdate.PermissionId);

                        if (pUpdate.State == "clear")
                        {
                            if (existingPermission != null)
                                connection.Delete(existingPermission);
                        }
                        else
                        {
                            bool isAllowed = pUpdate.State == "allow";
                            if (existingPermission != null)
                            {
                                existingPermission.IsAllowed = isAllowed;
                                connection.Update(existingPermission);
                            }
                            else
                            {
                                var newPermission = connection.CreateNew<AuthKit.Data.Authorization.PermissionInUser>();
                                newPermission.RefUserId = userId;
                                newPermission.RefPermissionId = pUpdate.PermissionId;
                                newPermission.IsAllowed = isAllowed;
                                connection.Add(newPermission);
                            }
                        }
                    }

                    if (moduleStates != null && moduleStates.Any())
                    {
                        var allModules = connection.Get<AuthKit.Data.Authorization.Module>().ToDictionary(m => m.ModuleName, m => m.Id, StringComparer.OrdinalIgnoreCase);

                        foreach (var msUpdate in moduleStates)
                        {
                            if (allModules.TryGetValue(msUpdate.ModuleName, out string moduleId))
                            {
                                var existingState = connection.Get<AuthKit.Data.Authorization.UserModuleState>()
                                    .FirstOrDefault(ums => ums.RefUserId == userId && ums.RefModuleId == moduleId);

                                if (existingState != null)
                                {
                                    existingState.IsActive = msUpdate.IsActive;
                                    connection.Update(existingState);
                                }
                                else
                                {
                                    var newState = connection.CreateNew<AuthKit.Data.Authorization.UserModuleState>();
                                    newState.RefUserId = userId;
                                    newState.RefModuleId = moduleId;
                                    newState.IsActive = msUpdate.IsActive;
                                    connection.Add(newState);
                                }
                            }
                        }
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("UpdateDirectUserPermissions", ex.Message);
                return (false, "A database error occurred while updating user permissions.");
            }
        }

        public static Dictionary<string, bool> GetPermissionsForGroup(string groupId)
        {
            using (var conn = new DataConnection())
            {
                return conn.Get<AuthKit.Data.Authorization.PermissionInGroup>()
                           .Where(p => p.RefGroupId == groupId)
                           .ToDictionary(p => p.RefPermissionId, p => p.IsAllowed);
            }
        }

        /// <summary>
        /// Bir grubun yetkilerini günceller.
        /// </summary>
        public static (bool IsSuccess, string ErrorMessage) UpdatePermissionsForGroup(string groupId, List<GroupPermissionUpdate> permissions)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    foreach (var pUpdate in permissions)
                    {
                        var existingPermission = connection.Get<AuthKit.Data.Authorization.PermissionInGroup>()
                            .FirstOrDefault(p => p.RefGroupId == groupId && p.RefPermissionId == pUpdate.PermissionId);

                        if (pUpdate.State == "clear")
                        {
                            if (existingPermission != null)
                                connection.Delete(existingPermission);
                        }
                        else
                        {
                            bool isAllowed = pUpdate.State == "allow";
                            if (existingPermission != null)
                            {
                                existingPermission.IsAllowed = isAllowed;
                                connection.Update(existingPermission);
                            }
                            else
                            {
                                var newPermission = connection.CreateNew<AuthKit.Data.Authorization.PermissionInGroup>();
                                newPermission.RefGroupId = groupId;
                                newPermission.RefPermissionId = pUpdate.PermissionId;
                                newPermission.IsAllowed = isAllowed;
                                connection.Add(newPermission);
                            }
                        }
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("UpdatePermissionsForGroup", ex.Message);
                return (false, "A database error occurred while updating group permissions.");
            }
        }

        /// <summary>
        /// Bir kullanıcının tüm etkin yetkilerini ve modül durumlarını hesaplar.
        /// </summary>
        public static object GetEffectivePermissionsForUser(string userId)
        {
            using (var connection = new DataConnection())
            {
                var allModules = connection.Get<AuthKit.Data.Authorization.Module>().OrderBy(m => m.SortOrder).ToList();
                var allPermissions = connection.Get<AuthKit.Data.Authorization.Permission>().ToList();
                var allGroups = connection.Get<AuthKit.Data.Authorization.Group>().ToDictionary(g => g.Id, g => g.GroupName);

                var userModuleStates = connection.Get<AuthKit.Data.Authorization.UserModuleState>()
                    .Where(ums => ums.RefUserId == userId)
                    .ToDictionary(ums => ums.RefModuleId, ums => ums.IsActive);

                var directUserPermissions = connection.Get<AuthKit.Data.Authorization.PermissionInUser>()
                    .Where(p => p.RefUserId == userId)
                    .ToDictionary(p => p.RefPermissionId, p => p.IsAllowed);

                var userGroupIds = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                    .Where(u => u.RefUserId == userId)
                    .Select(u => u.RefGroupId)
                    .ToList();

                var groupPermissions = connection.Get<AuthKit.Data.Authorization.PermissionInGroup>()
                    .Where(p => userGroupIds.Contains(p.RefGroupId))
                    .ToList();

                var inheritedPermissions = new Dictionary<string, (bool IsAllowed, string SourceGroup)>();
                foreach (var p in groupPermissions.OrderByDescending(gp => gp.IsAllowed))
                {
                    if (!inheritedPermissions.ContainsKey(p.RefPermissionId))
                    {
                        inheritedPermissions.Add(p.RefPermissionId, (p.IsAllowed, allGroups.ContainsKey(p.RefGroupId) ? allGroups[p.RefGroupId] : "Bilinmeyen Grup"));
                    }
                }

                var result = allModules.Select(module => new
                {
                    module.Id,
                    module.ModuleName,
                    Permissions = allPermissions
                        .Where(p => p.RefModuleId == module.Id)
                        .OrderBy(p => p.Name)
                        .Select(p =>
                        {
                            string effectiveState = "NotSet";
                            string inheritedState = "NotSet";
                            string source = null;

                            if (inheritedPermissions.ContainsKey(p.Id))
                            {
                                var inherited = inheritedPermissions[p.Id];
                                inheritedState = inherited.IsAllowed ? "InheritedAllow" : "InheritedDeny";
                                source = inherited.SourceGroup;
                            }

                            if (directUserPermissions.ContainsKey(p.Id))
                                effectiveState = directUserPermissions[p.Id] ? "DirectAllow" : "DirectDeny";
                            else
                                effectiveState = inheritedState;

                            return new
                            {
                                p.Id,
                                p.Name,
                                p.Description,
                                State = effectiveState,
                                InheritedState = inheritedState,
                                Source = source
                            };
                        })
                });

                return result.ToList();
            }
        }

        /// <summary>
        /// Bir grubun tüm yetki atamalarını başka bir gruba kopyalar.
        /// </summary>
        public static (bool IsSuccess, string ErrorMessage) CloneGroupPermissions(string sourceGroupId, string newGroupId)
        {
            if (sourceGroupId == newGroupId)
                return (false, "Source and target group cannot be the same.");

            try
            {
                using (var connection = new DataConnection())
                {
                    var sourcePermissions = connection.Get<AuthKit.Data.Authorization.PermissionInGroup>()
                        .Where(p => p.RefGroupId == sourceGroupId)
                        .ToList();

                    var newPermissions = sourcePermissions.Select(sp =>
                    {
                        var newP = connection.CreateNew<AuthKit.Data.Authorization.PermissionInGroup>();
                        newP.RefGroupId = newGroupId;
                        newP.RefPermissionId = sp.RefPermissionId;
                        newP.IsAllowed = sp.IsAllowed;
                        return newP;
                    }).ToList();

                    foreach (var p in newPermissions) { connection.Add(p); }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("CloneGroupPermissions", ex.Message);
                return (false, "A database error occurred while cloning group permissions.");
            }
        }

        /// <summary>
        /// Bir kullanıcının grup üyeliklerini, doğrudan yetkilerini ve modül ayarlarını başka bir kullanıcıya kopyalar.
        /// </summary>
        public static (bool IsSuccess, string ErrorMessage) CloneUserPermissionsAndGroups(string sourceUserId, string newUserId)
        {
            if (sourceUserId == newUserId)
                return (false, "Source and target user cannot be the same.");

            try
            {
                using (var connection = new DataConnection())
                {
                    var sourceGroups = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                        .Where(u => u.RefUserId == sourceUserId)
                        .ToList();

                    foreach (var sourceGroupMembership in sourceGroups)
                    {
                        var newUserInGroup = connection.CreateNew<AuthKit.Data.Authorization.UserInGroup>();
                        newUserInGroup.RefUserId = newUserId;
                        newUserInGroup.RefGroupId = sourceGroupMembership.RefGroupId;
                        connection.Add(newUserInGroup);
                    }

                    var sourceDirectPermissions = connection.Get<AuthKit.Data.Authorization.PermissionInUser>()
                        .Where(p => p.RefUserId == sourceUserId)
                        .ToList();

                    foreach (var sourcePermission in sourceDirectPermissions)
                    {
                        var newPermission = connection.CreateNew<AuthKit.Data.Authorization.PermissionInUser>();
                        newPermission.RefUserId = newUserId;
                        newPermission.RefPermissionId = sourcePermission.RefPermissionId;
                        newPermission.IsAllowed = sourcePermission.IsAllowed;
                        connection.Add(newPermission);
                    }

                    var sourceModuleStates = connection.Get<AuthKit.Data.Authorization.UserModuleState>()
                        .Where(ums => ums.RefUserId == sourceUserId)
                        .ToList();

                    foreach (var sourceState in sourceModuleStates)
                    {
                        var newState = connection.CreateNew<AuthKit.Data.Authorization.UserModuleState>();
                        newState.RefUserId = newUserId;
                        newState.RefModuleId = sourceState.RefModuleId;
                        newState.IsActive = sourceState.IsActive;
                        connection.Add(newState);
                    }
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("CloneUserPermissionsAndGroups", ex.Message);
                return (false, "A database error occurred while cloning user settings.");
            }
        }
    }
}
