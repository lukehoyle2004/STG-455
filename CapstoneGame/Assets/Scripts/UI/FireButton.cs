using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// On-screen fire button for touch devices. Put it on a UI Image inside a Canvas and drag the
/// player's <see cref="PlayerShooting"/> into the Shooter slot.
///
/// Holding the button keeps firing. It works with multi-touch, so another finger can drag to move
/// at the same time. Because it is a UI element, drags that start on it never move the ship.
/// </summary>
public class FireButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private PlayerShooting shooter;

    [Tooltip("Hide the button on devices without a touchscreen (keyboard/gamepad players use Space/A).")]
    [SerializeField] private bool hideWithoutTouchscreen = true;

    private int pressCount;

    private void Start()
    {
        if (shooter == null) shooter = FindAnyObjectByType<PlayerShooting>();

        if (hideWithoutTouchscreen && UnityEngine.InputSystem.Touchscreen.current == null && !Application.isMobilePlatform)
            gameObject.SetActive(false);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressCount++;
        if (shooter != null) shooter.SetFireHeld(true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressCount = Mathf.Max(0, pressCount - 1);
        if (pressCount == 0 && shooter != null) shooter.SetFireHeld(false);
    }

    private void OnDisable()
    {
        pressCount = 0;
        if (shooter != null) shooter.SetFireHeld(false);
    }
}
