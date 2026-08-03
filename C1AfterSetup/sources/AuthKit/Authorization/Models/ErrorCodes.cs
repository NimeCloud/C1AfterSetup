namespace AuthKit.Authorization
{
    /// <summary>
    /// Hata kodlari ve mesajlari.
    /// </summary>
    public static class ErrorCodes
    {
        public const string Unauthorized = "UNAUTHORIZED";
        public const string Forbidden = "FORBIDDEN";
        public const string InvalidToken = "INVALID_TOKEN";
        public const string UserNotFound = "USER_NOT_FOUND";
    }
}
