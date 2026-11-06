using System;
using System.Threading;
using System.Threading.Tasks;
using CapstoneGame.Auth;
using CapstoneGame.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit Mode tests for <see cref="ScoreSubmitter"/>: queuing finished games, sending them to the submit-score
/// Edge Function, and keeping them for later when offline. Uses a fake network.
/// </summary>
public class ScoreSubmitterTests
{
    private const string Url = "https://test-project.supabase.co";
    private const string Key = "test-anon-key";
    private const string PlayerId = "11111111-2222-3333-4444-555555555555";
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private FakeHttpTransport http;
    private MemorySessionStore sessions;
    private MemoryScoreQueueStore queue;
    private SupabaseAuth auth;
    private ScoreSubmitter submitter;
    private SynchronizationContext savedContext;

    [SetUp]
    public void SetUp()
    {
        savedContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null); // run awaits immediately (see SupabaseAuthTests)

        http = new FakeHttpTransport();
        sessions = new MemorySessionStore();
        queue = new MemoryScoreQueueStore();
        auth = new SupabaseAuth(Url, Key, http, sessions, () => Now);
        submitter = new ScoreSubmitter(auth, queue, () => Now);
    }

    [TearDown]
    public void TearDown() => SynchronizationContext.SetSynchronizationContext(savedContext);

    private void SignIn()
    {
        sessions.Save(FakeHttpTransport.Session(Now.AddHours(1).ToUnixTimeSeconds(), accessToken: "player-token"));
        Assert.IsTrue(Run(auth.RestoreSession()).Succeeded);
        Assert.IsEmpty(http.Requests);
    }

    private static T Run<T>(Task<T> task)
    {
        Assert.IsTrue(task.IsCompleted, "Call did not finish (the fake network answers immediately).");
        return task.GetAwaiter().GetResult();
    }

    [Test]
    public void GuestGamesAreNotRecorded()
    {
        Assert.IsFalse(submitter.Enqueue(500, 2, 60f));
        Assert.AreEqual(0, submitter.PendingCount);
    }

    [Test]
    public void NotConfiguredDoesNothing()
    {
        var offline = new ScoreSubmitter(SupabaseAuth.NotConfigured(), queue);
        Assert.IsFalse(offline.Enqueue(500, 2, 60f));
        Assert.AreEqual(0, Run(offline.Flush()));
    }

    [Test]
    public void EnqueueRecordsGameForSignedInPlayer()
    {
        SignIn();

        Assert.IsTrue(submitter.Enqueue(1234, 5, 95.6f));

        PendingScore game = queue.Load()[0];
        Assert.AreEqual(PlayerId, game.user_id);
        Assert.AreEqual(1234, game.score);
        Assert.AreEqual(5, game.wave);
        Assert.AreEqual(96, game.duration_s);
        Assert.AreEqual("2026-11-06T12:00:00Z", game.played_at);
        Assert.IsTrue(Guid.TryParse(game.client_game_id, out _), "client_game_id must be a UUID.");
    }

    [Test]
    public void FlushSendsGameToEdgeFunctionWithPlayerToken()
    {
        SignIn();
        submitter.Enqueue(1234, 5, 96f);
        string id = queue.Load()[0].client_game_id;
        http.Respond(201, "{\"status\":\"saved\"}");

        Assert.AreEqual(1, Run(submitter.Flush()));

        Assert.AreEqual("POST", http.Last.Method);
        Assert.AreEqual(Url + "/functions/v1/submit-score", http.Last.Url);
        Assert.AreEqual(Key, http.Last.Headers["apikey"]);
        Assert.AreEqual("Bearer player-token", http.Last.Headers["Authorization"]);
        StringAssert.Contains("\"score\":1234", http.Last.Body);
        StringAssert.Contains("\"wave\":5", http.Last.Body);
        StringAssert.Contains("\"duration_s\":96", http.Last.Body);
        StringAssert.Contains(id, http.Last.Body);
        StringAssert.DoesNotContain("user_id", http.Last.Body, "The server decides the player from the token, not the body.");
        Assert.AreEqual(0, submitter.PendingCount);
    }

    [Test]
    public void DuplicateReplyCountsAsSent()
    {
        SignIn();
        submitter.Enqueue(10, 1, 5f);
        http.Respond(200, "{\"status\":\"duplicate\"}");

        Run(submitter.Flush());

        Assert.AreEqual(0, submitter.PendingCount);
    }

    [Test]
    public void OfflineGamesWaitForNextTime()
    {
        SignIn();
        submitter.Enqueue(10, 1, 5f);
        submitter.Enqueue(20, 2, 9f);
        http.RespondNetworkError();

        Assert.AreEqual(0, Run(submitter.Flush()));
        Assert.AreEqual(2, submitter.PendingCount);
        Assert.AreEqual(1, http.Requests.Count, "Should stop after the first network failure.");

        http.Respond(201, "{}");
        http.Respond(201, "{}");
        Assert.AreEqual(2, Run(submitter.Flush()));
        Assert.AreEqual(0, submitter.PendingCount);
        StringAssert.Contains("\"score\":10", http.Requests[1].Body, "Oldest game is sent first.");
    }

    [TestCase(401)]
    [TestCase(429)]
    [TestCase(500)]
    public void TemporaryServerProblemsKeepTheGame(int status)
    {
        SignIn();
        submitter.Enqueue(10, 1, 5f);
        http.Respond(status, "{}");

        Run(submitter.Flush());

        Assert.AreEqual(1, submitter.PendingCount);
    }

    [Test]
    public void RejectedGameIsDropped()
    {
        SignIn();
        submitter.Enqueue(10, 1, 5f);
        submitter.Enqueue(20, 2, 9f);
        http.Respond(400, "{\"error\":\"invalid_score\"}");
        http.Respond(201, "{}");

        LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("rejected"));
        Assert.AreEqual(2, Run(submitter.Flush()));
        Assert.AreEqual(0, submitter.PendingCount, "A game the server will never accept must not block the queue.");
    }

    [Test]
    public void OtherPlayersGamesWaitForThem()
    {
        queue.Save(new System.Collections.Generic.List<PendingScore>
        {
            new PendingScore { user_id = "someone-else", score = 1, wave = 1, client_game_id = Guid.NewGuid().ToString(), played_at = "2026-11-06T11:00:00Z" },
        });
        SignIn();

        Assert.AreEqual(0, Run(submitter.Flush()));
        Assert.IsEmpty(http.Requests);
        Assert.AreEqual(1, submitter.PendingCount);
    }

    [Test]
    public void QueueIsCapped()
    {
        SignIn();
        for (int i = 0; i < ScoreSubmitter.MaxQueued + 5; i++) submitter.Enqueue(i, 1, 1f);

        Assert.AreEqual(ScoreSubmitter.MaxQueued, submitter.PendingCount);
        Assert.AreEqual(5, queue.Load()[0].score, "The oldest games are dropped first.");
    }

    [Test]
    public void PlayerPrefsQueueRoundTrips()
    {
        string backup = PlayerPrefs.GetString(PlayerPrefsScoreQueueStore.Key, null);
        try
        {
            var store = new PlayerPrefsScoreQueueStore();
            var real = new ScoreSubmitter(auth, store, () => Now);
            store.Save(null);
            SignIn();
            real.Enqueue(77, 3, 30f);

            Assert.AreEqual(77, new PlayerPrefsScoreQueueStore().Load()[0].score);
            store.Save(null);
            Assert.AreEqual(0, store.Load().Count);
        }
        finally
        {
            if (string.IsNullOrEmpty(backup)) PlayerPrefs.DeleteKey(PlayerPrefsScoreQueueStore.Key);
            else PlayerPrefs.SetString(PlayerPrefsScoreQueueStore.Key, backup);
        }
    }
}
