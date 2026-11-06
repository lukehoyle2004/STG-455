using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The title screen. Play starts a game.
///
/// There is no Quit button: this is a mobile game, and players leave by closing the app.
/// Touch and mouse players tap Play. Keyboard and gamepad players start with Play already selected,
/// so Enter, Space, or the A button starts the game.
/// </summary>
public class StartMenu : MonoBehaviour
{
    [SerializeField] private Button playButton;

    [Tooltip("Optional. Shows the best score saved on this device.")]
    [SerializeField] private Text highScoreText;

    private void Awake()
    {
        if (playButton != null) playButton.onClick.AddListener(GameFlow.StartGame);
    }

    private void Start()
    {
        if (highScoreText != null)
        {
            int best = GameFlow.HighScore;
            highScoreText.text = best > 0 ? $"HIGH SCORE  {best}" : "";
        }

        if (playButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(playButton.gameObject);
    }
}
