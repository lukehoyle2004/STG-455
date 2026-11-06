using UnityEngine;

namespace CapstoneGame.Auth
{
    /// <summary>
    /// The Supabase project URL and public (anon / publishable) key.
    ///
    /// The real file lives at Assets/Resources/SupabaseSecrets.asset and is gitignored, because this repo is
    /// public. Each teammate creates their own with Tools > Supabase > Create or Select Secrets File and pastes
    /// the values from the Supabase dashboard (Project Settings > API Keys / Data API).
    ///
    /// NEVER put the service_role or secret key here: it bypasses row level security, and anything in this
    /// file ships inside the game build.
    /// </summary>
    public sealed class SupabaseSecrets : ScriptableObject
    {
        /// <summary>Path passed to Resources.Load (the file name without extension).</summary>
        public const string ResourcePath = "SupabaseSecrets";

        [Tooltip("Project URL, e.g. https://abcdefghijklmnop.supabase.co")]
        [SerializeField] private string url;

        [Tooltip("The anon (legacy) or publishable (sb_publishable_...) key. Never the service_role or secret key.")]
        [SerializeField, TextArea(2, 6)] private string anonKey;

        public string Url => url?.Trim();
        public string AnonKey => anonKey?.Trim();

        public bool IsValid =>
            !string.IsNullOrEmpty(Url) && Url.StartsWith("http") && !string.IsNullOrEmpty(AnonKey);

        /// <summary>Loads the gitignored secrets file, or returns null if this copy of the project has none.</summary>
        public static SupabaseSecrets Load() => Resources.Load<SupabaseSecrets>(ResourcePath);

        /// <summary>Creates an in-memory instance (for tests or tools); not saved to disk.</summary>
        public static SupabaseSecrets Create(string url, string anonKey)
        {
            var secrets = CreateInstance<SupabaseSecrets>();
            secrets.url = url;
            secrets.anonKey = anonKey;
            return secrets;
        }
    }
}
