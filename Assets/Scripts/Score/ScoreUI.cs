using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the ScoreManager's fill value to a UI Image set to Image Type = Filled.
///
/// Setup:
///   1. Add this component to any GameObject that has (or is a child of) a Canvas.
///   2. Assign the target Image (must have Image Type = Filled) in the Inspector.
///   3. Ensure ScoreManager exists in the scene.
/// </summary>
public class ScoreUI : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Image component with Image Type = Filled that represents the score bar.")]
    [SerializeField] private Image _fillImage;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        if (_fillImage == null)
        {
            Debug.LogError("[ScoreUI] Fill Image reference is not assigned in the Inspector.");
            return;
        }

        // Initialise fill to 0.
        _fillImage.fillAmount = 0f;

        // Subscribe after ScoreManager.Awake() has run.
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnFillChanged += UpdateFill;
        else
            Debug.LogWarning("[ScoreUI] ScoreManager.Instance not found. " +
                             "Make sure ScoreManager is in the scene and initialises before ScoreUI.");
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnFillChanged -= UpdateFill;
    }

    // ─── Private handlers ─────────────────────────────────────────────────────

    /// <summary>Sets fillAmount on the target Image (value is already clamped to [0,1]).</summary>
    private void UpdateFill(float fillAmount)
    {
        if (_fillImage != null)
            _fillImage.fillAmount = fillAmount;
    }
}
