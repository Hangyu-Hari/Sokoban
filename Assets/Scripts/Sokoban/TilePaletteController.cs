using System;
using System.Collections.Generic;
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

    TilePaletteLayer _displayedLayer = TilePaletteLayer.Ground;
    TilePaletteBrush _currentBrush;
    readonly List<TilePaletteItem> _paletteItems = new();

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
    }

    void OnDestroy()
    {
        UnwireLayerToggleButtons();
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

    void Start()
    {
        _displayedLayer = defaultPaletteLayer;
        if (buildPaletteInStart)
            RebuildPalette();
        else
            RefreshLayerToggleButtonVisuals();
    }

    /// <summary> 供 UI「背景」按钮：Content 只显示 Ground 层笔刷（默认）。 </summary>
    public void ShowGroundPalette()
    {
        _displayedLayer = TilePaletteLayer.Ground;
        RebuildPalette();
    }

    /// <summary> 供 UI「物体」按钮：Content 只显示 Objects 层笔刷。 </summary>
    public void ShowObjectsPalette()
    {
        _displayedLayer = TilePaletteLayer.Objects;
        RebuildPalette();
    }

    /// <summary> 按当前 <see cref="_displayedLayer"/> 重新生成 Content（与按钮切换后调用）。 </summary>
    public void BuildPalette() => RebuildPalette();

    void RebuildPalette()
    {
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
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Ground, assets.GoalUncompletedTile, false), "目标（空）");

        if (assets.GoalCompletedTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Ground, assets.GoalCompletedTile, false), "目标（完成）");
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
        c.fadeDuration = 0.08f;
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
