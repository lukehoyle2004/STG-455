using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Play Mode checks for the player ship in the gameplay scene: keyboard movement, staying on screen,
/// firing, the two-shots-on-screen rule, shots hitting things, and the touch fire button layout.
/// </summary>
public class PlayerSmokeTests
{
    /// <summary>The scene that contains the player. Update this when the real Game scene is added.</summary>
    private const string GameplayScene = "SampleScene";

    private PlayerController player;
    private PlayerShooting shooter;
    private Keyboard testKeyboard;

    private InputSettings.BackgroundBehavior savedBackground;
#if UNITY_EDITOR
    private InputSettings.EditorInputBehaviorInPlayMode savedEditorBehavior;
#endif

    [UnitySetUp]
    public IEnumerator LoadGameplayScene()
    {
        // In batch mode (CI) there is no focused Game view, so by default the Input System ignores
        // device input. Send all input to the game during these tests.
        savedBackground = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
        savedEditorBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif

        yield return SceneManager.LoadSceneAsync(GameplayScene, LoadSceneMode.Single);
        yield return null; // let Awake/Start run

        player = Object.FindAnyObjectByType<PlayerController>();
        shooter = Object.FindAnyObjectByType<PlayerShooting>();
        Assert.IsNotNull(player, $"{GameplayScene} has no PlayerController.");
        Assert.IsNotNull(shooter, $"{GameplayScene} has no PlayerShooting.");
    }

    [TearDown]
    public void RemoveTestDevices()
    {
        if (testKeyboard != null) InputSystem.RemoveDevice(testKeyboard);
        testKeyboard = null;

        InputSystem.settings.backgroundBehavior = savedBackground;
#if UNITY_EDITOR
        InputSystem.settings.editorInputBehaviorInPlayMode = savedEditorBehavior;
#endif
    }

    [UnityTest]
    public IEnumerator ShipMovesRightWithArrowKey()
    {
        testKeyboard = InputSystem.AddDevice<Keyboard>("Test Keyboard");
        player.transform.position = new Vector3(0f, player.transform.position.y, 0f);
        yield return null;

        float startX = player.transform.position.x;
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.RightArrow));
        yield return new WaitForSeconds(0.25f);
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());
        yield return null;

        Assert.That(player.transform.position.x, Is.GreaterThan(startX + 0.5f),
            "Holding the right arrow did not move the ship right.");
    }

    [UnityTest]
    public IEnumerator ShipStaysInsideTheScreen()
    {
        Camera cam = Camera.main;
        var sprite = player.GetComponentInChildren<SpriteRenderer>();

        foreach (Vector3 far in new[] { new Vector3(-999, -999), new Vector3(999, 999), new Vector3(-999, 999), new Vector3(999, -999) })
        {
            player.transform.position = far;
            yield return null;
            yield return null;

            Vector3 min = cam.WorldToViewportPoint(sprite.bounds.min);
            Vector3 max = cam.WorldToViewportPoint(sprite.bounds.max);
            Assert.That(min.x, Is.GreaterThanOrEqualTo(0f), $"Ship left the screen on the left (pushed toward {far}).");
            Assert.That(max.x, Is.LessThanOrEqualTo(1f), $"Ship left the screen on the right (pushed toward {far}).");
            Assert.That(min.y, Is.GreaterThanOrEqualTo(0f), $"Ship left the screen at the bottom (pushed toward {far}).");
            Assert.That(max.y, Is.LessThanOrEqualTo(0.5f), $"Ship went above the lower half (pushed toward {far}).");
        }
    }

    [UnityTest]
    public IEnumerator SpaceBarFires()
    {
        testKeyboard = InputSystem.AddDevice<Keyboard>("Test Keyboard");
        yield return WaitForNoShots();

        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState(Key.Space));
        yield return null;
        yield return null;
        InputSystem.QueueStateEvent(testKeyboard, new KeyboardState());

        Assert.That(shooter.ActiveShots, Is.GreaterThan(0), "Pressing Space did not fire a shot.");
    }

    [UnityTest]
    public IEnumerator NoMoreThanTwoShotsOnScreen()
    {
        yield return WaitForNoShots();

        Assert.IsTrue(shooter.TryFire(), "First shot did not fire.");
        yield return new WaitForSeconds(0.2f); // past the cooldown
        Assert.IsTrue(shooter.TryFire(), "Second shot did not fire.");
        yield return new WaitForSeconds(0.2f);

        Assert.IsFalse(shooter.TryFire(), "A third shot fired while two were still on screen.");
        Assert.That(shooter.ActiveShots, Is.EqualTo(2));
    }

    [UnityTest]
    public IEnumerator ShotsLeaveTheScreenAndAreReused()
    {
        yield return WaitForNoShots();
        Assert.IsTrue(shooter.TryFire());

        yield return WaitForNoShots();
        Assert.That(shooter.ActiveShots, Is.EqualTo(0), "Shot never left the screen.");
        Assert.IsTrue(shooter.TryFire(), "Could not fire again after the first shot left the screen.");
    }

    [UnityTest]
    public IEnumerator ShotDamagesWhatItHits()
    {
        yield return WaitForNoShots();

        var target = new GameObject("Test Target");
        target.transform.position = player.transform.position + Vector3.up * 4f;
        target.AddComponent<BoxCollider2D>().size = Vector2.one;
        var damageable = target.AddComponent<TestTarget>();
        yield return new WaitForFixedUpdate();

        Assert.IsTrue(shooter.TryFire());

        float end = Time.time + 2f;
        while (damageable.Hits == 0 && Time.time < end) yield return null;

        Assert.That(damageable.Hits, Is.EqualTo(1), "The shot did not damage the target.");
        Assert.That(shooter.ActiveShots, Is.EqualTo(0), "The shot was not removed after hitting.");
        Object.Destroy(target);
    }

    [UnityTest]
    public IEnumerator FireButtonIsOnScreenAndLargeEnough()
    {
        var button = Object.FindAnyObjectByType<FireButton>(FindObjectsInactive.Include);
        Assert.IsNotNull(button, $"{GameplayScene} has no FireButton.");

        var canvas = button.GetComponentInParent<Canvas>(true);
        Assert.IsNotNull(canvas, "FireButton is not inside a Canvas.");
        canvas.gameObject.SetActive(true);
        button.gameObject.SetActive(true);
        Canvas.ForceUpdateCanvases();
        yield return null;

        var corners = new Vector3[4];
        ((RectTransform)button.transform).GetWorldCorners(corners);
        Vector2 sizePx = corners[2] - corners[0];

        Assert.That(corners[0].x, Is.GreaterThanOrEqualTo(0f), "Fire button is off the left of the screen.");
        Assert.That(corners[0].y, Is.GreaterThanOrEqualTo(0f), "Fire button is off the bottom of the screen.");
        Assert.That(corners[2].x, Is.LessThanOrEqualTo(Screen.width), "Fire button is off the right of the screen.");
        Assert.That(corners[2].y, Is.LessThanOrEqualTo(Screen.height), "Fire button is off the top of the screen.");

        // FR7: touch targets at least 44 x 44 points. One point is about 1/160 inch.
        float pixelsPerPoint = Screen.dpi > 0f ? Mathf.Max(1f, Screen.dpi / 160f) : 1f;
        float minPx = 44f * pixelsPerPoint;
        Assert.That(Mathf.Min(sizePx.x, sizePx.y), Is.GreaterThanOrEqualTo(minPx),
            $"Fire button is {sizePx.x:F0}x{sizePx.y:F0}px; needs at least {minPx:F0}px (44pt).");
    }

    private IEnumerator WaitForNoShots(float timeout = 3f)
    {
        float end = Time.time + timeout;
        while (shooter.ActiveShots > 0 && Time.time < end) yield return null;
        yield return new WaitForSeconds(0.2f); // clear the fire cooldown
    }

    private class TestTarget : MonoBehaviour, IDamageable
    {
        public int Hits { get; private set; }
        public void TakeDamage(int amount) => Hits++;
    }
}
