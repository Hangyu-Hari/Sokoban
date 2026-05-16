using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 地图编辑器内设置 UI：展开/收起面板；打开、保存、另存为关卡；开始测试按钮在测试中与「退出测试」切换；通过 <see cref="LevelUIManager.Instance"/> 在游戏对象上开关 HUD；返回主菜单通过 <see cref="GameSceneManager"/>。
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
    [Tooltip("保存：已有路径则覆盖；尚无路径则弹出保存对话框（与 Ctrl+S 首次保存一致）。另存为始终弹窗并可改路径。")]
    [SerializeField] Button saveLevelButton;
    [Tooltip("另存为：始终弹出保存对话框。")]
    [SerializeField] Button saveLevelAsButton;
    [Tooltip("未测试：进入测试；测试中：退出测试（文案与颜色会切换）。")]
    [SerializeField] Button startPlaytestButton;
    [Tooltip("开始测试按钮上的 TMP；可不拖，自动在按钮子级查找。")]
    [SerializeField] TextMeshProUGUI startPlaytestButtonLabelTmp;
    [SerializeField] RuntimeTilemapEditPainter tilemapEditPainter;
    [Tooltip("返回主菜单：先退出测试（若在测）；有未保存改动时弹出确认窗，否则直接加载主菜单")]
    [SerializeField] Button returnToMainMenuButton;
    [Tooltip("主菜单场景名（与 Build Settings / Start.unity 文件名一致，默认 Start）")]
    [SerializeField] string mainMenuSceneName = "Start";

    [Header("未保存确认")]
    [Tooltip("有未保存改动时显示；取消按钮可在 Inspector 里自行绑定关闭本物体。")]
    [SerializeField] GameObject unsavedChangesDialog;
    [Tooltip("是：走保存逻辑（首次保存会弹系统保存对话框），成功后再执行待办操作。")]
    [SerializeField] Button unsavedSaveBeforeLeaveButton;
    [Tooltip("否：不保存，直接执行待办操作（回主菜单或打开关卡）。")]
    [SerializeField] Button unsavedLeaveWithoutSavingButton;

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
    Action _unsavedPendingAction;

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
        if (returnToMainMenuButton != null)
            returnToMainMenuButton.onClick.AddListener(OnReturnToMainMenuClicked);
        if (unsavedSaveBeforeLeaveButton != null)
            unsavedSaveBeforeLeaveButton.onClick.AddListener(OnUnsavedSaveBeforeLeaveClicked);
        if (unsavedLeaveWithoutSavingButton != null)
            unsavedLeaveWithoutSavingButton.onClick.AddListener(OnUnsavedLeaveWithoutSavingClicked);
        HideUnsavedChangesDialog();
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
        TryRunWithUnsavedPrompt(OpenLevelFromFileDialog);
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

    void OnReturnToMainMenuClicked()
    {
        if (tilemapEditPainter != null && RuntimeTilemapEditPainter.IsPlaytestMode)
            tilemapEditPainter.ExitPlaytestToEditMode();

        TryRunWithUnsavedPrompt(LoadMainMenuScene);
    }

    void OnUnsavedLeaveWithoutSavingClicked()
    {
        HideUnsavedChangesDialog();
        RunPendingUnsavedAction();
    }

    void OnUnsavedSaveBeforeLeaveClicked()
    {
        HideUnsavedChangesDialog();
        if (tilemapEditPainter != null && !tilemapEditPainter.TrySaveCurrentLevelToFile())
        {
            ClearPendingUnsavedAction();
            return;
        }

        RunPendingUnsavedAction();
    }

    void TryRunWithUnsavedPrompt(Action action)
    {
        if (action == null)
            return;

        if (tilemapEditPainter != null && tilemapEditPainter.LevelDocumentIsDirty)
        {
            _unsavedPendingAction = action;
            ShowUnsavedChangesDialog();
            return;
        }

        action();
    }

    void RunPendingUnsavedAction()
    {
        var action = _unsavedPendingAction;
        ClearPendingUnsavedAction();
        action?.Invoke();
    }

    void ClearPendingUnsavedAction() => _unsavedPendingAction = null;

    void OpenLevelFromFileDialog()
    {
        if (tilemapEditPainter == null)
            return;
        tilemapEditPainter.OpenLevelFromFileDialog();
    }

    void ShowUnsavedChangesDialog()
    {
        if (unsavedChangesDialog != null)
            unsavedChangesDialog.SetActive(true);
    }

    void HideUnsavedChangesDialog()
    {
        if (unsavedChangesDialog != null)
            unsavedChangesDialog.SetActive(false);
    }

    /// <summary> 供取消按钮 OnClick：关弹窗并放弃待办（打开 / 回主菜单）。 </summary>
    public void OnUnsavedChangesDialogCancelled()
    {
        HideUnsavedChangesDialog();
        ClearPendingUnsavedAction();
    }

    void LoadMainMenuScene()
    {
        var name = string.IsNullOrWhiteSpace(mainMenuSceneName) ? "Start" : mainMenuSceneName.Trim();

        if (GameSceneManager.Instance != null)
            GameSceneManager.Instance.LoadScene(name);
        else
        {
            LevelUIManager.DestroySingletonInstanceIfAny();
            SceneManager.LoadScene(name, LoadSceneMode.Single);
        }
    }

    void Start()
    {
        HideUnsavedChangesDialog();
        EnsureLevelUiManagerDefaultHidden();
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
        if (returnToMainMenuButton != null)
            returnToMainMenuButton.onClick.RemoveListener(OnReturnToMainMenuClicked);
        if (unsavedSaveBeforeLeaveButton != null)
            unsavedSaveBeforeLeaveButton.onClick.RemoveListener(OnUnsavedSaveBeforeLeaveClicked);
        if (unsavedLeaveWithoutSavingButton != null)
            unsavedLeaveWithoutSavingButton.onClick.RemoveListener(OnUnsavedLeaveWithoutSavingClicked);
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
        var lm = LevelUIManager.Instance;
        if (lm == null)
            return;
        lm.gameObject.SetActive(active);
    }

    /// <summary> 刚进编辑场景且未测试时，关闭 <see cref="LevelUIManager"/> 常驻物体。 </summary>
    void EnsureLevelUiManagerDefaultHidden()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode)
            return;
        var lm = LevelUIManager.Instance;
        if (lm == null)
            return;
        lm.gameObject.SetActive(false);
    }

    void EnsurePlaytestButtonLabelTmp()
    {
        if (startPlaytestButtonLabelTmp != null || startPlaytestButton == null)
            return;
        startPlaytestButtonLabelTmp = startPlaytestButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }
}
