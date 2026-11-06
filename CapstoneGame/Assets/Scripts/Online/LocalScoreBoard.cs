using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace CapstoneGame.Online
{
    /// <summary>One entry in the on-phone top 10.</summary>
    [Serializable]
    public class LocalScore
    {
        public int score;
        public int wave;
        /// <summary>UTC time the game ended, ISO 8601.</summary>
        public string played_at;
        /// <summary>True if the game was played without signing in.</summary>
        public bool guest;
    }

    /// <summary>Where the on-phone top 10 is kept.</summary>
    public interface ILocalScoreStore
    {
        List<LocalScore> Load();
        void Save(List<LocalScore> scores);
    }

    /// <summary>
    /// The best games played on this phone, guests included. It never leaves the phone and works with no
    /// internet or Supabase setup at all. For a "This phone" tab on the leaderboard screen:
    /// <c>foreach (LocalScore s in LocalScores.Board.Top) ...</c>
    /// </summary>
    public sealed class LocalScoreBoard
    {
        public const int MaxEntries = 10;

        private readonly ILocalScoreStore store;
        private readonly Func<DateTimeOffset> clock;

        public LocalScoreBoard(ILocalScoreStore store, Func<DateTimeOffset> clock = null)
        {
            this.store = store ?? new MemoryLocalScoreStore();
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>The top games, best first (ties: the earlier game ranks higher).</summary>
        public IReadOnlyList<LocalScore> Top => store.Load();

        /// <summary>Adds a finished game. Returns its place (1 = best) or 0 if it didn't make the top 10.</summary>
        public int Record(int score, int wave, bool guest)
        {
            var entry = new LocalScore
            {
                score = Math.Max(0, score),
                wave = Math.Max(1, wave),
                played_at = clock().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                guest = guest,
            };

            List<LocalScore> scores = store.Load();
            scores.Add(entry);
            // Stable sort: equal scores keep their order, so an older game stays above a newer one.
            List<LocalScore> sorted = new List<LocalScore>(scores);
            sorted.Sort((a, b) =>
            {
                int byScore = b.score.CompareTo(a.score);
                return byScore != 0 ? byScore : scores.IndexOf(a).CompareTo(scores.IndexOf(b));
            });
            if (sorted.Count > MaxEntries) sorted.RemoveRange(MaxEntries, sorted.Count - MaxEntries);

            store.Save(sorted);
            return sorted.IndexOf(entry) + 1;
        }

        public void Clear() => store.Save(new List<LocalScore>());
    }

    /// <summary>Keeps the on-phone top 10 in PlayerPrefs.</summary>
    public sealed class PlayerPrefsLocalScoreStore : ILocalScoreStore
    {
        public const string Key = "localTopScores";

        [Serializable]
        private class Wrapper
        {
            public List<LocalScore> items = new List<LocalScore>();
        }

        public List<LocalScore> Load()
        {
            string json = PlayerPrefs.GetString(Key, null);
            if (string.IsNullOrEmpty(json)) return new List<LocalScore>();
            try { return JsonUtility.FromJson<Wrapper>(json)?.items ?? new List<LocalScore>(); }
            catch (ArgumentException) { return new List<LocalScore>(); }
        }

        public void Save(List<LocalScore> scores)
        {
            if (scores == null || scores.Count == 0) PlayerPrefs.DeleteKey(Key);
            else PlayerPrefs.SetString(Key, JsonUtility.ToJson(new Wrapper { items = scores }));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Keeps the on-phone top 10 in memory only (used by tests).</summary>
    public sealed class MemoryLocalScoreStore : ILocalScoreStore
    {
        private List<LocalScore> items = new List<LocalScore>();

        public List<LocalScore> Load() => new List<LocalScore>(items);
        public void Save(List<LocalScore> scores) => items = new List<LocalScore>(scores ?? new List<LocalScore>());
    }

    /// <summary>The game's shared on-phone top 10.</summary>
    public static class LocalScores
    {
        private static LocalScoreBoard board;

        public static LocalScoreBoard Board => board ??= new LocalScoreBoard(new PlayerPrefsLocalScoreStore());

        /// <summary>Tests use this to swap in a board that doesn't touch PlayerPrefs.</summary>
        public static void Override(LocalScoreBoard replacement) => board = replacement;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset() => board = null;
    }
}
