using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phát hiện trạng thái Game Over (main grid đầy) và hiển thị Game Over Canvas.
///
/// Cách hoạt động:
///   1. Subscribe GridManager.OnPlatePlaced.
///   2. Sau mỗi lần plate được đặt, kiểm tra xem toàn bộ main grid đã đầy chưa.
///   3. Nếu đầy AND MergeAnimator không còn đang chạy animation:
///      • FSM → GameOverState  (chặn DragController + PowerUp 3D input)
///      • Tạo UI blocker trong suốt (chặn click xuyên canvas)
///      • Enable _gameOverCanvas (hiển thị panel game over)
///      • Ghi điểm vào _scoreText từ ScoreManager.TotalPlatesCompleted
///
/// Lưu ý: MergeAnimator có thể giải phóng ô trống (dismiss plates) sau khi đặt.
/// Vì vậy việc kiểm tra grid đầy được delay đến khi merge animation kết thúc.
///
/// Setup trong Inspector:
///   - Assign _mainGrid       : GridManager của main grid
///   - Assign _fsm            : GameStateMachine
///   - Assign _gameOverCanvas : Canvas đang disabled (sẽ được enable khi game over)
///   - Assign _scoreText      : TMP_Text để hiển thị tổng số plate đã complete
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

    // ─── Private state ────────────────────────────────────────────────────────

    private bool  _gameOverTriggered;

    /// <summary>
    /// Image trong suốt full-screen được tạo tự động để hấp thụ mọi UI raycast,
    /// chặn tất cả button/interaction thuộc các canvas khác phía sau canvas game over.
    /// </summary>
    private Image _inputBlocker;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Đảm bảo Game Over Canvas đang tắt khi game khởi động.
        if (_gameOverCanvas != null)
            _gameOverCanvas.gameObject.SetActive(false);
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
    ///   3. Tạo UI input blocker (Image trong suốt, Raycast Target = true).
    ///   4. Enable Game Over Canvas.
    ///   5. Điền điểm vào TMP từ ScoreManager.TotalPlatesCompleted.
    /// </summary>
    private void TriggerGameOver()
    {
        _gameOverTriggered = true;

        // FSM → GameOverState: DragController và PowerUp đều block khi không ở Playing.
        _fsm?.ChangeState(_fsm.GameOver);

        // Tạo blocker TRƯỚC khi enable canvas để nó sẵn sàng ngay khi canvas hiện lên.
        AddInputBlocker();

        // Hiện Game Over Canvas.
        if (_gameOverCanvas != null)
            _gameOverCanvas.gameObject.SetActive(true);

        // Ghi điểm vào TMP.
        int totalScore = ScoreManager.Instance != null
            ? ScoreManager.Instance.TotalPlatesCompleted
            : 0;

        if (_scoreText != null)
            _scoreText.text = totalScore.ToString();

        Debug.Log($"[GameOverManager] Game Over! Total plates completed: {totalScore}");
    }

    /// <summary>
    /// Tạo một Image trong suốt (alpha = 0) full-screen stretch làm child đầu tiên
    /// của _gameOverCanvas, với Raycast Target = true.
    ///
    /// Tại sao cần blocker này?
    ///   Unity EventSystem gửi click event đến canvas có Sort Order cao nhất.
    ///   Nếu Game Over Canvas không có element nào che phủ toàn màn hình thì
    ///   click vẫn "xuyên qua" và chạm vào button/UI của canvas bên dưới.
    ///   Blocker Image này hấp thụ toàn bộ raycast, ngăn event đến canvas khác.
    ///
    /// Chỉ tạo 1 lần duy nhất — nếu đã tồn tại thì bỏ qua.
    /// </summary>
    private void AddInputBlocker()
    {
        if (_gameOverCanvas == null) return;
        if (_inputBlocker   != null) return; // đã tạo rồi, bỏ qua

        // Tạo GameObject blocker, child của Game Over Canvas.
        var blockerGo = new GameObject("InputBlocker",
            typeof(RectTransform),
            typeof(Image));

        blockerGo.transform.SetParent(_gameOverCanvas.transform, false);

        // SetAsFirstSibling → nằm dưới cùng trong hierarchy (vẽ trước),
        // nhưng Raycast Target = true trên toàn màn hình đủ để block mọi click.
        blockerGo.transform.SetAsFirstSibling();

        // Stretch full-screen theo canvas.
        RectTransform rt = blockerGo.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Alpha = 0: hoàn toàn trong suốt về mặt hình ảnh.
        // raycastTarget = true: hấp thụ mọi pointer event của EventSystem. ← mấu chốt
        _inputBlocker              = blockerGo.GetComponent<Image>();
        _inputBlocker.color        = new Color(0f, 0f, 0f, 0f);
        _inputBlocker.raycastTarget = true;

        Debug.Log("[GameOverManager] Full-screen input blocker created on Game Over Canvas.");
    }
}
