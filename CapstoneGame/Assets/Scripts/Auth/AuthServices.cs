using UnityEngine;

namespace CapstoneGame.Auth
{
    /// <summary>
    /// The game's single shared <see cref="SupabaseAuth"/>. Any script can check
    /// <c>AuthServices.Client.IsSignedIn</c> or call <c>await AuthServices.Client.GetAccessToken()</c>.
    /// </summary>
    public static class AuthServices
    {
        private static SupabaseAuth client;

        public static SupabaseAuth Client => client ??= CreateDefault();

        /// <summary>Tests use this to swap in an auth client with a fake network.</summary>
        public static void Override(SupabaseAuth replacement) => client = replacement;

        /// <summary>Forget the current client; the next access to <see cref="Client"/> builds a fresh one.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => client = null;

        private static SupabaseAuth CreateDefault()
        {
            var store = new PlayerPrefsSessionStore();
            SupabaseSecrets secrets = SupabaseSecrets.Load();
            if (secrets == null || !secrets.IsValid)
                return SupabaseAuth.NotConfigured(store);

            return new SupabaseAuth(secrets.Url, secrets.AnonKey, new UnityHttpTransport(), store);
        }
    }
}
