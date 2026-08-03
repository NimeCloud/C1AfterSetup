using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Composite.Data;

namespace AuthKit.Authorization
{
    /// <summary>
    /// Koddaki PermissionKeys sinifindaki tum yetki tanimlarini Reflection ile okuyup
    /// otomatik olarak C1 CMS veritabanina (AuthKit.Data.Authorization.Permission tablosuna) senkronize eder.
    /// Ayrica eksik modulleri de otomatik olusturur.
    /// </summary>
    public class PermissionSyncService
    {
        /// <summary>
        /// PermissionKeys sinifindaki tum yetkileri veritabanina senkronize eder.
        /// </summary>
        public void SynchronizePermissions()
        {
            var codePermissions = GetAllPermissionsFromClass(typeof(PermissionKeys));

            var dbPermissions = DataFacade.GetData<AuthKit.Data.Authorization.Permission>()
                                           .ToDictionary(p => p.Name, p => p);

            var dbModules = DataFacade.GetData<AuthKit.Data.Authorization.Module>()
                                       .ToDictionary(m => m.ModuleName, m => m.Id, StringComparer.OrdinalIgnoreCase);

            foreach (var codePerm in codePermissions)
            {
                if (!dbModules.TryGetValue(codePerm.ModuleName, out string moduleId))
                {
                    var newModule = DataFacade.BuildNew<AuthKit.Data.Authorization.Module>();
                    newModule.ModuleName = codePerm.ModuleName;
                    newModule.Description = $"Auto-generated module: {codePerm.ModuleName}";
                    newModule.SortOrder = 999;
                    newModule = DataFacade.AddNew(newModule);
                    moduleId = newModule.Id;
                    dbModules[codePerm.ModuleName] = moduleId;
                }

                if (!dbPermissions.ContainsKey(codePerm.PermissionName))
                {
                    var newPermission = DataFacade.BuildNew<AuthKit.Data.Authorization.Permission>();
                    newPermission.Name = codePerm.PermissionName;
                    newPermission.Description = codePerm.Description ?? codePerm.PermissionName;
                    newPermission.RefModuleId = moduleId;
                    DataFacade.AddNew(newPermission);
                }
                else
                {
                    var existing = dbPermissions[codePerm.PermissionName];
                    bool needsUpdate = false;

                    if (existing.Description != (codePerm.Description ?? codePerm.PermissionName))
                    {
                        existing.Description = codePerm.Description ?? codePerm.PermissionName;
                        needsUpdate = true;
                    }

                    if (existing.RefModuleId != moduleId)
                    {
                        existing.RefModuleId = moduleId;
                        needsUpdate = true;
                    }

                    if (needsUpdate)
                        DataFacade.Update(existing);
                }
            }
        }

        #region Internal helpers

        private class CodePermission
        {
            public string PermissionName { get; set; }
            public string Description { get; set; }
            public string ModuleName { get; set; }
        }

        private static List<CodePermission> GetAllPermissionsFromClass(Type rootType)
        {
            var result = new List<CodePermission>();
            TraversePermissions(rootType, null, result);
            return result;
        }

        private static void TraversePermissions(Type type, string prefix, List<CodePermission> result)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                             .Where(f => f.FieldType == typeof(string));

            foreach (var field in fields)
            {
                string permissionName = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
                var attribute = field.GetCustomAttribute<PermissionInfoAttribute>();
                string description = attribute?.Description ?? permissionName;

                string moduleName = string.IsNullOrEmpty(prefix) ? type.Name : prefix;

                result.Add(new CodePermission
                {
                    PermissionName = permissionName,
                    Description = description,
                    ModuleName = moduleName
                });
            }

            var nestedTypes = type.GetNestedTypes(BindingFlags.Public);
            foreach (var nestedType in nestedTypes)
            {
                string newPrefix = string.IsNullOrEmpty(prefix) ? nestedType.Name : $"{prefix}.{nestedType.Name}";
                TraversePermissions(nestedType, newPrefix, result);
            }
        }

        #endregion
    }
}
