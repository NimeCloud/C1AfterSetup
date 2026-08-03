using System;
using System.Linq;
using System.Reflection;

namespace AuthKit.Authorization
{
    /// <summary>
    /// Static "Key" class'larının içindeki alanları Reflection ile otomatik olarak doldurur.
    /// Ornegin GroupKeys.System.Administrators alanina "System.Administrators" degerini atar.
    /// </summary>
    public static class KeyInitializer
    {
        /// <summary>
        /// Belirtilen ana class'tan başlayarak tüm string alanlarını doldurur.
        /// </summary>
        public static void InitializeAllKeys(Type rootType)
        {
            InitializeFields(rootType, null);
        }

        private static void InitializeFields(Type type, string prefix)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static)
                             .Where(f => f.FieldType == typeof(string));

            foreach (var field in fields)
            {
                if (field.GetValue(null) == null)
                {
                    string keyValue = string.IsNullOrEmpty(prefix) ? field.Name : $"{prefix}.{field.Name}";
                    field.SetValue(null, keyValue);
                }
            }

            var nestedTypes = type.GetNestedTypes(BindingFlags.Public);
            foreach (var nestedType in nestedTypes)
            {
                string newPrefix = string.IsNullOrEmpty(prefix) ? nestedType.Name : $"{prefix}.{nestedType.Name}";
                InitializeFields(nestedType, newPrefix);
            }
        }
    }
}
