using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 挂在关卡内 UI 根物体上：DontDestroyOnLoad，用 CanvasGroup 控制显示（不会关掉脚本）。
/// 在「菜单类」场景里隐藏，进入「游戏类」场景后显示；规则可在 Inspector 配。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class LevelUIManager : MonoBehaviour
{
    static LevelUIManager _instance;

    /// <summary> 常驻单例；未创建时为 null。 </summary>
    public static LevelUIManager Instance => _instance;

    [Header("关卡完成")]
    [Tooltip("拖入「完成关」用的整块 UI 根物体（例如一个 Panel）。胜利时会 SetActive(true)；换场景或调用 HideLevelCompleteUI 时会关掉。")]
    [SerializeField] GameObject levelCompleteUI;

    [Header("暂停")]
    [Tooltip("拖入暂停界面根物体。激活时关卡操作输入会被忽略（见 IsGameplayInputBlocked）。")]
    [SerializeField] GameObject pauseUI;

    [Tooltip("当前场景名包含以下任一子串（不区分大小写）时隐藏 UI，例如 Start、MainMenu")]
    [SerializeField] string[] hideWhenActiveSceneNameContains = { "Start" };

    [Tooltip("-1：不按 buildIndex 判断。≥0：buildIndex 小于该值时隐藏（例如 1 表示只有 Build Settings 里第 0 个场景算菜单）。")]
    [SerializeField] int hideWhenBuildIndexLessThan = 1;

    CanvasGroup _canvasGroup;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        HideLevelCompleteUI();
        HidePauseUI();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_instance == this)
            _instance = null;
    }

    void Start()
    {
        ApplyVisibilityForScene(SceneManager.GetActiveScene());
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
            GameSceneManager.Instance?.RestartCurrentScene();

        if (Input.GetKeyDown(KeyCode.Escape))
            HandleEscapeForPause();
    }

    void HandleEscapeForPause()
    {
        var scene = SceneManager.GetActiveScene();
        if (ShouldHideForScene(scene))
            return;

        if (levelCompleteUI != null && levelCompleteUI.activeInHierarchy)
            return;

        if (pauseUI == null)
            return;

        if (pauseUI.activeInHierarchy)
            HidePauseUI();
        else
            ShowPauseUI();
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyVisibilityForScene(scene);
    }

    void ApplyVisibilityForScene(Scene scene)
    {
        var visible = !ShouldHideForScene(scene);
        _canvasGroup.alpha = visible ? 1f : 0f;
        _canvasGroup.interactable = visible;
        _canvasGroup.blocksRaycasts = visible;

        HideLevelCompleteUI();
        HidePauseUI();
    }

    /// <summary>
    /// 暂停界面或关卡完成界面处于显示链上时为 true；游戏逻辑（如推箱子输入）应据此跳过。
    /// </summary>
    public bool IsGameplayInputBlocked =>
        (pauseUI != null && pauseUI.activeInHierarchy) ||
        (levelCompleteUI != null && levelCompleteUI.activeInHierarchy);

    bool ShouldHideForScene(Scene scene)
    {
        if (!scene.IsValid())
            return true;

        var name = scene.name ?? string.Empty;
        if (hideWhenActiveSceneNameContains != null)
        {
            foreach (var sub in hideWhenActiveSceneNameContains)
            {
                if (string.IsNullOrEmpty(sub))
                    continue;
                if (name.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }

        if (hideWhenBuildIndexLessThan >= 0 && scene.buildIndex < hideWhenBuildIndexLessThan)
            return true;

        return false;
    }

    /// <summary> 胜利时调用：显示你在 Inspector 里拖入的完成界面。 </summary>
    public void ShowLevelCompleteUI()
    {
        if (levelCompleteUI != null)
            levelCompleteUI.SetActive(true);
    }

    /// <summary> 隐藏完成界面（换关、重开、下一关按钮里可调用）。 </summary>
    public void HideLevelCompleteUI()
    {
        if (levelCompleteUI != null)
            levelCompleteUI.SetActive(false);
    }

    /// <summary> 打开暂停界面（例如 ESC 按钮 OnClick 里调用）。 </summary>
    public void ShowPauseUI()
    {
        if (pauseUI != null)
            pauseUI.SetActive(true);
    }

    /// <summary> 关闭暂停界面（继续游戏按钮里调用）。 </summary>
    public void HidePauseUI()
    {
        if (pauseUI != null)
            pauseUI.SetActive(false);
    }
}
