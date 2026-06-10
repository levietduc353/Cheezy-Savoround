using System.IO;
using UnityEngine;

/// <summary>
/// Singleton quản lý toàn bộ dữ liệu người chơi (coin, power-up quantities).
///
/// Cơ chế lưu trữ:
///   - Dữ liệu được ghi vào Application.persistentDataPath/PlayerData/player_data.json
///     (tồn tại qua các lần cập nhật game, không bị xóa khi reinstall).
///   - Lần đầu chạy (file chưa tồn tại): load từ Resources/PlayerData/player_data_default.json
///     rồi lưu ngay ra persistentDataPath.
///
/// Observer Pattern:
///   - OnCoinChanged(int newCoin)             — fired khi coin thay đổi.
///   - OnPowerUpChanged(string id, int qty)   — fired khi số lượng 1 power-up thay đổi.
///
/// Usage:
///   PlayerDataManager.Instance.AddCoin(50);
///   PlayerDataManager.Instance.UsePowerUp("sausage");
///   PlayerDataManager.Instance.Save();
/// </summary>
public class PlayerDataManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static PlayerDataManager Instance { get; private set; }

    // ─── Constants ────────────────────────────────────────────────────────────

    private const string _saveFolder        = "PlayerData";
    private const string _saveFileName      = "player_data.json";
    private const string _defaultConfigPath = "PlayerData/player_data_default";

    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>Fired mỗi khi coin thay đổi. Argument: giá trị coin mới.</summary>
    public event System.Action<int>    OnCoinChanged;

    /// <summary>
    /// Fired khi highest score được cập nhật (vượt qua kỷ lục cũ).
    /// Argument: giá trị highest score mới.
    /// </summary>
    public event System.Action<int> OnHighestScoreChanged;

    /// <summary>
    /// Fired mỗi khi số lượng 1 power-up thay đổi.
    /// Argument 1: id power-up ("sausage" | "cutter" | "trashCan" | "swap").
    /// Argument 2: số lượng mới.
    /// </summary>
    public event System.Action<string, int> OnPowerUpChanged;

    // ─── Private state ────────────────────────────────────────────────────────

    private PlayerData _data;

    /// <summary>
    /// Tính toán đường dẫn save file mỗi lần gọi — không phụ thuộc vào Awake.
    /// Đảm bảo hoạt động đúng kể cả khi ContextMenu được gọi ngoài Play Mode.
    /// </summary>
    private string SavePath => Path.Combine(
        Application.persistentDataPath, _saveFolder, _saveFileName);

    // ─── Public properties ────────────────────────────────────────────────────

    public int Coin     => _data.coin;

    /// <summary>Điểm cao nhất từ trước đến nay (không bao giờ giảm).</summary>
    public int HighestScore => _data.highestScore;

    public int SausageQty  => _data.powerUpQuantities.sausage;
    public int CutterQty   => _data.powerUpQuantities.cutter;
    public int TrashCanQty => _data.powerUpQuantities.trashCan;
    public int SwapQty     => _data.powerUpQuantities.swap;

    /// <summary>
    /// Tham chiếu trực tiếp đến DailyRewardSaveData trong _data.
    /// DailyRewardManager đọc/ghi vào object này rồi gọi Save() để persist.
    /// </summary>
    public DailyRewardSaveData DailyReward => _data.dailyReward;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        Load();
    }

    // ─── Public API — HighestScore ────────────────────────────────────

    /// <summary>
    /// So sánh <paramref name="score"/> với highest score hiện tại.
    /// Nếu cao hơn, cập nhật và lưu xuống file. Fire OnHighestScoreChanged nếu có kỷ lục mới.
    /// </summary>
    /// <returns>True nếu đây là kỷ lục mới, false nếu không vượt qua.</returns>
    public bool UpdateHighestScore(int score)
    {
        if (score <= _data.highestScore) return false;

        _data.highestScore = score;
        OnHighestScoreChanged?.Invoke(_data.highestScore);
        Save();
        Debug.Log($"[PlayerDataManager] New highest score: {_data.highestScore}");
        return true;
    }

    // ─── Public API — Coin ────────────────────────────────────────

    /// <summary>Cộng thêm <paramref name="amount"/> coin. Tự động Save.</summary>
    public void AddCoin(int amount)
    {
        if (amount <= 0) return;
        _data.coin += amount;
        OnCoinChanged?.Invoke(_data.coin);
        Save();
        Debug.Log($"[PlayerDataManager] +{amount} coin → total: {_data.coin}");
    }

    /// <summary>
    /// Trừ <paramref name="amount"/> coin nếu đủ tiền.
    /// Trả về true nếu thành công, false nếu không đủ coin.
    /// Tự động Save khi thành công.
    /// </summary>
    public bool SpendCoin(int amount)
    {
        if (amount <= 0 || _data.coin < amount)
        {
            Debug.LogWarning($"[PlayerDataManager] SpendCoin({amount}) failed — current: {_data.coin}");
            return false;
        }

        _data.coin -= amount;
        OnCoinChanged?.Invoke(_data.coin);
        Save();
        Debug.Log($"[PlayerDataManager] -{amount} coin → total: {_data.coin}");
        return true;
    }

    // ─── Public API — PowerUp ────────────────────────────────────────────────

    /// <summary>
    /// Cộng thêm <paramref name="amount"/> đơn vị cho power-up <paramref name="powerUpId"/>.
    /// Tự động Save.
    /// </summary>
    public void AddPowerUp(string powerUpId, int amount = 1)
    {
        if (amount <= 0) return;

        int newQty = ModifyPowerUp(powerUpId, +amount);
        if (newQty < 0) return; // id không hợp lệ

        OnPowerUpChanged?.Invoke(powerUpId, newQty);
        Save();
        Debug.Log($"[PlayerDataManager] +{amount} '{powerUpId}' → total: {newQty}");
    }

    /// <summary>
    /// Sử dụng 1 đơn vị power-up <paramref name="powerUpId"/> nếu còn tồn kho.
    /// Trả về true nếu thành công, false nếu đã hết.
    /// Tự động Save khi thành công.
    /// </summary>
    public bool UsePowerUp(string powerUpId)
    {
        int current = GetPowerUpQty(powerUpId);
        if (current <= 0)
        {
            Debug.LogWarning($"[PlayerDataManager] UsePowerUp('{powerUpId}') failed — qty = 0");
            return false;
        }

        int newQty = ModifyPowerUp(powerUpId, -1);
        OnPowerUpChanged?.Invoke(powerUpId, newQty);
        Save();
        Debug.Log($"[PlayerDataManager] Used 1 '{powerUpId}' → remaining: {newQty}");
        return true;
    }

    /// <summary>Trả về số lượng hiện tại của power-up <paramref name="powerUpId"/>.</summary>
    public int GetPowerUpQty(string powerUpId)
    {
        return powerUpId switch
        {
            "sausage"  => _data.powerUpQuantities.sausage,
            "cutter"   => _data.powerUpQuantities.cutter,
            "trashCan" => _data.powerUpQuantities.trashCan,
            "swap"     => _data.powerUpQuantities.swap,
            _ => -1
        };
    }

    // ─── Save / Load ──────────────────────────────────────────────────────────

    /// <summary>
    /// Ghi _data hiện tại ra file JSON tại persistentDataPath.
    /// Tạo thư mục nếu chưa tồn tại.
    /// </summary>
    public void Save()
    {
        string dir = Path.GetDirectoryName(SavePath);
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string json = JsonUtility.ToJson(_data, prettyPrint: true);
        File.WriteAllText(SavePath, json);

        Debug.Log($"[PlayerDataManager] Saved to: {SavePath}");
    }

    /// <summary>
    /// Load dữ liệu từ persistentDataPath.
    /// Nếu file chưa tồn tại, load từ default config rồi save ngay.
    /// </summary>
    public void Load()
    {
        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            _data = JsonUtility.FromJson<PlayerData>(json);

            if (_data == null)
            {
                Debug.LogWarning("[PlayerDataManager] Failed to parse save file — resetting to default.");
                LoadDefault();
                return;
            }

            Debug.Log($"[PlayerDataManager] Loaded from: {SavePath}");
        }
        else
        {
            // Lần đầu chạy: dùng default config.
            LoadDefault();
        }
    }

    /// <summary>
    /// Xoá save file và load lại từ player_data_default.json.
    /// Gọi bằng cách chuột phải vào component trong Inspector → "Reset Save Data".
    /// Chỉ dùng trong Editor / debug — không gọi trong production code.
    /// </summary>
    [ContextMenu("Reset Save Data")]
    public void ResetSaveData()
    {
        if (File.Exists(SavePath))
        {
            File.Delete(SavePath);
            Debug.Log($"[PlayerDataManager] Save file deleted: {SavePath}");
        }
        else
        {
            Debug.Log("[PlayerDataManager] Save file không tồn tại, không cần xoá.");
        }

        LoadDefault();
        Debug.Log("[PlayerDataManager] Đã reset về default data.");
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Load từ Resources/PlayerData/player_data_default.json và save ngay.</summary>
    private void LoadDefault()
    {
        TextAsset asset = Resources.Load<TextAsset>(_defaultConfigPath);

        if (asset != null)
        {
            _data = JsonUtility.FromJson<PlayerData>(asset.text);
            Debug.Log("[PlayerDataManager] Loaded default player data.");
        }
        else
        {
            // Fallback hoàn toàn nếu default JSON không tìm thấy.
            Debug.LogWarning("[PlayerDataManager] Default config not found at " +
                             $"Resources/{_defaultConfigPath}.json — using hardcoded fallback.");
            _data = new PlayerData();
        }

        // Ghi ra persistentDataPath để lần sau đọc từ đó.
        Save();
    }

    /// <summary>
    /// Cộng/trừ <paramref name="delta"/> vào power-up <paramref name="powerUpId"/>.
    /// Trả về giá trị mới, hoặc -1 nếu id không hợp lệ.
    /// </summary>
    private int ModifyPowerUp(string powerUpId, int delta)
    {
        switch (powerUpId)
        {
            case "sausage":
                _data.powerUpQuantities.sausage  = Mathf.Max(0, _data.powerUpQuantities.sausage  + delta);
                return _data.powerUpQuantities.sausage;
            case "cutter":
                _data.powerUpQuantities.cutter   = Mathf.Max(0, _data.powerUpQuantities.cutter   + delta);
                return _data.powerUpQuantities.cutter;
            case "trashCan":
                _data.powerUpQuantities.trashCan = Mathf.Max(0, _data.powerUpQuantities.trashCan + delta);
                return _data.powerUpQuantities.trashCan;
            case "swap":
                _data.powerUpQuantities.swap     = Mathf.Max(0, _data.powerUpQuantities.swap     + delta);
                return _data.powerUpQuantities.swap;
            default:
                Debug.LogError($"[PlayerDataManager] Unknown powerUpId: '{powerUpId}'");
                return -1;
        }
    }
}
