using TMPro;
using UnityEngine;

/// <summary>
/// Phát hiện trạng thái Game Over (main grid đầy) và hiển thị Game Over Canvas.
///
/// Cách hoạt động:
///   1. Subscribe GridManager.OnPlatePlaced.
///   2. Sau mỗi lần plate được đặt, kiểm tra xem toàn bộ main grid đã đầy chưa.
///   3. Nếu đầy AND MergeAnimator không còn đang chạy animation:
///      • FSM → GameOverState  (chặn DragController + PowerUp 3D input)
///      • Cập nhật highest score trong PlayerDataManager
///      • Enable _gameOverCanvas (hiển thị panel game over)
///      • Ghi điểm vào _scoreText từ ScoreManager.TotalPlatesCompleted
///      • Ghi highest score vào _highestScoreText từ PlayerDataManager.HighestScore
///
/// Lưu ý: MergeAnimator có thể giải phóng ô trống (dismiss plates) sau khi đặt.
/// Vì vậy việc kiểm tra grid đầy được delay đến khi merge animation kết thúc.
///
/// Setup trong Inspector:
///   - Assign _mainGrid          : GridManager của main grid
///   - Assign _fsm               : GameStateMachine
///   - Assign _gameOverCanvas    : Canvas đang disabled (sẽ được enable khi game over)
///   - Assign _scoreText         : TMP_Text để hiển thị tổng số plate đã complete
///   - Assign _highestScoreText  : TMP_Text để hiển thị highest score từ PlayerDataManager
/// </summary>
public class GameOverManager : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("GridManager của main grid (id = 'main_grid').")]
    [SerializeField] private GridManager _mainGrid;

    [Tooltip("GameStateMachine trong scene.")]
    [SerializeField] private GameStateMachine _fsm;

    [Header("Game Over UI")]
    [Tooltip("Canvas Game Over — phải đang disabled theo default trong scene.")]
    [SerializeField] private Canvas _gameOverCanvas;

    [Tooltip("TMP_Text hiển thị tổng số plate đã hoàn thành (điểm cuối game).")]
    [SerializeField] private TMP_Text _scoreText;

    [Tooltip("TMP_Text hiển thị kỷ lục cao nhất (highest score) từ PlayerDataManager.")]
    [SerializeField] private TMP_Text _highestScoreText;

    [Header("Audio")]
    [Tooltip("AudioSource to play the game over sound.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Sound played when the game is over.")]
    [SerializeField] private AudioClip _gameOverSound;

    [Tooltip("AudioSource of another object to stop when game over happens (e.g., Background Music).")]
    [SerializeField] private AudioSource _audioToStopOnGameOver;

    [Header("Animation")]
    [Tooltip("Panel chính để tạo hiệu ứng trượt. Nếu null, sẽ cố tìm object con đầu tiên trong Canvas.")]
    [SerializeField] private RectTransform _gameOverPanel;

    [Tooltip("Độ cao (pixel) mà panel sẽ bắt đầu rơi xuống.")]
    [SerializeField] private float _dropOffset = 1000f;

    [Tooltip("Thời gian trượt xuống (giây).")]
    [SerializeField] private float _dropDuration = 0.5f;

    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>
    /// Fired khi game kết thúc (toàn bộ ô main grid đầy).
    /// AchievementManager subscribe để kiểm tra điều kiện achievement #3
    /// (hoàn thành 1 ván không dùng trợ giúp).
    /// Static để không cần reference trực tiếp tới GameOverManager instance.
    /// </summary>
    public static event System.Action OnGameOver;

    // ─── Private state ────────────────────────────────────────────────────────

    private bool _gameOverTriggered;
    private Vector2 _panelOriginalPos;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Đảm bảo Game Over Canvas đang tắt khi game khởi động.
        if (_gameOverCanvas != null)
            _gameOverCanvas.gameObject.SetActive(false);

        // Tự động tìm Panel nếu chưa gán
        if (_gameOverPanel == null && _gameOverCanvas != null && _gameOverCanvas.transform.childCount > 0)
        {
            _gameOverPanel = _gameOverCanvas.transform.GetChild(0).GetComponent<RectTransform>();
        }

        // Lưu lại vị trí gốc để làm đích đến cho animation
        if (_gameOverPanel != null)
        {
            _panelOriginalPos = _gameOverPanel.anchoredPosition;
        }
    }

    private void OnEnable()
    {
        if (_mainGrid != null)
            _mainGrid.OnPlatePlaced += HandlePlatePlaced;
    }

    private void OnDisable()
    {
        if (_mainGrid != null)
            _mainGrid.OnPlatePlaced -= HandlePlatePlaced;
    }

    // ─── Private logic ────────────────────────────────────────────────────────

    /// <summary>
    /// Được gọi bởi GridManager.OnPlatePlaced sau mỗi lần 1 plate được đặt.
    ///
    /// Chỉ khởi động coroutine kiểm tra khi grid trông có vẻ đầy (0 ô trống).
    /// Tránh kiểm tra sớm khi grid còn nhiều ô trống — giảm overhead.
    /// </summary>
    private void HandlePlatePlaced(int row, int col, PlateController plate)
    {
        if (_gameOverTriggered) return;

        // Chỉ quan tâm khi không còn ô trống nào trên main grid.
        if (CountEmptyCells() > 0) return;

        // Grid trông đầy — nhưng đây là cùng frame với OnPlatePlaced.
        // MergeChecker cũng subscribe OnPlatePlaced và sẽ gọi MergeAnimator
        // trong frame này, nhưng IsMergeAnimating chưa kịp = true.
        // → Dùng coroutine để delay đúng cách.
        StartCoroutine(CheckGameOverDelayed());
    }

    /// <summary>
    /// Coroutine kiểm tra game over an toàn, đảm bảo không bị race condition
    /// với MergeChecker và MergeAnimator:
    ///
    ///   Bước 1 — yield return null:
    ///     Chờ sang frame tiếp theo. Lúc này MergeChecker đã gọi
    ///     MergeAnimator.ExecuteMergeSequence() và IsMergeAnimating đã = true
    ///     (nếu có merge nào cần chạy).
    ///
    ///   Bước 2 — WaitUntil(!IsMergeAnimating):
    ///     Chờ cho đến khi toàn bộ merge animation (bao gồm dismiss plate)
    ///     hoàn tất. Lúc này các ô có thể đã được giải phóng.
    ///
    ///   Bước 3 — CheckAndTriggerGameOver():
    ///     Kiểm tra lại grid. Chỉ trigger nếu vẫn còn đầy.
    /// </summary>
    private System.Collections.IEnumerator CheckGameOverDelayed()
    {
        // Bước 1: nhường 1 frame để MergeAnimator kịp set IsMergeAnimating = true.
        yield return null;

        // Bước 2: chờ toàn bộ animation merge + dismiss hoàn tất.
        yield return new WaitUntil(
            () => MergeAnimator.Instance == null || !MergeAnimator.Instance.IsMergeAnimating);

        // Bước 3: kiểm tra lại — sau merge có thể đã có ô trống, chưa game over.
        CheckAndTriggerGameOver();
    }

    /// <summary>Đếm số ô trống trên main grid.</summary>
    private int CountEmptyCells()
    {
        if (_mainGrid == null) return int.MaxValue;

        int empty = 0;
        for (int r = 0; r < _mainGrid.Rows; r++)
            for (int c = 0; c < _mainGrid.Columns; c++)
                if (_mainGrid.GetPlateAt(r, c) == null) empty++;

        return empty;
    }

    /// <summary>
    /// Kiểm tra toàn bộ ô của main grid.
    /// Nếu tất cả đều có plate → Game Over; nếu còn ô trống → tiếp tục chơi.
    /// </summary>
    private void CheckAndTriggerGameOver()
    {
        if (_gameOverTriggered) return;
        if (_mainGrid == null)  return;

        for (int r = 0; r < _mainGrid.Rows; r++)
            for (int c = 0; c < _mainGrid.Columns; c++)
                if (_mainGrid.GetPlateAt(r, c) == null) return; // còn ô trống

        // Tất cả ô đều có plate → Game Over!
        TriggerGameOver();
    }

    /// <summary>
    /// Thực thi trình tự Game Over:
    ///   1. Đánh dấu đã trigger để tránh gọi lại.
    ///   2. FSM → GameOverState (chặn DragController + PowerUp 3D input).
    ///   3. Cập nhật highest score trong PlayerDataManager (nếu điểm mới cao hơn).
    ///   4. Enable Game Over Canvas.
    ///   5. Điền điểm và highest score vào TMP.
    /// </summary>
    public void TriggerGameOver()
    {
        _gameOverTriggered = true;

        if (_audioToStopOnGameOver != null)
            _audioToStopOnGameOver.Stop();

        if (_audioSource != null && _gameOverSound != null)
            _audioSource.PlayOneShot(_gameOverSound);

        // FSM → GameOverState: DragController và PowerUp đều block khi không ở Playing.
        _fsm?.ChangeState(_fsm.GameOver);

        // ── Fire game-over event (Observer) ─────────────────────────────────────────
        // AchievementManager dùng event này để kiểm tra achievement #3.
        OnGameOver?.Invoke();

        // ── Cập nhật highest score ────────────────────────────────────────────────
        int totalScore = ScoreManager.Instance != null
            ? ScoreManager.Instance.TotalPlatesCompleted
            : 0;

        if (PlayerDataManager.Instance != null)
        {
            bool isNewRecord = PlayerDataManager.Instance.UpdateHighestScore(totalScore);
            if (isNewRecord)
                Debug.Log($"[GameOverManager] New highest score record: {totalScore}");
        }

        // Hiện Game Over Canvas.
        if (_gameOverCanvas != null)
            _gameOverCanvas.gameObject.SetActive(true);

        // Chạy animation rơi xuống
        if (_gameOverPanel != null)
        {
            StartCoroutine(DropPanelCoroutine());
        }

        // Ghi điểm hiện tại vào TMP (bắt đầu từ 0 và nhảy số lên).
        if (_scoreText != null)
        {
            AnimatedNumberText animText = _scoreText.GetComponent<AnimatedNumberText>();
            if (animText == null) animText = _scoreText.gameObject.AddComponent<AnimatedNumberText>();
            
            animText.SetTargetValue(0, 0f, true); // Gán cứng bằng 0
            animText.SetTargetValue(totalScore, 1.5f); // Nhảy lên điểm thật trong 1.5s
        }

        // Ghi highest score vào TMP.
        if (_highestScoreText != null && PlayerDataManager.Instance != null)
        {
            AnimatedNumberText animText = _highestScoreText.GetComponent<AnimatedNumberText>();
            if (animText == null) animText = _highestScoreText.gameObject.AddComponent<AnimatedNumberText>();

            animText.SetTargetValue(0, 0f, true);
            animText.SetTargetValue(PlayerDataManager.Instance.HighestScore, 1.5f);
        }

        Debug.Log($"[GameOverManager] Game Over! Total plates completed: {totalScore}");
    }

    private System.Collections.IEnumerator DropPanelCoroutine()
    {
        Vector2 startPos = _panelOriginalPos + new Vector2(0f, _dropOffset);
        Vector2 endPos = _panelOriginalPos;

        _gameOverPanel.anchoredPosition = startPos;

        float elapsed = 0f;
        while (elapsed < _dropDuration)
        {
            // Dùng unscaledDeltaTime để đề phòng trường hợp game bị pause (Time.timeScale = 0)
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _dropDuration);
            
            // Công thức EaseOutBack: trượt xuống, nhún quá đà một chút rồi nảy lại vị trí gốc
            float c1 = 1.70158f;
            float c3 = c1 + 1f;
            float eased = 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);

            _gameOverPanel.anchoredPosition = Vector2.LerpUnclamped(startPos, endPos, eased);
            yield return null;
        }

        _gameOverPanel.anchoredPosition = endPos;
    }
}
