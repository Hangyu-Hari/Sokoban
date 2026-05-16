using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// 地图编辑器内设置 UI：展开/收起面板；打开、保存、另存为关卡；开始测试（委托 <see cref="RuntimeTilemapEditPainter"/>）。
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
    [Tooltip("开始测试：按当前 Tilemap 状态进入游玩（退出 F1 编辑）；并关闭胜利/暂停界面。")]
    [SerializeField] Button startPlaytestButton;
    [SerializeField] RuntimeTilemapEditPainter tilemapEditPainter;

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

    void Awake()
    {
        EnsureToggleButtonLabelTmp();
        if (togglePanelButton != null)
            togglePanelButton.onClick.AddListener(OnTogglePanelClicked);
        if (openLevelButton != null && tilemapEditPainter != null)
            openLevelButton.onClick.AddListener(tilemapEditPainter.OpenLevelFromFileDialog);
        if (saveLevelButton != null && tilemapEditPainter != null)
            saveLevelButton.onClick.AddListener(tilemapEditPainter.SaveCurrentLevelToFile);
        if (saveLevelAsButton != null && tilemapEditPainter != null)
            saveLevelAsButton.onClick.AddListener(tilemapEditPainter.SaveLevelAs);
        if (startPlaytestButton != null && tilemapEditPainter != null)
            startPlaytestButton.onClick.AddListener(tilemapEditPainter.StartPlaytestFromCurrentTilemaps);
    }

    void Start()
    {
        EnsureToggleButtonLabelTmp();
        if (panelContent == null)
            return;

        _expanded = expandedOnStart;
        var p = panelContent.anchoredPosition;
        p.x = _expanded ? showX : hideX;
        panelContent.anchoredPosition = p;
        ApplyToggleButtonLabel();
    }

    void OnDestroy()
    {
        if (togglePanelButton != null)
            togglePanelButton.onClick.RemoveListener(OnTogglePanelClicked);
        if (openLevelButton != null && tilemapEditPainter != null)
            openLevelButton.onClick.RemoveListener(tilemapEditPainter.OpenLevelFromFileDialog);
        if (saveLevelButton != null && tilemapEditPainter != null)
            saveLevelButton.onClick.RemoveListener(tilemapEditPainter.SaveCurrentLevelToFile);
        if (saveLevelAsButton != null && tilemapEditPainter != null)
            saveLevelAsButton.onClick.RemoveListener(tilemapEditPainter.SaveLevelAs);
        if (startPlaytestButton != null && tilemapEditPainter != null)
            startPlaytestButton.onClick.RemoveListener(tilemapEditPainter.StartPlaytestFromCurrentTilemaps);
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
}
