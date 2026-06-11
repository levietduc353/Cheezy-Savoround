using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Singleton that manages the per-session pizza-type unlock progression.
///
/// Rules:
///   • Reads unlock_config.json on Awake and immediately activates all types
///     whose atLevel ≤ 1 (the starting three: pizza_1, pizza_2, pizza_3).
///   • Subscribes to ScoreManager.OnLevelChanged.  When the event fires,
///     the current level is read from _levelText (TMP_Text) rather than the
///     event argument, making the TMP the single source of truth for level.
///   • On unlock: the type is added to _unlockedTypeIds and
///     PlayerDataManager.AddSliceUnlocked() is called (+1 to persistent save).
///   • NOT DontDestroyOnLoad — intentionally destroyed on scene reload so that
///     each new game session starts fresh with only the initial 3 types.
///
/// Observer Pattern:
///   OnTypeUnlocked(string typeId) — fired whenever a new type is unlocked.
///   Subscribers (e.g. UI) can react to show an unlock notification.
///
/// Usage (read-only from other systems):
///   UnlockManager.Instance.UnlockedTypeIds     → current unlocked type list
///   UnlockManager.Instance.CurrentFillerChance → filler probability [0,1]
/// </summary>
public class UnlockManager : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────

    public static UnlockManager Instance { get; private set; }

    // ─── Inspector fields ─────────────────────────────────────────────────────

    [Header("Config")]
    [Tooltip("Path inside Resources/ folder (no extension).")]
    [SerializeField] private string _configPath = "Configs/unlock_config";

    [Header("Level Source")]
    [Tooltip("TMP_Text whose integer value represents the current in-game level. " +
             "Assign the same label that ScoreUI drives (e.g. the CurrentLevelText TMP).")]
    [SerializeField] private TMP_Text _levelText;

    // ─── Events (Observer Pattern) ────────────────────────────────────────────

    /// <summary>
    /// Fired each time a new pizza type is unlocked during a session.
    /// Argument: the pool/type id that was just unlocked (e.g. "pizza_4").
    /// </summary>
    public event System.Action<string> OnTypeUnlocked;

    // ─── Private state ────────────────────────────────────────────────────────

    private UnlockConfigData _config;

    /// <summary>
    /// Type ids currently available for spawning this session.
    /// Starts with the level-1 types; grows as the player levels up.
    /// </summary>
    private readonly List<string> _unlockedTypeIds = new List<string>();

    /// <summary>Active filler chance [0,1] based on current level.</summary>
    private float _currentFillerChance;

    /// <summary>
    /// Last level value read from _levelText.
    /// Compared each time OnLevelChanged fires to detect genuine increases.
    /// </summary>
    private int _lastKnownLevel = 1;

    // ─── Public properties ────────────────────────────────────────────────────

    /// <summary>
    /// Read-only list of pizza type ids available for spawning this session.
    /// HoldGridManager reads this to restrict random type selection.
    /// </summary>
    public IReadOnlyList<string> UnlockedTypeIds => _unlockedTypeIds;

    /// <summary>
    /// Current probability [0, 1] that a full plate will carry filler slices.
    /// Increases at level milestones defined in unlock_config.json.
    /// </summary>
    public float CurrentFillerChance => _currentFillerChance;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        // Singleton — one instance per scene load. NOT DontDestroyOnLoad so
        // that each game session resets unlock state cleanly.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (_levelText == null)
            Debug.LogWarning("[UnlockManager] _levelText is not assigned in the Inspector. " +
                             "Unlock progression will not fire.");

        LoadConfig();
        ApplyInitialUnlocks();
    }

    private void Start()
    {
        // Subscribe in Start() — NOT OnEnable() — because Start() is guaranteed
        // to run after ALL Awake() calls in the scene, so ScoreManager.Instance
        // is always valid here regardless of GameObject order in the hierarchy.
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnLevelChanged += HandleLevelChangedEvent;
        }
        else
        {
            Debug.LogError("[UnlockManager] ScoreManager.Instance not found in Start(). " +
                           "Ensure ScoreManager is present in the scene. Unlock will not function.");
        }
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
            ScoreManager.Instance.OnLevelChanged -= HandleLevelChangedEvent;
    }

    // ─── Private logic ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads unlock_config.json and caches the data.
    /// Applies fallback values if the file cannot be found.
    /// </summary>
    private void LoadConfig()
    {
        TextAsset jsonAsset = Resources.Load<TextAsset>(_configPath);

        if (jsonAsset == null)
        {
            Debug.LogError($"[UnlockManager] Config not found at Resources/{_configPath}.json. " +
                           "All 6 pizza types will be available from the start.");
            // Fallback: unlock everything immediately so the game is still playable.
            _config = null;
            return;
        }

        _config = JsonUtility.FromJson<UnlockConfigData>(jsonAsset.text);

        if (_config == null)
        {
            Debug.LogError("[UnlockManager] Failed to parse UnlockConfigData. " +
                           "All 6 pizza types will be available from the start.");
        }
        else
        {
            Debug.Log($"[UnlockManager] Config loaded: {_config.unlockRules?.Length ?? 0} rules, " +
                      $"{_config.fillerChanceByLevel?.Length ?? 0} filler breakpoints.");
        }
    }

    /// <summary>
    /// Activates all pizza types that are available at the start of the session
    /// (atLevel ≤ 1). Called once during Awake so HoldGridManager always has
    /// a valid list to draw from when it fills the hold grid on Start.
    /// </summary>
    private void ApplyInitialUnlocks()
    {
        if (_config?.unlockRules == null)
        {
            // Fallback: add all known pizza types.
            for (int i = 1; i <= 6; i++)
                _unlockedTypeIds.Add($"pizza_{i}");
            return;
        }

        // Unlock every type whose atLevel is 1 (available from game start).
        foreach (UnlockRule rule in _config.unlockRules)
        {
            if (rule.atLevel <= 1)
                _unlockedTypeIds.Add(rule.unlockedPizzaTypeId);
        }

        // Set the initial filler chance (level 1 value).
        _currentFillerChance = GetFillerChanceForLevel(1);

        Debug.Log($"[UnlockManager] Session start — unlocked: [{string.Join(", ", _unlockedTypeIds)}], " +
                  $"fillerChance={_currentFillerChance:P0}");
    }

    /// <summary>
    /// Called by ScoreManager.OnLevelChanged when the fill bar completes a cycle.
    /// Defers reading the TMP by one frame so ScoreUI has time to update the
    /// label before we parse it — avoids a race condition where UnlockManager
    /// runs before ScoreUI in the same event callback batch.
    /// </summary>
    private void HandleLevelChangedEvent(int _ignored)
    {
        StartCoroutine(CheckLevelAfterUIUpdate());
    }

    /// <summary>
    /// Waits one frame, then reads _levelText to determine the current level.
    /// By the next frame ScoreUI.UpdateLevelLabels is guaranteed to have run,
    /// so the TMP value is up-to-date.
    /// </summary>
    private IEnumerator CheckLevelAfterUIUpdate()
    {
        // Wait for end of current frame so all OnLevelChanged subscribers
        // (including ScoreUI) finish updating before we read the TMP.
        yield return null;

        if (_levelText == null)
        {
            Debug.LogWarning("[UnlockManager] _levelText is not assigned — cannot read level from TMP.");
            yield break;
        }

        // Parse the TMP text as the authoritative level value.
        if (!int.TryParse(_levelText.text, out int displayedLevel))
        {
            Debug.LogWarning($"[UnlockManager] Could not parse level from TMP text: '{_levelText.text}'.");
            yield break;
        }

        // Guard: only act on a genuine increase.
        if (displayedLevel <= _lastKnownLevel) yield break;

        // Process every level crossed in order (handles multi-level jumps safely).
        for (int lvl = _lastKnownLevel + 1; lvl <= displayedLevel; lvl++)
            HandleLevelChanged(lvl);

        _lastKnownLevel = displayedLevel;
    }

    /// <summary>
    /// Checks all unlock rules for <paramref name="newLevel"/> and activates any
    /// matching pizza type. Also refreshes the active filler chance.
    /// Called from HandleLevelChangedEvent() for each level crossed.
    /// </summary>
    private void HandleLevelChanged(int newLevel)
    {
        if (_config?.unlockRules == null) return;

        foreach (UnlockRule rule in _config.unlockRules)
        {
            if (rule.atLevel != newLevel) continue;
            if (_unlockedTypeIds.Contains(rule.unlockedPizzaTypeId)) continue;

            // ── Unlock new type ────────────────────────────────────────────────
            _unlockedTypeIds.Add(rule.unlockedPizzaTypeId);

            // ── Persist: increment SliceUnlocked counter in save data ──────────
            PlayerDataManager.Instance?.AddSliceUnlocked();

            // ── Notify subscribers (e.g. UI unlock popup) ─────────────────────
            OnTypeUnlocked?.Invoke(rule.unlockedPizzaTypeId);

            Debug.Log($"[UnlockManager] Level {newLevel} → '{rule.unlockedPizzaTypeId}' unlocked! " +
                      $"Total unlocked: {_unlockedTypeIds.Count}");
        }

        // Update filler chance for the new level.
        _currentFillerChance = GetFillerChanceForLevel(newLevel);
        Debug.Log($"[UnlockManager] Level {newLevel} — fillerChance updated to {_currentFillerChance:P0}");
    }

    /// <summary>
    /// Returns the filler chance [0, 1] for a given level by finding the
    /// highest breakpoint whose fromLevel is ≤ <paramref name="level"/>.
    /// Falls back to 0 if no breakpoint applies or config is missing.
    /// </summary>
    private float GetFillerChanceForLevel(int level)
    {
        if (_config?.fillerChanceByLevel == null) return 0f;

        float result = 0f;

        // Iterate all breakpoints; keep the last one whose fromLevel ≤ level.
        // Config entries are assumed ordered ascending but we don't require it.
        foreach (FillerChanceEntry entry in _config.fillerChanceByLevel)
        {
            if (entry.fromLevel <= level)
                result = entry.fillerChance;
        }

        return result;
    }
}
