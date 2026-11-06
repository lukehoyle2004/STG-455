using UnityEngine;

/// <summary>
/// Keeps the play field the same width on every screen.
///
/// A plain orthographic camera keeps a fixed HEIGHT, so on a narrow phone the play field gets very thin
/// and the ship looks huge, while on a tablet it gets wide. This sets the camera size so that
/// <see cref="visibleWidth"/> world units always fit across the screen, which keeps the ship, enemies,
/// and formation the same size relative to the screen width on iOS, Android, and the web.
///
/// On wide screens (tablets) it also guarantees at least <see cref="minVisibleHeight"/> units of height,
/// so there's still room for the formation and dives.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class CameraFitter : MonoBehaviour
{
    [Tooltip("World units visible across the screen. The ship is 1 unit wide.")]
    [SerializeField, Min(1f)] private float visibleWidth = 9f;

    [Tooltip("Minimum world units visible top to bottom (matters on wide screens like tablets).")]
    [SerializeField, Min(1f)] private float minVisibleHeight = 20f;

    private Camera cam;
    private int lastWidth, lastHeight;

    public float VisibleWidth => visibleWidth;

    private void OnEnable()
    {
        cam = GetComponent<Camera>();
        Apply();
    }

    private void OnValidate()
    {
        cam = GetComponent<Camera>();
        Apply();
    }

    private void Update()
    {
        // Screen size changes on rotation, window resize (web), or switching Simulator devices.
        if (Screen.width != lastWidth || Screen.height != lastHeight) Apply();
    }

    private void Apply()
    {
        if (cam == null || !cam.orthographic) return;

        lastWidth = Screen.width;
        lastHeight = Screen.height;

        float aspect = cam.aspect > 0f ? cam.aspect : (float)Screen.width / Mathf.Max(1, Screen.height);
        float sizeForWidth = visibleWidth * 0.5f / aspect;
        cam.orthographicSize = Mathf.Max(sizeForWidth, minVisibleHeight * 0.5f);
    }
}
