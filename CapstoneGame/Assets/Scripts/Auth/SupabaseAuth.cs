using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace CapstoneGame.Auth
{
    /// <summary>
    /// Email + password login against Supabase Auth, using its REST API directly
    /// (https://supabase.com/docs/reference/api — the /auth/v1 endpoints).
    ///
    /// Sign up, sign in, sign out, and "stay signed in": the session is saved through an
    /// <see cref="ISessionStore"/> and refreshed with the refresh token when the access token is about to expire.
    /// Use <see cref="AuthServices.Client"/> to get the game's shared instance.
    /// </summary>
    public sealed class SupabaseAuth
    {
        /// <summary>Supabase's default minimum password length.</summary>
        public const int MinPasswordLength = 6;

        /// <summary>Refresh this long before the access token actually expires.</summary>
        public static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(60);

        public const string NotConfiguredMessage = "Online login isn't set up in this copy of the game.";
        public const string NetworkErrorMessage = "Couldn't reach the server. Check your internet connection and try again.";

        private readonly string baseUrl;
        private readonly string apiKey;
        private readonly IHttpTransport http;
        private readonly ISessionStore store;
        private readonly Func<DateTimeOffset> clock;
        private Task<AuthResult> refreshInFlight;

        /// <summary>The current session, or null when signed out.</summary>
        public AuthSession Session { get; private set; }

        public bool IsSignedIn => Session != null;
        public bool IsConfigured => http != null && !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiKey);

        /// <summary>The project URL without a trailing slash, for other Supabase calls (functions, RPC).</summary>
        public string ProjectUrl => baseUrl;

        /// <summary>The public (anon / publishable) key sent as the <c>apikey</c> header.</summary>
        public string ApiKey => apiKey;

        /// <summary>The network transport, shared so other Supabase calls behave (and are faked in tests) the same way.</summary>
        public IHttpTransport Http => http;

        /// <summary>Raised after every sign in, refresh and sign out. The argument is the new session or null.</summary>
        public event Action<AuthSession> SessionChanged;

        public SupabaseAuth(string supabaseUrl, string apiKey, IHttpTransport http, ISessionStore store,
            Func<DateTimeOffset> clock = null)
        {
            baseUrl = supabaseUrl?.Trim().TrimEnd('/');
            this.apiKey = apiKey?.Trim();
            this.http = http;
            this.store = store ?? new MemorySessionStore();
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>An instance that refuses every call (used when there is no secrets file).</summary>
        public static SupabaseAuth NotConfigured(ISessionStore store = null) => new SupabaseAuth(null, null, null, store);

        public async Task<AuthResult> SignUp(string email, string password)
        {
            AuthResult invalid = Validate(ref email, password, newPassword: true);
            if (invalid != null) return invalid;

            HttpResponse response = await Post("/auth/v1/signup", JsonUtility.ToJson(new EmailPasswordBody { email = email, password = password }), null);
            if (!response.IsSuccess) return Fail(response);

            // With "Confirm email" on (the Supabase default) sign-up returns the user but no session.
            AuthSession session = ParseSession(response.Body);
            if (session == null)
                return AuthResult.NeedsConfirmation($"Almost done! Open the link we emailed to {email}, then sign in.");

            SetSession(session);
            return AuthResult.Ok();
        }

        public async Task<AuthResult> SignIn(string email, string password)
        {
            AuthResult invalid = Validate(ref email, password, newPassword: false);
            if (invalid != null) return invalid;

            HttpResponse response = await Post("/auth/v1/token?grant_type=password",
                JsonUtility.ToJson(new EmailPasswordBody { email = email, password = password }), null);
            if (!response.IsSuccess) return Fail(response);

            AuthSession session = ParseSession(response.Body);
            if (session == null) return AuthResult.Fail("The server sent an unexpected reply. Please try again.");

            SetSession(session);
            return AuthResult.Ok();
        }

        /// <summary>
        /// Call once at startup. Signs the player back in from the saved session, refreshing it if needed.
        /// When offline, keeps the saved session so the player stays signed in; it is refreshed later.
        /// </summary>
        public async Task<AuthResult> RestoreSession()
        {
            if (!IsConfigured) return AuthResult.Fail(NotConfiguredMessage);

            AuthSession saved = store.Load();
            if (saved == null || string.IsNullOrEmpty(saved.refresh_token)) return AuthResult.Fail(null);

            if (!saved.ExpiresWithin(clock(), RefreshMargin))
            {
                SetSession(saved);
                return AuthResult.Ok();
            }

            Session = saved;
            return await Refresh();
        }

        /// <summary>
        /// Returns an access token that is valid for at least a minute (refreshing first if needed), for calling
        /// Supabase APIs such as the submit-score Edge Function. Returns null when signed out or offline.
        /// </summary>
        public async Task<string> GetAccessToken()
        {
            if (Session == null) return null;
            if (Session.ExpiresWithin(clock(), RefreshMargin) && !(await Refresh()).Succeeded) return null;
            return Session?.access_token;
        }

        /// <summary>Signs out on the server (best effort) and always forgets the session on this device.</summary>
        public async Task<AuthResult> SignOut()
        {
            AuthSession current = Session ?? store.Load();
            SetSession(null);

            if (IsConfigured && current != null && !string.IsNullOrEmpty(current.access_token))
            {
                var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer " + current.access_token };
                // A failure here (offline, token already expired) is fine: the device no longer has the tokens.
                await Post("/auth/v1/logout", "{}", headers);
            }

            return AuthResult.Ok();
        }

        private Task<AuthResult> Refresh()
        {
            // Supabase refresh tokens are single-use, so two refreshes at once must share one request.
            if (refreshInFlight != null) return refreshInFlight;
            Task<AuthResult> task = RefreshOnce();
            if (!task.IsCompleted) refreshInFlight = task;
            return task;
        }

        private async Task<AuthResult> RefreshOnce()
        {
            try
            {
                string refreshToken = Session?.refresh_token;
                if (string.IsNullOrEmpty(refreshToken)) return AuthResult.Fail(null);

                HttpResponse response = await Post("/auth/v1/token?grant_type=refresh_token",
                    JsonUtility.ToJson(new RefreshBody { refresh_token = refreshToken }), null);

                if (response.NetworkError || response.StatusCode >= 500)
                {
                    // Can't tell right now; keep the player signed in and try again later.
                    SessionChanged?.Invoke(Session);
                    return AuthResult.Ok(NetworkErrorMessage);
                }

                AuthSession session = response.IsSuccess ? ParseSession(response.Body) : null;
                if (session == null)
                {
                    SetSession(null);
                    return AuthResult.Fail("Your login expired. Please sign in again.");
                }

                SetSession(session);
                return AuthResult.Ok();
            }
            finally
            {
                refreshInFlight = null;
            }
        }

        private AuthResult Validate(ref string email, string password, bool newPassword)
        {
            if (!IsConfigured) return AuthResult.Fail(NotConfiguredMessage);

            email = email?.Trim() ?? "";
            if (!LooksLikeEmail(email)) return AuthResult.Fail("Enter a valid email address.");
            if (string.IsNullOrEmpty(password)) return AuthResult.Fail("Enter your password.");
            if (newPassword && password.Length < MinPasswordLength)
                return AuthResult.Fail($"Password must be at least {MinPasswordLength} characters.");
            return null;
        }

        public static bool LooksLikeEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || email.Contains(" ")) return false;
            int at = email.IndexOf('@');
            return at > 0 && at == email.LastIndexOf('@') && email.IndexOf('.', at) > at + 1 && !email.EndsWith(".");
        }

        private Task<HttpResponse> Post(string path, string json, Dictionary<string, string> extraHeaders)
        {
            var headers = new Dictionary<string, string> { ["apikey"] = apiKey };
            if (extraHeaders != null)
                foreach (KeyValuePair<string, string> h in extraHeaders) headers[h.Key] = h.Value;
            return http.Send("POST", baseUrl + path, json, headers);
        }

        private AuthSession ParseSession(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;

            AuthSession session;
            try { session = JsonUtility.FromJson<AuthSession>(json); }
            catch (ArgumentException) { return null; }

            if (session == null || string.IsNullOrEmpty(session.access_token) || string.IsNullOrEmpty(session.refresh_token))
                return null;

            if (session.expires_at <= 0)
                session.expires_at = clock().ToUnixTimeSeconds() + Math.Max(session.expires_in, 0);
            return session;
        }

        private void SetSession(AuthSession session)
        {
            Session = session;
            if (session == null) store.Clear();
            else store.Save(session);
            SessionChanged?.Invoke(session);
        }

        private static AuthResult Fail(HttpResponse response)
        {
            if (response.NetworkError) return AuthResult.Fail(NetworkErrorMessage);
            if (response.StatusCode == 429) return AuthResult.Fail("Too many attempts. Wait a minute and try again.");

            AuthErrorBody body = null;
            if (!string.IsNullOrEmpty(response.Body))
            {
                try { body = JsonUtility.FromJson<AuthErrorBody>(response.Body); }
                catch (ArgumentException) { /* not JSON */ }
            }

            switch (body?.error_code)
            {
                case "invalid_credentials": return AuthResult.Fail("Wrong email or password.");
                case "email_not_confirmed": return AuthResult.Fail("Confirm your email first: open the link we sent you, then sign in.");
                case "user_already_exists":
                case "email_exists": return AuthResult.Fail("An account with this email already exists. Try signing in.");
                case "signup_disabled":
                case "email_provider_disabled": return AuthResult.Fail("New accounts are turned off right now.");
                case "over_email_send_rate_limit":
                case "over_request_rate_limit": return AuthResult.Fail("Too many attempts. Wait a minute and try again.");
            }

            string message = FirstNonEmpty(body?.msg, body?.message, body?.error_description, body?.error);
            return AuthResult.Fail(message ?? $"Login failed (error {response.StatusCode}). Please try again.");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string v in values)
                if (!string.IsNullOrWhiteSpace(v)) return v;
            return null;
        }
    }
}
