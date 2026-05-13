using System.Text.RegularExpressions;
using UnityEngine;
using SceneMgr = UnityEngine.SceneManagement.SceneManager;
using LoadMode = UnityEngine.SceneManagement.LoadSceneMode;
using UnitySceneManagement = UnityEngine.SceneManagement;

/// <summary>
/// 常驻场景管理：按名加载、重开当前关、按场景名中的关卡号加载下一关，以及加载后主相机背景色。
/// </summary>
public sealed class GameSceneManager : MonoBehaviour
{
    static GameSceneManager _instance;

    public static GameSceneManager Instance => _instance;

    static readonly Regex s_TrailingLevelDigits = new(@"(\d+)\s*$", RegexOptions.Compiled);

    [Header("下一关")]
    [Tooltip("当前场景名末尾解析出关卡号 N 后，下一关场景名为 string.Format(本格式, N+1)。需与关卡 .unity 文件名一致，例如 Level {0}。")]
    [SerializeField] string nextLevelSceneNameFormat = "Level {0}";

    [Header("相机背景颜色")]
    [SerializeField] bool applyMainCameraBackgroundColor = true;
    [SerializeField] Color cameraBackgroundColor = new(0.19215687f, 0.3019608f, 0.4745098f, 0f);

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnEnable()
    {
        UnitySceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        UnitySceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this)
            _instance = null;
    }

    void OnSceneLoaded(UnitySceneManagement.Scene scene, UnitySceneManagement.LoadSceneMode mode)
    {
        if (!isActiveAndEnabled)
            return;
        ApplyMainCameraBackground();
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
