using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 地图编辑器内设置 UI：展开/收起面板；打开、保存、另存为关卡；开始测试按钮在测试中与「退出测试」切换；<see cref="levelUIManagerRoot"/> 仅在测试时启用。
/// 展开时按钮文案为 <c>&lt;&lt;</c>，收起时为 <c>&gt;&gt;</c>。
/// </summary>
[DisallowMultipleComponent]
public sealed class EditorSettings : MonoBehaviour
{
    const string LabelWhenExpanded = "<<";
    const string LabelWhenCollapsed = ">>";

    [Header("引用")]
    [Tooltip("点击后在展开与收起之间切换。")]
    [SerializeField] Button togglePanelButton;
    [Tooltip("要横向滑入/滑出的面板根（RectTransform）。")]
    [SerializeField] RectTransform panelContent;
    [Tooltip("按钮上的 TMP 文案；可不拖，会自动在按钮子级里查找 TextMeshProUGUI。展开为 <<，收起为 >>。")]
    [FormerlySerializedAs("toggleLabelTmp")]
    [SerializeField] TextMeshProUGUI toggleButtonLabelTmp;

    [Tooltip("打开关卡 JSON（默认从 Assets/LevelFiles 选文件）；需 F1 编辑模式。")]
    [SerializeField] Button openLevelButton;
    [Tooltip("保存到当前已关联的 JSON 文件；无路径时请先另存为或 Ctrl+S 首次保存。")]
    [SerializeField] Button saveLevelButton;
    [Tooltip("另存为：始终弹出保存对话框。")]
    [SerializeField] Button saveLevelAsButton;
    [Tooltip("未测试：进入测试；测试中：退出测试（文案与颜色会切换）。")]
    [SerializeField] Button startPlaytestButton;
    [Tooltip("开始测试按钮上的 TMP；可不拖，自动在按钮子级查找。")]
    [SerializeField] TextMeshProUGUI startPlaytestButtonLabelTmp;
    [SerializeField] RuntimeTilemapEditPainter tilemapEditPainter;
    [Tooltip("挂有 LevelUIManager 的根物体（或整块关卡 HUD）；编辑模式下默认关闭，进入测试时为 true，退出测试为 false")]
    [SerializeField] GameObject levelUIManagerRoot;

    [Header("开始测试 / 退出测试 外观")]
    [SerializeField] string playtestStartLabel = "开始测试";
    [SerializeField] string playtestExitLabel = "退出测试";
    [SerializeField] Color playtestExitNormalColor = new(0.92f, 0.32f, 0.32f, 1f);
    [SerializeField] Color playtestExitHighlightedColor = new(1f, 0.45f, 0.45f, 1f);
    [SerializeField] Color playtestExitPressedColor = new(0.72f, 0.22f, 0.22f, 1f);

    [Header("面板 anchoredPosition.x")]
    [SerializeField] float showX;
    [SerializeField] float hideX;

    [Tooltip("进入场景时是否为展开状态（决定初始落在 showX 还是 hideX）。")]
    [SerializeField] bool expandedOnStart = true;

    [Header("过渡动画")]
    [SerializeField, Min(0.01f)] float transitionDuration = 0.28f;
    [SerializeField] AnimationCurve transitionEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    bool _expanded;
    Coroutine _transitionRoutine;
    ColorBlock _cachedStartPlaytestButtonColors;
    bool _cachedStartPlaytestButtonColorsStored;

    void OnEnable()
    {
        RuntimeTilemapEditPainter.PlaytestModeChanged += OnPlaytestModeChanged;
        CacheStartPlaytestButtonColorsIfNeeded();
        var inPlaytest = RuntimeTilemapEditPainter.IsPlaytestMode;
        ApplyPlaytestButtonVisual(inPlaytest);
        ApplyLevelUiManagerActive(inPlaytest);
    }

    void OnDisable()
    {
        RuntimeTilemapEditPainter.PlaytestModeChanged -= OnPlaytestModeChanged;
    }

    void Awake()
    {
        EnsureToggleButtonLabelTmp();
        EnsurePlaytestButtonLabelTmp();
        EnsureLevelUiManagerDefaultHidden();
        if (togglePanelButton != null)
            togglePanelButton.onClick.AddListener(OnTogglePanelClicked);
        if (openLevelButton != null && tilemapEditPainter != null)
            openLevelButton.onClick.AddListener(OnOpenLevelClicked);
        if (saveLevelButton != null && tilemapEditPainter != null)
            saveLevelButton.onClick.AddListener(OnSaveLevelClicked);
        if (saveLevelAsButton != null && tilemapEditPainter != null)
            saveLevelAsButton.onClick.AddListener(OnSaveLevelAsClicked);
        if (startPlaytestButton != null && tilemapEditPainter != null)
            startPlaytestButton.onClick.AddListener(OnStartPlaytestToggleClicked);
    }

    void OnPlaytestModeChanged(bool inPlaytest)
    {
        ApplyPlaytestButtonVisual(inPlaytest);
        ApplyLevelUiManagerActive(inPlaytest);
    }

    void OnStartPlaytestToggleClicked()
    {
        if (tilemapEditPainter == null)
            return;

        if (RuntimeTilemapEditPainter.IsPlaytestMode)
            tilemapEditPainter.ExitPlaytestToEditMode();
        else
            tilemapEditPainter.StartPlaytestFromCurrentTilemaps();

        ApplyPlaytestButtonVisual(RuntimeTilemapEditPainter.IsPlaytestMode);
    }

    void OnOpenLevelClicked()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode || tilemapEditPainter == null)
            return;
        tilemapEditPainter.OpenLevelFromFileDialog();
    }

    void OnSaveLevelClicked()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode || tilemapEditPainter == null)
            return;
        tilemapEditPainter.SaveCurrentLevelToFile();
    }

    void OnSaveLevelAsClicked()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode || tilemapEditPainter == null)
            return;
        tilemapEditPainter.SaveLevelAs();
    }

    void Start()
    {
        EnsureToggleButtonLabelTmp();
        EnsurePlaytestButtonLabelTmp();
        if (panelContent == null)
            return;

        _expanded = expandedOnStart;
        var p = panelContent.anchoredPosition;
        p.x = _expanded ? showX : hideX;
        panelContent.anchoredPosition = p;
        ApplyToggleButtonLabel();
        CacheStartPlaytestButtonColorsIfNeeded();
        ApplyPlaytestButtonVisual(RuntimeTilemapEditPainter.IsPlaytestMode);
    }

    void OnDestroy()
    {
        if (togglePanelButton != null)
            togglePanelButton.onClick.RemoveListener(OnTogglePanelClicked);
        if (openLevelButton != null)
            openLevelButton.onClick.RemoveListener(OnOpenLevelClicked);
        if (saveLevelButton != null)
            saveLevelButton.onClick.RemoveListener(OnSaveLevelClicked);
        if (saveLevelAsButton != null)
            saveLevelAsButton.onClick.RemoveListener(OnSaveLevelAsClicked);
        if (startPlaytestButton != null && tilemapEditPainter != null)
            startPlaytestButton.onClick.RemoveListener(OnStartPlaytestToggleClicked);
    }

    void CacheStartPlaytestButtonColorsIfNeeded()
    {
        if (_cachedStartPlaytestButtonColorsStored || startPlaytestButton == null)
            return;
        _cachedStartPlaytestButtonColors = startPlaytestButton.colors;
        _cachedStartPlaytestButtonColorsStored = true;
    }

    void ApplyPlaytestButtonVisual(bool inPlaytest)
    {
        EnsurePlaytestButtonLabelTmp();
        if (startPlaytestButtonLabelTmp != null)
            startPlaytestButtonLabelTmp.text = inPlaytest ? playtestExitLabel : playtestStartLabel;

        if (startPlaytestButton == null)
            return;

        CacheStartPlaytestButtonColorsIfNeeded();

        if (inPlaytest)
        {
            var c = startPlaytestButton.colors;
            c.normalColor = playtestExitNormalColor;
            c.highlightedColor = playtestExitHighlightedColor;
            c.pressedColor = playtestExitPressedColor;
            c.selectedColor = playtestExitNormalColor;
            startPlaytestButton.colors = c;
        }
        else if (_cachedStartPlaytestButtonColorsStored)
            startPlaytestButton.colors = _cachedStartPlaytestButtonColors;
    }

    void OnTogglePanelClicked()
    {
        if (panelContent == null)
            return;

        _expanded = !_expanded;
        var targetX = _expanded ? showX : hideX;
        ApplyToggleButtonLabel();

        if (_transitionRoutine != null)
            StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(AnimatePanelToX(targetX));
    }

    IEnumerator AnimatePanelToX(float targetX)
    {
        var startX = panelContent.anchoredPosition.x;
        var elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / transitionDuration);
            var k = transitionEase.Evaluate(t);
            var x = Mathf.LerpUnclamped(startX, targetX, k);
            var pos = panelContent.anchoredPosition;
            pos.x = x;
            panelContent.anchoredPosition = pos;
            yield return null;
        }

        var end = panelContent.anchoredPosition;
        end.x = targetX;
        panelContent.anchoredPosition = end;
        _transitionRoutine = null;
    }

    void ApplyToggleButtonLabel()
    {
        if (toggleButtonLabelTmp == null)
            return;
        toggleButtonLabelTmp.text = _expanded ? LabelWhenExpanded : LabelWhenCollapsed;
    }

    void EnsureToggleButtonLabelTmp()
    {
        if (toggleButtonLabelTmp != null || togglePanelButton == null)
            return;
        toggleButtonLabelTmp = togglePanelButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    void ApplyLevelUiManagerActive(bool active)
    {
        if (levelUIManagerRoot == null)
            return;
        levelUIManagerRoot.SetActive(active);
    }

    /// <summary> 刚进编辑场景或未测试时 HUD 应保持关闭（见 <see cref="levelUIManagerRoot"/>）。 </summary>
    void EnsureLevelUiManagerDefaultHidden()
    {
        if (levelUIManagerRoot == null || RuntimeTilemapEditPainter.IsPlaytestMode)
            return;
        levelUIManagerRoot.SetActive(false);
    }

    void EnsurePlaytestButtonLabelTmp()
    {
        if (startPlaytestButtonLabelTmp != null || startPlaytestButton == null)
            return;
        startPlaytestButtonLabelTmp = startPlaytestButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }
}
