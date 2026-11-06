using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

/// <summary>
/// Moves the player's ship around the lower part of the screen.
///
/// Input (all through the Unity Input System so one codebase serves web, iOS, and Android):
///  - Keyboard / gamepad: the "Player/Move" action (arrows, WASD, left stick, d-pad).
///  - Touch (and mouse drag in the Editor/web): dragging anywhere inside the bottom control zone moves the
///    ship by the same distance the finger travels, so the player's thumb never has to cover the ship.
///
/// The ship is kept inside a movement area: the full screen width, and from the bottom of the screen up to
/// <see cref="maxHeight"/> of the screen height, so it can't fly up into the enemy formation.
/// </summary>
[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("The Player/Move action from Assets/Settings/InputSystem_Actions. If left empty, a built-in keyboard + gamepad binding is used.")]
    [SerializeField] private InputActionReference moveAction;

    [Header("Keyboard / Gamepad")]
    [Tooltip("Top speed in world units per second.")]
    [SerializeField, Min(0f)] private float moveSpeed = 8f;
    [Tooltip("How quickly the ship reaches top speed (units/sec^2). Higher = snappier, more arcade-like.")]
    [SerializeField, Min(0f)] private float acceleration = 60f;
    [Tooltip("Ignore stick input smaller than this so a resting thumbstick doesn't drift the ship.")]
    [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.15f;

    [Header("Touch")]
    [Tooltip("Fraction of the screen height (from the bottom) where a drag can start.")]
    [SerializeField, Range(0.1f, 1f)] private float touchZoneHeight = 0.2f;
    [Tooltip("Multiplier on finger movement. 1 = ship moves exactly as far as the finger.")]
    [SerializeField, Min(0.1f)] private float touchSensitivity = 1.2f;

    [Header("Movement Area")]
    [Tooltip("Allow up/down movement. Turn off for classic left/right-only movement.")]
    [SerializeField] private bool allowVertical = true;
    [Tooltip("Lowest the ship can go, as a fraction of the screen height from the bottom. Keeping this at or " +
             "above the touch zone means the player's thumb never covers the ship.")]
    [SerializeField, Range(0f, 0.9f)] private float minHeight = 0.2f;
    [Tooltip("Highest the ship can go, as a fraction of the screen height measured from the bottom.")]
    [SerializeField, Range(0.1f, 1f)] private float maxHeight = 0.45f;
    [Tooltip("Extra space kept between the ship and the screen edges, in world units.")]
    [SerializeField, Min(0f)] private float edgePadding = 0.1f;

    private Camera cam;
    private Vector2 halfSize;         // half the ship's size, so the whole sprite stays inside the area
    private Vector2 currentVelocity;  // velocity for keyboard/gamepad smoothing
    private InputAction fallbackMove; // used only if no action reference is assigned

    // Touch drag state
    private bool isDragging;
    private Vector2 lastPointerWorld;

    /// <summary>Current velocity, useful for tilting/banking the sprite later.</summary>
    public Vector2 Velocity => currentVelocity;

    private InputAction Move => moveAction != null ? moveAction.action : fallbackMove;

    private void Awake()
    {
        cam = Camera.main;

        var sprite = GetComponentInChildren<SpriteRenderer>();
        halfSize = sprite != null ? (Vector2)sprite.bounds.extents : new Vector2(0.5f, 0.5f);

        if (moveAction == null)
        {
            fallbackMove = new InputAction("Move", InputActionType.Value);
            fallbackMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            fallbackMove.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            fallbackMove.AddBinding("<Gamepad>/leftStick");
            fallbackMove.AddBinding("<Gamepad>/dpad");
        }
    }

    private void OnEnable() => Move?.Enable();

    private void OnDisable()
    {
        // Don't disable a shared asset action other scripts may rely on; only our private fallback.
        fallbackMove?.Disable();
        isDragging = false;
        currentVelocity = Vector2.zero;
    }

    private void OnDestroy() => fallbackMove?.Dispose();

    private void Update()
    {
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        float dt = Time.deltaTime;
        Vector2 pos = transform.position;

        if (TryGetTouchDelta(out Vector2 dragDelta))
        {
            // Direct drag: move with the finger, no acceleration (feels 1:1).
            if (!allowVertical) dragDelta.y = 0f;
            pos += dragDelta * touchSensitivity;
            currentVelocity = dt > 0f ? dragDelta / dt : Vector2.zero;
        }
        else
        {
            Vector2 target = ReadMoveInput() * moveSpeed;
            currentVelocity = Vector2.MoveTowards(currentVelocity, target, acceleration * dt);
            pos += currentVelocity * dt;
        }

        // Keep the whole ship inside the movement area.
        GetBounds(out Vector2 min, out Vector2 max);
        Vector2 clamped = new Vector2(Mathf.Clamp(pos.x, min.x, max.x), Mathf.Clamp(pos.y, min.y, max.y));

        // Stop instantly at a wall instead of "sticking" to it.
        if (clamped.x != pos.x) currentVelocity.x = 0f;
        if (clamped.y != pos.y) currentVelocity.y = 0f;

        transform.position = new Vector3(clamped.x, clamped.y, transform.position.z);
    }

    /// <summary>World-space rectangle the ship's center must stay inside.</summary>
    private void GetBounds(out Vector2 min, out Vector2 max)
    {
        Vector2 center = cam.transform.position;
        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;

        float screenBottom = center.y - halfH;
        float bottom = screenBottom + 2f * halfH * minHeight;
        float top = screenBottom + 2f * halfH * Mathf.Max(maxHeight, minHeight);

        min = new Vector2(center.x - halfW + halfSize.x + edgePadding, bottom + halfSize.y + edgePadding);
        max = new Vector2(center.x + halfW - halfSize.x - edgePadding, top - halfSize.y);

        // If the area is smaller than the ship, pin to the middle rather than flip.
        if (max.x < min.x) min.x = max.x = center.x;
        if (max.y < min.y) max.y = min.y;

        if (!allowVertical)
        {
            // Lock to the current height (whatever was placed in the scene).
            min.y = max.y = transform.position.y;
        }
    }

    private Vector2 ReadMoveInput()
    {
        if (Move == null) return Vector2.zero;

        Vector2 input = Move.ReadValue<Vector2>();
        if (!allowVertical) input.y = 0f;
        if (input.magnitude < deadZone) return Vector2.zero;

        // Clamp so diagonals aren't faster than straight movement.
        return Vector2.ClampMagnitude(input, 1f);
    }

    /// <summary>
    /// Returns true while a touch/mouse drag that started in the control zone is active,
    /// with the distance (world units) the pointer moved this frame.
    ///
    /// Supports multi-touch: one finger can hold the fire button while another finger drags.
    /// A drag only starts on a fresh press inside the control zone that is not on a UI element.
    /// </summary>
    private bool TryGetTouchDelta(out Vector2 delta)
    {
        delta = Vector2.zero;

        // 1) Continue the drag we're already tracking.
        if (isDragging)
        {
            if (TryGetTrackedPointer(out Vector2 pos))
                return StepDrag(pos, out delta);

            isDragging = false; // finger lifted (or mouse released)
        }

        // 2) Look for a new press that can start a drag.
        var touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            foreach (TouchControl touch in touchscreen.touches)
            {
                if (!touch.press.wasPressedThisFrame) continue;
                Vector2 pos = touch.position.ReadValue();
                if (!CanStartDrag(pos)) continue;
                return BeginDrag(touch.touchId.ReadValue(), pos);
            }
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 pos = mouse.position.ReadValue();
            if (CanStartDrag(pos)) return BeginDrag(MouseId, pos);
        }

        return false;
    }

    private const int MouseId = -1;
    private int dragPointerId;

    private bool BeginDrag(int pointerId, Vector2 screenPos)
    {
        isDragging = true;
        dragPointerId = pointerId;
        lastPointerWorld = ScreenToWorld(screenPos);
        return true; // first frame: no movement yet
    }

    private bool StepDrag(Vector2 screenPos, out Vector2 delta)
    {
        Vector2 world = ScreenToWorld(screenPos);
        delta = world - lastPointerWorld;
        lastPointerWorld = world;
        return true;
    }

    private bool TryGetTrackedPointer(out Vector2 screenPos)
    {
        screenPos = default;

        if (dragPointerId == MouseId)
        {
            var mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.isPressed) return false;
            screenPos = mouse.position.ReadValue();
            return true;
        }

        var touchscreen = Touchscreen.current;
        if (touchscreen == null) return false;

        foreach (TouchControl touch in touchscreen.touches)
        {
            if (touch.touchId.ReadValue() != dragPointerId || !touch.press.isPressed) continue;
            screenPos = touch.position.ReadValue();
            return true;
        }
        return false;
    }

    private bool CanStartDrag(Vector2 screenPos)
    {
        // Only the bottom control zone, and never on top of a UI element (fire button, pause button...).
        return screenPos.y <= Screen.height * touchZoneHeight && !IsOverUI(screenPos);
    }

    private Vector2 ScreenToWorld(Vector2 screenPos) =>
        cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));

    private static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    private static bool IsOverUI(Vector2 screenPos)
    {
        EventSystem es = EventSystem.current;
        if (es == null) return false;

        var data = new PointerEventData(es) { position = screenPos };
        uiHits.Clear();
        es.RaycastAll(data, uiHits);
        return uiHits.Count > 0;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        // Show the movement area in the Scene view.
        Camera c = Camera.main;
        if (c == null) return;

        Camera saved = cam;
        cam = c;
        if (!Application.isPlaying)
        {
            var sprite = GetComponentInChildren<SpriteRenderer>();
            halfSize = sprite != null ? (Vector2)sprite.bounds.extents : new Vector2(0.5f, 0.5f);
        }
        GetBounds(out Vector2 min, out Vector2 max);
        cam = saved;

        // Expand by the ship's half-size so the box shows where the sprite's edges can reach.
        Vector2 size = (max - min) + 2f * halfSize;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube((min + max) * 0.5f, new Vector3(size.x, size.y, 0f));
    }
#endif
}
