using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý hiệu ứng bay của các đồng xu trên Canvas.
/// Quy trình: Canvas bật -> 3 đồng xu phóng to (có độ trễ) -> Chờ một chút -> Bay về đích và nhỏ dần -> Canvas tắt.
/// </summary>
public class CoinAnimationController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Canvas con chứa 3 đồng xu. Sẽ tự động tắt bật khi cần.")]
    public GameObject coinCanvas;
    
    [Tooltip("Danh sách các RectTransform của Image Đồng Xu (kéo thả vào đây).")]
    public RectTransform[] coinRects;
    
    [Tooltip("Empty Object làm đích đến (thường là cái Icon UI Coin).")]
    public RectTransform targetRect;

    [Header("Settings")]
    [Tooltip("Thời gian phóng to từ 0 lên 1 (Pop up).")]
    public float popUpDuration = 0.3f; 
    
    [Tooltip("Thời gian bay từ điểm xuất phát đến đích.")]
    public float flyDuration = 0.6f;   
    
    [Tooltip("Độ trễ thời gian xuất phát giữa từng đồng xu (tạo cảm giác bay liên tiếp).")]
    public float delayBetweenCoins = 0.15f; 

    [Header("Audio")]
    [Tooltip("AudioSource để phát âm thanh.")]
    public AudioSource audioSource;
    
    [Tooltip("Âm thanh phát ra mỗi khi 1 đồng xu chạm đích.")]
    public AudioClip coinArriveSound;

    private Vector2[] _startPositions;
    private Vector3[] _startScales;
    private int _lastCoinAmount = -1;

    public static CoinAnimationController Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (coinRects == null || coinRects.Length == 0)
        {
            Debug.LogWarning("[CoinAnimationController] Chưa gán coinRects!");
            return;
        }

        // Lưu lại vị trí và scale xuất phát mặc định của các đồng xu
        _startPositions = new Vector2[coinRects.Length];
        _startScales = new Vector3[coinRects.Length];
        for (int i = 0; i < coinRects.Length; i++)
        {
            if (coinRects[i] != null)
            {
                _startPositions[i] = coinRects[i].anchoredPosition;
                _startScales[i] = coinRects[i].localScale;
            }
        }

        // Đảm bảo Canvas tắt lúc đầu
        if (coinCanvas != null)
            coinCanvas.SetActive(false);
    }

    private void OnDestroy()
    {
        // Dọn dẹp Singleton
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Phát animation từ vị trí mặc định đã setup trên Scene.
    /// (Sẽ được gọi thủ công từ các script khác như ScoreManager)
    /// </summary>
    public void PlayCoinAnimation()
    {
        if (coinRects == null || coinRects.Length == 0 || targetRect == null) return;
        
        // Bắt buộc script phải nằm trên 1 GameObject đang active để chạy được Coroutine
        gameObject.SetActive(true);
        
        StartCoroutine(AnimationSequenceRoutine(null));
    }

    /// <summary>
    /// Phát animation từ một vị trí cụ thể trên màn hình (Ví dụ: Vị trí của đĩa Pizza vừa merge).
    /// </summary>
    public void PlayCoinAnimationFromPosition(Vector2 screenPosition)
    {
        if (coinRects == null || coinRects.Length == 0 || targetRect == null) return;
        gameObject.SetActive(true);
        StartCoroutine(AnimationSequenceRoutine(screenPosition));
    }

    private IEnumerator AnimationSequenceRoutine(Vector2? customStartPos)
    {
        if (coinCanvas != null)
            coinCanvas.SetActive(true);

        // Reset vị trí và scale về 0 trước khi chạy
        for (int i = 0; i < coinRects.Length; i++)
        {
            if (coinRects[i] != null)
            {
                coinRects[i].anchoredPosition = customStartPos ?? _startPositions[i];
                coinRects[i].localScale = Vector3.zero;
            }
        }

        // Chạy Coroutine độc lập cho TỪNG đồng xu, kèm theo độ trễ tăng dần
        for (int i = 0; i < coinRects.Length; i++)
        {
            if (coinRects[i] != null)
            {
                float delay = i * delayBetweenCoins; 
                StartCoroutine(AnimateSingleCoin(coinRects[i], delay, _startScales[i]));
            }
        }

        // Tính tổng thời gian animation dài nhất để biết lúc nào tắt Canvas
        float maxWaitTime = (coinRects.Length - 1) * delayBetweenCoins + popUpDuration + 0.2f + flyDuration;
        
        // Đợi tất cả bay xong
        yield return new WaitForSeconds(maxWaitTime);

        // Kết thúc: Tắt canvas
        if (coinCanvas != null)
            coinCanvas.SetActive(false);
    }

    private IEnumerator AnimateSingleCoin(RectTransform coin, float delay, Vector3 originalScale)
    {
        // 1. Chờ độ trễ
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        // 2. Phóng to (Pop Up)
        float elapsed = 0f;
        while (elapsed < popUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popUpDuration);
            float easeOut = 1f - Mathf.Pow(1f - t, 3f); 

            coin.localScale = originalScale * easeOut;
            yield return null;
        }
        coin.localScale = originalScale;

        // 3. Nghỉ một chút trên màn hình
        yield return new WaitForSeconds(0.2f);

        // 4. Bay và thu nhỏ
        Vector3 startPos = coin.position;
        elapsed = 0f;
        while (elapsed < flyDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / flyDuration);
            float easeIn = t * t; 

            // Bay đến đích
            coin.position = Vector3.Lerp(startPos, targetRect.position, easeIn);
            // Thu nhỏ dần về 0
            coin.localScale = Vector3.Lerp(originalScale, Vector3.zero, easeIn);
            
            yield return null;
        }

        // Đảm bảo coin biến mất hoàn toàn khi đến đích
        coin.localScale = Vector3.zero;
        coin.position = targetRect.position;

        // Phát âm thanh khi đồng xu chạm đích
        if (audioSource != null && coinArriveSound != null)
        {
            audioSource.PlayOneShot(coinArriveSound);
        }
    }
}
