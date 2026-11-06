using System;
using System.Collections;
using CapstoneGame.Auth;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>
/// Play Mode checks for the Login scene: the form shows up with big enough touch targets, signing in / out
/// switches screens, errors are shown, a saved login is remembered, and guests can still play.
/// Uses a fake network, so no Supabase project is needed.
/// </summary>
public class LoginScreenTests
{
    private const string LoginScene = "Login";
    private const string GameScene = "SampleScene";

    private FakeHttpTransport http;
    private MemorySessionStore store;
    private LoginScreen screen;

    private static long InOneHour => DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();

    private IEnumerator LoadLogin(AuthSession saved = null, bool configured = true)
    {
        http = new FakeHttpTransport();
        store = new MemorySessionStore(saved);
        AuthServices.Override(configured
            ? new SupabaseAuth("https://test-project.supabase.co", "test-anon-key", http, store)
            : SupabaseAuth.NotConfigured(store));

        yield return SceneManager.LoadSceneAsync(LoginScene, LoadSceneMode.Single);
        yield return null; // Start runs

        screen = UnityEngine.Object.FindAnyObjectByType<LoginScreen>();
        Assert.IsNotNull(screen, $"{LoginScene} has no LoginScreen.");
        yield return WaitUntilIdle();
    }

    [TearDown]
    public void ResetAuth() => AuthServices.Reset();

    private IEnumerator WaitUntilIdle(float timeout = 3f)
    {
        float end = Time.realtimeSinceStartup + timeout;
        while (screen.Busy && Time.realtimeSinceStartup < end) yield return null;
        yield return null;
        Assert.IsFalse(screen.Busy, "Login screen is stuck waiting for the server.");
    }

    [UnityTest]
    public IEnumerator SignedOutPlayerSeesFormWithLargeTouchTargets()
    {
        yield return LoadLogin();

        Assert.IsTrue(screen.FormPanel.activeInHierarchy, "Sign-in form is not showing.");
        Assert.IsFalse(screen.SignedInPanel.activeInHierarchy);
        Assert.IsTrue(screen.SignInButton.interactable);

        Canvas.ForceUpdateCanvases();
        yield return null;

        foreach (Selectable control in new Selectable[]
                 { screen.EmailField, screen.PasswordField, screen.SignInButton, screen.SignUpButton, screen.GuestButton })
            AssertOnScreenAndLargeEnough((RectTransform)control.transform);
    }

    [UnityTest]
    public IEnumerator SigningInShowsPlayAndSignOut()
    {
        yield return LoadLogin();
        http.Respond(200, FakeHttpTransport.SessionJson(expiresAt: InOneHour));

        screen.EmailField.text = "pilot@example.com";
        screen.PasswordField.text = "hunter22";
        screen.SignInButton.onClick.Invoke();
        yield return WaitUntilIdle();

        Assert.IsTrue(screen.SignedInPanel.activeInHierarchy, "Signed-in screen did not appear.");
        Assert.IsFalse(screen.FormPanel.activeInHierarchy);
        StringAssert.Contains("pilot@example.com", screen.SignedInLabel.text);
        Assert.IsEmpty(screen.PasswordField.text, "Password should be cleared after signing in.");
        Assert.IsNotNull(store.Saved, "Session was not saved, so the player would have to sign in every launch.");

        Canvas.ForceUpdateCanvases();
        yield return null;
        AssertOnScreenAndLargeEnough((RectTransform)screen.PlayButton.transform);
        AssertOnScreenAndLargeEnough((RectTransform)screen.SignOutButton.transform);
    }

    [UnityTest]
    public IEnumerator WrongPasswordShowsError()
    {
        yield return LoadLogin();
        http.Respond(400, "{\"code\":400,\"error_code\":\"invalid_credentials\",\"msg\":\"Invalid login credentials\"}");

        screen.EmailField.text = "pilot@example.com";
        screen.PasswordField.text = "wrong-password";
        screen.SignInButton.onClick.Invoke();
        yield return WaitUntilIdle();

        Assert.AreEqual("Wrong email or password.", screen.StatusText.text);
        Assert.IsTrue(screen.FormPanel.activeInHierarchy);
    }

    [UnityTest]
    public IEnumerator SavedLoginIsRemembered()
    {
        yield return LoadLogin(FakeHttpTransport.Session(InOneHour));

        Assert.IsTrue(screen.SignedInPanel.activeInHierarchy, "A saved login should skip the sign-in form.");
        Assert.IsEmpty(http.Requests, "A fresh saved session should not need the network.");
    }

    [UnityTest]
    public IEnumerator SignOutReturnsToForm()
    {
        yield return LoadLogin(FakeHttpTransport.Session(InOneHour));
        http.Respond(204, "");

        screen.SignOutButton.onClick.Invoke();
        yield return WaitUntilIdle();

        Assert.IsTrue(screen.FormPanel.activeInHierarchy);
        Assert.IsNull(store.Saved, "Signing out must forget the saved login.");
        StringAssert.EndsWith("/auth/v1/logout", http.Last.Url);
    }

    [UnityTest]
    public IEnumerator WithoutSupabaseSetupGuestsCanStillPlay()
    {
        yield return LoadLogin(configured: false);

        StringAssert.Contains(SupabaseAuth.NotConfiguredMessage, screen.StatusText.text);
        Assert.IsFalse(screen.SignInButton.interactable);
        Assert.IsTrue(screen.GuestButton.interactable);

        screen.GuestButton.onClick.Invoke();
        float end = Time.realtimeSinceStartup + 5f;
        while (SceneManager.GetActiveScene().name != GameScene && Time.realtimeSinceStartup < end) yield return null;

        Assert.AreEqual(GameScene, SceneManager.GetActiveScene().name, "Play as guest did not open the game.");
    }

    private static void AssertOnScreenAndLargeEnough(RectTransform rect)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 sizePx = corners[2] - corners[0];

        Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(0f), $"{rect.name} is off the left of the screen.");
        Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(0f), $"{rect.name} is off the bottom of the screen.");
        Assert.That(corners[2].x, Is.LessThanOrEqualTo(Screen.width), $"{rect.name} is off the right of the screen.");
        Assert.That(corners[2].y, Is.LessThanOrEqualTo(Screen.height), $"{rect.name} is off the top of the screen.");

        // FR7: touch targets at least 44 x 44 points. One point is about 1/160 inch.
        float pixelsPerPoint = Screen.dpi > 0f ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
        float minPx = 44f * pixelsPerPoint;
        Assert.That(Mathf.Min(sizePx.x, sizePx.y), Is.GreaterThanOrEqualTo(minPx),
            $"{rect.name} is {sizePx.x:F0}x{sizePx.y:F0}px; needs at least {minPx:F0}px (44pt).");
    }
}
