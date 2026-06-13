using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gắn vào bất kỳ Button nào.
/// Wire Button.OnClick → OnRestartClicked() trong Inspector.
///
/// Khi nhấn: reload lại scene đang chạy từ đầu (Time.timeScale cũng được reset về 1).
/// </summary>
public class RestartButton : MonoBehaviour
{
    [Header("Sound")]
    [Tooltip("Sound to play when button is clicked.")]
    [SerializeField] private AudioClip _clickSound;
    [Tooltip("AudioSource to play the sound. If null, PlayClipAtPoint will be used.")]
    [SerializeField] private AudioSource _audioSource;
    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Gọi hàm này từ Button.OnClick trong Inspector.
    /// Reload lại scene hiện tại từ đầu.
    /// </summary>
    public void OnRestartClicked()
    {
        StartCoroutine(RestartWithDelayCoroutine());
    }

    private System.Collections.IEnumerator RestartWithDelayCoroutine()
    {
        float delay = 0f;
        if (_clickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(_clickSound);
            delay = _clickSound.length;
        }

        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        // Đảm bảo game không bị pause khi restart.
        Time.timeScale = 1f;

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);

        Debug.Log($"[RestartButton] Restarting scene: '{currentScene.name}' (index {currentScene.buildIndex}).");
    }
}
