using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the ScoreManager's data to the UI:
///   • _fillImage    — Image Type = Filled, driven by ScoreManager.OnFillChanged.
///   • _currentLevelText — TMP showing the current level (e.g. "1").
///   • _nextLevelText    — TMP showing the next level    (e.g. "2").
///
/// Setup:
///   1. Add this component to any GameObject that has (or is a child of) a Canvas.
///   2. Assign the three references in the Inspector.
///   3. Ensure ScoreManager exists in the scene.
/// </summary>
public class ScoreUI : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Fill Bar")]
    [Tooltip("Image component with Image Type = Filled that represents the score bar.")]
    [SerializeField] private Image _fillImage;

    [Header("Level Labels")]
    [Tooltip("TMP label that displays the CURRENT level number.")]
    [SerializeField] private TMP_Text _currentLevelText;

    [Tooltip("TMP label that displays the NEXT level number.")]
    [SerializeField] private TMP_Text _nextLevelText;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        if (_fillImage == null)
            Debug.LogError("[ScoreUI] Fill Image reference is not assigned in the Inspector.");

        // Initialise fill to 0.
        if (_fillImage != null)
            _fillImage.fillAmount = 0f;

        ScoreManager sm = ScoreManager.Instance;

        if (sm == null)
        {
            Debug.LogWarning("[ScoreUI] ScoreManager.Instance not found. " +
                             "Make sure ScoreManager is in the scene and initialises before ScoreUI.");
            return;
        }

        // Subscribe to fill bar updates.
        sm.OnFillChanged  += UpdateFill;

        // Subscribe to level-up updates.
        sm.OnLevelChanged += UpdateLevelLabels;

        // Set initial label values from the manager's current state.
        UpdateLevelLabels(sm.CurrentLevel);
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance == null) return;

        ScoreManager.Instance.OnFillChanged  -= UpdateFill;
        ScoreManager.Instance.OnLevelChanged -= UpdateLevelLabels;
    }

    // ─── Private handlers ─────────────────────────────────────────────────────

    /// <summary>Sets fillAmount on the target Image (value is already clamped to [0,1]).</summary>
    private void UpdateFill(float fillAmount)
    {
        if (_fillImage != null)
            _fillImage.fillAmount = fillAmount;
    }

    /// <summary>
    /// Updates both TMP labels whenever the level changes.
    /// Called once on Start (with the initial level) and again each time the fill bar completes.
    /// </summary>
    /// <param name="newLevel">The new current level (1-based).</param>
    private void UpdateLevelLabels(int newLevel)
    {
        if (_currentLevelText != null)
            _currentLevelText.text = newLevel.ToString();

        if (_nextLevelText != null)
            _nextLevelText.text = (newLevel + 1).ToString();
    }
}
