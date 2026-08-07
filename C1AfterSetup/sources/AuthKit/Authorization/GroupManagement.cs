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
                        return (false, "A group with this name already exists.", null);

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
                return (false, "A database error occurred while creating the group.", null);
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
                        return (false, "Group to update not found.", null);

                    bool nameExists = connection.Get<AuthKit.Data.Authorization.Group>()
                                                .Any(g => g.GroupName.Equals(groupName, StringComparison.OrdinalIgnoreCase) && g.Id != groupId);
                    if (nameExists)
                        return (false, "Another group with this name already exists.", null);

                    groupToUpdate.GroupName = groupName;
                    groupToUpdate.Description = description;
                    connection.Update(groupToUpdate);

                    return (true, null, groupToUpdate);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthorizationManager.UpdateGroup", ex.Message);
                return (false, "A database error occurred while updating the group.", null);
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
                        return (false, "Group to delete not found.");

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
                return (false, "A database error occurred while deleting the group and its related data.");
            }
        }
    }
}
