using UnityEngine;

/// <summary>
/// 挂在关卡内 UI 根物体上：DontDestroyOnLoad。
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
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
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
