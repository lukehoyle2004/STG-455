using System;
using UnityEngine;

/// <summary>
/// A pooled shot that flies in a straight line and switches itself off when it leaves the screen
/// or hits something. It never calls Destroy: the owner (e.g. <see cref="PlayerShooting"/>) reuses it,
/// which avoids garbage-collection pauses on older phones.
///
/// Uses a gravity-free Rigidbody2D + trigger collider so enemies can detect it with OnTriggerEnter2D.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class Projectile : MonoBehaviour
{
    [Tooltip("Speed in world units per second.")]
    [SerializeField, Min(0f)] private float speed = 18f;

    [Tooltip("Damage dealt to whatever it hits.")]
    [SerializeField, Min(1)] private int damage = 1;

    [Tooltip("How far past the screen edge the shot travels before it is recycled.")]
    [SerializeField, Min(0f)] private float offscreenMargin = 0.5f;

    private Rigidbody2D body;
    private Camera cam;
    private Vector2 direction = Vector2.up;
    private Transform owner;

    /// <summary>Raised when the shot is switched off (hit or off-screen) so the owner can count active shots.</summary>
    public event Action<Projectile> Despawned;

    public int Damage => damage;

    private void Awake() => Init();

    // Also called from Launch, because a pooled shot may be launched before Awake has ever run.
    private void Init()
    {
        if (body != null) return;
        body = GetComponent<Rigidbody2D>();
        // Dynamic (not Kinematic) so triggers fire against static and kinematic enemies too.
        // Gravity off + trigger collider means nothing actually pushes it around.
        body.bodyType = RigidbodyType2D.Dynamic;
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous; // fast shots shouldn't skip thin enemies
        GetComponent<Collider2D>().isTrigger = true;
    }

    /// <summary>Places and launches the shot. Shots never hurt their <paramref name="owner"/>.</summary>
    public void Launch(Vector2 position, Vector2 dir, Transform owner = null)
    {
        Init();
        this.owner = owner;
        cam = Camera.main;
        direction = dir.sqrMagnitude > 0f ? dir.normalized : Vector2.up;
        transform.SetPositionAndRotation(position, Quaternion.FromToRotation(Vector3.up, direction));
        body.position = position;
        gameObject.SetActive(true);
        body.linearVelocity = direction * speed;
    }

    /// <summary>Switches the shot off and returns it to its pool. Safe to call more than once.</summary>
    public void Despawn()
    {
        if (!gameObject.activeSelf) return;
        body.linearVelocity = Vector2.zero;
        gameObject.SetActive(false);
        Despawned?.Invoke(this);
    }

    private void FixedUpdate()
    {
        if (cam == null) return;

        float halfH = cam.orthographicSize + offscreenMargin;
        float halfW = cam.orthographicSize * cam.aspect + offscreenMargin;
        Vector2 offset = body.position - (Vector2)cam.transform.position;

        if (Mathf.Abs(offset.x) > halfW || Mathf.Abs(offset.y) > halfH)
            Despawn();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Never hit whoever fired the shot.
        if (owner != null && other.transform.IsChildOf(owner)) return;

        // Enemies will implement IDamageable; anything else (e.g. other shots) is ignored.
        if (other.TryGetComponent(out IDamageable target))
        {
            target.TakeDamage(damage);
            Despawn();
        }
    }
}

/// <summary>Anything a projectile can hurt (enemies, the player).</summary>
public interface IDamageable
{
    void TakeDamage(int amount);
}
