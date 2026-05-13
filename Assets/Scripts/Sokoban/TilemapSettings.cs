using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 每个关卡绑定本场景的Tilemap和相机
/// </summary>
[DisallowMultipleComponent]
public sealed class TilemapSettings : MonoBehaviour
{
    [Header("Tilemaps")]
    [SerializeField] Tilemap groundTilemap;
    [SerializeField] Tilemap objectsTilemap;

    [Header("相机设置")]
    [SerializeField] bool centerCameraOnStart = true;
    [SerializeField] Camera targetCamera;
    [Tooltip("勾选后会调整相机大小，使整块地图尽量落在屏幕内")]
    [SerializeField] bool fitOrthographicSize;
    [Tooltip("边缘空隙间距")]
    [SerializeField] float orthographicPadding = 0.5f;

    SokobanRuntimeState _state;
    bool _winCompleteUiShown;

    TileAssetSettings Assets => TileAssetSettings.Instance;

    void Start()
    {
        TryBootstrapLevel();
    }

    bool TryBootstrapLevel()
    {
        var assets = Assets;
        if (assets == null)
        {
            Debug.LogError(
                "[Sokoban] 未找到 TileAssetSettings：请在常驻场景挂一份 TileAssetSettings 并配置瓦片。",
                this);
            _state = null;
            return false;
        }

        if (groundTilemap == null || objectsTilemap == null)
        {
            _state = null;
            return false;
        }

        if (assets.GoalCompletedTile == null)
        {
            Debug.LogError(
                "[Sokoban] TileAssetSettings 未指定「Goal Completed Tile」（画在背景层上、箱子占满目标时的瓦片）。",
                assets);
            _state = null;
            return false;
        }

        if (!SokobanRuntimeState.TryFromTilemaps(
                groundTilemap,
                objectsTilemap,
                assets.WallBaseTiles,
                assets.WallCapTile,
                assets.FloorTiles,
                assets.GoalUncompletedTile,
                assets.PlayerTile,
                assets.BoxTile,
                out _state,
                out var err))
        {
            Debug.LogError("[Sokoban] " + err, this);
            _state = null;
            return false;
        }

        SokobanRuntimeState.ApplyWallBaseAndCap(groundTilemap, assets.WallBaseTiles, assets.WallCapTile);
        SyncTilemapsFromState(assets);

        if (centerCameraOnStart)
            ApplyCamera();

        _winCompleteUiShown = false;
        return true;
    }

    /// <summary> 关卡刷新后如需再对准相机可调用。 </summary>
    public void ApplyCamera()
    {
        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || !cam.orthographic)
            return;

        if (!TryGetTilemapsWorldBounds(out var world))
            return;

        var p = cam.transform.position;
        cam.transform.position = new Vector3(world.center.x, world.center.y, p.z);

        if (!fitOrthographicSize)
            return;

        var pad = Mathf.Max(0f, orthographicPadding);
        var halfH = world.extents.y + pad;
        var halfW = world.extents.x + pad;
        cam.orthographicSize = Mathf.Max(halfH, halfW / Mathf.Max(0.0001f, cam.aspect));
    }

    bool TryGetTilemapsWorldBounds(out Bounds world)
    {
        world = default;
        if (groundTilemap == null)
            return false;

        groundTilemap.CompressBounds();

        if (TryWorldBoundsFromTilemapLocalBounds(groundTilemap, out world))
            return true;

        var r = groundTilemap.GetComponent<TilemapRenderer>();
        if (r == null || !r.enabled)
            return false;
        world = r.bounds;
        return world.size.sqrMagnitude > 1e-12f;
    }

    static bool TryWorldBoundsFromTilemapLocalBounds(Tilemap tm, out Bounds world)
    {
        world = default;
        if (tm == null)
            return false;

        var local = tm.localBounds;
        if (local.size.sqrMagnitude < 1e-18f)
            return false;

        var t = tm.transform;
        var c = local.center;
        var e = local.extents;
        world = BoundsFromTransformedLocalAabb(t, c, e);
        return world.size.sqrMagnitude > 1e-18f;
    }

    static Bounds BoundsFromTransformedLocalAabb(Transform t, Vector3 c, Vector3 e)
    {
        var w = new Bounds();
        var init = false;
        for (var ix = -1; ix <= 1; ix += 2)
        {
            for (var iy = -1; iy <= 1; iy += 2)
            {
                for (var iz = -1; iz <= 1; iz += 2)
                {
                    var p = t.TransformPoint(c + new Vector3(ix * e.x, iy * e.y, iz * e.z));
                    if (!init)
                    {
                        w = new Bounds(p, Vector3.zero);
                        init = true;
                    }
                    else
                        w.Encapsulate(p);
                }
            }
        }

        return w;
    }

    void Update()
    {
        if (_state == null)
            return;

        var assets = Assets;
        if (assets == null)
            return;

        var ui = LevelUIManager.Instance;
        if (ui != null && ui.IsGameplayInputBlocked)
            return;

        var d = Vector3Int.zero;
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            d = Vector3Int.up;
        else if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow))
            d = Vector3Int.down;
        else if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow))
            d = Vector3Int.left;
        else if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow))
            d = Vector3Int.right;

        if (d == Vector3Int.zero)
            return;

        if (!_state.TryMove(d))
            return;

        SyncTilemapsFromState(assets);

        if (_state.IsWin() && !_winCompleteUiShown)
        {
            _winCompleteUiShown = true;
            Debug.Log("[Sokoban] Level complete.", this);
            LevelUIManager.Instance?.ShowLevelCompleteUI();
        }
    }

    void SyncTilemapsFromState(TileAssetSettings assets)
    {
        var b = _state.Bounds;
        for (int y = b.yMin; y < b.yMax; y++)
        {
            for (int x = b.xMin; x < b.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                objectsTilemap.SetTile(cell, null);

                if (_state.IsGoal(cell))
                {
                    var completed = _state.HasBoxAt(cell);
                    groundTilemap.SetTile(
                        cell,
                        completed ? assets.GoalCompletedTile : assets.GoalUncompletedTile);
                }
            }
        }

        foreach (var box in _state.Boxes)
            objectsTilemap.SetTile(box, assets.BoxTile);

        objectsTilemap.SetTile(_state.Player, assets.PlayerTile);
    }
}
