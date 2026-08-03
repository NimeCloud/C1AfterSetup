using System;

namespace AuthKit.Authorization
{
    [AttributeUsage(AttributeTargets.Field)]
    public class PermissionInfoAttribute : Attribute
    {
        public string Description { get; }
        public string ErrorMessage { get; set; }

        public PermissionInfoAttribute(string description)
        {
            Description = description;
        }
    }
}
