using System;

/// <summary>
/// Dữ liệu lưu trữ của người chơi — được serialize/deserialize sang JSON.
///
/// Bao gồm:
///   - coin                : số coin player sở hữu.
///   - highestScore        : điểm cao nhất từ trước đến nay (tổng plate đã hoàn thành).
///   - powerUpQuantities   : số lượng từng loại power-up còn lại.
///   - dailyReward         : trạng thái daily reward (ngày cuối nhận, streak hiện tại).
///
/// Lớp này là pure data (không kế thừa MonoBehaviour).
/// PlayerDataManager chịu trách nhiệm load/save instance này.
/// </summary>
[Serializable]
public class PlayerData
{
    /// <summary>Số coin hiện tại của player.</summary>
    public int coin;

    /// <summary>Điểm cao nhất (tổng số plate đã hoàn thành) từ trước đến nay. Không bao giờ giảm.</summary>
    public int highestScore;

    /// <summary>Số lượng từng loại power-up player đang sở hữu.</summary>
    public PowerUpQuantities powerUpQuantities = new PowerUpQuantities();

    /// <summary>Trạng thái daily reward — ngày cuối claim và ngày đã nhận trong chu kỳ.</summary>
    public DailyRewardSaveData dailyReward = new DailyRewardSaveData();
}

/// <summary>
/// Số lượng tồn kho của từng power-up.
/// Tên field khớp chính xác với key trong player_data_default.json.
/// </summary>
[Serializable]
public class PowerUpQuantities
{
    /// <summary>Power-up Sausage (Fill) — sinh ra đĩa bổ sung để lấp đầy.</summary>
    public int sausage;

    /// <summary>Power-up Cutter (Unify) — thống nhất toàn bộ slice thành 1 loại pizza.</summary>
    public int cutter;

    /// <summary>Power-up TrashCan (Remove) — xoá 1 đĩa khỏi main grid.</summary>
    public int trashCan;

    /// <summary>Power-up Swap — hoán đổi vị trí 2 đĩa trên main grid.</summary>
    public int swap;
}

/// <summary>
/// Trạng thái daily reward được lưu vào save file.
/// Tên field khớp chính xác với key trong player_data_default.json.
/// </summary>
[Serializable]
public class DailyRewardSaveData
{
    /// <summary>
    /// Ngày cuối cùng player đã claim reward, định dạng "yyyy-MM-dd".
    /// Chuỗi rỗng nếu chưa bao giờ claim.
    /// </summary>
    public string lastClaimDate = "";

    /// <summary>
    /// Ngày trong chu kỳ (1–7) đã được claim gần nhất.
    /// 0 = chưa bao giờ claim trong chu kỳ hiện tại.
    /// </summary>
    public int lastClaimedDay = 0;
}
