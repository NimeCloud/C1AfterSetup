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
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Invalid or expired reset token." });
            }

            if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            {
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "The new password must be at least 6 characters." });
            }

            try
            {
                var user = DataFacade.GetData<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userIdStr);
                if (user == null)
                {
                    return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "User not found." });
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

                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "Password updated successfully." });
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("ResetPassword", ex.Message);
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "A database error occurred while updating the password." });
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
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "Request received." });
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
                                Composite.Core.Log.LogError("ForgotPassword", $"Password reset page ({ResetPasswordPageId}) not found.");
                        }
                    }

                    string resetLink = $"http://{HttpContext.Current.Request.Url.Authority}/{pagePath}?token={token}";

                    string emailBody = $"Hello {user.UserName},\n\nPlease click the following link to reset your password: {resetLink}";
                    Composite.Core.Log.LogInformation("PasswordReset", $"Token for {user.UserName}: {resetLink}");

                    if (SendEmailDelegate != null)
                    {
                        SendEmailDelegate(user.Email, "Password Reset Request", emailBody);
                    }
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.ForgotPassword", ex.Message);
            }

            return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = true, message = "If the email address you entered is registered in our system, password reset instructions have been sent." });
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
        /// Sadece credential doğrulaması yapar; cookie basmaz, token oluşturmaz.
        /// Provider chain tarafından kullanılır.
        /// </summary>
        public static bool ValidateCredentialsOnly(string username, string password)
        {
            var user = FindUserByUsernameOrEmail(username);
            if (user == null) return false;
            if (user.IsTemplate) return false;
            if (!user.IsActive) return false; // Hard ban: pasif kullanicilar giris yapamaz.
            if (string.IsNullOrEmpty(user.PasswordHash)) return false;

            bool isHashed = user.PasswordHash.StartsWith("$2");
            if (isHashed)
                return BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
            else
                return password == user.PasswordHash;
        }

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
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Invalid username or password." });

            if (user.IsTemplate)
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Template accounts cannot log in." });

            if (!user.IsActive)
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "This account has been disabled. Please contact an administrator." });

            if (string.IsNullOrEmpty(user.PasswordHash))
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "This account has no password set. Please contact an administrator." });

            if (!VerifyPasswordAndUpgrade(user.Id, password, user.PasswordHash))
                return new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new { ok = false, error = "Invalid username or password." });

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
                        return (false, "This username or email address is already in use.", null);

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

                    // Yeni kullanicilari otomatik olarak "Customers" grubuna kat
                    // (React dashboard temel yetkileri icin). Idempotent; admin paneli vermez.
                    if (!addedUser.IsTemplate)
                    {
                        try
                        {
                            AuthKit.Authorization.AuthorizationManager.JoinGroupByName(
                                addedUser.Id, AuthKit.Authorization.GroupKeys.App.Customers);
                        }
                        catch (Exception joinEx)
                        {
                            Composite.Core.Log.LogError("AuthenticationManager.CreateUser.JoinCustomers",
                                $"Could not join user '{username}' to Customers group: {joinEx.Message}");
                        }
                    }

                    return (true, null, addedUser);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.CreateUser", ex.Message);
                return (false, "A database error occurred while creating the user.", null);
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
                        return (false, "User to update not found.", null);

                    bool userExists = connection.Get<AuthKit.Data.Authentication.User>().Any(u =>
                        u.Id != userId &&
                        (
                            (u.UserName != null && u.UserName.Equals(username, StringComparison.OrdinalIgnoreCase)) ||
                            (u.Email != null && u.Email.Equals(email, StringComparison.OrdinalIgnoreCase))
                        )
                    );

                    if (userExists)
                        return (false, "This username or email address is used by another user.", null);

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
                return (false, "A database error occurred while updating the user.", null);
            }
        }

        public static (bool IsSuccess, string ErrorMessage) ChangePasswordAsAdmin(string userId, string newPlainPassword)
        {
            if (string.IsNullOrWhiteSpace(newPlainPassword) || newPlainPassword.Length < 6)
                return (false, "The new password must be at least 6 characters.");

            try
            {
                using (var connection = new DataConnection())
                {
                    var userToUpdate = connection.Get<AuthKit.Data.Authentication.User>().FirstOrDefault(u => u.Id == userId);
                    if (userToUpdate == null)
                        return (false, "User not found.");

                    string newHashedPassword = BCrypt.Net.BCrypt.HashPassword(newPlainPassword);
                    userToUpdate.PasswordHash = newHashedPassword;
                    connection.Update(userToUpdate);
                    return (true, null);
                }
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.ChangePasswordAsAdmin", ex.Message);
                return (false, "A database error occurred while updating the password.");
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
                        return (false, "User to delete not found.");

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
                return (false, "A database error occurred while deleting the user.");
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
            get { return global::KeyTreeStoreKit.KeyTreeStoreManager.Get("Auth.OAuth.Google.ClientId", ""); }
        }

        /// <summary>
        /// Facebook OAuth App ID. AuthKit Admin sayfasindan veya Settings'ten ayarlanir.
        /// </summary>
        public static string FacebookAppId
        {
            get { return global::KeyTreeStoreKit.KeyTreeStoreManager.Get("Auth.OAuth.Facebook.AppId", ""); }
        }

        /// <summary>
        /// Google id_token ile giris yapar. Kullanici yoksa e-posta uzerinden olusturur.
        /// </summary>
        public static string LoginWithGoogle(string idToken, bool rememberMe)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                return SerializeOAuthError("Google login information could not be retrieved.");

            try
            {
                var pu = OAuthHelper.ValidateGoogleToken(idToken);
                if (pu == null || string.IsNullOrEmpty(pu.ProviderUserId))
                    return SerializeOAuthError("Google identity could not be verified.");

                return LoginWithProvider("google", pu, rememberMe);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.LoginWithGoogle", ex.Message);
                return SerializeOAuthError("An error occurred during Google login.");
            }
        }

        /// <summary>
        /// Facebook erisim token'i ile giris yapar. Kullanici yoksa e-posta uzerinden olusturur.
        /// </summary>
        public static string LoginWithFacebook(string accessToken, bool rememberMe)
        {
            if (string.IsNullOrWhiteSpace(accessToken))
                return SerializeOAuthError("Facebook login information could not be retrieved.");

            try
            {
                var pu = OAuthHelper.ValidateFacebookToken(accessToken);
                if (pu == null || string.IsNullOrEmpty(pu.ProviderUserId))
                    return SerializeOAuthError("Facebook identity could not be verified.");

                return LoginWithProvider("facebook", pu, rememberMe);
            }
            catch (Exception ex)
            {
                Composite.Core.Log.LogError("AuthenticationManager.LoginWithFacebook", ex.Message);
                return SerializeOAuthError("An error occurred during Facebook login.");
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
                return SerializeOAuthError("Could not create the OAuth user.");

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

