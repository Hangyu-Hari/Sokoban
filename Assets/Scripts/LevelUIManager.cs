using System;
using UnityEngine;
using UnityEngine.UI;
using UnitySceneManagement = UnityEngine.SceneManagement;

/// <summary>
/// 挂在关卡内 UI 根物体上：DontDestroyOnLoad。
/// 关卡完成界面显示时，在非编辑器场景可按 Enter（主键盘或数字区）跳转下一关，与「下一关」按钮一致。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class LevelUIManager : MonoBehaviour
{
    static LevelUIManager _instance;

    /// <summary> 常驻单例；未创建时为 null。 </summary>
    public static LevelUIManager Instance => _instance;

    /// <summary>
    /// 加载 Editor、主菜单等「关卡 HUD 不沿用」的场景前调用，销毁上一场景带来的 DontDestroyOnLoad 实例；
    /// 否则新场景里的 <see cref="LevelUIManager"/> 会在 <see cref="Awake"/> 中被当作重复而销毁。
    /// </summary>
    public static void DestroySingletonInstanceIfAny()
    {
        if (_instance == null)
            return;
        Destroy(_instance.gameObject);
    }

    [Header("关卡完成")]
    [Tooltip("拖入「完成关」用的整块 UI 根物体（例如一个 Panel）。胜利时会 SetActive(true)；换场景或调用 HideLevelCompleteUI 时会关掉。")]
    [SerializeField] GameObject levelCompleteUI;

    [Header("暂停")]
    [Tooltip("拖入暂停界面根物体。激活时关卡操作输入会被忽略（见 IsGameplayInputBlocked）。")]
    [SerializeField] GameObject pauseUI;

    [Header("导航按钮")]
    [SerializeField] Button[] restartCurrentLevelButtons;
    [SerializeField] Button[] nextLevelButtons;
    [SerializeField] Button[] returnToMainMenuButtons;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        HideLevelCompleteUI();
        HidePauseUI();
        WireNavigationButtons();
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
        UnwireNavigationButtons();
    }

    void WireNavigationButtons()
    {
        AddClicks(restartCurrentLevelButtons, OnRestartButtonClicked);
        AddClicks(nextLevelButtons, OnNextLevelButtonClicked);
        AddClicks(returnToMainMenuButtons, OnReturnToMainMenuButtonClicked);
    }

    void UnwireNavigationButtons()
    {
        RemoveClicks(restartCurrentLevelButtons, OnRestartButtonClicked);
        RemoveClicks(nextLevelButtons, OnNextLevelButtonClicked);
        RemoveClicks(returnToMainMenuButtons, OnReturnToMainMenuButtonClicked);
    }

    static void AddClicks(Button[] buttons, UnityEngine.Events.UnityAction handler)
    {
        if (buttons == null || handler == null)
            return;

        foreach (var b in buttons)
        {
            if (b == null)
                continue;
            b.onClick.RemoveListener(handler);
            b.onClick.AddListener(handler);
        }
    }

    static void RemoveClicks(Button[] buttons, UnityEngine.Events.UnityAction handler)
    {
        if (buttons == null || handler == null)
            return;

        foreach (var b in buttons)
        {
            if (b == null)
                continue;
            b.onClick.RemoveListener(handler);
        }
    }

    /// <summary> 与 R 键一致：测试模式重启测试快照，否则重开当前场景（经 <see cref="GameSceneManager"/>）。 </summary>
    void OnRestartButtonClicked()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode)
        {
            var painter = FindFirstObjectByType<RuntimeTilemapEditPainter>();
            painter?.RestartPlaytestFromSnapshot();
            return;
        }

        GameSceneManager.Instance?.RestartCurrentScene();
    }

    /// <summary> 与过关后 Enter 一致：下一关或最后一关回主菜单（经 <see cref="GameSceneManager"/>）。 </summary>
    void OnNextLevelButtonClicked()
    {
        GameSceneManager.Instance?.LoadNextScene();
    }

    /// <summary> 返回主菜单（经 <see cref="GameSceneManager.LoadMainMenu"/>）。 </summary>
    void OnReturnToMainMenuButtonClicked()
    {
        GameSceneManager.Instance?.LoadMainMenu();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (RuntimeTilemapEditPainter.IsPlaytestMode)
            {
                var painter = FindFirstObjectByType<RuntimeTilemapEditPainter>();
                painter?.RestartPlaytestFromSnapshot();
            }
            else
                GameSceneManager.Instance?.RestartCurrentScene();
        }

        if (Input.GetKeyDown(KeyCode.Escape))
            HandleEscapeForPause();

        TryProceedToNextLevelOnEnterAfterWin();
    }

    /// <summary> 编辑器场景不参与「完成关 → Enter 下一关」快捷方式（与 HUD 在非关卡场景的隐藏约定一致）。 </summary>
    static bool IsExcludedEditorGameplayScene(string sceneName)
    {
        return !string.IsNullOrEmpty(sceneName) &&
               sceneName.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    void TryProceedToNextLevelOnEnterAfterWin()
    {
        if (levelCompleteUI == null || !levelCompleteUI.activeInHierarchy)
            return;

        var active = UnitySceneManagement.SceneManager.GetActiveScene();
        if (!active.IsValid())
            return;
        if (IsExcludedEditorGameplayScene(active.name ?? string.Empty))
            return;

        if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter))
            return;

        GameSceneManager.Instance?.LoadNextScene();
    }

    void HandleEscapeForPause()
    {
        if (levelCompleteUI != null && levelCompleteUI.activeInHierarchy)
            return;

        if (pauseUI == null)
            return;

        if (pauseUI.activeInHierarchy)
            HidePauseUI();
        else
            ShowPauseUI();
    }

    /// <summary>
    /// 暂停界面或关卡完成界面处于显示链上时为 true；游戏逻辑（如推箱子输入）应据此跳过。
    /// </summary>
    public bool IsGameplayInputBlocked =>
        (pauseUI != null && pauseUI.activeInHierarchy) ||
        (levelCompleteUI != null && levelCompleteUI.activeInHierarchy);

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
