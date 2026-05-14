using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// Runtime：编辑模式（F1）下，工具为「光标 / 绘制 / 橡皮」三选一（默认光标）；绘制或橡皮时左键改 Tilemap。
/// 网格范围由 Inspector 固定，不随已铺瓦片变化。
/// </summary>
[DisallowMultipleComponent]
public sealed class RuntimeTilemapEditPainter : MonoBehaviour
{
    public static bool IsEditMode { get; private set; }

    /// <summary> 编辑地图时鼠标工具模式（默认 <see cref="RuntimeTilemapEditToolMode.Cursor"/>）。 </summary>
    public enum RuntimeTilemapEditToolMode
    {
        Cursor,
        Draw,
        Eraser,
    }

    [Header("引用")]
    [SerializeField] TilePaletteController palette;
    [SerializeField] Tilemap groundTilemap;
    [SerializeField] Tilemap objectsTilemap;
    [SerializeField] Camera worldCamera;

    [Tooltip("落笔或开关编辑后刷新关卡逻辑；不拖则在场景里查找。")]
    [SerializeField] TilemapSettings tilemapSettings;

    [Header("编辑模式")]
    [SerializeField] KeyCode toggleEditModeKey = KeyCode.F1;
    [SerializeField] bool startInEditMode;

    [Header("网格")]
    [FormerlySerializedAs("showGridInEditMode")]
    [SerializeField] bool showGridLines = true;
    [Tooltip("勾选时仅在编辑模式（F1）下显示网格；取消勾选则游玩时网格也一直显示。")]
    [SerializeField] bool gridVisibleOnlyInEditMode = true;
    [SerializeField] Color gridColor = new(1f, 1f, 1f, 0.35f);
    [Tooltip("沿相机朝向略抬高，减轻与瓦片 Z 重叠闪烁。")]
    [SerializeField] float gridZOffset = -0.02f;
    [SerializeField] int gridSortingOrder = 50;
    [Tooltip("网格覆盖的格子范围（与是否铺瓦无关）。x,y,z 为最小格坐标，后三项为 size。")]
    [FormerlySerializedAs("gridBoundsWhenNoTiles")]
    [SerializeField] BoundsInt fixedEditGridCellBounds = new(-8, -8, 0, 16, 16, 1);

    [Header("笔刷预览")]
    [SerializeField] bool showBrushTilePreview = true;
    [Range(0.05f, 1f)]
    [SerializeField] float brushPreviewAlpha = 0.45f;
    [Tooltip("橡皮模式下，鼠标下格子在该层有瓦片时显示半透明预览（提示将擦除此格）。")]
    [SerializeField] bool showEraserHoverPreview = true;
    [Range(0.05f, 1f)]
    [SerializeField] float eraserHoverPreviewAlpha = 0.45f;
    [Tooltip("橡皮叠在格内同一张 Sprite 上时的 RGB 乘色；(1,1,1) 会与底下瓦片糊成一片。")]
    [SerializeField] Color eraserHoverMultiplyOnTile = new(1f, 0.52f, 0.52f, 1f);
    [SerializeField] int brushPreviewSortingOffset = 40;

    [Header("绘制 / 光标 / 橡皮")]
    [Tooltip("为 true 时为绘制模式（与橡皮互斥）。")]
    [SerializeField] bool drawModeEnabled;
    [Tooltip("为 true 时为橡皮模式（与绘制互斥）。")]
    [SerializeField] bool eraserModeEnabled;

    /// <summary> 当前工具：未勾选绘制且未勾选橡皮时为光标（默认）。 </summary>
    public RuntimeTilemapEditToolMode CurrentToolMode =>
        eraserModeEnabled ? RuntimeTilemapEditToolMode.Eraser
        : drawModeEnabled ? RuntimeTilemapEditToolMode.Draw
        : RuntimeTilemapEditToolMode.Cursor;

    /// <summary> 供 UI「光标」按钮：不绘制、不擦除（默认进入编辑时应为此模式）。 </summary>
    public void SetCursorMode() => SetToolMode(RuntimeTilemapEditToolMode.Cursor);

    /// <summary> 三选一设置工具。 </summary>
    public void SetToolMode(RuntimeTilemapEditToolMode mode)
    {
        drawModeEnabled = mode == RuntimeTilemapEditToolMode.Draw;
        eraserModeEnabled = mode == RuntimeTilemapEditToolMode.Eraser;
    }

    /// <summary> 是否允许用当前笔刷在地图上绘制。 </summary>
    public bool DrawModeEnabled => drawModeEnabled;

    /// <summary> 是否为橡皮模式。 </summary>
    public bool EraserModeEnabled => eraserModeEnabled;

    /// <summary> 是否为光标模式（既不绘制也不擦除）。 </summary>
    public bool IsCursorMode => !drawModeEnabled && !eraserModeEnabled;

    /// <summary> 供 UI「绘制」按钮：开启绘制并关掉橡皮与光标。 </summary>
    public void SetDrawModeEnabled(bool enabled)
    {
        if (enabled)
            SetToolMode(RuntimeTilemapEditToolMode.Draw);
        else if (drawModeEnabled)
            SetToolMode(RuntimeTilemapEditToolMode.Cursor);
    }

    /// <summary> 供 UI「橡皮」按钮：开启橡皮并关掉绘制与光标。 </summary>
    public void SetEraserModeEnabled(bool enabled)
    {
        if (enabled)
            SetToolMode(RuntimeTilemapEditToolMode.Eraser);
        else if (eraserModeEnabled)
            SetToolMode(RuntimeTilemapEditToolMode.Cursor);
    }

    Mesh _gridMesh;
    MeshFilter _gridMeshFilter;
    MeshRenderer _gridMeshRenderer;
    Material _gridMaterial;
    BoundsInt _lastGridBounds;

    SpriteRenderer _brushPreviewRenderer;
    Material _brushPreviewMaterial;

    void Awake()
    {
        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();

        if (startInEditMode)
            IsEditMode = true;

        EnsureGridResources();
    }

    void OnDestroy()
    {
        if (IsEditMode)
            IsEditMode = false;

        if (_gridMaterial != null)
            Destroy(_gridMaterial);
        if (_gridMesh != null)
            Destroy(_gridMesh);
        if (_gridMeshFilter != null && _gridMeshFilter.gameObject != null)
            Destroy(_gridMeshFilter.gameObject);

        if (_brushPreviewMaterial != null)
            Destroy(_brushPreviewMaterial);
        if (_brushPreviewRenderer != null && _brushPreviewRenderer.gameObject != null)
            Destroy(_brushPreviewRenderer.gameObject);
    }

    void LateUpdate()
    {
        UpdateBrushPreview();

        EnsureGridResources();
        if (_gridMeshRenderer == null)
            return;

        if (!showGridLines || groundTilemap == null)
        {
            SetGridVisible(false);
            return;
        }

        if (gridVisibleOnlyInEditMode && !IsEditMode)
        {
            SetGridVisible(false);
            return;
        }

        var b = fixedEditGridCellBounds;
        if (b.size.x <= 0 || b.size.y <= 0)
        {
            SetGridVisible(false);
            return;
        }

        if (!BoundsIntEquals(b, _lastGridBounds) || !_gridMeshRenderer.enabled)
        {
            _lastGridBounds = b;
            RebuildGridMesh(b);
        }

        SetGridVisible(true);
    }

    static bool BoundsIntEquals(BoundsInt a, BoundsInt b) =>
        a.xMin == b.xMin && a.xMax == b.xMax && a.yMin == b.yMin && a.yMax == b.yMax && a.zMin == b.zMin && a.zMax == b.zMax;

    void EnsureGridResources()
    {
        if (_gridMesh != null)
            return;

        if (groundTilemap == null)
            return;

        _gridMesh = new Mesh { name = "EditModeGrid" };

        var gridGo = new GameObject("EditModeGridOverlay");
        gridGo.transform.SetParent(groundTilemap.transform, false);
        gridGo.transform.localPosition = Vector3.zero;
        gridGo.transform.localRotation = Quaternion.identity;
        gridGo.transform.localScale = Vector3.one;

        _gridMeshFilter = gridGo.AddComponent<MeshFilter>();
        _gridMeshFilter.sharedMesh = _gridMesh;

        _gridMeshRenderer = gridGo.AddComponent<MeshRenderer>();
        _gridMeshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _gridMeshRenderer.receiveShadows = false;
        _gridMeshRenderer.sortingOrder = gridSortingOrder;

        var groundRenderer = groundTilemap.GetComponent<TilemapRenderer>();
        if (groundRenderer != null)
        {
            _gridMeshRenderer.sortingLayerID = groundRenderer.sortingLayerID;
            _gridMeshRenderer.sortingOrder = groundRenderer.sortingOrder + gridSortingOrder;
        }

        var shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        _gridMaterial = new Material(shader) { color = gridColor, name = "EditModeGridMat" };
        if (_gridMaterial.HasProperty("_BaseColor"))
            _gridMaterial.SetColor("_BaseColor", gridColor);
        _gridMeshRenderer.sharedMaterial = _gridMaterial;

        SetGridVisible(false);
    }

    void SetGridVisible(bool visible)
    {
        if (_gridMeshRenderer != null)
            _gridMeshRenderer.enabled = visible;
    }

    void RebuildGridMesh(BoundsInt cellBounds)
    {
        EnsureGridResources();

        var grid = groundTilemap.layoutGrid;
        if (grid == null)
            return;

        var tm = groundTilemap.transform;
        var cam = worldCamera != null ? worldCamera : Camera.main;
        var bump = cam != null ? -cam.transform.forward * gridZOffset : new Vector3(0f, 0f, gridZOffset);

        var verts = new List<Vector3>(cellBounds.size.x * cellBounds.size.y * 8);
        var indices = new List<int>(cellBounds.size.x * cellBounds.size.y * 8);

        var idx = 0;
        for (var y = cellBounds.yMin; y < cellBounds.yMax; y++)
        {
            for (var x = cellBounds.xMin; x < cellBounds.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                GetCellCornersWorld(grid, cell, bump, out var ll, out var lr, out var ur, out var ul);

                void Seg(Vector3 a, Vector3 b)
                {
                    verts.Add(tm.InverseTransformPoint(a));
                    verts.Add(tm.InverseTransformPoint(b));
                    indices.Add(idx);
                    indices.Add(idx + 1);
                    idx += 2;
                }

                Seg(ll, lr);
                Seg(lr, ur);
                Seg(ur, ul);
                Seg(ul, ll);
            }
        }

        _gridMesh.Clear();
        if (verts.Count == 0)
            return;

        _gridMesh.SetVertices(verts);
        _gridMesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0, true);
        _gridMesh.RecalculateBounds();

        if (_gridMaterial != null)
            _gridMaterial.color = gridColor;
    }

    /// <summary>
    /// 用相邻格中心推算四角（兼容无 <c>GetCellCornerWorld</c> 的旧版 Unity）。
    /// </summary>
    static void GetCellCornersWorld(Grid grid, Vector3Int cell, Vector3 bump, out Vector3 ll, out Vector3 lr, out Vector3 ur, out Vector3 ul)
    {
        var center = grid.GetCellCenterWorld(cell);
        var nextX = grid.GetCellCenterWorld(new Vector3Int(cell.x + 1, cell.y, cell.z));
        var nextY = grid.GetCellCenterWorld(new Vector3Int(cell.x, cell.y + 1, cell.z));
        var halfX = (nextX - center) * 0.5f;
        var halfY = (nextY - center) * 0.5f;

        ll = center - halfX - halfY + bump;
        lr = center + halfX - halfY + bump;
        ul = center - halfX + halfY + bump;
        ur = center + halfX + halfY + bump;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleEditModeKey))
            IsEditMode = !IsEditMode;

        if (!IsEditMode)
            return;

        if (palette == null || groundTilemap == null || objectsTilemap == null)
            return;

        if (!drawModeEnabled && !eraserModeEnabled)
            return;

        if (!Input.GetMouseButton(0))
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null)
            return;

        if (!TryScreenToCell(cam, groundTilemap, Input.mousePosition, out var cell))
            return;

        ApplyBrushAtCell(cell);
    }

    static bool TryScreenToCell(Camera cam, Tilemap tm, Vector3 screenPosition, out Vector3Int cell)
    {
        cell = default;
        var z = tm.transform.position.z;
        var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, z));
        var ray = cam.ScreenPointToRay(screenPosition);
        if (!plane.Raycast(ray, out var dist))
            return false;

        var world = ray.GetPoint(dist);
        cell = tm.WorldToCell(world);
        return true;
    }

    void ApplyBrushAtCell(Vector3Int cell)
    {
        if ((!drawModeEnabled && !eraserModeEnabled) || palette == null)
            return;

        var assets = TileAssetSettings.Instance;

        TilePaletteBrush brush;
        if (eraserModeEnabled)
            brush = new TilePaletteBrush(palette.DisplayedLayer, null, true);
        else
            brush = palette.CurrentBrush;

        if (brush.Layer == TilePaletteLayer.Ground)
        {
            groundTilemap.SetTile(cell, brush.IsErase ? null : brush.Tile);
        }
        else
        {
            if (!brush.IsErase && assets != null && brush.Tile == assets.PlayerTile)
                RemovePlayerTilesExcept(cell);

            objectsTilemap.SetTile(cell, brush.IsErase ? null : brush.Tile);
        }

        tilemapSettings?.RefreshFromTilemaps();
    }

    void RemovePlayerTilesExcept(Vector3Int keepCell)
    {
        var assets = TileAssetSettings.Instance;
        if (assets == null)
            return;

        var player = assets.PlayerTile;
        var b = objectsTilemap.cellBounds;
        for (var y = b.yMin; y < b.yMax; y++)
        {
            for (var x = b.xMin; x < b.xMax; x++)
            {
                var c = new Vector3Int(x, y, 0);
                if (c == keepCell)
                    continue;
                if (objectsTilemap.GetTile(c) == player)
                    objectsTilemap.SetTile(c, null);
            }
        }
    }

    void EnsureBrushPreviewResources()
    {
        if (_brushPreviewRenderer != null || groundTilemap == null)
            return;

        var go = new GameObject("BrushTilePreview");
        var grid = groundTilemap.layoutGrid;
        go.transform.SetParent(grid != null ? grid.transform : groundTilemap.transform, false);

        _brushPreviewRenderer = go.AddComponent<SpriteRenderer>();
        _brushPreviewRenderer.enabled = false;
        _brushPreviewRenderer.maskInteraction = SpriteMaskInteraction.None;

        var sh = Shader.Find("Sprites/Default");
        if (sh == null)
            sh = Shader.Find("Unlit/Transparent");
        _brushPreviewMaterial = new Material(sh) { name = "BrushPreviewMat" };
        _brushPreviewMaterial.renderQueue = 3000;
        _brushPreviewRenderer.material = _brushPreviewMaterial;
    }

    void UpdateBrushPreview()
    {
        EnsureBrushPreviewResources();
        if (_brushPreviewRenderer == null)
            return;

        if (!IsEditMode || palette == null || groundTilemap == null)
        {
            SetBrushPreviewVisible(false);
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            SetBrushPreviewVisible(false);
            return;
        }

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null || !TryScreenToCell(cam, groundTilemap, Input.mousePosition, out var cell))
        {
            SetBrushPreviewVisible(false);
            return;
        }

        if (eraserModeEnabled)
        {
            if (!showEraserHoverPreview)
            {
                SetBrushPreviewVisible(false);
                return;
            }

            var sourceTm = palette.DisplayedLayer == TilePaletteLayer.Ground
                ? groundTilemap
                : objectsTilemap;
            if (sourceTm == null)
            {
                SetBrushPreviewVisible(false);
                return;
            }

            if (!sourceTm.HasTile(cell))
            {
                SetBrushPreviewVisible(false);
                return;
            }

            if (!TryGetSpriteAtCell(sourceTm, cell, out var sp) || sp == null)
            {
                SetBrushPreviewVisible(false);
                return;
            }

            var tint = new Color(
                eraserHoverMultiplyOnTile.r,
                eraserHoverMultiplyOnTile.g,
                eraserHoverMultiplyOnTile.b,
                eraserHoverPreviewAlpha);

            LayoutBrushPreviewAtCell(cell, sp, tint, cam, sourceTm);
            return;
        }

        if (!showBrushTilePreview || !drawModeEnabled)
        {
            SetBrushPreviewVisible(false);
            return;
        }

        var brush = palette.CurrentBrush;
        if (brush.IsErase)
        {
            SetBrushPreviewVisible(false);
            return;
        }

        var sprite = TryGetSpriteFromTileBase(brush.Tile);
        if (sprite == null)
        {
            SetBrushPreviewVisible(false);
            return;
        }

        LayoutBrushPreviewAtCell(cell, sprite, new Color(1f, 1f, 1f, brushPreviewAlpha), cam, groundTilemap);
    }

    void LayoutBrushPreviewAtCell(Vector3Int cell, Sprite sprite, Color tint, Camera cam, Tilemap sortingReferenceTilemap)
    {
        var grid = groundTilemap.layoutGrid;
        if (grid == null)
        {
            SetBrushPreviewVisible(false);
            return;
        }

        var bump = cam != null ? -cam.transform.forward * gridZOffset : new Vector3(0f, 0f, gridZOffset);
        GetCellCornersWorld(grid, cell, bump, out var ll, out var lr, out var ur, out var ul);

        var centerWorld = (ll + ur) * 0.5f;
        if (cam != null && tint.a < 0.999f)
            centerWorld -= cam.transform.forward * 0.03f;

        var cellW = (lr - ll).magnitude;
        var cellH = (ul - ll).magnitude;

        _brushPreviewRenderer.sprite = sprite;

        _brushPreviewRenderer.transform.SetParent(grid.transform, true);
        _brushPreviewRenderer.transform.SetPositionAndRotation(centerWorld, grid.transform.rotation);

        var ls = grid.transform.lossyScale;
        var sb = sprite.bounds;
        var sx = Mathf.Max(sb.size.x, 1e-5f);
        var sy = Mathf.Max(sb.size.y, 1e-5f);
        _brushPreviewRenderer.transform.localScale = new Vector3(
            Mathf.Max(cellW / (Mathf.Abs(ls.x) * sx), 0.01f),
            Mathf.Max(cellH / (Mathf.Abs(ls.y) * sy), 0.01f),
            1f);

        _brushPreviewRenderer.color = tint;

        var refTm = sortingReferenceTilemap != null ? sortingReferenceTilemap : groundTilemap;
        var refR = refTm.GetComponent<TilemapRenderer>();
        var rg = groundTilemap.GetComponent<TilemapRenderer>();
        var orderGround = 0;
        var orderObjects = 0;
        if (rg != null)
            orderGround = rg.sortingOrder;
        if (objectsTilemap != null)
        {
            var ro = objectsTilemap.GetComponent<TilemapRenderer>();
            if (ro != null)
                orderObjects = ro.sortingOrder;
        }

        if (refR != null)
        {
            _brushPreviewRenderer.sortingLayerID = refR.sortingLayerID;
            _brushPreviewRenderer.sortingOrder = refR.sortingOrder + brushPreviewSortingOffset;
        }
        else
        {
            _brushPreviewRenderer.sortingLayerID = rg != null ? rg.sortingLayerID : _brushPreviewRenderer.sortingLayerID;
            _brushPreviewRenderer.sortingOrder = Mathf.Max(orderGround, orderObjects) + brushPreviewSortingOffset;
        }

        SetBrushPreviewVisible(true);
    }

    /// <summary> 取 Tilemap 在该格用于预览的 Sprite；失败则退回 <see cref="Tile"/> 的 sprite。 </summary>
    static bool TryGetSpriteAtCell(Tilemap map, Vector3Int cell, out Sprite sprite)
    {
        sprite = null;
        if (map == null)
            return false;

        sprite = map.GetSprite(cell);
        if (sprite != null)
            return true;

        sprite = TryGetSpriteFromTileBase(map.GetTile(cell));
        return sprite != null;
    }

    static Sprite TryGetSpriteFromTileBase(TileBase tileBase)
    {
        if (tileBase == null)
            return null;
        return tileBase is Tile tile ? tile.sprite : null;
    }

    void SetBrushPreviewVisible(bool visible)
    {
        if (_brushPreviewRenderer != null)
            _brushPreviewRenderer.enabled = visible;
    }
}
