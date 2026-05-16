using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary> 笔刷落在 Ground 还是 Objects。 </summary>
public enum TilePaletteLayer
{
    Ground,
    Objects,
}

/// <summary> 当前选中的调色板笔刷（供关卡编辑落笔使用）。 </summary>
public readonly struct TilePaletteBrush : IEquatable<TilePaletteBrush>
{
    public TilePaletteLayer Layer { get; }
    /// <summary> 非擦除时写入 Tilemap 的瓦片；擦除时忽略。 </summary>
    public TileBase Tile { get; }
    public bool IsErase { get; }

    public TilePaletteBrush(TilePaletteLayer layer, TileBase tile, bool isErase)
    {
        Layer = layer;
        Tile = tile;
        IsErase = isErase;
    }

    public bool Equals(TilePaletteBrush other) =>
        Layer == other.Layer && Tile == other.Tile && IsErase == other.IsErase;

    public override bool Equals(object obj) => obj is TilePaletteBrush other && Equals(other);

    public override int GetHashCode() => HashCode.Combine((int)Layer, Tile, IsErase);
}

/// <summary>
/// 从 <see cref="TileAssetSettings"/> 在运行时生成 ScrollView Content 下的 TileUI，并维护 <see cref="CurrentBrush"/>。
/// 可按「背景 / 物体」切换 Content 中展示的笔刷列表（<see cref="ShowGroundPalette"/> / <see cref="ShowObjectsPalette"/>）。
/// 可选：横向滑入/滑出调色板面板（与 <see cref="EditorSettings"/> 相同，改 <see cref="RectTransform.anchoredPosition"/> 的 X）；按钮文案：展开可见时为 <c>&gt;&gt;</c>，收起隐藏时为 <c>&lt;&lt;</c>。
/// </summary>
[DisallowMultipleComponent]
public sealed class TilePaletteController : MonoBehaviour
{
    [Header("引用")]
    [Tooltip("ScrollView 下的 Content（子物体将在此处生成）。")]
    [SerializeField] RectTransform content;

    [Tooltip("带 TilePaletteItem 的 TileUI 预制体。")]
    [SerializeField] GameObject tileUiPrefab;

    [Tooltip("不填则用 TileAssetSettings.Instance。")]
    [SerializeField] TileAssetSettings tileAssetsOverride;

    [Header("生成时机")]
    [SerializeField] bool buildPaletteInStart = true;

    [Tooltip("首次生成时默认展示背景层还是物体层笔刷。")]
    [SerializeField] TilePaletteLayer defaultPaletteLayer = TilePaletteLayer.Ground;

    [Header("层切换按钮（拖引用即可；运行时绑定 onClick，选中项略灰）")]
    [SerializeField] Button groundLayerButton;
    [SerializeField] Button objectsLayerButton;

    const string PaletteSlideLabelWhenExpandedVisible = ">>";
    const string PaletteSlideLabelWhenCollapsedHidden = "<<";

    [Header("调色板面板收起（与 EditorSettings 相同逻辑：anchoredPosition.x 在 showX / hideX 间动画切换）")]
    [Tooltip("点击后在展开与收起之间切换；展开可见时按钮为 >>，收起隐藏时为 <<。")]
    [SerializeField] Button paletteSlideToggleButton;
    [Tooltip("要横向滑入/滑出的调色板面板根（RectTransform）。")]
    [SerializeField] RectTransform paletteSlidePanel;
    [Tooltip("按钮上的 TMP；可不拖，会自动在按钮子级里查找 TextMeshProUGUI。")]
    [SerializeField] TextMeshProUGUI paletteSlideToggleLabelTmp;

    [Header("调色板滑轨 anchoredPosition.x")]
    [SerializeField] float paletteSlideShowX;
    [SerializeField] float paletteSlideHideX;

    [Tooltip("进入场景时调色板滑轨是否为展开（决定初始落在 showX 还是 hideX）。")]
    [SerializeField] bool paletteSlideExpandedOnStart = true;

    [Header("调色板滑轨过渡")]
    [SerializeField, Min(0.01f)] float paletteSlideTransitionDuration = 0.28f;
    [SerializeField] AnimationCurve paletteSlideTransitionEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    TilePaletteLayer _displayedLayer = TilePaletteLayer.Ground;
    TilePaletteBrush _currentBrush;
    readonly List<TilePaletteItem> _paletteItems = new();

    bool _paletteSlideExpanded;
    Coroutine _paletteSlideRoutine;

    /// <summary> 当前 Scroll 里展示的是哪一类（背景 / 物体）。 </summary>
    public TilePaletteLayer DisplayedLayer => _displayedLayer;

    /// <summary> 当前选中的笔刷；落笔脚本读取此值。 </summary>
    public TilePaletteBrush CurrentBrush => _currentBrush;

    /// <summary> 玩家是否已在列表里点选过瓦片（重建列表后需重新点选）。 </summary>
    public bool HasPaintBrushSelected => !_currentBrush.IsErase && _currentBrush.Tile != null;

    /// <summary> 选中笔刷变化时触发。 </summary>
    public event Action<TilePaletteBrush> BrushChanged;

    void Awake()
    {
        WireLayerToggleButtons();
        WirePaletteSlideToggleButton();
    }

    void OnDestroy()
    {
        UnwireLayerToggleButtons();
        UnwirePaletteSlideToggleButton();
    }

    void WireLayerToggleButtons()
    {
        if (groundLayerButton != null)
            groundLayerButton.onClick.AddListener(ShowGroundPalette);
        if (objectsLayerButton != null)
            objectsLayerButton.onClick.AddListener(ShowObjectsPalette);
    }

    void UnwireLayerToggleButtons()
    {
        if (groundLayerButton != null)
            groundLayerButton.onClick.RemoveListener(ShowGroundPalette);
        if (objectsLayerButton != null)
            objectsLayerButton.onClick.RemoveListener(ShowObjectsPalette);
    }

    void WirePaletteSlideToggleButton()
    {
        if (paletteSlideToggleButton != null)
            paletteSlideToggleButton.onClick.AddListener(OnPaletteSlideToggleClicked);
    }

    void UnwirePaletteSlideToggleButton()
    {
        if (paletteSlideToggleButton != null)
            paletteSlideToggleButton.onClick.RemoveListener(OnPaletteSlideToggleClicked);
    }

    void Start()
    {
        _displayedLayer = defaultPaletteLayer;

        EnsurePaletteSlideToggleLabelTmp();
        if (paletteSlidePanel != null)
        {
            _paletteSlideExpanded = paletteSlideExpandedOnStart;
            var p = paletteSlidePanel.anchoredPosition;
            p.x = _paletteSlideExpanded ? paletteSlideShowX : paletteSlideHideX;
            paletteSlidePanel.anchoredPosition = p;
        }

        ApplyPaletteSlideToggleButtonLabel();

        if (buildPaletteInStart)
            RebuildPalette();
        else
            RefreshLayerToggleButtonVisuals();
    }

    void OnPaletteSlideToggleClicked()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode)
            return;

        if (paletteSlidePanel == null)
            return;

        _paletteSlideExpanded = !_paletteSlideExpanded;
        var targetX = _paletteSlideExpanded ? paletteSlideShowX : paletteSlideHideX;
        ApplyPaletteSlideToggleButtonLabel();

        if (_paletteSlideRoutine != null)
            StopCoroutine(_paletteSlideRoutine);
        _paletteSlideRoutine = StartCoroutine(AnimatePaletteSlidePanelToX(targetX));
    }

    IEnumerator AnimatePaletteSlidePanelToX(float targetX)
    {
        var startX = paletteSlidePanel.anchoredPosition.x;
        var elapsed = 0f;
        var duration = paletteSlideTransitionDuration;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            var k = paletteSlideTransitionEase.Evaluate(t);
            var x = Mathf.LerpUnclamped(startX, targetX, k);
            var pos = paletteSlidePanel.anchoredPosition;
            pos.x = x;
            paletteSlidePanel.anchoredPosition = pos;
            yield return null;
        }

        var end = paletteSlidePanel.anchoredPosition;
        end.x = targetX;
        paletteSlidePanel.anchoredPosition = end;
        _paletteSlideRoutine = null;
    }

    void ApplyPaletteSlideToggleButtonLabel()
    {
        if (paletteSlideToggleLabelTmp == null)
            return;
        paletteSlideToggleLabelTmp.text = _paletteSlideExpanded
            ? PaletteSlideLabelWhenExpandedVisible
            : PaletteSlideLabelWhenCollapsedHidden;
    }

    void EnsurePaletteSlideToggleLabelTmp()
    {
        if (paletteSlideToggleLabelTmp != null || paletteSlideToggleButton == null)
            return;
        paletteSlideToggleLabelTmp = paletteSlideToggleButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    /// <summary> 供 UI「背景」按钮：Content 只显示 Ground 层笔刷（默认）。 </summary>
    public void ShowGroundPalette()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode)
            return;

        if (_displayedLayer == TilePaletteLayer.Ground)
            return;

        _displayedLayer = TilePaletteLayer.Ground;
        RefreshLayerToggleButtonVisuals();
        RebuildPalette();
    }

    /// <summary> 供 UI「物体」按钮：Content 只显示 Objects 层笔刷。 </summary>
    public void ShowObjectsPalette()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode)
            return;

        if (_displayedLayer == TilePaletteLayer.Objects)
            return;

        _displayedLayer = TilePaletteLayer.Objects;
        RefreshLayerToggleButtonVisuals();
        RebuildPalette();
    }

    /// <summary> 按当前 <see cref="_displayedLayer"/> 重新生成 Content（与按钮切换后调用）。 </summary>
    public void BuildPalette() => RebuildPalette();

    void RebuildPalette()
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode)
            return;

        if (content == null)
        {
            Debug.LogError("[TilePaletteController] Content 未赋值。", this);
            return;
        }

        if (tileUiPrefab == null)
        {
            Debug.LogError("[TilePaletteController] TileUI Prefab 未赋值。", this);
            return;
        }

        var assets = tileAssetsOverride != null ? tileAssetsOverride : TileAssetSettings.Instance;
        if (assets == null)
        {
            Debug.LogError("[TilePaletteController] 未找到 TileAssetSettings。", this);
            return;
        }

        for (var i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        _paletteItems.Clear();

        if (_displayedLayer == TilePaletteLayer.Ground)
            AddGroundPaletteEntries(assets);
        else
            AddObjectsPaletteEntries(assets);

        _currentBrush = default;
        RefreshPaletteTileEntryVisuals();
        RefreshLayerToggleButtonVisuals();
        BrushChanged?.Invoke(_currentBrush);
    }

    void AddGroundPaletteEntries(TileAssetSettings assets)
    {
        var wallIndex = 0;
        foreach (var t in assets.WallBaseTiles)
        {
            if (t == null)
                continue;
            wallIndex++;
            var brush = new TilePaletteBrush(TilePaletteLayer.Ground, t, false);
            AddPaletteRow(brush, $"底墙 {wallIndex}");
        }

        if (assets.WallCapTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Ground, assets.WallCapTile, false), "顶墙");

        var floorIndex = 0;
        foreach (var t in assets.FloorTiles)
        {
            if (t == null)
                continue;
            floorIndex++;
            var brush = new TilePaletteBrush(TilePaletteLayer.Ground, t, false);
            AddPaletteRow(brush, $"地板 {floorIndex}");
        }

        if (assets.GoalUncompletedTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Ground, assets.GoalUncompletedTile, false), "未完成目标点");

        if (assets.GoalCompletedTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Ground, assets.GoalCompletedTile, false), "已完成目标点");
    }

    void AddObjectsPaletteEntries(TileAssetSettings assets)
    {
        if (assets.PlayerTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Objects, assets.PlayerTile, false), "玩家");

        if (assets.BoxTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Objects, assets.BoxTile, false), "箱子");
    }

    void AddPaletteRow(TilePaletteBrush brush, string displayName)
    {
        var go = Instantiate(tileUiPrefab, content);
        var item = go.GetComponent<TilePaletteItem>();
        if (item == null)
        {
            Debug.LogError("[TilePaletteController] Prefab 上缺少 TilePaletteItem。", go);
            Destroy(go);
            return;
        }

        if (brush.IsErase)
            item.Setup((Sprite)null, displayName, brush, () => SetCurrentBrush(brush));
        else
            item.Setup(brush.Tile, displayName, brush, () => SetCurrentBrush(brush));

        _paletteItems.Add(item);
    }

    void SetCurrentBrush(TilePaletteBrush brush)
    {
        if (RuntimeTilemapEditPainter.IsPlaytestMode)
            return;

        _currentBrush = brush;
        RefreshPaletteTileEntryVisuals();
        BrushChanged?.Invoke(brush);
    }

    void RefreshPaletteTileEntryVisuals()
    {
        var has = HasPaintBrushSelected;
        for (var i = 0; i < _paletteItems.Count; i++)
        {
            var item = _paletteItems[i];
            if (item == null)
                continue;
            item.SetEntrySelected(has && _currentBrush.Equals(item.BoundBrush));
        }
    }

    void RefreshLayerToggleButtonVisuals()
    {
        ApplyToggleButtonSelectedLook(groundLayerButton, _displayedLayer == TilePaletteLayer.Ground);
        ApplyToggleButtonSelectedLook(objectsLayerButton, _displayedLayer == TilePaletteLayer.Objects);
    }

    static void ApplyToggleButtonSelectedLook(Button button, bool selected)
    {
        if (button == null)
            return;

        var c = button.colors;
        c.colorMultiplier = 1f;
        c.fadeDuration = 0f;
        if (selected)
        {
            c.normalColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            c.highlightedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            c.pressedColor = new Color(0.62f, 0.62f, 0.62f, 1f);
            c.selectedColor = c.normalColor;
            c.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
        }
        else
        {
            c.normalColor = Color.white;
            c.highlightedColor = new Color(0.95f, 0.95f, 0.95f, 1f);
            c.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            c.selectedColor = Color.white;
            c.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f);
        }

        button.colors = c;
    }
}
