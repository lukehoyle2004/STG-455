using System;
using System.Collections.Generic;
using UnityEngine;

namespace CapstoneGame.Online
{
    /// <summary>Where finished games wait until they are sent.</summary>
    public interface IScoreQueueStore
    {
        /// <summary>Returns a copy of the queue, oldest first.</summary>
        List<PendingScore> Load();
        void Save(List<PendingScore> queue);
    }

    /// <summary>Keeps the queue in PlayerPrefs so it survives closing the app.</summary>
    public sealed class PlayerPrefsScoreQueueStore : IScoreQueueStore
    {
        public const string Key = "supabase.pendingScores";

        [Serializable]
        private class Wrapper
        {
            public List<PendingScore> items = new List<PendingScore>();
        }

        public List<PendingScore> Load()
        {
            string json = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(json)) return new List<PendingScore>();
            try { return JsonUtility.FromJson<Wrapper>(json)?.items ?? new List<PendingScore>(); }
            catch (ArgumentException) { return new List<PendingScore>(); }
        }

        public void Save(List<PendingScore> queue)
        {
            if (queue == null || queue.Count == 0) PlayerPrefs.DeleteKey(Key);
            else PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Wrapper { items = queue }));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Keeps the queue in memory only (used by tests).</summary>
    public sealed class MemoryScoreQueueStore : IScoreQueueStore
    {
        private List<PendingScore> items = new List<PendingScore>();

        public List<PendingScore> Load() => new List<PendingScore>(items);
        public void Save(List<PendingScore> queue) => items = new List<PendingScore>(queue ?? new List<PendingScore>());
    }
}
