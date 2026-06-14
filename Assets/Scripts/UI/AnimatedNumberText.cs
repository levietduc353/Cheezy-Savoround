using TMPro;
using UnityEngine;
using System.Collections;

/// <summary>
/// Component dùng để tạo hiệu ứng "nhảy số" dần dần từ giá trị cũ lên giá trị mới.
/// Áp dụng được cho bất kỳ UI Text nào cần hiển thị con số (Coin, Score, Level...).
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class AnimatedNumberText : MonoBehaviour
{
    private TMP_Text _textComponent;
    private int _displayValue = -1;
    private Coroutine _countCoroutine;

    private void Awake()
    {
        _textComponent = GetComponent<TMP_Text>();
    }

    /// <summary>
    /// Gọi hàm này để số tự động nhảy đến giá trị mới.
    /// Nếu isFirstTime = true, nó sẽ hiện luôn số đó mà không đếm dần.
    /// </summary>
    public void SetTargetValue(int targetValue, float duration = 0.5f, bool isFirstTime = false)
    {
        // Khởi tạo component nếu chưa được Awake gọi (có thể xảy ra nếu gọi ngay trong Awake của script khác)
        if (_textComponent == null) 
            _textComponent = GetComponent<TMP_Text>();

        // Nếu là lần đầu, chưa có số nào, HOẶC object đang bị tắt (không thể chạy Coroutine), thì set cứng luôn
        if (isFirstTime || _displayValue == -1 || !gameObject.activeInHierarchy)
        {
            _displayValue = targetValue;
            _textComponent.text = _displayValue.ToString();
            return;
        }

        // Dừng animation cũ nếu đang chạy dở
        if (_countCoroutine != null)
        {
            StopCoroutine(_countCoroutine);
        }

        // Bắt đầu chạy animation đếm số mới
        _countCoroutine = StartCoroutine(CountRoutine(targetValue, duration));
    }

    private IEnumerator CountRoutine(int targetValue, float duration)
    {
        int startValue = _displayValue;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Dùng ease-out (SmoothStep nửa sau) để số chạy nhanh lúc đầu và chậm lại lúc cuối
            float easeT = 1f - Mathf.Pow(1f - t, 3f); // Cubic ease out

            _displayValue = Mathf.RoundToInt(Mathf.Lerp(startValue, targetValue, easeT));
            _textComponent.text = _displayValue.ToString();
            
            yield return null;
        }

        // Đảm bảo kết thúc bằng đúng giá trị target
        _displayValue = targetValue;
        _textComponent.text = _displayValue.ToString();
        _countCoroutine = null;
    }
}
