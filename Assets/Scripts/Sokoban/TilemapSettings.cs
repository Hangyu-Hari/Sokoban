using UnityEngine;
using UnityEngine.Tilemaps;


[DisallowMultipleComponent]
public sealed class TilemapSettings : MonoBehaviour
{
    [Header("Tilemaps")]
    [SerializeField] Tilemap groundTilemap;
    [SerializeField] Tilemap objectsTilemap;

    [Header("Tile assets")]
    [Tooltip("底墙")]
    [SerializeField] TileBase[] wallBaseTiles;
    [Tooltip("顶墙")]
    [SerializeField] TileBase wallCapTile;
    [Tooltip("地板")]
    [SerializeField] TileBase[] floorTiles;
    [SerializeField] TileBase goalTile;
    [SerializeField] TileBase playerTile;
    [SerializeField] TileBase boxTile;

    [Header("相机设置")]
    [SerializeField] bool centerCameraOnStart = true;
    [SerializeField] Camera targetCamera;
    [Tooltip("勾选后会调整相机大小，使整块地图尽量落在屏幕内")]
    [SerializeField] bool fitOrthographicSize;
    [Tooltip("边缘空隙间距")]
    [SerializeField] float orthographicPadding = 0.5f;
    

    SokobanRuntimeState _state;

    void Start()
    {
        if (!SokobanRuntimeState.TryFromTilemaps(
                groundTilemap,
                objectsTilemap,
                wallBaseTiles,
                wallCapTile,
                floorTiles,
                goalTile,
                playerTile,
                boxTile,
                out _state,
                out var err))
        {
            Debug.LogError("[Sokoban] " + err, this);
            enabled = false;
            return;
        }

        SokobanRuntimeState.ApplyWallBaseAndCap(groundTilemap, wallBaseTiles, wallCapTile);

        SyncObjectsLayer();

        if (centerCameraOnStart)
            ApplyCamera();
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

    /// <summary> localBounds 的 8 角点变换到世界；失败时由调用方用 Renderer.bounds。 </summary>
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

        SyncObjectsLayer();

        if (_state.IsWin())
            Debug.Log("[Sokoban] Level complete.", this);
    }

    void SyncObjectsLayer()
    {
        var b = _state.Bounds;
        for (int y = b.yMin; y < b.yMax; y++)
        {
            for (int x = b.xMin; x < b.xMax; x++)
                objectsTilemap.SetTile(new Vector3Int(x, y, 0), null);
        }

        foreach (var box in _state.Boxes)
            objectsTilemap.SetTile(box, boxTile);

        objectsTilemap.SetTile(_state.Player, playerTile);
    }
}
