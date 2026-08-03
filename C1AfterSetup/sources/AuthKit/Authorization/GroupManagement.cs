using Composite.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AuthKit.Authorization
{
    public static partial class AuthorizationManager
    {
        /// <summary>
        /// Veritabanına yeni bir kullanıcı grubu ekler.
        /// </summary>
        public static (bool IsSuccess, string ErrorMessage, AuthKit.Data.Authorization.Group NewGroup) CreateGroup(string groupName, string description)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    bool groupExists = connection.Get<AuthKit.Data.Authorization.Group>().Any(g => g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase));
                    if (groupExists)
                        return (false, "Bu isimde bir grup zaten mevcut.", null);

                    var newGroup = connection.CreateNew<AuthKit.Data.Authorization.Group>();
                    newGroup.GroupName = groupName;
                    newGroup.Description = description;
                    newGroup = connection.Add(newGroup);

                    return (true, null, newGroup);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthorizationManager.CreateGroup", ex.Message);
                return (false, "Grup oluşturulurken bir veritabanı hatası oluştu.", null);
            }
        }

        /// <summary>
        /// Veritabanındaki mevcut bir kullanıcı grubunu günceller.
        /// </summary>
        public static (bool IsSuccess, string ErrorMessage, AuthKit.Data.Authorization.Group UpdatedGroup) UpdateGroup(string groupId, string groupName, string description)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    var groupToUpdate = connection.Get<AuthKit.Data.Authorization.Group>().FirstOrDefault(g => g.Id == groupId);
                    if (groupToUpdate == null)
                        return (false, "Güncellenecek grup bulunamadı.", null);

                    bool nameExists = connection.Get<AuthKit.Data.Authorization.Group>()
                                                .Any(g => g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase) && g.Id != groupId);
                    if (nameExists)
                        return (false, "Bu isimde başka bir grup zaten mevcut.", null);

                    groupToUpdate.GroupName = groupName;
                    groupToUpdate.Description = description;
                    connection.Update(groupToUpdate);

                    return (true, null, groupToUpdate);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthorizationManager.UpdateGroup", ex.Message);
                return (false, "Grup güncellenirken bir veritabanı hatası oluştu.", null);
            }
        }

        /// <summary>
        /// Veritabanından bir kullanıcı grubunu ve ilişkili tüm bağlantılarını siler.
        /// </summary>
        public static (bool IsSuccess, string ErrorMessage) DeleteGroup(string groupId)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    var groupToDelete = connection.Get<AuthKit.Data.Authorization.Group>().FirstOrDefault(g => g.Id == groupId);
                    if (groupToDelete == null)
                        return (false, "Silinecek grup bulunamadı.");

                    var userRelations = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                                                  .Where(u => u.RefGroupId == groupId).ToList();
                    if (userRelations.Any())
                        connection.Delete<AuthKit.Data.Authorization.UserInGroup>(userRelations);

                    var permissionRelations = connection.Get<AuthKit.Data.Authorization.PermissionInGroup>()
                                                        .Where(p => p.RefGroupId == groupId).ToList();
                    if (permissionRelations.Any())
                        connection.Delete<AuthKit.Data.Authorization.PermissionInGroup>(permissionRelations);

                    connection.Delete(groupToDelete);
                    return (true, null);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthorizationManager.DeleteGroup", ex.Message);
                return (false, "Grup ve ilişkili veriler silinirken bir veritabanı hatası oluştu.");
            }
        }
    }
}
