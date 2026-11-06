using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CapstoneGame.Auth;
using UnityEngine;

namespace CapstoneGame.Online
{
    /// <summary>A finished game waiting to be sent to the submit-score Edge Function.</summary>
    [Serializable]
    public class PendingScore
    {
        public string user_id;
        public int score;
        public int wave;
        public int duration_s;
        public string client_game_id;
        public string played_at;
    }

    /// <summary>
    /// Sends finished games to the leaderboard. Games are queued first (saved on the device), so a game played
    /// offline is sent the next time the player is online. Each game has its own id, so a retry is never
    /// stored twice.
    ///
    /// From game-over code: <c>ScoreServices.Submitter.Record(score, wave, secondsPlayed);</c>
    ///
    /// Guest games are kept on the phone too. When someone signs in or creates an account on this phone,
    /// guest games from the last <see cref="MaxGuestAgeDays"/> days become theirs and are uploaded.
    /// </summary>
    public sealed class ScoreSubmitter
    {
        public const string FunctionPath = "/functions/v1/submit-score";

        /// <summary>Oldest games are dropped beyond this many waiting games.</summary>
        public const int MaxQueued = 50;

        /// <summary>Marks a queued game that was played as a guest and not claimed by an account yet.</summary>
        public const string GuestUserId = "guest";

        /// <summary>Older guest games are not uploaded (the server rejects games older than this).</summary>
        public const int MaxGuestAgeDays = 30;

        private readonly SupabaseAuth auth;
        private readonly IScoreQueueStore store;
        private readonly Func<DateTimeOffset> clock;
        private Task<int> flushInFlight;

        public ScoreSubmitter(SupabaseAuth auth, IScoreQueueStore store, Func<DateTimeOffset> clock = null)
        {
            this.auth = auth ?? throw new ArgumentNullException(nameof(auth));
            this.store = store ?? new MemoryScoreQueueStore();
            this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>Games waiting to be sent, for every player who has used this device.</summary>
        public int PendingCount => store.Load().Count;

        /// <summary>Guest games waiting on this phone for someone to sign in.</summary>
        public int GuestPendingCount => store.Load().FindAll(p => p.user_id == GuestUserId).Count;

        /// <summary>
        /// Queues a finished game. For a signed-in player it is sent right away in the background; a guest's game
        /// waits on the phone until someone signs in. Returns true when the game was sent or queued to send.
        /// </summary>
        public bool Record(int score, int wave, float secondsPlayed)
        {
            bool forPlayer = Enqueue(score, wave, secondsPlayed);
            if (forPlayer) FlushInBackground();
            return forPlayer;
        }

        /// <summary>
        /// Queues a game without sending it yet (tests use this directly). Returns true if it belongs to a
        /// signed-in player, false if it was kept as a guest game.
        /// </summary>
        public bool Enqueue(int score, int wave, float secondsPlayed)
        {
            bool signedIn = auth.IsConfigured && auth.IsSignedIn;

            List<PendingScore> queue = store.Load();
            queue.Add(new PendingScore
            {
                user_id = signedIn ? auth.Session.UserId : GuestUserId,
                score = Math.Max(0, score),
                wave = Math.Max(1, wave),
                duration_s = Mathf.Max(0, Mathf.RoundToInt(secondsPlayed)),
                client_game_id = Guid.NewGuid().ToString(),
                played_at = clock().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            });
            if (queue.Count > MaxQueued) queue.RemoveRange(0, queue.Count - MaxQueued);
            store.Save(queue);
            return signedIn;
        }

        /// <summary>
        /// Gives the guest games on this phone to the signed-in player so they get uploaded. Guest games older
        /// than <see cref="MaxGuestAgeDays"/> are dropped instead. Returns how many games were claimed.
        /// </summary>
        public int ClaimGuestGames()
        {
            if (!auth.IsConfigured || !auth.IsSignedIn) return 0;

            List<PendingScore> queue = store.Load();
            DateTimeOffset oldest = clock().AddDays(-MaxGuestAgeDays);
            int claimed = 0;

            queue.RemoveAll(p => p.user_id == GuestUserId && !PlayedSince(p, oldest));
            foreach (PendingScore p in queue)
            {
                if (p.user_id != GuestUserId) continue;
                p.user_id = auth.Session.UserId;
                claimed++;
            }

            store.Save(queue);
            return claimed;
        }

        private static bool PlayedSince(PendingScore game, DateTimeOffset oldest) =>
            DateTimeOffset.TryParse(game.played_at, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset playedAt) &&
            playedAt >= oldest;

        /// <summary>Sends waiting games without waiting for the result; problems are logged as warnings.</summary>
        public async void FlushInBackground()
        {
            try { await Flush(); }
            catch (Exception e) { Debug.LogWarning($"Couldn't send scores yet: {e.Message}"); }
        }

        /// <summary>
        /// Sends the signed-in player's waiting games, oldest first. Stops at the first network or server problem
        /// and keeps the rest for next time. Returns how many games were removed from the queue.
        /// </summary>
        public Task<int> Flush()
        {
            if (flushInFlight != null) return flushInFlight;
            Task<int> task = FlushOnce();
            if (!task.IsCompleted) flushInFlight = task;
            return task;
        }

        private async Task<int> FlushOnce()
        {
            try
            {
                if (!auth.IsConfigured || !auth.IsSignedIn) return 0;
                string userId = auth.Session.UserId;
                ClaimGuestGames(); // guest games played on this phone become this player's
                if (!store.Load().Exists(p => p.user_id == userId)) return 0;

                string token = await auth.GetAccessToken();
                if (string.IsNullOrEmpty(token) || auth.Session?.UserId != userId) return 0;

                var headers = new Dictionary<string, string>
                {
                    ["apikey"] = auth.ApiKey,
                    ["Authorization"] = "Bearer " + token,
                };

                int removed = 0;
                foreach (PendingScore game in store.Load())
                {
                    if (game.user_id != userId) continue; // another player's game: wait until they sign in

                    HttpResponse response = await auth.Http.Send("POST", auth.ProjectUrl + FunctionPath, ToJson(game), headers);

                    bool keep = response.NetworkError || response.StatusCode == 401 || response.StatusCode == 429 ||
                                response.StatusCode >= 500;
                    if (keep) break; // try again later

                    if (!response.IsSuccess)
                        Debug.LogWarning($"Score {game.score} was rejected by the server ({response.StatusCode}): {response.Body}");

                    Remove(game.client_game_id);
                    removed++;
                }

                return removed;
            }
            finally
            {
                flushInFlight = null;
            }
        }

        private void Remove(string clientGameId)
        {
            List<PendingScore> queue = store.Load();
            queue.RemoveAll(p => p.client_game_id == clientGameId);
            store.Save(queue);
        }

        private static string ToJson(PendingScore game) =>
            JsonUtility.ToJson(new SubmitBody
            {
                score = game.score,
                wave = game.wave,
                duration_s = game.duration_s,
                client_game_id = game.client_game_id,
                played_at = game.played_at,
            });

        [Serializable]
        private class SubmitBody
        {
            public int score;
            public int wave;
            public int duration_s;
            public string client_game_id;
            public string played_at;
        }
    }

    /// <summary>The game's shared <see cref="ScoreSubmitter"/>, tied to <see cref="AuthServices.Client"/>.</summary>
    public static class ScoreServices
    {
        private static ScoreSubmitter submitter;
        private static SupabaseAuth boundTo;

        public static ScoreSubmitter Submitter
        {
            get
            {
                SupabaseAuth auth = AuthServices.Client;
                if (submitter == null || boundTo != auth)
                {
                    submitter = new ScoreSubmitter(auth, new PlayerPrefsScoreQueueStore());
                    boundTo = auth;
                }
                return submitter;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            submitter = null;
            boundTo = null;
        }
    }
}
