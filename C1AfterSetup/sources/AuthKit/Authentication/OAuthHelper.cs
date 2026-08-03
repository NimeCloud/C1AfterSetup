using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace AuthKit.Authentication
{
    /// <summary>
    /// Google ve Facebook OAuth token dogrulama yardimcilari.
    /// Harici bagimlilik yoktur; System.Net.Http + JavaScriptSerializer kullanir.
    /// </summary>
    public static class OAuthHelper
    {
        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// Google Identity Services'in urettigi id_token'i dogrular ve kullanici bilgilerini dondurur.
        /// </summary>
        public static ProviderUser ValidateGoogleToken(string idToken)
        {
            if (string.IsNullOrWhiteSpace(idToken)) return null;
            try
            {
                var url = "https://oauth2.googleapis.com/tokeninfo?id_token=" + Uri.EscapeDataString(idToken);
                var json = Task.Run(() => _http.GetStringAsync(url)).Result;
                var dict = ParseJson(json);
                if (dict == null || dict.ContainsKey("error")) return null;
                return new ProviderUser
                {
                    ProviderUserId = GetString(dict, "sub"),
                    Email = GetString(dict, "email"),
                    DisplayName = GetString(dict, "name")
                };
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("OAuthHelper.ValidateGoogleToken", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Facebook Graph API erisim token'ini dogrular ve kullanici bilgilerini dondurur.
        /// </summary>
        public static ProviderUser ValidateFacebookToken(string accessToken)
        {
            if (string.IsNullOrWhiteSpace(accessToken)) return null;
            try
            {
                var url = "https://graph.facebook.com/me?fields=id,name,email&access_token=" + Uri.EscapeDataString(accessToken);
                var json = Task.Run(() => _http.GetStringAsync(url)).Result;
                var dict = ParseJson(json);
                if (dict == null || dict.ContainsKey("error")) return null;
                return new ProviderUser
                {
                    ProviderUserId = GetString(dict, "id"),
                    Email = GetString(dict, "email"),
                    DisplayName = GetString(dict, "name")
                };
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("OAuthHelper.ValidateFacebookToken", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Dogrulanmis bir OAuth kullanicisinin temsilidir.
        /// </summary>
        public class ProviderUser
        {
            public string ProviderUserId { get; set; }
            public string Email { get; set; }
            public string DisplayName { get; set; }
        }

        private static Dictionary<string, object> ParseJson(string json)
        {
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                return ser.Deserialize<Dictionary<string, object>>(json);
            }
            catch
            {
                return null;
            }
        }

        private static string GetString(Dictionary<string, object> dict, string key)
        {
            if (dict != null && dict.ContainsKey(key) && dict[key] != null)
                return Convert.ToString(dict[key]);
            return null;
        }
    }
}
