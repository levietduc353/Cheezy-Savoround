using UnityEngine;

/// <summary>
/// Gắn vào bất kỳ Button nào.
/// Wire Button.OnClick → OnOpenClicked() trong Inspector.
///
/// Khi nhấn: enable canvas được chỉ định qua _targetCanvas.
/// Nếu _targetCanvas không được gán, sẽ log warning và không làm gì.
/// </summary>
public class OpenButton : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Tooltip("Canvas sẽ được enable khi nhấn button này.")]
    [SerializeField] private Canvas _targetCanvas;

    [Header("Sound")]
    [Tooltip("Sound to play when button is clicked.")]
    [SerializeField] private AudioClip _clickSound;
    [Tooltip("AudioSource to play the sound. If null, PlayClipAtPoint will be used.")]
    [SerializeField] private AudioSource _audioSource;

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Gọi hàm này từ Button.OnClick trong Inspector.
    /// Enable _targetCanvas.
    /// </summary>
    public void OnOpenClicked()
    {
        PlayClickSound();

        if (_targetCanvas == null)
        {
            Debug.LogWarning("[OpenButton] _targetCanvas chưa được gán trong Inspector.");
            return;
        }

        _targetCanvas.gameObject.SetActive(true);

        Debug.Log($"[OpenButton] Đã enable canvas: '{_targetCanvas.name}'.");
    }

    private void PlayClickSound()
    {
        if (_clickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(_clickSound);
        }
    }
}
