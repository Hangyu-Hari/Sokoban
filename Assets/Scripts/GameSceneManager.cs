using System;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityScene = UnityEngine.SceneManagement.Scene;
using SceneMgr = UnityEngine.SceneManagement.SceneManager;
using LoadMode = UnityEngine.SceneManagement.LoadSceneMode;
using UnitySceneManagement = UnityEngine.SceneManagement;

/// <summary>
/// 常驻场景管理：按名加载、重开当前关、按场景名中的关卡号加载下一关；加载后主相机背景色；按场景启用/禁用 <see cref="LevelUIManager"/> 根物体（如 Start 关、关卡开）。
/// </summary>
public sealed class GameSceneManager : MonoBehaviour
{
    public const string MaxUnlockedLevelPrefsKey = "Sokoban.MaxUnlockedLevel";

    static GameSceneManager _instance;

    public static GameSceneManager Instance => _instance;

    static readonly Regex s_TrailingLevelDigits = new(@"(\d+)\s*$", RegexOptions.Compiled);

    [Header("下一关")]
    [Tooltip("当前场景名末尾解析出关卡号 N 后，下一关场景名为 string.Format(本格式, N+1)。需与关卡 .unity 文件名一致，例如 Level {0}。")]
    [SerializeField] string nextLevelSceneNameFormat = "Level {0}";

    [Header("存档 / 调试")]
    [Tooltip("勾选后，游戏开局（本对象 Awake）时清除 PlayerPrefs 里的最高解锁关卡，恢复为默认仅第 1 关。发行版请取消勾选。")]
    [SerializeField] bool resetMaxUnlockedLevelOnGameStart;

    [Header("相机背景颜色")]
    [SerializeField] bool applyMainCameraBackgroundColor = true;
    [SerializeField] Color cameraBackgroundColor = new(0.19215687f, 0.3019608f, 0.4745098f, 0f);

    [Header("LevelUIManager HUD")]
    [Tooltip("勾选后按「场景名是否包含下列子串」启用或禁用 LevelUIManager 的根 GameObject")]
    [SerializeField] bool manageLevelUiManagerVisibility = true;
    [Tooltip("当前场景名包含以下任一子串（不区分大小写）则关闭 HUD，例如 Start（主菜单）、Editor（地图编辑）；未匹配则显示")]
    [SerializeField] string[] hideLevelUiWhenActiveSceneNameContains = { "Start", "Editor" };

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        if (resetMaxUnlockedLevelOnGameStart)
        {
            PlayerPrefs.DeleteKey(MaxUnlockedLevelPrefsKey);
            PlayerPrefs.Save();
        }
    }

    void OnEnable()
    {
        UnitySceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

        var active = UnitySceneManagement.SceneManager.GetActiveScene();
        if (active.IsValid() && TryParseTrailingLevelNumber(active.name, out var n))
            RegisterReachedLevelNumber(n);

        ApplyLevelUiManagerVisibilityForScene(active);
    }

    /// <summary> 晚于一帧内所有 <c>Awake</c>，确保 <see cref="LevelUIManager.Instance"/> 已就绪。 </summary>
    void Start()
    {
        ApplyLevelUiManagerVisibilityForScene(UnitySceneManagement.SceneManager.GetActiveScene());
    }

    void OnDestroy()
    {
        UnitySceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this)
            _instance = null;
    }

    void OnSceneLoaded(UnityScene scene, UnitySceneManagement.LoadSceneMode mode)
    {
        if (TryParseTrailingLevelNumber(scene.name, out var levelNumber))
            RegisterReachedLevelNumber(levelNumber);

        if (!isActiveAndEnabled)
            return;
        ApplyLevelUiManagerVisibilityForScene(scene);
        ApplyMainCameraBackground();
    }

    bool ShouldHideLevelUiManagerForScene(UnityScene scene)
    {
        if (!scene.IsValid())
            return true;

        var name = scene.name ?? string.Empty;

        if (hideLevelUiWhenActiveSceneNameContains != null && hideLevelUiWhenActiveSceneNameContains.Length > 0)
        {
            foreach (var sub in hideLevelUiWhenActiveSceneNameContains)
            {
                if (string.IsNullOrEmpty(sub))
                    continue;
                if (name.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }

        return false;
    }

    void ApplyLevelUiManagerVisibilityForScene(UnityScene scene)
    {
        if (!manageLevelUiManagerVisibility || !enabled)
            return;

        var lm = LevelUIManager.Instance;
        if (lm == null)
            return;

        bool hideHud = ShouldHideLevelUiManagerForScene(scene);

        lm.gameObject.SetActive(!hideHud);

        // 进入关卡时清掉上一轮留下的完成窗 / 暂停（与昔日 CanvasGroup 切场景逻辑一致）。
        if (!hideHud)
        {
            lm.HideLevelCompleteUI();
            lm.HidePauseUI();
        }
    }

    /// <summary> 已解锁的最高关卡号（持久化在 PlayerPrefs）：进入某关或<strong>过关</strong>时会取较大值写入。新存档默认为 1。 </summary>
    public static int GetMaxUnlockedLevel() =>
        PlayerPrefs.GetInt(MaxUnlockedLevelPrefsKey, 1);

    /// <inheritdoc cref="GetMaxUnlockedLevel"/>
    public int MaxUnlockedLevel => GetMaxUnlockedLevel();

    /// <summary>
    /// 当前关卡胜利时调用：把记录推进到「下一关」关卡号（当前场景名末尾数字 + 1），不必等加载下一关场景。
    /// </summary>
    public void RegisterUnlockedNextLevelAfterWin()
    {
        var active = UnitySceneManagement.SceneManager.GetActiveScene();
        if (!active.IsValid())
            return;

        if (!TryParseTrailingLevelNumber(active.name, out var currentLevel))
            return;

        RegisterReachedLevelNumber(currentLevel + 1);
    }

    static void RegisterReachedLevelNumber(int levelNumber)
    {
        var prev = PlayerPrefs.GetInt(MaxUnlockedLevelPrefsKey, 1);
        var next = Mathf.Max(prev, levelNumber);
        if (next == prev)
            return;
        PlayerPrefs.SetInt(MaxUnlockedLevelPrefsKey, next);
        PlayerPrefs.Save();
    }

    /// <summary>加载名为 <paramref name="sceneName"/> 的场景（单场景模式，会卸载当前场景）。</summary>
    public void LoadScene(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[GameSceneManager] 场景名为空。", this);
            return;
        }

        SceneMgr.LoadScene(sceneName.Trim(), LoadMode.Single);
        ApplyMainCameraBackground();
    }

    /// <summary>重新加载当前场景（同 buildIndex，用于重开本关）。</summary>
    public void RestartCurrentScene()
    {
        var active = UnitySceneManagement.SceneManager.GetActiveScene();
        if (!active.IsValid())
        {
            Debug.LogWarning("[GameSceneManager] 当前场景无效，无法重开。", this);
            return;
        }

        SceneMgr.LoadScene(active.buildIndex, LoadMode.Single);
        ApplyMainCameraBackground();
    }

    /// <summary>
    /// 根据<strong>当前场景名末尾的数字</strong>（如 <c>Level 3</c> → 3）拼出 <c>Level 4</c> 并加载；
    /// 与 Build Settings 中的顺序无关。场景名中无数字则放弃并打日志。
    /// </summary>
    public void LoadNextScene()
    {
        var active = UnitySceneManagement.SceneManager.GetActiveScene();
        if (!active.IsValid())
        {
            Debug.LogWarning("[GameSceneManager] 当前场景无效，无法加载下一关。", this);
            return;
        }

        if (!TryParseTrailingLevelNumber(active.name, out var levelNumber))
        {
            Debug.LogWarning(
                $"[GameSceneManager] 场景名「{active.name}」末尾没有可解析的关卡数字，无法按名加载下一关。",
                this);
            return;
        }

        if (string.IsNullOrWhiteSpace(nextLevelSceneNameFormat) || !nextLevelSceneNameFormat.Contains("{0}"))
        {
            Debug.LogWarning(
                "[GameSceneManager] nextLevelSceneNameFormat 无效：需非空且包含占位符 {0}（关卡号）。",
                this);
            return;
        }

        var nextSceneName = string.Format(nextLevelSceneNameFormat.Trim(), levelNumber + 1);
        if (!Application.CanStreamedLevelBeLoaded(nextSceneName))
        {
            Debug.LogWarning(
                $"[GameSceneManager] 没有下一关或场景未加入 Build Settings：无法加载「{nextSceneName}」。",
                this);
            return;
        }

        SceneMgr.LoadScene(nextSceneName, LoadMode.Single);
        ApplyMainCameraBackground();
    }

    static bool TryParseTrailingLevelNumber(string sceneName, out int levelNumber)
    {
        levelNumber = 0;
        if (string.IsNullOrEmpty(sceneName))
            return false;

        var m = s_TrailingLevelDigits.Match(sceneName.TrimEnd());
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out levelNumber))
            return false;

        return true;
    }

    void ApplyMainCameraBackground()
    {
        if (!applyMainCameraBackgroundColor)
            return;

        var cam = Camera.main;
        if (cam == null)
            return;

        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = cameraBackgroundColor;
    }
}
