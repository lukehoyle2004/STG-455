using UnityEngine;

/// <summary>
/// Shrinks a full-screen UI panel to the device's safe area, so buttons and HUD text are never hidden
/// under a notch, camera cutout, rounded corner, or the iPhone home indicator.
/// Put it on a RectTransform that is a direct child of the Canvas and place HUD elements inside it.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RectTransform))]
public class SafeArea : MonoBehaviour
{
    private RectTransform rect;
    private Rect lastSafeArea;
    private Vector2Int lastScreen;

    private void OnEnable()
    {
        rect = GetComponent<RectTransform>();
        Apply();
    }

    private void Update()
    {
        if (Screen.safeArea != lastSafeArea || lastScreen.x != Screen.width || lastScreen.y != Screen.height)
            Apply();
    }

    private void Apply()
    {
        Rect safe = Screen.safeArea;
        lastSafeArea = safe;
        lastScreen = new Vector2Int(Screen.width, Screen.height);
        if (Screen.width <= 0 || Screen.height <= 0) return;

        // Keep the panel on screen even if the reported safe area isn't (the Editor can report the
        // Device Simulator's safe area while the Game view renders at a different size).
        Vector2 min = safe.position;
        Vector2 max = safe.position + safe.size;
        min = new Vector2(Mathf.Clamp01(min.x / Screen.width), Mathf.Clamp01(min.y / Screen.height));
        max = new Vector2(Mathf.Clamp01(max.x / Screen.width), Mathf.Clamp01(max.y / Screen.height));
        if (max.x <= min.x || max.y <= min.y) { min = Vector2.zero; max = Vector2.one; }

        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}
