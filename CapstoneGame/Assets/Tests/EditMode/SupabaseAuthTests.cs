using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CapstoneGame.Auth;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Edit Mode tests for <see cref="SupabaseAuth"/>: sign up, sign in, staying signed in (restore + refresh),
/// sign out, and friendly error messages. A fake network is used, so no Supabase project is needed.
/// </summary>
public class SupabaseAuthTests
{
    private const string Url = "https://test-project.supabase.co";
    private const string Key = "test-anon-key";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    private FakeHttpTransport http;
    private MemorySessionStore store;
    private SupabaseAuth auth;
    private SynchronizationContext savedContext;

    [SetUp]
    public void SetUp()
    {
        // Without Unity's main-thread context, awaits resume immediately, so each test runs start to finish
        // synchronously instead of waiting for editor frames.
        savedContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);

        http = new FakeHttpTransport();
        store = new MemorySessionStore();
        auth = new SupabaseAuth(Url + "/", Key, http, store, () => Now);
    }

    [TearDown]
    public void TearDown() => SynchronizationContext.SetSynchronizationContext(savedContext);

    private static T Run<T>(Task<T> task)
    {
        Assert.IsTrue(task.IsCompleted, "Auth call did not finish (the fake network answers immediately).");
        return task.GetAwaiter().GetResult();
    }

    private long InOneHour => Now.AddHours(1).ToUnixTimeSeconds();

    // ---------- sign in ----------

    [Test]
    public void SignInSendsPasswordGrantWithApiKey()
    {
        http.Respond(200, FakeHttpTransport.SessionJson(expiresAt: InOneHour));

        AuthResult result = Run(auth.SignIn("  pilot@example.com ", "hunter22"));

        Assert.IsTrue(result.Succeeded, result.Message);
        Assert.AreEqual("POST", http.Last.Method);
        Assert.AreEqual(Url + "/auth/v1/token?grant_type=password", http.Last.Url);
        Assert.AreEqual(Key, http.Last.Headers["apikey"]);
        StringAssert.Contains("\"email\":\"pilot@example.com\"", http.Last.Body, "Email should be trimmed.");
        StringAssert.Contains("\"password\":\"hunter22\"", http.Last.Body);
    }

    [Test]
    public void SignInSavesSessionAndRaisesEvent()
    {
        http.Respond(200, FakeHttpTransport.SessionJson(expiresAt: InOneHour));
        AuthSession raised = null;
        auth.SessionChanged += s => raised = s;

        Run(auth.SignIn("pilot@example.com", "hunter22"));

        Assert.IsTrue(auth.IsSignedIn);
        Assert.AreEqual("pilot@example.com", auth.Session.Email);
        Assert.AreEqual("access-1", store.Saved.access_token);
        Assert.AreEqual("refresh-1", store.Saved.refresh_token);
        Assert.AreSame(auth.Session, raised);
    }

    [Test]
    public void SignInWorksOutExpiryWhenServerOmitsIt()
    {
        http.Respond(200, FakeHttpTransport.SessionJson(expiresAt: 0));

        Run(auth.SignIn("pilot@example.com", "hunter22"));

        Assert.AreEqual(Now.ToUnixTimeSeconds() + 3600, auth.Session.expires_at);
    }

    [Test]
    public void WrongPasswordShowsFriendlyMessageAndStaysSignedOut()
    {
        http.Respond(400, "{\"code\":400,\"error_code\":\"invalid_credentials\",\"msg\":\"Invalid login credentials\"}");

        AuthResult result = Run(auth.SignIn("pilot@example.com", "wrong-password"));

        Assert.AreEqual(AuthStatus.Failed, result.Status);
        Assert.AreEqual("Wrong email or password.", result.Message);
        Assert.IsFalse(auth.IsSignedIn);
        Assert.IsNull(store.Saved);
    }

    [Test]
    public void UnknownServerErrorShowsServerMessage()
    {
        http.Respond(422, "{\"code\":422,\"error_code\":\"weak_password\",\"msg\":\"Password should contain a number.\"}");

        AuthResult result = Run(auth.SignUp("pilot@example.com", "abcdefg"));

        Assert.AreEqual("Password should contain a number.", result.Message);
    }

    [Test]
    public void OfflineSignInExplainsTheProblem()
    {
        http.RespondNetworkError();

        AuthResult result = Run(auth.SignIn("pilot@example.com", "hunter22"));

        Assert.AreEqual(SupabaseAuth.NetworkErrorMessage, result.Message);
        Assert.IsFalse(auth.IsSignedIn);
    }

    [Test]
    public void TooManyAttemptsIsReported()
    {
        http.Respond(429, "");
        StringAssert.Contains("Too many attempts", Run(auth.SignIn("pilot@example.com", "hunter22")).Message);
    }

    [TestCase("", "hunter22")]
    [TestCase("not-an-email", "hunter22")]
    [TestCase("pilot@example.com", "")]
    public void BadInputIsRejectedWithoutContactingServer(string email, string password)
    {
        AuthResult result = Run(auth.SignIn(email, password));

        Assert.AreEqual(AuthStatus.Failed, result.Status);
        Assert.IsNotEmpty(result.Message);
        Assert.IsEmpty(http.Requests);
    }

    // ---------- sign up ----------

    [Test]
    public void SignUpWithEmailConfirmationAsksPlayerToCheckEmail()
    {
        // With "Confirm email" on, Supabase returns the new user but no tokens.
        http.Respond(200, "{\"id\":\"11111111-2222-3333-4444-555555555555\",\"email\":\"pilot@example.com\",\"confirmation_sent_at\":\"2026-11-06T00:00:00Z\"}");

        AuthResult result = Run(auth.SignUp("pilot@example.com", "hunter22"));

        Assert.AreEqual(Url + "/auth/v1/signup", http.Last.Url);
        Assert.AreEqual(AuthStatus.NeedsEmailConfirmation, result.Status);
        StringAssert.Contains("pilot@example.com", result.Message);
        Assert.IsFalse(auth.IsSignedIn);
    }

    [Test]
    public void SignUpWithoutEmailConfirmationSignsStraightIn()
    {
        http.Respond(200, FakeHttpTransport.SessionJson(expiresAt: InOneHour));

        AuthResult result = Run(auth.SignUp("pilot@example.com", "hunter22"));

        Assert.IsTrue(result.Succeeded);
        Assert.IsTrue(auth.IsSignedIn);
    }

    [Test]
    public void SignUpRequiresMinimumPasswordLength()
    {
        AuthResult result = Run(auth.SignUp("pilot@example.com", "12345"));

        StringAssert.Contains(SupabaseAuth.MinPasswordLength.ToString(), result.Message);
        Assert.IsEmpty(http.Requests);
    }

    [Test]
    public void SignUpWithExistingEmailSuggestsSigningIn()
    {
        http.Respond(422, "{\"code\":422,\"error_code\":\"user_already_exists\",\"msg\":\"User already registered\"}");
        StringAssert.Contains("already exists", Run(auth.SignUp("pilot@example.com", "hunter22")).Message);
    }

    // ---------- staying signed in ----------

    [Test]
    public void RestoreWithNoSavedSessionStaysSignedOutQuietly()
    {
        AuthResult result = Run(auth.RestoreSession());

        Assert.IsFalse(result.Succeeded);
        Assert.IsNull(result.Message, "Nothing to show the player when they simply aren't signed in yet.");
        Assert.IsEmpty(http.Requests);
    }

    [Test]
    public void RestoreWithFreshSessionNeedsNoNetwork()
    {
        store.Save(FakeHttpTransport.Session(InOneHour));

        Assert.IsTrue(Run(auth.RestoreSession()).Succeeded);
        Assert.IsTrue(auth.IsSignedIn);
        Assert.IsEmpty(http.Requests);
    }

    [Test]
    public void RestoreWithExpiredSessionRefreshesIt()
    {
        store.Save(FakeHttpTransport.Session(Now.AddMinutes(-5).ToUnixTimeSeconds()));
        http.Respond(200, FakeHttpTransport.SessionJson("new-access", "new-refresh", InOneHour));

        Assert.IsTrue(Run(auth.RestoreSession()).Succeeded);

        Assert.AreEqual(Url + "/auth/v1/token?grant_type=refresh_token", http.Last.Url);
        StringAssert.Contains("\"refresh_token\":\"old-refresh\"", http.Last.Body);
        Assert.AreEqual("new-access", auth.Session.access_token);
        Assert.AreEqual("new-refresh", store.Saved.refresh_token, "The new refresh token must be saved (they are single-use).");
    }

    [Test]
    public void RestoreWhenRefreshIsRejectedSignsOut()
    {
        store.Save(FakeHttpTransport.Session(Now.AddMinutes(-5).ToUnixTimeSeconds()));
        http.Respond(400, "{\"code\":400,\"error_code\":\"refresh_token_not_found\",\"msg\":\"Invalid Refresh Token\"}");

        AuthResult result = Run(auth.RestoreSession());

        Assert.IsFalse(result.Succeeded);
        StringAssert.Contains("sign in again", result.Message);
        Assert.IsFalse(auth.IsSignedIn);
        Assert.IsNull(store.Saved);
    }

    [Test]
    public void RestoreWhileOfflineKeepsPlayerSignedIn()
    {
        store.Save(FakeHttpTransport.Session(Now.AddMinutes(-5).ToUnixTimeSeconds()));
        http.RespondNetworkError();

        Assert.IsTrue(Run(auth.RestoreSession()).Succeeded);
        Assert.IsTrue(auth.IsSignedIn);
        Assert.AreEqual("old-refresh", store.Saved.refresh_token);
    }

    [Test]
    public void GetAccessTokenRefreshesWhenAboutToExpire()
    {
        store.Save(FakeHttpTransport.Session(Now.AddSeconds(30).ToUnixTimeSeconds()));
        http.Respond(200, FakeHttpTransport.SessionJson("new-access", "new-refresh", InOneHour)); // used by restore
        Run(auth.RestoreSession());
        Assert.AreEqual(1, http.Requests.Count);

        Assert.AreEqual("new-access", Run(auth.GetAccessToken()));
        Assert.AreEqual(1, http.Requests.Count, "A fresh token should not be refreshed again.");
    }

    [Test]
    public void SimultaneousRefreshesShareOneRequest()
    {
        store.Save(FakeHttpTransport.Session(Now.AddMinutes(-5).ToUnixTimeSeconds()));
        var pending = http.HoldNext();

        Task<AuthResult> restore = auth.RestoreSession();
        Task<string> token = auth.GetAccessToken();
        Assert.IsFalse(restore.IsCompleted);

        pending.SetResult(new HttpResponse { StatusCode = 200, Body = FakeHttpTransport.SessionJson("new-access", "new-refresh", InOneHour) });

        Assert.IsTrue(Run(restore).Succeeded);
        Assert.AreEqual("new-access", Run(token));
        Assert.AreEqual(1, http.Requests.Count, "Refresh tokens are single-use, so only one refresh may be sent.");
    }

    // ---------- sign out ----------

    [Test]
    public void SignOutForgetsSessionAndTellsServer()
    {
        http.Respond(200, FakeHttpTransport.SessionJson(expiresAt: InOneHour));
        Run(auth.SignIn("pilot@example.com", "hunter22"));
        http.Respond(204, "");

        Assert.IsTrue(Run(auth.SignOut()).Succeeded);

        Assert.IsFalse(auth.IsSignedIn);
        Assert.IsNull(store.Saved);
        Assert.AreEqual(Url + "/auth/v1/logout", http.Last.Url);
        Assert.AreEqual("Bearer access-1", http.Last.Headers["Authorization"]);
    }

    [Test]
    public void SignOutWhileOfflineStillSignsOutOnDevice()
    {
        store.Save(FakeHttpTransport.Session(InOneHour));
        Run(auth.RestoreSession());
        http.RespondNetworkError();

        Assert.IsTrue(Run(auth.SignOut()).Succeeded);
        Assert.IsFalse(auth.IsSignedIn);
        Assert.IsNull(store.Saved);
    }

    // ---------- configuration ----------

    [Test]
    public void NotConfiguredRefusesWithoutCrashing()
    {
        var offline = SupabaseAuth.NotConfigured();

        Assert.IsFalse(offline.IsConfigured);
        Assert.AreEqual(SupabaseAuth.NotConfiguredMessage, Run(offline.SignIn("pilot@example.com", "hunter22")).Message);
        Assert.AreEqual(SupabaseAuth.NotConfiguredMessage, Run(offline.SignUp("pilot@example.com", "hunter22")).Message);
        Assert.IsFalse(Run(offline.RestoreSession()).Succeeded);
        Assert.IsTrue(Run(offline.SignOut()).Succeeded);
    }

    [Test]
    public void SecretsNeedUrlAndKey()
    {
        Assert.IsTrue(SupabaseSecrets.Create(" https://abc.supabase.co ", " key ").IsValid);
        Assert.AreEqual("https://abc.supabase.co", SupabaseSecrets.Create(" https://abc.supabase.co ", "key").Url);
        Assert.IsFalse(SupabaseSecrets.Create("", "key").IsValid);
        Assert.IsFalse(SupabaseSecrets.Create("https://abc.supabase.co", "").IsValid);
        Assert.IsFalse(SupabaseSecrets.Create("abc.supabase.co", "key").IsValid);
    }

    [Test]
    public void SecretsFileIsGitignored()
    {
        // The repo is public: the file holding the Supabase URL and key must never be committed.
        string gitignore = Path.Combine(Application.dataPath, "..", "..", ".gitignore");
        Assume.That(File.Exists(gitignore), "No .gitignore found next to the Unity project.");
        StringAssert.Contains("SupabaseSecrets*.asset", File.ReadAllText(gitignore));
    }

    [TestCase("pilot@example.com", true)]
    [TestCase("a.b+c@sub.example.co", true)]
    [TestCase("pilot@", false)]
    [TestCase("@example.com", false)]
    [TestCase("pilot@example", false)]
    [TestCase("pi lot@example.com", false)]
    [TestCase("a@b@example.com", false)]
    public void EmailCheck(string email, bool expected)
    {
        Assert.AreEqual(expected, SupabaseAuth.LooksLikeEmail(email));
    }

    [Test]
    public void PlayerPrefsStoreRoundTrips()
    {
        string backup = PlayerPrefs.GetString(PlayerPrefsSessionStore.Key, null);
        try
        {
            var prefsStore = new PlayerPrefsSessionStore();
            prefsStore.Save(FakeHttpTransport.Session(InOneHour));

            AuthSession loaded = prefsStore.Load();
            Assert.AreEqual("old-refresh", loaded.refresh_token);
            Assert.AreEqual("pilot@example.com", loaded.Email);

            prefsStore.Clear();
            Assert.IsNull(prefsStore.Load());
        }
        finally
        {
            if (string.IsNullOrEmpty(backup)) PlayerPrefs.DeleteKey(PlayerPrefsSessionStore.Key);
            else PlayerPrefs.SetString(PlayerPrefsSessionStore.Key, backup);
        }
    }
}
