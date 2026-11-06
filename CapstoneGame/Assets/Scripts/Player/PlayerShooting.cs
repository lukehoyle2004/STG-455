using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Fires blasts straight up from the player's ship.
///
///  - At most <see cref="maxShotsOnScreen"/> shots exist at once (2 by default), so the player has to aim
///    rather than spray, as described in the design document.
///  - Holding the fire input keeps firing as fast as the limit and cooldown allow.
///  - Shots come from a small pool that is created once, so firing never allocates memory.
///
/// Input: Space / Enter / Z / J on keyboard, A/X or right trigger on gamepad, or an on-screen
/// <see cref="FireButton"/> for touch, which calls <see cref="SetFireHeld"/>.
/// </summary>
[DisallowMultipleComponent]
public class PlayerShooting : MonoBehaviour
{
    [Header("Projectile")]
    [Tooltip("Optional prefab with a Projectile component. If empty, shots are built at runtime from the sprite below.")]
    [SerializeField] private Projectile projectilePrefab;
    [Tooltip("Sprite used when no prefab is assigned (Assets/Art/Sprites/PlayerBlast).")]
    [SerializeField] private Sprite blastSprite;
    [Tooltip("Where shots appear, relative to the ship's nose (top-center of its sprite), in world units.")]
    [SerializeField] private Vector2 muzzleOffset = new Vector2(0f, 0.1f);

    [Header("Firing Rules")]
    [Tooltip("Maximum player shots on screen at once.")]
    [SerializeField, Range(1, 10)] private int maxShotsOnScreen = 2;
    [Tooltip("Minimum seconds between shots, even when a slot is free.")]
    [SerializeField, Min(0f)] private float cooldown = 0.15f;
    [Tooltip("Keep firing while the button is held.")]
    [SerializeField] private bool autoFire = true;

    [Header("Input")]
    [Tooltip("Optional fire action. If empty, a built-in keyboard + gamepad binding is used. " +
             "Avoid the template's Player/Attack: it fires on every mouse click and screen tap.")]
    [SerializeField] private InputActionReference fireAction;

    private readonly List<Projectile> pool = new List<Projectile>();
    private int activeShots;
    private float nextFireTime;
    private bool touchFireHeld;
    private bool firedThisPress;
    private InputAction fallbackFire;
    private Transform poolRoot;

    private InputAction Fire => fireAction != null ? fireAction.action : fallbackFire;

    /// <summary>Number of the player's shots currently on screen.</summary>
    public int ActiveShots => activeShots;

    /// <summary>Raised each time a shot is fired (hook up sound or a muzzle flash here).</summary>
    public event System.Action Fired;

    private void Awake()
    {
        if (fireAction == null)
        {
            fallbackFire = new InputAction("Fire", InputActionType.Button);
            fallbackFire.AddBinding("<Keyboard>/space");
            fallbackFire.AddBinding("<Keyboard>/enter");
            fallbackFire.AddBinding("<Keyboard>/z");
            fallbackFire.AddBinding("<Keyboard>/j");
            fallbackFire.AddBinding("<Gamepad>/buttonSouth");
            fallbackFire.AddBinding("<Gamepad>/buttonWest");
            fallbackFire.AddBinding("<Gamepad>/rightTrigger");
        }

        // Pool lives outside the ship so shots don't move sideways with it.
        // It copies the ship's scale so blast pixels are the same size as ship pixels.
        poolRoot = new GameObject($"{name} Shots").transform;
        poolRoot.localScale = transform.lossyScale;
        for (int i = 0; i < maxShotsOnScreen; i++) pool.Add(CreateShot(i));
    }

    private void OnEnable() => Fire?.Enable();

    private void OnDisable()
    {
        fallbackFire?.Disable();
        touchFireHeld = false;
    }

    private void OnDestroy()
    {
        fallbackFire?.Dispose();
        if (poolRoot != null) Destroy(poolRoot.gameObject);
    }

    /// <summary>Called by the on-screen fire button (or tests) to press/release fire.</summary>
    public void SetFireHeld(bool held) => touchFireHeld = held;

    private void Update()
    {
        bool held = touchFireHeld || (Fire != null && Fire.IsPressed());

        if (!held)
        {
            firedThisPress = false;
            return;
        }

        if (!autoFire && firedThisPress) return;
        if (TryFire()) firedThisPress = true;
    }

    /// <summary>Fires one shot if the cooldown has passed and a slot is free. Returns true if it fired.</summary>
    public bool TryFire()
    {
        if (Time.time < nextFireTime || activeShots >= maxShotsOnScreen) return false;

        Projectile shot = GetInactiveShot();
        if (shot == null) return false;

        activeShots++;
        nextFireTime = Time.time + cooldown;
        shot.Launch(MuzzlePosition(), Vector2.up, transform);
        Fired?.Invoke();
        return true;
    }

    private Vector2 MuzzlePosition()
    {
        var sr = GetComponentInChildren<SpriteRenderer>();
        float noseY = sr != null ? sr.bounds.max.y : transform.position.y;
        return new Vector2(transform.position.x, noseY) + muzzleOffset;
    }

    private Projectile GetInactiveShot()
    {
        foreach (Projectile p in pool)
            if (p != null && !p.gameObject.activeSelf) return p;
        return null;
    }

    private void OnShotDespawned(Projectile p) => activeShots = Mathf.Max(0, activeShots - 1);

    private Projectile CreateShot(int index)
    {
        Projectile shot;
        if (projectilePrefab != null)
        {
            shot = Instantiate(projectilePrefab, poolRoot);
        }
        else
        {
            var go = new GameObject("PlayerBlast");
            go.transform.SetParent(poolRoot, false);
            go.SetActive(false); // add components while inactive so nothing runs early

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = blastSprite;
            sr.sortingOrder = 5;
            if (blastSprite == null) Debug.LogWarning($"{name}: no Blast Sprite assigned on PlayerShooting.", this);

            var body = go.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;

            var col = go.AddComponent<BoxCollider2D>();
            if (blastSprite != null) col.size = blastSprite.bounds.size;

            shot = go.AddComponent<Projectile>();
        }

        shot.name = $"PlayerBlast {index}";
        shot.gameObject.SetActive(false);
        shot.Despawned += OnShotDespawned;
        return shot;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(MuzzlePosition(), 0.08f);
    }
#endif
}
