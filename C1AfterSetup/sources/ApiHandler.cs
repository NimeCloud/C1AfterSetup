using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Composite.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

public class DataTableRequest
{
    public int Draw { get; set; }
    public int Start { get; set; }
    public int Length { get; set; }
    public SearchRequest Search { get; set; }
    public List<ColumnRequest> Columns { get; set; }
    public List<OrderRequest> Order { get; set; }
}

public class SearchRequest
{
    public string Value { get; set; }
}

public class ColumnRequest
{
    public string Data { get; set; }
    public string Name { get; set; }
    public SearchRequest Search { get; set; }
}

public class OrderRequest
{
    public int Column { get; set; }
    public string Dir { get; set; }
}

public class DataTableResponse<T>
{
    public int Draw { get; set; }
    public int RecordsTotal { get; set; }
    public int RecordsFiltered { get; set; }
    public List<T> Data { get; set; }
}

public class CreateUserRequest
{
    public string UserName { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }
    public bool IsActive { get; set; }
    public bool IsTemplate { get; set; }
    public string SourceUserId { get; set; }
}

public class CreateGroupRequest
{
    public string GroupName { get; set; }
    public string Description { get; set; }
    public string SourceGroupId { get; set; }
}

public class UserForGroupManagement
{
    public string Id { get; set; }
    public string UserName { get; set; }
    public string Email { get; set; }
    public bool IsMember { get; set; }
}

public class GroupForUserManagement
{
    public string Id { get; set; }
    public string GroupName { get; set; }
    public string Description { get; set; }
    public bool IsMember { get; set; }
}

public class GetUsersForGroupManagementRequest
{
    public string GroupId { get; set; }
    public DataTableRequest DtRequest { get; set; }
}

public class GetAllGroupsForUserManagementRequest
{
    public string UserId { get; set; }
    public DataTableRequest DtRequest { get; set; }
}

public class UpdateGroupMembersDeltaRequest
{
    public string GroupId { get; set; }
    public List<string> UserIdsToAdd { get; set; }
    public List<string> UserIdsToRemove { get; set; }
}

public class UpdateUserGroupsDeltaRequest
{
    public string UserId { get; set; }
    public List<string> GroupIdsToAdd { get; set; }
    public List<string> GroupIdsToRemove { get; set; }
}

public class ApiHandler : IHttpHandler
{
    public void ProcessRequest(HttpContext context)
    {
        context.Response.ContentType = "application/json";
        context.Response.Cache.SetCacheability(HttpCacheability.NoCache);

        var action = (context.Items["RouteAction"] as string)
                     ?? context.Request.QueryString["action"]
                     ?? "time";

        try
        {
            switch (action)
            {
                // --- Temel yardımcı uçlar ---
                case "time": Write(context, TimeResponse()); break;
                case "hello": Write(context, HelloResponse(context)); break;
                case "status": Write(context, StatusResponse(context)); break;

                // --- Kullanıcı Yönetimi (AuthKit) ---
                case "GetUsers": Write(context, GetUsers()); break;
                case "GetRealUsers": Write(context, GetRealUsers()); break;
                case "GetTemplateUsers": Write(context, GetTemplateUsers()); break;
                case "GetAllUsersForDropdown": Write(context, GetAllUsersForDropdown()); break;
                case "AddUser": Write(context, AddUser(context)); break;
                case "UpdateUser": Write(context, UpdateUser(context)); break;
                case "DeleteUser": Write(context, DeleteUser(context)); break;
                case "AdminChangePassword": Write(context, AdminChangePassword(context)); break;

                // --- Grup Yönetimi (AuthKit) ---
                case "GetGroups": Write(context, GetGroups()); break;
                case "GetAllGroupsForDropdown": Write(context, GetAllGroupsForDropdown()); break;
                case "AddGroup": Write(context, AddGroup(context)); break;
                case "UpdateGroup": Write(context, UpdateGroup(context)); break;
                case "DeleteGroup": Write(context, DeleteGroup(context)); break;
                case "GetAllGroupIdsForUser": Write(context, GetAllGroupIdsForUser(context)); break;
                case "GetAllUserIdsForGroup": Write(context, GetAllUserIdsForGroup(context)); break;
                case "GetUsersForGroupManagement": Write(context, GetUsersForGroupManagement(context)); break;
                case "GetAllGroupsForUserManagement": Write(context, GetAllGroupsForUserManagement(context)); break;
                case "UpdateGroupMembersDelta": Write(context, UpdateGroupMembersDelta(context)); break;
                case "UpdateUserGroupsDelta": Write(context, UpdateUserGroupsDelta(context)); break;

                // --- Yetki (Permission) Yönetimi (AuthKit) ---
                case "GetPermissions": Write(context, GetPermissions()); break;
                case "GetAllModulesAndPermissions": Write(context, GetAllModulesAndPermissions()); break;
                case "GetPermissionsForGroup": Write(context, GetPermissionsForGroup(context)); break;
                case "UpdatePermissionsForGroup": Write(context, UpdatePermissionsForGroup(context)); break;
                case "GetEffectivePermissionsForUser": Write(context, GetEffectivePermissionsForUser(context)); break;
                case "UpdateDirectUserPermissions": Write(context, UpdateDirectUserPermissions(context)); break;

                default:
                    context.Response.StatusCode = 400;
                    Write(context, new { success = false, error = "Bilinmeyen action: '" + action + "'" });
                    break;
            }
        }
        catch (Exception ex)
        {
            Composite.Core.Log.LogError("ApiHandler." + action, ex.ToString());
            if (context.Response.StatusCode == 200) context.Response.StatusCode = 500;
            Write(context, new { success = false, error = ex.Message });
        }
    }

    private static void Write(HttpContext context, object data)
    {
        // Permission checks (CheckApiPermission) return an already-serialized JSON string.
        // Write those verbatim to avoid double-encoding ("{\"error\":\"...\"}").
        if (data is string)
        {
            context.Response.Write((string)data);
            return;
        }
        context.Response.Write(JsonConvert.SerializeObject(data));
    }

    private static string ReadBody(HttpContext context)
    {
        if (context.Request.InputStream != null && context.Request.InputStream.Length > 0)
        {
            context.Request.InputStream.Position = 0;
            using (var reader = new System.IO.StreamReader(context.Request.InputStream))
            {
                return reader.ReadToEnd();
            }
        }
        return context.Request.Form.ToString();
    }

    // ============ Temel uçlar ============
    private static object TimeResponse()
    {
        return new
        {
            success = true,
            servertime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            timezone = TimeZoneInfo.Local.DisplayName,
            timestamp = DateTimeOffset.Now.ToUnixTimeSeconds(),
            utc = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private static object HelloResponse(HttpContext context)
    {
        return new
        {
            success = true,
            message = "C1 API calisiyor.",
            servertime = DateTime.Now.ToString("HH:mm:ss")
        };
    }

    private static object StatusResponse(HttpContext context)
    {
        var c1LoggedIn = false;
        var c1Username = "?";
        try
        {
            c1Username = Composite.C1Console.Security.UserValidationFacade.GetUsername();
            c1LoggedIn = Composite.C1Console.Security.UserValidationFacade.IsLoggedIn();
        }
        catch { }
        return new
        {
            success = true,
            server = Environment.MachineName,
            runtime = Environment.Version.ToString(),
            authenticated = c1LoggedIn,
            username = c1LoggedIn ? c1Username : null,
            c1LoggedIn = c1LoggedIn
        };
    }

    // ============ Kullanıcı Yönetimi ============
    private static object GetUsers()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.View);
        if (permissionError != null) return permissionError;

        using (var conn = new DataConnection())
        {
            var users = conn.Get<AuthKit.Data.Authentication.User>().OrderBy(u => u.UserName).ToList();
            return new { data = users };
        }
    }

    private static object GetRealUsers()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.View);
        if (permissionError != null) return permissionError;

        using (var conn = new DataConnection())
        {
            var users = conn.Get<AuthKit.Data.Authentication.User>()
                            .Where(u => u.IsTemplate == false)
                            .OrderBy(u => u.UserName)
                            .ToList();
            return new { data = users };
        }
    }

    private static object GetTemplateUsers()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.View);
        if (permissionError != null) return permissionError;

        using (var conn = new DataConnection())
        {
            var users = conn.Get<AuthKit.Data.Authentication.User>()
                            .Where(u => u.IsTemplate == true)
                            .OrderBy(u => u.UserName)
                            .ToList();
            return new { data = users };
        }
    }

    private static object GetAllUsersForDropdown()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.View);
        if (permissionError != null) return permissionError;

        return new { data = AuthKit.Authentication.AuthenticationManager.GetAllActiveUsersSummary() };
    }

    private static object AddUser(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.Add);
        if (permissionError != null) return permissionError;

        var request = JsonConvert.DeserializeObject<CreateUserRequest>(ReadBody(context));
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Email))
        {
            context.Response.StatusCode = 400;
            return new { error = "Kullanıcı adı ve e-posta alanları zorunludur." };
        }

        var result = AuthKit.Authentication.AuthenticationManager.CreateUser(
            request.UserName, request.Email, request.PasswordHash, request.IsActive, request.IsTemplate);

        if (!result.IsSuccess)
        {
            context.Response.StatusCode = 400;
            return new { error = result.ErrorMessage };
        }

        if (!string.IsNullOrEmpty(request.SourceUserId))
        {
            var cloneResult = AuthKit.Authorization.AuthorizationManager.CloneUserPermissionsAndGroups(request.SourceUserId, result.NewUser.Id);
            if (!cloneResult.IsSuccess)
                Composite.Core.Log.LogWarning("ApiHandler.AddUser",
                    "User '" + result.NewUser.UserName + "' created but clone from '" + request.SourceUserId + "' failed: " + cloneResult.ErrorMessage);
        }

        return result.NewUser;
    }

    private static object UpdateUser(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.Edit);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string id = data.GetValue("Id", StringComparison.OrdinalIgnoreCase)?.ToString();
        string username = data.GetValue("UserName", StringComparison.OrdinalIgnoreCase)?.ToString();
        string email = data.GetValue("Email", StringComparison.OrdinalIgnoreCase)?.ToString();
        string password = data.GetValue("PasswordHash", StringComparison.OrdinalIgnoreCase)?.ToString();
        bool isActive = data.GetValue("IsActive", StringComparison.OrdinalIgnoreCase)?.Value<bool>() ?? false;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email))
        {
            context.Response.StatusCode = 400;
            return new { error = "Kullanıcı adı ve e-posta alanları zorunludur." };
        }

        var result = AuthKit.Authentication.AuthenticationManager.UpdateUser(id, username, email, password, isActive);
        if (result.IsSuccess) return result.UpdatedUser;

        context.Response.StatusCode = 400;
        return new { error = result.ErrorMessage };
    }

    private static object DeleteUser(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.Delete);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string id = data.GetValue("Id", StringComparison.OrdinalIgnoreCase)?.ToString();

        var result = AuthKit.Authentication.AuthenticationManager.DeleteUser(id);
        if (result.IsSuccess) return new { };

        context.Response.StatusCode = 400;
        return new { error = result.ErrorMessage };
    }

    private static object AdminChangePassword(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Users.Edit);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string userIdStr = data.Value<string>("userId");
        string newPassword = data.Value<string>("newPassword");

        var result = AuthKit.Authentication.AuthenticationManager.ChangePasswordAsAdmin(userIdStr, newPassword);
        if (result.IsSuccess) return new { ok = true };

        context.Response.StatusCode = 400;
        return new { error = result.ErrorMessage };
    }

    // ============ Grup Yönetimi ============
    private static object GetGroups()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.View);
        if (permissionError != null) return permissionError;

        using (var conn = new DataConnection())
        {
            var groups = conn.Get<AuthKit.Data.Authorization.Group>()
                             .OrderBy(g => g.GroupName)
                             .Select(g => new { g.Id, g.GroupName, g.Description })
                             .ToList();
            return new { data = groups };
        }
    }

    private static object GetAllGroupsForDropdown()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.View);
        if (permissionError != null) return permissionError;

        using (var conn = new DataConnection())
        {
            var groups = conn.Get<AuthKit.Data.Authorization.Group>()
                             .OrderBy(g => g.GroupName)
                             .Select(g => new { g.Id, g.GroupName })
                             .ToList();
            return new { data = groups };
        }
    }

    private static object AddGroup(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.Add);
        if (permissionError != null) return permissionError;

        var request = JsonConvert.DeserializeObject<CreateGroupRequest>(ReadBody(context));
        if (string.IsNullOrWhiteSpace(request.GroupName))
        {
            context.Response.StatusCode = 400;
            return new { error = "Grup adı boş olamaz." };
        }

        var result = AuthKit.Authorization.AuthorizationManager.CreateGroup(request.GroupName, request.Description);
        if (!result.IsSuccess)
        {
            context.Response.StatusCode = 400;
            return new { error = result.ErrorMessage };
        }

        if (!string.IsNullOrEmpty(request.SourceGroupId))
        {
            var cloneResult = AuthKit.Authorization.AuthorizationManager.CloneGroupPermissions(request.SourceGroupId, result.NewGroup.Id);
            if (!cloneResult.IsSuccess)
                Composite.Core.Log.LogWarning("ApiHandler.AddGroup",
                    "Group '" + result.NewGroup.GroupName + "' created but clone from '" + request.SourceGroupId + "' failed: " + cloneResult.ErrorMessage);
        }

        return result.NewGroup;
    }

    private static object UpdateGroup(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.Edit);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string id = data.GetValue("Id", StringComparison.OrdinalIgnoreCase)?.ToString();
        string groupName = data.GetValue("GroupName", StringComparison.OrdinalIgnoreCase)?.ToString();
        string description = data.GetValue("Description", StringComparison.OrdinalIgnoreCase)?.ToString();

        var result = AuthKit.Authorization.AuthorizationManager.UpdateGroup(id, groupName, description);
        if (result.IsSuccess) return result.UpdatedGroup;

        context.Response.StatusCode = 400;
        return new { error = result.ErrorMessage };
    }

    private static object DeleteGroup(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.Delete);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string id = data.GetValue("Id", StringComparison.OrdinalIgnoreCase)?.ToString();

        var result = AuthKit.Authorization.AuthorizationManager.DeleteGroup(id);
        if (result.IsSuccess) return new { };

        context.Response.StatusCode = 400;
        return new { error = result.ErrorMessage };
    }

    private static object GetAllGroupIdsForUser(HttpContext context)
    {
        JObject data = JObject.Parse(ReadBody(context));
        string userId = data.GetValue("userId", StringComparison.OrdinalIgnoreCase)?.ToString();

        using (var conn = new DataConnection())
        {
            var memberGroupIds = conn.Get<AuthKit.Data.Authorization.UserInGroup>()
                                     .Where(ug => ug.RefUserId == userId)
                                     .Select(ug => ug.RefGroupId)
                                     .ToList();
            return new { data = memberGroupIds };
        }
    }

    private static object GetAllUserIdsForGroup(HttpContext context)
    {
        JObject data = JObject.Parse(ReadBody(context));
        string groupId = data.GetValue("groupId", StringComparison.OrdinalIgnoreCase)?.ToString();

        using (var conn = new DataConnection())
        {
            var memberUserIds = conn.Get<AuthKit.Data.Authorization.UserInGroup>()
                                    .Where(ug => ug.RefGroupId == groupId)
                                    .Select(ug => ug.RefUserId)
                                    .ToList();
            return new { data = memberUserIds };
        }
    }

    private static object GetUsersForGroupManagement(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.Edit);
        if (permissionError != null) return permissionError;

        var payload = JsonConvert.DeserializeObject<GetUsersForGroupManagementRequest>(ReadBody(context));
        string groupId = payload.GroupId;
        DataTableRequest request = payload.DtRequest ?? new DataTableRequest { Start = 0, Length = 10 };

        using (var conn = new DataConnection())
        {
            IQueryable<AuthKit.Data.Authentication.User> allUsersQuery = conn.Get<AuthKit.Data.Authentication.User>().Where(u => !u.IsTemplate);
            var memberIds = new HashSet<string>(conn.Get<AuthKit.Data.Authorization.UserInGroup>()
                                                  .Where(ug => ug.RefGroupId == groupId)
                                                  .Select(ug => ug.RefUserId));

            int totalRecords = allUsersQuery.Count();

            if (request.Search != null && !string.IsNullOrEmpty(request.Search.Value))
            {
                var searchTerm = request.Search.Value.ToLower();
                allUsersQuery = allUsersQuery.Where(u =>
                    (u.UserName != null && u.UserName.ToLower().Contains(searchTerm)) ||
                    (u.Email != null && u.Email.ToLower().Contains(searchTerm)));
            }

            int filteredRecords = allUsersQuery.Count();
            var users = allUsersQuery.OrderBy(u => u.UserName)
                                     .Skip(request.Start).Take(request.Length).ToList()
                                     .Select(u => new UserForGroupManagement
                                     {
                                         Id = u.Id,
                                         UserName = u.UserName,
                                         Email = u.Email,
                                         IsMember = memberIds.Contains(u.Id)
                                     }).ToList();

            var response = new DataTableResponse<UserForGroupManagement>
            {
                Draw = request.Draw,
                RecordsTotal = totalRecords,
                RecordsFiltered = filteredRecords,
                Data = users
            };

            var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
            return JsonConvert.SerializeObject(response, settings);
        }
    }

    private static object GetAllGroupsForUserManagement(HttpContext context)
    {
        var payload = JsonConvert.DeserializeObject<GetAllGroupsForUserManagementRequest>(ReadBody(context));
        string userId = payload.UserId;
        DataTableRequest request = payload.DtRequest ?? new DataTableRequest { Start = 0, Length = 10 };

        using (var conn = new DataConnection())
        {
            IQueryable<AuthKit.Data.Authorization.Group> allGroupsQuery = conn.Get<AuthKit.Data.Authorization.Group>();
            var memberGroupIds = new HashSet<string>(conn.Get<AuthKit.Data.Authorization.UserInGroup>()
                                                         .Where(ug => ug.RefUserId == userId)
                                                         .Select(ug => ug.RefGroupId));

            int totalRecords = allGroupsQuery.Count();

            if (request.Search != null && !string.IsNullOrEmpty(request.Search.Value))
            {
                var searchTerm = request.Search.Value.ToLower();
                allGroupsQuery = allGroupsQuery.Where(g =>
                    (g.GroupName != null && g.GroupName.ToLower().Contains(searchTerm)) ||
                    (g.Description != null && g.Description.ToLower().Contains(searchTerm)));
            }

            int filteredRecords = allGroupsQuery.Count();
            var groups = allGroupsQuery.OrderBy(g => g.GroupName)
                                       .Skip(request.Start).Take(request.Length).ToList()
                                       .Select(g => new GroupForUserManagement
                                       {
                                           Id = g.Id,
                                           GroupName = g.GroupName,
                                           Description = g.Description,
                                           IsMember = memberGroupIds.Contains(g.Id)
                                       }).ToList();

            var response = new DataTableResponse<GroupForUserManagement>
            {
                Draw = request.Draw,
                RecordsTotal = totalRecords,
                RecordsFiltered = filteredRecords,
                Data = groups
            };

            var settings = new JsonSerializerSettings { ContractResolver = new CamelCasePropertyNamesContractResolver() };
            return JsonConvert.SerializeObject(response, settings);
        }
    }

    private static object UpdateGroupMembersDelta(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.Edit);
        if (permissionError != null) return permissionError;

        var request = JsonConvert.DeserializeObject<UpdateGroupMembersDeltaRequest>(ReadBody(context));
        var result = AuthKit.Authorization.AuthorizationManager.UpdateGroupMembersDelta(
            request.GroupId, request.UserIdsToAdd, request.UserIdsToRemove);
        return new { ok = result.IsSuccess, error = result.ErrorMessage };
    }

    private static object UpdateUserGroupsDelta(HttpContext context)
    {
        var request = JsonConvert.DeserializeObject<UpdateUserGroupsDeltaRequest>(ReadBody(context));
        var result = AuthKit.Authorization.AuthorizationManager.UpdateUserGroupsDelta(
            request.UserId, request.GroupIdsToAdd, request.GroupIdsToRemove);

        if (result.IsSuccess) return new { ok = true };

        context.Response.StatusCode = 500;
        return new { error = result.ErrorMessage };
    }

    // ============ Yetki (Permission) Yönetimi ============
    private static object GetPermissions()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Groups.View);
        if (permissionError != null) return permissionError;

        using (var conn = new DataConnection())
        {
            var permissions = conn.Get<AuthKit.Data.Authorization.Permission>().ToList();
            var data = permissions.Select(p => new
            {
                p.Id,
                PermissionName = p.Name,
                p.Description,
                p.IsObsolete
            }).OrderBy(p => p.PermissionName).ToList();
            return new { data = data };
        }
    }

    private static object GetAllModulesAndPermissions()
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Permissions.Assign);
        if (permissionError != null) return permissionError;

        var result = AuthKit.Authorization.AuthorizationManager.GetAllModulesAndPermissions();
        return new { data = result };
    }

    private static object GetPermissionsForGroup(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Permissions.Assign);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string groupId = data.GetValue("groupId", StringComparison.OrdinalIgnoreCase)?.ToString();

        if (string.IsNullOrEmpty(groupId))
        {
            context.Response.StatusCode = 400;
            return new { error = "Grup ID boş olamaz." };
        }

        var result = AuthKit.Authorization.AuthorizationManager.GetPermissionsForGroup(groupId);
        return new { data = result };
    }

    private static object UpdatePermissionsForGroup(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Permissions.Assign);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string groupId = data.GetValue("groupId", StringComparison.OrdinalIgnoreCase)?.ToString();
        var permissions = data.GetValue("permissions", StringComparison.OrdinalIgnoreCase)?.ToObject<List<AuthKit.Authorization.GroupPermissionUpdate>>();

        var result = AuthKit.Authorization.AuthorizationManager.UpdatePermissionsForGroup(groupId, permissions);
        if (result.IsSuccess) return new { ok = true };

        context.Response.StatusCode = 400;
        return new { error = result.ErrorMessage };
    }

    private static object GetEffectivePermissionsForUser(HttpContext context)
    {
        JObject data = JObject.Parse(ReadBody(context));
        string userId = data.GetValue("userId", StringComparison.OrdinalIgnoreCase)?.ToString();

        var permissions = AuthKit.Authorization.AuthorizationManager.GetEffectivePermissionsForUser(userId);
        return new { data = permissions };
    }

    private static object UpdateDirectUserPermissions(HttpContext context)
    {
        var permissionError = AuthKit.Authorization.AuthorizationManager.CheckApiPermission(AuthKit.Authorization.PermissionKeys.Auth.Permissions.Assign);
        if (permissionError != null) return permissionError;

        JObject data = JObject.Parse(ReadBody(context));
        string userId = data.GetValue("userId", StringComparison.OrdinalIgnoreCase)?.ToString();
        var permissions = data.GetValue("permissions", StringComparison.OrdinalIgnoreCase)?.ToObject<List<AuthKit.Authorization.UserPermissionUpdate>>();
        var moduleStates = data.GetValue("moduleStates", StringComparison.OrdinalIgnoreCase)?.ToObject<List<AuthKit.Authorization.ModuleStateUpdate>>();

        var result = AuthKit.Authorization.AuthorizationManager.UpdateDirectUserPermissions(userId, permissions, moduleStates);
        if (result.IsSuccess) return new { ok = true };

        context.Response.StatusCode = 400;
        return new { error = result.ErrorMessage };
    }

    public bool IsReusable => false;
}
