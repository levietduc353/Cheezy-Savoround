using UnityEngine;

/// <summary>
/// Gắn vào bất kỳ Button nào.
/// Wire Button.OnClick → OnCloseClicked() trong Inspector.
///
/// Khi nhấn: disable canvas được chỉ định qua _targetCanvas.
/// Nếu _targetCanvas không được gán, sẽ log warning và không làm gì.
/// </summary>
public class CloseButton : MonoBehaviour
{
    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Tooltip("Canvas sẽ bị disable khi nhấn button này.")]
    [SerializeField] private Canvas _targetCanvas;

    // ─── Public API (Button OnClick) ──────────────────────────────────────────

    /// <summary>
    /// Gọi hàm này từ Button.OnClick trong Inspector.
    /// Disable _targetCanvas.
    /// </summary>
    public void OnCloseClicked()
    {
        if (_targetCanvas == null)
        {
            Debug.LogWarning("[CloseButton] _targetCanvas chưa được gán trong Inspector.");
            return;
        }

        _targetCanvas.gameObject.SetActive(false);

        Debug.Log($"[CloseButton] Đã disable canvas: '{_targetCanvas.name}'.");
    }
}
