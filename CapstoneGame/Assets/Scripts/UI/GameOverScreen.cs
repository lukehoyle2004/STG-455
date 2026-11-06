using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The Game Over screen. Shows the final score, wave, and best score, with Play Again and Main Menu.
///
/// The buttons stay locked for a moment after the screen opens, so a player who was still holding
/// fire (Space, A, or a finger on the fire button) when the game ended doesn't skip straight past it.
/// Play Again is then selected, so Enter, Space, or A starts a new game.
/// </summary>
public class GameOverScreen : MonoBehaviour
{
    [SerializeField] private Button playAgainButton;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Text scoreText;
    [SerializeField] private Text waveText;
    [SerializeField] private Text highScoreText;

    [Tooltip("Seconds before the buttons respond.")]
    [SerializeField, Min(0f)] private float inputDelay = 0.6f;

    private void Awake()
    {
        if (playAgainButton != null) playAgainButton.onClick.AddListener(GameFlow.StartGame);
        if (mainMenuButton != null) mainMenuButton.onClick.AddListener(GameFlow.ToStartMenu);
    }

    private IEnumerator Start()
    {
        if (scoreText != null) scoreText.text = $"SCORE  {GameFlow.LastScore}";
        if (waveText != null) waveText.text = $"WAVE  {Mathf.Max(1, GameFlow.LastWave)}";
        if (highScoreText != null)
            highScoreText.text = GameFlow.LastWasNewHighScore ? "NEW HIGH SCORE!" : $"HIGH SCORE  {GameFlow.HighScore}";

        SetButtonsInteractable(false);
        yield return new WaitForSecondsRealtime(inputDelay);
        SetButtonsInteractable(true);

        if (playAgainButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(playAgainButton.gameObject);
    }

    /// <summary>True once the buttons respond to input.</summary>
    public bool IsReady => playAgainButton == null || playAgainButton.interactable;

    private void SetButtonsInteractable(bool on)
    {
        if (playAgainButton != null) playAgainButton.interactable = on;
        if (mainMenuButton != null) mainMenuButton.interactable = on;
    }
}
