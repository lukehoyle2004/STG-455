using System;

namespace CapstoneGame.Auth
{
    /// <summary>
    /// A signed-in player's tokens, as returned by Supabase Auth. Field names match the JSON so
    /// <see cref="UnityEngine.JsonUtility"/> can read and write it directly.
    /// </summary>
    [Serializable]
    public class AuthSession
    {
        public string access_token;
        public string token_type;
        public int expires_in;
        /// <summary>Unix time (seconds) when <see cref="access_token"/> stops working.</summary>
        public long expires_at;
        public string refresh_token;
        public AuthUser user;

        public string UserId => user?.id;
        public string Email => user?.email;

        public bool ExpiresWithin(DateTimeOffset now, TimeSpan margin) =>
            DateTimeOffset.FromUnixTimeSeconds(expires_at) <= now + margin;
    }

    [Serializable]
    public class AuthUser
    {
        public string id;
        public string email;
    }

    public enum AuthStatus
    {
        Success,
        /// <summary>The account was created but the player must click the link in their email first.</summary>
        NeedsEmailConfirmation,
        Failed,
    }

    /// <summary>Outcome of an auth call. <see cref="Message"/> is safe to show to the player.</summary>
    public sealed class AuthResult
    {
        public AuthStatus Status { get; }
        public string Message { get; }
        public bool Succeeded => Status == AuthStatus.Success;

        private AuthResult(AuthStatus status, string message)
        {
            Status = status;
            Message = message;
        }

        public static AuthResult Ok(string message = null) => new AuthResult(AuthStatus.Success, message);
        public static AuthResult NeedsConfirmation(string message) => new AuthResult(AuthStatus.NeedsEmailConfirmation, message);
        public static AuthResult Fail(string message) => new AuthResult(AuthStatus.Failed, message);
    }

    /// <summary>Error body shapes Supabase Auth (GoTrue) uses; only the fields that are present get filled.</summary>
    [Serializable]
    internal class AuthErrorBody
    {
        public string error_code;
        public string msg;
        public string message;
        public string error;
        public string error_description;
    }

    [Serializable]
    internal class EmailPasswordBody
    {
        public string email;
        public string password;
    }

    [Serializable]
    internal class RefreshBody
    {
        public string refresh_token;
    }
}
