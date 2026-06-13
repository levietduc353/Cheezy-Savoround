using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gắn vào bất kỳ Button nào.
/// Wire Button.OnClick → OnLoadSceneClicked() trong Inspector.
///
/// Cho phép chỉ định scene đích theo:
///   • Tên scene  (_loadByName = true,  điền _sceneName)
///   • Build index (_loadByName = false, điền _sceneBuildIndex)
///
/// Lưu ý: scene đích phải được thêm vào File → Build Settings.
/// </summary>
public class SceneLoaderButton : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Target Scene")]
    [Tooltip("Nếu true  → dùng _sceneName để load.\n" +
             "Nếu false → dùng _sceneBuildIndex để load.")]
    [SerializeField] private bool _loadByName = true;

    [Tooltip("Tên scene cần load (phải khớp với tên file .unity trong Build Settings).")]
    [SerializeField] private string _sceneName = "";

    [Tooltip("Build index của scene cần load (xem trong File → Build Settings).")]
    [SerializeField] private int _sceneBuildIndex = 0;

    [Header("Sound")]
    [Tooltip("Sound to play when button is clicked.")]
    [SerializeField] private AudioClip _clickSound;
    [Tooltip("AudioSource to play the sound. If null, PlayClipAtPoint will be used.")]
    [SerializeField] private AudioSource _audioSource;

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Gọi hàm này từ Button.OnClick trong Inspector.
    /// Chuyển sang scene được chỉ định (Time.timeScale được reset về 1 trước khi load).
    /// </summary>
    public void OnLoadSceneClicked()
    {
        StartCoroutine(LoadSceneWithDelayCoroutine());
    }

    private System.Collections.IEnumerator LoadSceneWithDelayCoroutine()
    {
        float delay = 0f;
        if (_clickSound != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(_clickSound);
            delay = _clickSound.length;
        }

        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        // Đảm bảo game không bị pause khi chuyển scene.
        Time.timeScale = 1f;

        if (_loadByName)
        {
            if (string.IsNullOrWhiteSpace(_sceneName))
            {
                Debug.LogError("[SceneLoaderButton] Scene name is empty. " +
                               "Assign a valid scene name in the Inspector.");
                yield break;
            }

            Debug.Log($"[SceneLoaderButton] Loading scene by name: '{_sceneName}'.");
            SceneManager.LoadScene(_sceneName);
        }
        else
        {
            if (_sceneBuildIndex < 0 || _sceneBuildIndex >= SceneManager.sceneCountInBuildSettings)
            {
                Debug.LogError($"[SceneLoaderButton] Build index {_sceneBuildIndex} is out of range. " +
                               "Check File → Build Settings.");
                yield break;
            }

            Debug.Log($"[SceneLoaderButton] Loading scene by build index: {_sceneBuildIndex}.");
            SceneManager.LoadScene(_sceneBuildIndex);
        }
    }
}
