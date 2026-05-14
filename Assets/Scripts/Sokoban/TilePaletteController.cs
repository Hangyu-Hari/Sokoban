using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

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

    TilePaletteBrush _currentBrush;
    readonly List<TilePaletteBrush> _builtBrushes = new();

    /// <summary> 当前选中的笔刷；落笔脚本读取此值。 </summary>
    public TilePaletteBrush CurrentBrush => _currentBrush;

    /// <summary> 选中笔刷变化时触发。 </summary>
    public event Action<TilePaletteBrush> BrushChanged;

    void Start()
    {
        if (buildPaletteInStart)
            BuildPalette();
    }

    /// <summary> 清空 Content 并按 TileAssetSettings 重新生成条目。 </summary>
    public void BuildPalette()
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

        _builtBrushes.Clear();

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

        if (assets.PlayerTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Objects, assets.PlayerTile, false), "玩家");

        if (assets.BoxTile != null)
            AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Objects, assets.BoxTile, false), "箱子");

        AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Ground, null, true), "擦除 · 背景");
        AddPaletteRow(new TilePaletteBrush(TilePaletteLayer.Objects, null, true), "擦除 · 物体");

        if (_builtBrushes.Count > 0)
            SetCurrentBrush(_builtBrushes[0]);
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

        _builtBrushes.Add(brush);

        if (brush.IsErase)
            item.Setup((Sprite)null, displayName, () => SetCurrentBrush(brush));
        else
            item.Setup(brush.Tile, displayName, () => SetCurrentBrush(brush));
    }

    void SetCurrentBrush(TilePaletteBrush brush)
    {
        _currentBrush = brush;
        BrushChanged?.Invoke(brush);
    }
}
