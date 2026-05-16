using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Runtime：编辑模式（F1）下，工具为「光标 / 绘制 / 橡皮」三选一；Ctrl+S 见保存逻辑；<see cref="SaveCurrentLevelToFile"/> / <see cref="SaveLevelAs"/> / <see cref="OpenLevelFromFileDialog"/> 供 UI；光标拖曳平移；Ctrl+滚轮缩放；可限制在编辑网格内；绘制或橡皮时左键改 Tilemap。无打开文件时标题为 <c>Unsaved</c>；自上次保存或打开后有实际编辑时 <see cref="LevelDocumentTitleText"/> 加 <c>*</c>（<see cref="LevelDocumentIsDirty"/> 同为 true）。
/// <see cref="StartPlaytestFromCurrentTilemaps"/> 会先拍编辑网格内瓦片快照，<see cref="ExitPlaytestToEditMode"/> 写回后再 <c>Refresh</c>；与是否保存 JSON 无关。
/// 编辑网格从格坐标 (0,0,0) 起，仅配置宽高格数；开局将相机对准该网格世界中心（见 <see cref="centerCameraOnEditGridAtStart"/>）。
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class RuntimeTilemapEditPainter : MonoBehaviour
{
    public static bool IsEditMode { get; private set; }

    /// <summary> 由 <see cref="StartPlaytestFromCurrentTilemaps"/> / <see cref="ExitPlaytestToEditMode"/> 更新；供 UI 同步「开始测试 / 退出测试」外观。 </summary>
    public static event Action<bool> PlaytestModeChanged;

    /// <summary> 由 <see cref="StartPlaytestFromCurrentTilemaps"/> 置 true；为 true 时禁止一切地图编辑 UI/快捷键，直至 <see cref="ExitPlaytestToEditMode"/>（或 F1）。 </summary>
    public static bool IsPlaytestMode { get; private set; }

    static void SetPlaytestMode(bool value)
    {
        if (IsPlaytestMode == value)
            return;
        IsPlaytestMode = value;
        PlaytestModeChanged?.Invoke(value);
    }

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
    [Tooltip("编辑模式（F1）开启时：E 橡皮、B 绘制、S 光标。")]
    [SerializeField] bool toolHotkeysEnabled = true;
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
    [Tooltip("编辑网格宽高（格数）；格范围固定为 x∈[0,width)、y∈[0,height)、z 单层。")]
    [SerializeField] Vector2Int fixedEditGridCellCount = new(16, 16);

    [Tooltip("开局（Start）将相机 XY 对准编辑网格的世界包围盒中心，保留相机原 Z。晚于默认脚本执行，以便覆盖 TilemapSettings 的初次对相机。")]
    [SerializeField] bool centerCameraOnEditGridAtStart = true;

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

    [Header("光标模式 · 拖曳平移相机")]
    [Tooltip("编辑模式 + 光标工具时：按住鼠标在地图上拖曳，平移相机（与鼠标同向「抓地图」手感）。")]
    [SerializeField] bool cursorDragPanCameraEnabled = true;
    [Tooltip("0=左键，1=右键，2=中键。")]
    [Range(0, 2)]
    [SerializeField] int cursorPanMouseButton;
    [Tooltip("编辑模式（F1）下将相机限制在「网格」世界范围内，避免看到编辑网格外的空白。")]
    [SerializeField] bool clampCameraToEditGridBounds = true;

    [Header("编辑模式 · Ctrl 滚轮缩放")]
    [Tooltip("F1 编辑模式下按住 Ctrl 并滚动滚轮，调整正交相机 Orthographic Size。")]
    [SerializeField] bool editModeCtrlScrollZoomEnabled = true;
    [Tooltip("相对开局时记录的 Orthographic Size：最小倍率（正交 Size 的下限 = 参考 × 本值）。数值越小允许放得越大（例如 0.2）。")]
    [SerializeField, Min(0.05f)] float orthographicZoomMinMultiplier = 0.25f;
    [Tooltip("相对开局时记录的 Orthographic Size：最大倍率（正交 Size 的上限 = 参考 × 本值）。数值越大允许缩得越远（例如 4）。")]
    [SerializeField, Min(0.05f)] float orthographicZoomMaxMultiplier = 4f;
    [Tooltip("滚轮一步改变 Size 的力度（配合 Input.GetAxis(\"Mouse ScrollWheel\")）。")]
    [SerializeField, Min(0.01f)] float orthographicZoomScrollSensitivity = 1.25f;

    [Header("工具模式按钮（拖引用即可；运行时绑定 onClick，选中项略灰）")]
    [SerializeField] Button cursorToolButton;
    [SerializeField] Button drawToolButton;
    [SerializeField] Button eraserToolButton;

    [Header("关卡文件名 UI（可选，可拖多个 TMP：有关联文件时为文件名；否则为 Unsaved；未落盘或未保存改动时加 *)")]
    [SerializeField] TextMeshProUGUI[] levelDocumentTitleLabels;

    /// <summary> 当前工具：未勾选绘制且未勾选橡皮时为光标（默认）。 </summary>
    public RuntimeTilemapEditToolMode CurrentToolMode =>
        eraserModeEnabled ? RuntimeTilemapEditToolMode.Eraser
        : drawModeEnabled ? RuntimeTilemapEditToolMode.Draw
        : RuntimeTilemapEditToolMode.Cursor;

    /// <summary> 供 UI「光标」按钮，或与快捷键 S。 </summary>
    public void SetCursorMode() => SetToolMode(RuntimeTilemapEditToolMode.Cursor);

    /// <summary> 三选一设置工具。 </summary>
    public void SetToolMode(RuntimeTilemapEditToolMode mode)
    {
        if (IsPlaytestMode)
            return;
        drawModeEnabled = mode == RuntimeTilemapEditToolMode.Draw;
        eraserModeEnabled = mode == RuntimeTilemapEditToolMode.Eraser;
        RefreshToolModeButtonVisuals();
    }

    /// <summary> 供 UI「绘制」按钮，或与快捷键 B。 </summary>
    public void SetDrawMode() => SetToolMode(RuntimeTilemapEditToolMode.Draw);

    /// <summary> 供 UI「橡皮」按钮，或与快捷键 E。 </summary>
    public void SetEraserMode() => SetToolMode(RuntimeTilemapEditToolMode.Eraser);

    /// <summary> 是否允许用当前笔刷在地图上绘制。 </summary>
    public bool DrawModeEnabled => drawModeEnabled;

    /// <summary> 是否为橡皮模式。 </summary>
    public bool EraserModeEnabled => eraserModeEnabled;

    /// <summary> 是否为光标模式（既不绘制也不擦除）。 </summary>
    public bool IsCursorMode => !drawModeEnabled && !eraserModeEnabled;

    Mesh _gridMesh;
    MeshFilter _gridMeshFilter;
    MeshRenderer _gridMeshRenderer;
    Material _gridMaterial;
    BoundsInt _lastGridBounds;

    SpriteRenderer _brushPreviewRenderer;
    Material _brushPreviewMaterial;

    bool _cursorPanDragging;
    Vector3 _cursorPanLastScreen;
    float _orthographicZoomReferenceSize;
    /// <summary> 当前关卡 JSON 路径；空表示尚未通过打开/另存为/Ctrl+S 首次落盘。 </summary>
    string _activeLevelSavePath;

    bool _levelDocumentDirty;
    string _lastPublishedLevelDocumentTitle;
    float _levelMaxOrthographicSize = SokobanLevelSaveData.DefaultMaxOrthographicSize;

    /// <summary> 点击「开始测试」前在 <see cref="FixedEditGridCellBounds"/> 内拍的瓦片快照；退出测试时写回（与 <c>GetTilesBlock</c> 相同的一维布局）。 </summary>
    BoundsInt _playtestSnapshotBounds;
    TileBase[] _playtestSnapshotGround;
    TileBase[] _playtestSnapshotObjects;
    bool _hasPlaytestTileSnapshot;

    const string NoOpenFileDisplayName = "Unsaved";

    /// <summary> 供 UI 显示：当前是否已有「保存目标」文件路径。 </summary>
    public bool HasActiveLevelSavePath => !string.IsNullOrEmpty(_activeLevelSavePath);

    /// <summary> 相对磁盘：自上次成功保存或打开后，编辑区是否还有未写入的更改。 </summary>
    public bool LevelDocumentIsDirty => _levelDocumentDirty;

    /// <summary> 供标题栏显示：有关联保存路径时为文件名；否则为 <c>Unsaved</c>；未落盘或未保存更改时后缀 <c>*</c>。 </summary>
    public string LevelDocumentTitleText => BuildLevelDocumentTitleText();

    /// <summary> 标题文案变化时触发（与 <see cref="levelDocumentTitleLabels"/> 同步刷新）。 </summary>
    public event Action OnLevelDocumentTitleChanged;

    /// <summary> 关卡 JSON 中的最大镜头正交 Size；默认 <see cref="SokobanLevelSaveData.DefaultMaxOrthographicSize"/>。 </summary>
    public float LevelMaxOrthographicSize => _levelMaxOrthographicSize;

    /// <summary> <see cref="LevelMaxOrthographicSize"/> 变化时（含打开关卡）。 </summary>
    public event Action OnLevelMaxOrthographicSizeChanged;

    /// <summary> 编辑用固定网格：最小角 <c>(0,0,0)</c>，宽高见 <see cref="fixedEditGridCellCount"/>。 </summary>
    public BoundsInt FixedEditGridCellBounds
    {
        get
        {
            var w = Mathf.Max(1, fixedEditGridCellCount.x);
            var h = Mathf.Max(1, fixedEditGridCellCount.y);
            return new BoundsInt(0, 0, 0, w, h, 1);
        }
    }

    void ClearPlaytestTileSnapshot()
    {
        _playtestSnapshotGround = null;
        _playtestSnapshotObjects = null;
        _hasPlaytestTileSnapshot = false;
    }

    static TileBase[] CloneTilesBlock(Tilemap tm, BoundsInt bounds)
    {
        if (tm == null)
            return null;

        var src = tm.GetTilesBlock(bounds);
        if (src == null || src.Length == 0)
            return null;

        var dst = new TileBase[src.Length];
        Array.Copy(src, dst, src.Length);
        return dst;
    }

    void CapturePlaytestTileSnapshot()
    {
        ClearPlaytestTileSnapshot();
        if (groundTilemap == null || objectsTilemap == null)
            return;

        var b = FixedEditGridCellBounds;
        if (b.size.x <= 0 || b.size.y <= 0 || b.size.z <= 0)
            return;

        _playtestSnapshotGround = CloneTilesBlock(groundTilemap, b);
        _playtestSnapshotObjects = CloneTilesBlock(objectsTilemap, b);
        if (_playtestSnapshotGround == null || _playtestSnapshotObjects == null)
        {
            ClearPlaytestTileSnapshot();
            return;
        }

        _playtestSnapshotBounds = b;
        _hasPlaytestTileSnapshot = true;
    }

    void ApplyPlaytestSnapshotTilesToMaps()
    {
        if (!_hasPlaytestTileSnapshot || groundTilemap == null || objectsTilemap == null)
            return;

        groundTilemap.SetTilesBlock(_playtestSnapshotBounds, _playtestSnapshotGround);
        objectsTilemap.SetTilesBlock(_playtestSnapshotBounds, _playtestSnapshotObjects);
    }

    void RestorePlaytestTileSnapshot()
    {
        ApplyPlaytestSnapshotTilesToMaps();
        ClearPlaytestTileSnapshot();
    }

    /// <summary>
    /// 仍在测试中时：用开始测试时的快照重置盘面并 <see cref="TilemapSettings.RefreshFromTilemaps"/>，不整场景 <c>LoadScene</c>（供 R 键等）。
    /// </summary>
    public void RestartPlaytestFromSnapshot()
    {
        if (!IsPlaytestMode || !_hasPlaytestTileSnapshot)
            return;

        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();

        ApplyPlaytestSnapshotTilesToMaps();
        tilemapSettings?.RefreshFromTilemaps(applyCameraDuringEditMode: true);

        var ui = LevelUIManager.Instance;
        if (ui != null)
        {
            ui.HideLevelCompleteUI();
            ui.HidePauseUI();
        }
    }

    string BuildLevelDocumentTitleText()
    {
        var baseName = string.IsNullOrEmpty(_activeLevelSavePath)
            ? NoOpenFileDisplayName
            : Path.GetFileName(_activeLevelSavePath);
        return _levelDocumentDirty ? baseName + "*" : baseName;
    }

    void MarkLevelDocumentDirty()
    {
        if (_levelDocumentDirty)
            return;
        _levelDocumentDirty = true;
        RefreshLevelDocumentTitleUi();
    }

    void ClearLevelDocumentDirty()
    {
        _levelDocumentDirty = false;
        RefreshLevelDocumentTitleUi();
    }

    void RefreshLevelDocumentTitleUi()
    {
        var t = BuildLevelDocumentTitleText();
        if (levelDocumentTitleLabels != null)
        {
            for (var i = 0; i < levelDocumentTitleLabels.Length; i++)
            {
                var tmp = levelDocumentTitleLabels[i];
                if (tmp != null)
                    tmp.text = t;
            }
        }

        if (t != _lastPublishedLevelDocumentTitle)
        {
            _lastPublishedLevelDocumentTitle = t;
            OnLevelDocumentTitleChanged?.Invoke();
        }
    }

    void OnValidate()
    {
        fixedEditGridCellCount.x = Mathf.Max(1, fixedEditGridCellCount.x);
        fixedEditGridCellCount.y = Mathf.Max(1, fixedEditGridCellCount.y);

        if (orthographicZoomMinMultiplier > orthographicZoomMaxMultiplier)
            (orthographicZoomMinMultiplier, orthographicZoomMaxMultiplier) =
                (orthographicZoomMaxMultiplier, orthographicZoomMinMultiplier);
    }

    void Awake()
    {
        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();

        if (startInEditMode)
            IsEditMode = true;

        WireToolModeButtons();
        EnsureGridResources();
        RefreshToolModeButtonVisuals();
    }

    void Start()
    {
        var cam = worldCamera != null ? worldCamera : Camera.main;

        if (centerCameraOnEditGridAtStart && cam != null && groundTilemap != null)
        {
            if (TryGetFixedEditGridPlaneMinMax(out var minX, out var maxX, out var minY, out var maxY))
            {
                var cx = (minX + maxX) * 0.5f;
                var cy = (minY + maxY) * 0.5f;
                var p = cam.transform.position;
                cam.transform.position = new Vector3(cx, cy, p.z);

                if (clampCameraToEditGridBounds && cam.orthographic)
                    ClampCameraToFixedEditGrid(cam);
            }
        }

        if (cam != null && cam.orthographic)
            _orthographicZoomReferenceSize = Mathf.Max(1e-4f, cam.orthographicSize);

        RefreshLevelDocumentTitleUi();
        ApplyLevelMaxOrthographicSizeToTilemapSettings();
    }

    /// <summary> 供编辑器 InputField：修改最大镜头并标记未保存。 </summary>
    public void SetLevelMaxOrthographicSize(float value)
    {
        var clamped = Mathf.Max(0.1f, value);
        if (Mathf.Approximately(_levelMaxOrthographicSize, clamped))
            return;

        _levelMaxOrthographicSize = clamped;
        MarkLevelDocumentDirty();
        ApplyLevelMaxOrthographicSizeToTilemapSettings();
        OnLevelMaxOrthographicSizeChanged?.Invoke();
    }

    public void ApplyLevelMaxOrthographicSizeToTilemapSettings()
    {
        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();
        tilemapSettings?.SetMaxOrthographicSize(_levelMaxOrthographicSize);
    }

    void WireToolModeButtons()
    {
        if (cursorToolButton != null)
            cursorToolButton.onClick.AddListener(SetCursorMode);
        if (drawToolButton != null)
            drawToolButton.onClick.AddListener(SetDrawMode);
        if (eraserToolButton != null)
            eraserToolButton.onClick.AddListener(SetEraserMode);
    }

    void UnwireToolModeButtons()
    {
        if (cursorToolButton != null)
            cursorToolButton.onClick.RemoveListener(SetCursorMode);
        if (drawToolButton != null)
            drawToolButton.onClick.RemoveListener(SetDrawMode);
        if (eraserToolButton != null)
            eraserToolButton.onClick.RemoveListener(SetEraserMode);
    }

    void OnDestroy()
    {
        UnwireToolModeButtons();

        ClearPlaytestTileSnapshot();

        if (IsEditMode)
            IsEditMode = false;
        if (IsPlaytestMode)
            SetPlaytestMode(false);

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

        var b = FixedEditGridCellBounds;
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

        // 默认 Mesh 为 UInt16 索引，顶点数 >65535 时线框会截断/不显示；大编辑范围需 32 位索引。
        _gridMesh.indexFormat = verts.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

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
        {
            if (IsPlaytestMode)
                ExitPlaytestToEditMode();
            else
                IsEditMode = !IsEditMode;
        }

        if (IsEditMode)
            TrySaveEditLevelIfHotkey();

        if (toolHotkeysEnabled && IsEditMode)
        {
            if (Input.GetKeyDown(KeyCode.E))
                SetEraserMode();
            if (Input.GetKeyDown(KeyCode.B))
                SetDrawMode();
            if (Input.GetKeyDown(KeyCode.S)
                && !Input.GetKey(KeyCode.LeftControl)
                && !Input.GetKey(KeyCode.RightControl))
                SetCursorMode();
        }

        UpdateCursorDragPanCamera();
        UpdateEditModeCtrlOrthographicZoom();

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

    void TrySaveEditLevelIfHotkey()
    {
        if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            return;
        if (!Input.GetKeyDown(KeyCode.S))
            return;

        if (!TryGetLevelSaveContext(out var assets))
            return;

        if (string.IsNullOrEmpty(_activeLevelSavePath))
        {
            if (!LevelSavePathPicker.TryPickSaveJsonPath(out var picked))
                return;
            TrySaveLevelToPath(assets, picked);
        }
        else
            TrySaveLevelToPath(assets, _activeLevelSavePath);
    }

    /// <summary> 供 UI「保存」：有已关联路径则直接写入；尚无路径则与 Ctrl+S 相同，弹出保存对话框后落盘并记住路径。 </summary>
    public void SaveCurrentLevelToFile() => TrySaveCurrentLevelToFile();

    /// <summary>
    /// 与 <see cref="SaveCurrentLevelToFile"/> 相同；成功落盘返回 true。
    /// 用户取消保存对话框、缺少引用或写入失败时返回 false。
    /// </summary>
    public bool TrySaveCurrentLevelToFile()
    {
        if (IsPlaytestMode)
            return false;

        if (!IsEditMode)
        {
            Debug.LogWarning("[Sokoban] 请先按 F1 进入编辑模式后再保存。", this);
            return false;
        }

        if (!TryGetLevelSaveContext(out var assets))
            return false;

        if (string.IsNullOrEmpty(_activeLevelSavePath))
        {
            if (!LevelSavePathPicker.TryPickSaveJsonPath(out var picked))
                return false;
            return TrySaveLevelToPath(assets, picked);
        }

        return TrySaveLevelToPath(assets, _activeLevelSavePath);
    }

    /// <summary> 供 UI「另存为」：始终弹出保存对话框，成功后切换为当前保存路径。 </summary>
    public void SaveLevelAs()
    {
        if (IsPlaytestMode)
            return;

        if (!IsEditMode)
        {
            Debug.LogWarning("[Sokoban] 请先按 F1 进入编辑模式后再另存为。", this);
            return;
        }

        if (!TryGetLevelSaveContext(out var assets))
            return;

        if (!LevelSavePathPicker.TryPickSaveJsonPath(out var path))
            return;

        TrySaveLevelToPath(assets, path);
    }

    bool TryGetLevelSaveContext(out TileAssetSettings assets)
    {
        assets = TileAssetSettings.Instance;
        if (assets == null || groundTilemap == null || objectsTilemap == null)
        {
            Debug.LogWarning("[Sokoban] 保存失败：缺少 Tilemap 或 TileAssetSettings。", this);
            return false;
        }

        return true;
    }

    bool TrySaveLevelToPath(TileAssetSettings assets, string path)
    {
        if (!SokobanLevelSaveFile.TrySave(
                groundTilemap,
                objectsTilemap,
                FixedEditGridCellBounds,
                assets,
                path,
                _levelMaxOrthographicSize,
                out var err))
        {
            Debug.LogWarning("[Sokoban] 保存失败：" + err, this);
            return false;
        }

        _activeLevelSavePath = path;
        Debug.Log("[Sokoban] 已保存关卡到 " + path, this);
        ClearLevelDocumentDirty();
        return true;
    }

    /// <summary>
    /// 退出「开始测试」：先把编辑网格内瓦片恢复为点击「开始测试」前的快照，再刷新关卡逻辑，回到可编辑（F1 开启），并关闭胜利/暂停界面。
    /// </summary>
    public void ExitPlaytestToEditMode()
    {
        if (!IsPlaytestMode)
            return;

        RestorePlaytestTileSnapshot();

        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();
        tilemapSettings?.RefreshFromTilemaps(applyCameraDuringEditMode: false);

        SetPlaytestMode(false);
        IsEditMode = true;
        _cursorPanDragging = false;

        var ui = LevelUIManager.Instance;
        if (ui != null)
        {
            ui.HideLevelCompleteUI();
            ui.HidePauseUI();
        }

        RefreshToolModeButtonVisuals();
    }

    /// <summary> 能否开始测试；<paramref name="userMessage"/> 为面向玩家的简短说明（中文）。 </summary>
    public bool TryGetPlaytestStartUserMessage(out string userMessage)
    {
        userMessage = null;

        if (groundTilemap == null || objectsTilemap == null)
        {
            userMessage = "地图层未配置";
            return false;
        }

        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();

        if (tilemapSettings == null)
        {
            userMessage = "关卡组件未就绪";
            return false;
        }

        if (!tilemapSettings.TryValidateLevelForPlaytest(out var technicalError))
        {
            userMessage = PlaytestStartUserMessages.FromTechnicalError(technicalError);
            return false;
        }

        var b = FixedEditGridCellBounds;
        if (b.size.x <= 0 || b.size.y <= 0)
        {
            userMessage = "编辑网格无效";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 供 UI「开始测试」：在 <see cref="FixedEditGridCellBounds"/> 内拍下当前瓦片快照后，按 Tilemap 进入游玩（<see cref="IsPlaytestMode"/>）。
    /// 退出测试时快照会写回，与是否保存 JSON 无关。解析失败时不进入测试并丢弃快照。
    /// </summary>
    public void StartPlaytestFromCurrentTilemaps()
    {
        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();

        if (tilemapSettings == null)
        {
            Debug.LogWarning("[Sokoban] 未找到 TilemapSettings，无法开始测试。", this);
            return;
        }

        CapturePlaytestTileSnapshot();
        if (!_hasPlaytestTileSnapshot)
        {
            Debug.LogWarning("[Sokoban] 无法拍下测试前瓦片快照（请检查 Ground/Objects Tilemap 是否已赋值）。", this);
            return;
        }

        if (!tilemapSettings.RefreshFromTilemaps(applyCameraDuringEditMode: true))
        {
            ClearPlaytestTileSnapshot();
            Debug.LogWarning("[Sokoban] 无法开始测试：当前地图不满足游玩条件（见上一条警告）。", this);
            return;
        }

        SetPlaytestMode(true);
        IsEditMode = false;
        _cursorPanDragging = false;
        _lastGridBounds = default;

        var ui = LevelUIManager.Instance;
        if (ui != null)
        {
            ui.HideLevelCompleteUI();
            ui.HidePauseUI();
        }

        RefreshToolModeButtonVisuals();
    }

    /// <summary> 供 UI「打开关卡」按钮：从 JSON 读入当前编辑网格；需已在编辑模式（F1）。 </summary>
    public void OpenLevelFromFileDialog()
    {
        if (IsPlaytestMode)
            return;

        if (!IsEditMode)
        {
            Debug.LogWarning("[Sokoban] 请先按 F1 进入编辑模式后再打开关卡。", this);
            return;
        }

        var assets = TileAssetSettings.Instance;
        if (assets == null || groundTilemap == null || objectsTilemap == null)
        {
            Debug.LogWarning("[Sokoban] 打开失败：缺少 Tilemap 或 TileAssetSettings。", this);
            return;
        }

        if (!LevelSavePathPicker.TryPickOpenJsonPath(out var path))
            return;

        if (!SokobanLevelSaveFile.TryLoad(
                path,
                groundTilemap,
                objectsTilemap,
                FixedEditGridCellBounds,
                assets,
                out var loadedMaxOrtho,
                out var err))
        {
            Debug.LogWarning("[Sokoban] 打开关卡失败：" + err, this);
            return;
        }

        _levelMaxOrthographicSize = loadedMaxOrtho;
        ApplyLevelMaxOrthographicSizeToTilemapSettings();
        OnLevelMaxOrthographicSizeChanged?.Invoke();

        tilemapSettings?.RefreshFromTilemaps(applyCameraDuringEditMode: true);
        _lastGridBounds = default;
        _activeLevelSavePath = path;
        Debug.Log("[Sokoban] 已打开关卡：" + path, this);
        ClearLevelDocumentDirty();
    }

    /// <summary> 新建空白关卡：清空编辑网格内两层 Tilemap，重置保存路径与镜头默认值。需在编辑模式；测试中请先退出测试。 </summary>
    public void CreateNewLevel()
    {
        if (IsPlaytestMode)
            return;

        if (!IsEditMode)
            IsEditMode = true;

        ClearPlaytestTileSnapshot();

        if (groundTilemap != null && objectsTilemap != null)
        {
            var bounds = FixedEditGridCellBounds;
            var z = bounds.zMin;
            for (var y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (var x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var cell = new Vector3Int(x, y, z);
                    groundTilemap.SetTile(cell, null);
                    objectsTilemap.SetTile(cell, null);
                }
            }
        }

        _activeLevelSavePath = null;
        _levelMaxOrthographicSize = SokobanLevelSaveData.DefaultMaxOrthographicSize;
        _lastGridBounds = default;
        ClearLevelDocumentDirty();
        ApplyLevelMaxOrthographicSizeToTilemapSettings();
        OnLevelMaxOrthographicSizeChanged?.Invoke();

        if (tilemapSettings == null)
            tilemapSettings = FindFirstObjectByType<TilemapSettings>();
        tilemapSettings?.RefreshFromTilemaps(applyCameraDuringEditMode: true);
    }

    void UpdateEditModeCtrlOrthographicZoom()
    {
        if (!IsEditMode || !editModeCtrlScrollZoomEnabled)
            return;

        if (!(Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            return;

        var cam = worldCamera != null ? worldCamera : Camera.main;
        if (cam == null || !cam.orthographic)
            return;

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        var wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) < 1e-5f)
            return;

        if (_orthographicZoomReferenceSize < 1e-4f)
            _orthographicZoomReferenceSize = Mathf.Max(1e-4f, cam.orthographicSize);

        // 滚轮向前（>0）为放大画面 → 减小 Orthographic Size
        cam.orthographicSize -= wheel * orthographicZoomScrollSensitivity;

        var refS = _orthographicZoomReferenceSize;
        var lo = refS * orthographicZoomMinMultiplier;
        var hi = refS * orthographicZoomMaxMultiplier;
        cam.orthographicSize = Mathf.Clamp(cam.orthographicSize, Mathf.Min(lo, hi), Mathf.Max(lo, hi));

        if (clampCameraToEditGridBounds && groundTilemap != null)
            ClampCameraToFixedEditGrid(cam);
    }

    void UpdateCursorDragPanCamera()
    {
        var btn = cursorPanMouseButton;

        if (!IsEditMode || groundTilemap == null)
        {
            _cursorPanDragging = false;
            return;
        }

        var cam = worldCamera != null ? worldCamera : Camera.main;

        if (cursorDragPanCameraEnabled && IsCursorMode)
        {
            if (Input.GetMouseButtonDown(btn))
            {
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    _cursorPanDragging = false;
                else
                {
                    _cursorPanDragging = true;
                    _cursorPanLastScreen = Input.mousePosition;
                }
            }

            if (Input.GetMouseButtonUp(btn))
                _cursorPanDragging = false;

            if (_cursorPanDragging && Input.GetMouseButton(btn) && cam != null)
            {
                if (TryScreenToWorldOnGroundPlane(cam, groundTilemap, _cursorPanLastScreen, out var wPrev)
                    && TryScreenToWorldOnGroundPlane(cam, groundTilemap, Input.mousePosition, out var wCur))
                {
                    // 鼠标往右移时 wCur.x > wPrev.x，相机减去该差值即向左移，画面与指针同向拖动（抓地图）。
                    cam.transform.position += wPrev - wCur;
                    _cursorPanLastScreen = Input.mousePosition;
                }
            }
        }
        else
            _cursorPanDragging = false;

        if (clampCameraToEditGridBounds && cam != null)
            ClampCameraToFixedEditGrid(cam);
    }

    void ClampCameraToFixedEditGrid(Camera cam)
    {
        if (!cam.orthographic || groundTilemap == null || !TryGetFixedEditGridPlaneMinMax(out var gx0, out var gx1, out var gy0, out var gy1))
            return;

        SokobanOrthographicCameraUtility.ClampPositionToWorldBounds(
            cam, gx0, gx1, gy0, gy1, groundTilemap.transform.position.z);
    }

    bool TryGetFixedEditGridPlaneMinMax(out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = minY = float.PositiveInfinity;
        maxX = maxY = float.NegativeInfinity;

        var cellBounds = FixedEditGridCellBounds;
        if (cellBounds.size.x <= 0 || cellBounds.size.y <= 0)
            return false;

        var grid = groundTilemap.layoutGrid;
        if (grid == null)
            return false;

        var bump = Vector3.zero;
        var z = cellBounds.zMin;
        var xMin = float.PositiveInfinity;
        var xMax = float.NegativeInfinity;
        var yMin = float.PositiveInfinity;
        var yMax = float.NegativeInfinity;

        void EncasePoint(Vector3 p)
        {
            xMin = Mathf.Min(xMin, p.x);
            xMax = Mathf.Max(xMax, p.x);
            yMin = Mathf.Min(yMin, p.y);
            yMax = Mathf.Max(yMax, p.y);
        }

        void EncaseCell(Vector3Int cell)
        {
            GetCellCornersWorld(grid, cell, bump, out var ll, out var lr, out var ur, out var ul);
            EncasePoint(ll);
            EncasePoint(lr);
            EncasePoint(ur);
            EncasePoint(ul);
        }

        for (var x = cellBounds.xMin; x < cellBounds.xMax; x++)
        {
            EncaseCell(new Vector3Int(x, cellBounds.yMin, z));
            EncaseCell(new Vector3Int(x, cellBounds.yMax - 1, z));
        }

        for (var y = cellBounds.yMin + 1; y < cellBounds.yMax - 1; y++)
        {
            EncaseCell(new Vector3Int(cellBounds.xMin, y, z));
            EncaseCell(new Vector3Int(cellBounds.xMax - 1, y, z));
        }

        if (float.IsPositiveInfinity(xMin))
            return false;

        minX = xMin;
        maxX = xMax;
        minY = yMin;
        maxY = yMax;
        return true;
    }

    static bool TryScreenToWorldOnGroundPlane(Camera cam, Tilemap tm, Vector3 screenPosition, out Vector3 world)
    {
        world = default;
        var z = tm.transform.position.z;
        var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, z));
        var ray = cam.ScreenPointToRay(screenPosition);
        if (!plane.Raycast(ray, out var dist))
            return false;

        world = ray.GetPoint(dist);
        return true;
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
        {
            if (!palette.HasPaintBrushSelected)
                return;
            brush = palette.CurrentBrush;
        }

        var changed = false;

        if (brush.Layer == TilePaletteLayer.Ground)
        {
            var prev = groundTilemap.GetTile(cell);
            var next = brush.IsErase ? null : brush.Tile;
            if (prev != next)
            {
                groundTilemap.SetTile(cell, next);
                changed = true;
            }
        }
        else
        {
            if (!brush.IsErase && assets != null && brush.Tile == assets.PlayerTile)
            {
                if (RemovePlayerTilesExcept(cell))
                    changed = true;
            }

            var prevObj = objectsTilemap.GetTile(cell);
            var nextObj = brush.IsErase ? null : brush.Tile;
            if (prevObj != nextObj)
            {
                objectsTilemap.SetTile(cell, nextObj);
                changed = true;
            }
        }

        if (changed)
            MarkLevelDocumentDirty();

        tilemapSettings?.RefreshFromTilemaps();
    }

    bool RemovePlayerTilesExcept(Vector3Int keepCell)
    {
        var assets = TileAssetSettings.Instance;
        if (assets == null)
            return false;

        var player = assets.PlayerTile;
        var b = objectsTilemap.cellBounds;
        var any = false;
        for (var y = b.yMin; y < b.yMax; y++)
        {
            for (var x = b.xMin; x < b.xMax; x++)
            {
                var c = new Vector3Int(x, y, 0);
                if (c == keepCell)
                    continue;
                if (objectsTilemap.GetTile(c) == player)
                {
                    objectsTilemap.SetTile(c, null);
                    any = true;
                }
            }
        }

        return any;
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

        if (!palette.HasPaintBrushSelected)
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

    void RefreshToolModeButtonVisuals()
    {
        ApplyToggleButtonSelectedLook(cursorToolButton, CurrentToolMode == RuntimeTilemapEditToolMode.Cursor);
        ApplyToggleButtonSelectedLook(drawToolButton, CurrentToolMode == RuntimeTilemapEditToolMode.Draw);
        ApplyToggleButtonSelectedLook(eraserToolButton, CurrentToolMode == RuntimeTilemapEditToolMode.Eraser);
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
