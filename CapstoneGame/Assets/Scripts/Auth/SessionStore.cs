using UnityEngine;

namespace CapstoneGame.Auth
{
    /// <summary>Where the signed-in session is kept between app launches.</summary>
    public interface ISessionStore
    {
        AuthSession Load();
        void Save(AuthSession session);
        void Clear();
    }

    /// <summary>
    /// Keeps the session in PlayerPrefs so players stay signed in after closing the app.
    /// PlayerPrefs is app-private storage on Android/iOS, but it is not encrypted; that's acceptable here
    /// because the tokens only grant this player's own rows (row level security) and expire.
    /// </summary>
    public sealed class PlayerPrefsSessionStore : ISessionStore
    {
        public const string Key = "supabase.session";

        public AuthSession Load()
        {
            string json = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(json)) return null;

            AuthSession session = null;
            try { session = JsonUtility.FromJson<AuthSession>(json); }
            catch (System.ArgumentException) { /* corrupted value: treat as signed out */ }

            return session != null && !string.IsNullOrEmpty(session.refresh_token) ? session : null;
        }

        public void Save(AuthSession session)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(session));
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }

    /// <summary>Keeps the session in memory only (used by tests).</summary>
    public sealed class MemorySessionStore : ISessionStore
    {
        public AuthSession Saved { get; private set; }

        public MemorySessionStore(AuthSession initial = null) => Saved = initial;

        public AuthSession Load() => Saved;
        public void Save(AuthSession session) => Saved = session;
        public void Clear() => Saved = null;
    }
}
