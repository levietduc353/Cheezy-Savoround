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
    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Gọi hàm này từ Button.OnClick trong Inspector.
    /// Reload lại scene hiện tại từ đầu.
    /// </summary>
    public void OnRestartClicked()
    {
        // Đảm bảo game không bị pause khi restart.
        Time.timeScale = 1f;

        Scene currentScene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(currentScene.buildIndex);

        Debug.Log($"[RestartButton] Restarting scene: '{currentScene.name}' (index {currentScene.buildIndex}).");
    }
}
