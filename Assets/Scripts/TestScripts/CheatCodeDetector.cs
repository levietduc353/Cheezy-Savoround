using UnityEngine;
using UnityEngine.InputSystem; // Bắt buộc phải dùng namespace này cho Input System mới

/// <summary>
/// Script ẩn dùng để test game.
/// Chức năng: Gõ phím theo thứ tự "258963" trên bàn phím sẽ kích hoạt Game Over ngay lập tức.
/// </summary>
public class CheatCodeDetector : MonoBehaviour
{
    // Chuỗi phím cần gõ để kích hoạt cheat
    private const string CHEAT_CODE = "258963";
    
    // Bộ đệm lưu trữ các phím vừa gõ
    private string _inputBuffer = "";

    private void OnEnable()
    {
        // Đăng ký sự kiện gõ phím của Input System mới
        if (Keyboard.current != null)
        {
            Keyboard.current.onTextInput += OnTextInput;
        }
    }

    private void OnDisable()
    {
        // Hủy đăng ký sự kiện
        if (Keyboard.current != null)
        {
            Keyboard.current.onTextInput -= OnTextInput;
        }
    }

    // Hàm này tự động được gọi mỗi khi người chơi gõ một ký tự
    private void OnTextInput(char c)
    {
        // Bỏ qua các ký tự điều khiển (Enter, Backspace, Esc...)
        if (char.IsControl(c))
            return;

        // Thêm ký tự vào buffer
        _inputBuffer += c;

        // Giới hạn độ dài buffer để tránh tốn bộ nhớ (dài hơn code một chút là được)
        if (_inputBuffer.Length > 20)
        {
            _inputBuffer = _inputBuffer.Substring(_inputBuffer.Length - 20);
        }

        // Kiểm tra xem chuỗi gõ gần đây có khớp với cheat code không
        if (_inputBuffer.EndsWith(CHEAT_CODE))
        {
            ActivateCheat();
            
            // Xóa buffer sau khi cheat kích hoạt để tránh kích hoạt liên tục
            _inputBuffer = "";
        }
    }

    private void ActivateCheat()
    {
        Debug.LogWarning("[CheatCodeDetector] Cheat code kích hoạt! Ép Game Over.");

        // Tìm GameOverManager trong scene và kích hoạt
        GameOverManager gameOverManager = FindObjectOfType<GameOverManager>();
        
        if (gameOverManager != null)
        {
            gameOverManager.TriggerGameOver();
        }
        else
        {
            Debug.LogError("[CheatCodeDetector] Không tìm thấy GameOverManager trong scene!");
        }
    }
}
