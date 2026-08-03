using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Web;
using Composite.Core.Routing.Pages;
using Composite.Data;
using Composite.Data.Types;

namespace AuthKit.Authentication
{
    public static partial class AuthenticationManager
    {
        /// <summary>
        /// Dropdown'ları doldurmak için tüm aktif kullanıcıların özet bir listesini döndürür.
        /// </summary>
        public static object GetAllActiveUsersSummary()
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    return connection.Get<AuthKit.Data.Authentication.User>()
                                   .Where(u => u.IsActive)
                                   .OrderBy(u => u.UserName)
                                   .Select(u => new { u.Id, u.UserName })
                                   .ToList();
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.GetAllActiveUsersSummary", ex.Message);
                return new List<object>();
            }
        }

        /// <summary>
        /// Geçerli bir sıfırlama token'ı kullanarak kullanıcının parolasını günceller.
        /// </summary>
        public static string ResetPassword(string token, string newPassword)
        {
            string userIdStr = ValidateResetToken(token);

            if (userIdStr == null)
            {
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Geçersiz veya süresi dolmuş sıfırlama anahtarı." });
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Yeni parola en az 6 karakter olmalıdır." });
            }

            try
            {
                var user = DataFacade.GetData<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userIdStr);
                if (user == null)
                {
                    return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Kullanıcı bulunamadı." });
                }

                string newHashedPassword = BCrypt.Net.BCrypt.HashPassword(newPassword);
                user.PasswordHash = newHashedPassword;
                DataFacade.Update(user);

                using (var connection = new DataConnection())
                {
                    var tokenRecord = connection.Get<AuthKit.Data.Authentication.Token>()
                                                .FirstOrDefault(t => t.ResetToken == token);
                    if (tokenRecord != null)
                    {
                        connection.Delete(tokenRecord);
                    }
                }

                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "Parola başarıyla güncellendi." });
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("ResetPassword", ex.Message);
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Parola güncellenirken bir veritabanı hatası oluştu." });
            }
        }

        /// <summary>
        /// Verilen bir tanımlayıcıya (kullanıcı adı VEYA e-posta) göre kullanıcıyı bulur.
        /// </summary>
        public static AuthKit.Data.Authentication.User FindUserByUsernameOrEmail(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return null;

            try
            {
                var user = DataFacade.GetData<AuthKit.Data.Authentication.User>()
                 .FirstOrDefault(u =>
                     (u.UserName != null && u.UserName.Equals(identifier, StringComparison.OrdinalIgnoreCase)) ||
                     (u.Email != null && u.Email.Equals(identifier, StringComparison.OrdinalIgnoreCase))
                 );
                return user;
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.FindUserByUsernameOrEmail", ex.Message);
                return null;
            }
        }

        #region --- Parola Sıfırlama Mekanizması ---

        /// <summary>
        /// E-posta gönderme delegate'i. Uygulama tarafında set edilmelidir.
        /// </summary>
        public static Func<string, string, string, bool> SendEmailDelegate { get; set; }

        /// <summary>
        /// Parola sıfırlama sayfasının C1 page GUID'i. Uygulama tarafında set edilmelidir.
        /// </summary>
        public static Guid ResetPasswordPageId { get; set; } = Guid.Empty;

        /// <summary>
        /// Bir e-posta adresi için parola sıfırlama süreci başlatır.
        /// </summary>
        public static string ForgotPassword(string email)
        {
            CleanupExpiredAuthTokens();

            if (string.IsNullOrWhiteSpace(email))
            {
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "İstek alındı." });
            }

            try
            {
                var user = FindUserByUsernameOrEmail(email);
                if (user != null)
                {
                    string token = Guid.NewGuid().ToString("N");

                    using (var connection = new DataConnection())
                    {
                        var newToken = connection.CreateNew<AuthKit.Data.Authentication.Token>();
                        newToken.RefUserId = user.Id;
                        newToken.ResetToken = token;
                        newToken.ExpiresOn = DateTime.UtcNow.AddHours(1);
                        newToken.CreatedOn = DateTime.UtcNow;
                        connection.Add(newToken);
                    }

                    // Sayfa yolunu belirle: GUID varsa C1'den al, yoksa fallback kullan
                    string pagePath = "/reset-password";
                    if (ResetPasswordPageId != Guid.Empty)
                    {
                        using (var connection = new DataConnection())
                        {
                            var resetPage = connection.Get<IPage>().FirstOrDefault(p => p.Id == ResetPasswordPageId);
                            if (resetPage != null)
                                pagePath = resetPage.UrlTitle;
                            else
                                Composite.Core.Log.LogError("ForgotPassword", $"Parola sıfırlama sayfası ({ResetPasswordPageId}) bulunamadı.");
                        }
                    }

                    string resetLink = $"http://{HttpContext.Current.Request.Url.Authority}/{pagePath}?token={token}";

                    string emailBody = $"Merhaba {user.UserName},\n\nParolanızı sıfırlamak için lütfen şu linke tıklayın: {resetLink}";
                    Composite.Core.Log.LogInformation("PasswordReset", $"Token for {user.UserName}: {resetLink}");

                    if (SendEmailDelegate != null)
                    {
                        SendEmailDelegate(user.Email, "Parola Sıfırlama Talebi", emailBody);
                    }
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.ForgotPassword", ex.Message);
            }

            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "Eğer girdiğiniz e-posta adresi sistemimizde kayıtlıysa, parola sıfırlama talimatları gönderilmiştir." });
        }

        /// <summary>
        /// Verilen bir parola sıfırlama token'ını doğrular.
        /// </summary>
        public static string ValidateResetToken(string authToken)
        {
            if (string.IsNullOrWhiteSpace(authToken)) return null;
            CleanupExpiredAuthTokens();

            using (var connection = new DataConnection())
            {
                var token = connection.Get<AuthKit.Data.Authentication.Token>()
                    .FirstOrDefault(t => t.ResetToken == authToken && t.ExpiresOn > DateTime.UtcNow);
                return token?.RefUserId;
            }
        }
        #endregion

        #region --- Login / Logout ---

        /// <summary>
        /// Login sayfasının C1 page GUID'i. Uygulama tarafında set edilmelidir.
        /// </summary>
        public static Guid LoginPageId { get; set; } = Guid.Empty;

        /// <summary>
        /// Kullanıcı adı veya e-posta ve parola ile giriş yapar.
        /// </summary>
        public static string Login(string username, string password, bool rememberMe)
        {
            var user = FindUserByUsernameOrEmail(username);

            if (user == null)
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Kullanıcı adı veya parola hatalı." });

            if (user.IsTemplate)
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Şablon hesaplar ile giriş yapılamaz." });

            if (string.IsNullOrEmpty(user.PasswordHash))
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Bu hesabın parolası ayarlanmamış. Lütfen yönetici ile iletişime geçin." });

            if (!VerifyPasswordAndUpgrade(user.Id, password, user.PasswordHash))
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Kullanıcı adı veya parola hatalı." });

            PerformLogin(user.Id, rememberMe);
            user.LastSeenOn = DateTime.UtcNow;
            DataFacade.Update(user);

            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true });
        }

        /// <summary>
        /// Tarayıcıdan gelen authToken cookie'sini okur, doğrular ve kullanıcıyı getirir.
        /// </summary>
        public static AuthKit.Data.Authentication.User GetCurrentUser()
        {
            HttpCookie authTokenCookie = HttpContext.Current.Request.Cookies["authToken"];
            if (authTokenCookie == null || string.IsNullOrEmpty(authTokenCookie.Value))
                return null;

            string token = authTokenCookie.Value;
            string userIdFromToken = ValidateTokenAndGetUserId(token);

            if (userIdFromToken == null)
                return null;

            try
            {
                using (var connection = new DataConnection())
                {
                    return connection.Get<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userIdFromToken);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("GetCurrentUser", ex.Message);
                throw;
            }
        }

        private static string ValidateTokenAndGetUserId(string authToken)
        {
            var token = DataFacade.GetData<AuthKit.Data.Authentication.Token>().FirstOrDefault(t => t.AuthToken == authToken);
            if (token == null || token.ExpiresOn < DateTime.UtcNow)
                return null;
            return token.RefUserId;
        }

        public static void InvalidateCurrentToken()
        {
            HttpCookie authTokenCookie = HttpContext.Current.Request.Cookies["authToken"];
            if (authTokenCookie != null && !string.IsNullOrEmpty(authTokenCookie.Value))
            {
                string tokenValue = authTokenCookie.Value;
                try
                {
                    using (var conn = new DataConnection())
                    {
                        var tokenInDb = conn.Get<AuthKit.Data.Authentication.Token>().FirstOrDefault(t => t.AuthToken == tokenValue);
                        if (tokenInDb != null)
                            conn.Delete(tokenInDb);
                    }
                }
                catch (Exception ex)
                {
                    Composite.Core.Log.LogError("InvalidateToken", "Failed to delete token from DB: " + ex.Message);
                }
            }
        }

        public static void CleanupExpiredAuthTokens()
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    var expiredTokens = connection.Get<AuthKit.Data.Authentication.Token>()
                                                  .Where(t => t.ExpiresOn < DateTime.UtcNow)
                                                  .ToList();
                    if (expiredTokens.Any())
                        connection.Delete<AuthKit.Data.Authentication.Token>(expiredTokens);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("CleanupExpiredAuthTokens", ex.Message);
            }
        }

        public static string Logout()
        {
            try
            {
                HttpCookie authTokenCookie = HttpContext.Current.Request.Cookies["authToken"];
                if (authTokenCookie == null || string.IsNullOrEmpty(authTokenCookie.Value))
                    return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "Already logged out" });

                string token = authTokenCookie.Value;
                var tokenEntry = DataFacade.GetData<AuthKit.Data.Authentication.Token>()
                    .FirstOrDefault(t => t.AuthToken == token);

                if (tokenEntry != null)
                    DataFacade.Delete(tokenEntry);

                var expiredCookie = new HttpCookie("authToken", "")
                {
                    Expires = DateTime.UtcNow.AddDays(-3),
                    HttpOnly = true
                };
                HttpContext.Current.Response.Cookies.Add(expiredCookie);

                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = ex.Message });
            }
        }

        private static bool VerifyPasswordAndUpgrade(string userId, string providedPassword, string storedPassword)
        {
            bool isHashed = storedPassword.StartsWith("$2");

            if (isHashed)
            {
                return BCrypt.Net.BCrypt.Verify(providedPassword, storedPassword);
            }
            else
            {
                if (providedPassword == storedPassword)
                {
                    string newHashedPassword = BCrypt.Net.BCrypt.HashPassword(providedPassword);
                    var user = DataFacade.GetData<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userId);
                    user.PasswordHash = newHashedPassword;
                    DataFacade.Update(user);
                    return true;
                }
            }

            return false;
        }

        public static string ValidateToken(string authToken)
        {
            var token = DataFacade.GetData<AuthKit.Data.Authentication.Token>().FirstOrDefault(x => x.AuthToken == authToken);
            if (token == null || token.ExpiresOn < DateTime.UtcNow) return null;

            token.LastSeenOn = DateTime.UtcNow;
            DataFacade.Update(token);
            return token.RefUserId;
        }

        public static AuthKit.Data.Authentication.User FindUserByUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return null;

            try
            {
                using (var connection = new DataConnection())
                {
                    return connection.Get<AuthKit.Data.Authentication.User>()
                                     .FirstOrDefault(u => u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.FindUserByUsername", ex.Message);
                return null;
            }
        }
        #endregion

        #region --- Kullanıcı CRUD ---

        public class AuthCreationResult
        {
            public bool IsSuccess { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static (bool IsSuccess, string ErrorMessage, AuthKit.Data.Authentication.User NewUser) CreateUser(
            string username, string email, string plainPassword, bool isActive, bool isTemplate)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    bool userExists = connection.Get<AuthKit.Data.Authentication.User>().Any(u =>
                        (u.UserName != null && u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase)) ||
                        (u.Email != null && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
                    );

                    if (userExists)
                        return (false, "Bu kullanıcı adı veya e-posta adresi zaten kullanılıyor.", null);

                    string hashedPassword = "";
                    if (!string.IsNullOrWhiteSpace(plainPassword))
                        hashedPassword = BCrypt.Net.BCrypt.HashPassword(plainPassword);

                    var newUser = connection.CreateNew<AuthKit.Data.Authentication.User>();
                    newUser.UserName = username;
                    newUser.Email = email;
                    newUser.PasswordHash = hashedPassword;
                    newUser.IsActive = isActive;
                    newUser.IsTemplate = isTemplate;
                    newUser.CreatedOn = DateTime.UtcNow;

                    var addedUser = connection.Add(newUser);
                    return (true, null, addedUser);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.CreateUser", ex.Message);
                return (false, "Kullanıcı oluşturulurken bir veritabanı hatası oluştu.", null);
            }
        }

        public static (bool IsSuccess, string ErrorMessage, AuthKit.Data.Authentication.User UpdatedUser) UpdateUser(
            string userId, string username, string email, string plainPassword, bool isActive)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    var userToUpdate = connection.Get<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userId);
                    if (userToUpdate == null)
                        return (false, "Güncellenecek kullanıcı bulunamadı.", null);

                    bool userExists = connection.Get<AuthKit.Data.Authentication.User>().Any(u =>
                        u.Id != userId &&
                        (
                            (u.UserName != null && u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase)) ||
                            (u.Email != null && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
                        )
                    );

                    if (userExists)
                        return (false, "Bu kullanıcı adı veya e-posta adresi başka bir kullanıcı tarafından kullanılıyor.", null);

                    userToUpdate.UserName = username;
                    userToUpdate.Email = email;
                    userToUpdate.IsActive = isActive;

                    if (!string.IsNullOrWhiteSpace(plainPassword))
                    {
                        string newHashedPassword = BCrypt.Net.BCrypt.HashPassword(plainPassword);
                        userToUpdate.PasswordHash = newHashedPassword;
                    }

                    connection.Update(userToUpdate);
                    return (true, null, userToUpdate);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.UpdateUser", ex.Message);
                return (false, "Kullanıcı güncellenirken bir veritabanı hatası oluştu.", null);
            }
        }

        public static (bool IsSuccess, string ErrorMessage) ChangePasswordAsAdmin(string userId, string newPlainPassword)
        {
            if (string.IsNullOrWhiteSpace(newPlainPassword) || newPlainPassword.Length < 6)
                return (false, "Yeni parola en az 6 karakter olmalıdır.");

            try
            {
                using (var connection = new DataConnection())
                {
                    var userToUpdate = connection.Get<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userId);
                    if (userToUpdate == null)
                        return (false, "Kullanıcı bulunamadı.");

                    string newHashedPassword = BCrypt.Net.BCrypt.HashPassword(newPlainPassword);
                    userToUpdate.PasswordHash = newHashedPassword;
                    connection.Update(userToUpdate);
                    return (true, null);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.ChangePasswordAsAdmin", ex.Message);
                return (false, "Parola güncellenirken bir veritabanı hatası oluştu.");
            }
        }

        public static (bool IsSuccess, string ErrorMessage) DeleteUser(string userId)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    var userToDelete = connection.Get<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userId);
                    if (userToDelete == null)
                        return (false, "Silinecek kullanıcı bulunamadı.");

                    var userGroupRelations = connection.Get<AuthKit.Data.Authorization.UserInGroup>()
                                                       .Where(ug => ug.RefUserId == userId).ToList();
                    if (userGroupRelations.Any())
                        connection.Delete<AuthKit.Data.Authorization.UserInGroup>(userGroupRelations);

                    var userPermissionRelations = connection.Get<AuthKit.Data.Authorization.PermissionInUser>()
                                                            .Where(up => up.RefUserId == userId).ToList();
                    if (userPermissionRelations.Any())
                        connection.Delete<AuthKit.Data.Authorization.PermissionInUser>(userPermissionRelations);

                    var userTokens = connection.Get<AuthKit.Data.Authentication.Token>()
                                               .Where(t => t.RefUserId == userId).ToList();
                    if (userTokens.Any())
                        connection.Delete<AuthKit.Data.Authentication.Token>(userTokens);

                    connection.Delete(userToDelete);
                    return (true, null);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.DeleteUser", ex.Message);
                return (false, "Kullanıcı silinirken bir veritabanı hatası oluştu.");
            }
        }

        #endregion

        #region --- Core Login Logic ---

        /// <summary>
        /// Başarılı login sonrası token oluşturup cookie'yi ayarlar.
        /// NOT: Bu metot App_Code/Controllers/1ControllerAuthentication.cs içindeki PerformLogin ile aynı mantıktadır.
        /// </summary>
        public static void PerformLogin(string userId, bool rememberMe)
        {
            try
            {
                using (var connection = new DataConnection())
                {
                    var token = connection.CreateNew<AuthKit.Data.Authentication.Token>();
                    token.RefUserId = userId;
                    token.AuthToken = Guid.NewGuid().ToString("N");
                    token.CreatedOn = DateTime.UtcNow;

                    if (rememberMe)
                        token.ExpiresOn = DateTime.UtcNow.AddDays(30);
                    else
                        token.ExpiresOn = DateTime.UtcNow.AddDays(1);

                    connection.Add(token);

                    HttpCookie authCookie = new HttpCookie("authToken", token.AuthToken)
                    {
                        HttpOnly = true,
                        Secure = HttpContext.Current.Request.IsSecureConnection,
                        SameSite = SameSiteMode.Lax
                    };

                    if (rememberMe)
                        authCookie.Expires = DateTime.UtcNow.AddDays(30);

                    HttpContext.Current.Response.Cookies.Add(authCookie);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("PerformLogin", ex.Message);
                throw;
            }
        }

        #endregion

        #region --- OAuth (Google / Facebook) ---

        /// <summary>
        /// Google OAuth Client ID. AuthKit Admin sayfasindan veya Settings'ten ayarlanir.
        /// </summary>
        public static string GoogleClientId
        {
            get { return KeyTreeStore.KeyTreeStoreManager.Get("Auth.OAuth.Google.ClientId", ""); }
        }

        /// <summary>
        /// Facebook OAuth App ID. AuthKit Admin sayfasindan veya Settings'ten ayarlanir.
        /// </summary>
        public static string FacebookAppId
        {
            get { return KeyTreeStore.KeyTreeStoreManager.Get("Auth.OAuth.Facebook.AppId", ""); }
        }

        /// <summary>
        /// Google id_token ile giris yapar. Kullanici yoksa e-posta uzerinden olusturur.
        /// </summary>
        public static string LoginWithGoogle(string idToken, bool rememberMe)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                return SerializeOAuthError("Google giris bilgisi alinamadi.");

            try
            {
                var pu = OAuthHelper.ValidateGoogleToken(idToken);
                if (pu == null || string.IsNullOrEmpty(pu.ProviderUserId))
                    return SerializeOAuthError("Google kimligi dogrulanamadi.");

                return LoginWithProvider("google", pu, rememberMe);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.LoginWithGoogle", ex.Message);
                return SerializeOAuthError("Google girisinde bir hata olustu.");
            }
        }

        /// <summary>
        /// Facebook erisim token'i ile giris yapar. Kullanici yoksa e-posta uzerinden olusturur.
        /// </summary>
        public static string LoginWithFacebook(string accessToken, bool rememberMe)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SerializeOAuthError("Facebook giris bilgisi alinamadi.");

            try
            {
                var pu = OAuthHelper.ValidateFacebookToken(accessToken);
                if (pu == null || string.IsNullOrEmpty(pu.ProviderUserId))
                    return SerializeOAuthError("Facebook kimligi dogrulanamadi.");

                return LoginWithProvider("facebook", pu, rememberMe);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.LoginWithFacebook", ex.Message);
                return SerializeOAuthError("Facebook girisinde bir hata olustu.");
            }
        }

        /// <summary>
        /// OAuth kullanicisini e-posta ile eslestirir; yoksa yeni kullanici olusturup giris yapar.
        /// </summary>
        private static string LoginWithProvider(string provider, OAuthHelper.ProviderUser pu, bool rememberMe)
        {
            AuthKit.Data.Authentication.User user = null;

            if (!string.IsNullOrEmpty(pu.Email))
                user = FindUserByUsernameOrEmail(pu.Email);

            if (user == null)
            {
                var username = !string.IsNullOrEmpty(pu.Email)
                    ? SanitizeUsername(pu.Email.Split('@')[0])
                    : SanitizeUsername(provider + "_" + pu.ProviderUserId);

                var create = CreateUser(username, pu.Email, "", true, false);
                if (create.IsSuccess && create.NewUser != null)
                {
                    user = create.NewUser;
                }
                else
                {
                    // Kullanici adi cakismasi olabilir; provider+id ile benzersiz ad dene
                    username = SanitizeUsername(provider + "_" + pu.ProviderUserId);
                    create = CreateUser(username, pu.Email, "", true, false);
                    if (create.IsSuccess && create.NewUser != null)
                        user = create.NewUser;
                }
            }

            if (user == null)
                return SerializeOAuthError("OAuth kullanicisi olusturulamadi.");

            if (!user.IsActive)
                return SerializeOAuthError("Bu hesap aktif degil.");

            PerformLogin(user.Id, rememberMe);
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true });
        }

        private static string SanitizeUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return "user";
            var sb = new System.Text.StringBuilder();
            foreach (var c in username)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                    sb.Append(c);
            }
            return sb.Length > 0 ? sb.ToString() : "user";
        }

        private static string SerializeOAuthError(string msg)
        {
            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = msg });
        }

        #endregion
    }
}
