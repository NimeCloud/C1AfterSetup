using System.Collections.Generic;

namespace AuthKit.Authorization
{
    // Frontend'den gelen modül durumunu temsil eder (Kaydetme işlemi için)
    public class ModuleStateUpdate
    {
        public string ModuleName { get; set; }
        public bool IsActive { get; set; }
    }

    // Veritabanı güncelleme işlemleri için kullanılan temel DTO'lar
    public class UserPermissionUpdate
    {
        public string PermissionId { get; set; }
        public string State { get; set; } // "allow", "deny", veya "clear"
    }

    public class GroupPermissionUpdate
    {
        public string PermissionId { get; set; }
        public string State { get; set; } // "allow", "deny", veya "clear"
    }

    // API Controller'a gelen istekleri modellemek için kullanılan sınıflar
    public class UserPermissionsUpdateRequest
    {
        public string UserId { get; set; }
        public List<UserPermissionUpdate> Permissions { get; set; }
        public List<ModuleStateUpdate> ModuleStates { get; set; }
    }

    public class GroupPermissionsUpdateRequest
    {
        public string GroupId { get; set; }
        public List<GroupPermissionUpdate> Permissions { get; set; }
    }
}
