using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
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
    [Tooltip("地图很大时，正交 Size 不超过此值；超出部分通过跟随玩家查看")]
    [SerializeField, Min(0.1f)] float maxOrthographicSize = 6.5f;
    [Tooltip("当地图大于当前镜头视野时，镜头跟随玩家，并在地图边缘停止移动")]
    [SerializeField] bool followPlayerWhenMapExceedsView = true;
    [Tooltip("进入关卡场景时，从 LevelFiles 下 JSON 读取 maxOrthographicSize（文件名默认与场景名一致）")]
    [SerializeField] bool loadMaxOrthographicSizeFromLevelJsonOnStart = true;
    [Tooltip("留空则用当前场景名 + .json，例如 Level 1.json")]
    [SerializeField] string levelJsonFileName;

    SokobanRuntimeState _state;
    bool _winCompleteUiShown;
    bool _cameraFollowPlayer;
    Bounds _mapWorldBounds;
    bool _hasMapWorldBounds;

    TileAssetSettings Assets => TileAssetSettings.Instance;

    void Start()
    {
        if (loadMaxOrthographicSizeFromLevelJsonOnStart && !IsEditorScene())
            TryApplyMaxOrthographicSizeFromLevelJson();

        TryBootstrapLevel(applyCameraDuringEditMode: false);
    }

    /// <summary> 运行时设置最大镜头（编辑器 InputField / 读 JSON 后调用）。 </summary>
    public void SetMaxOrthographicSize(float size)
    {
        maxOrthographicSize = Mathf.Max(0.1f, size);
        if (_state != null && centerCameraOnStart)
            ApplyCamera();
    }

    static bool IsEditorScene()
    {
        var name = SceneManager.GetActiveScene().name ?? string.Empty;
        return name.IndexOf("Editor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    bool TryApplyMaxOrthographicSizeFromLevelJson()
    {
        var path = ResolveLevelJsonPath();
        if (string.IsNullOrEmpty(path))
            return false;

        if (!SokobanLevelSaveFile.TryReadMaxOrthographicSize(path, out var size))
            return false;

        SetMaxOrthographicSize(size);
        return true;
    }

    string ResolveLevelJsonPath()
    {
        var fileName = string.IsNullOrWhiteSpace(levelJsonFileName)
            ? SceneManager.GetActiveScene().name + ".json"
            : levelJsonFileName.Trim();
        if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fileName += ".json";
        return Path.Combine(LevelSavePathPicker.GetDefaultLevelFilesDirectory(), fileName);
    }

    /// <summary>
    /// 从当前 Tilemap 重新解析关卡（例如 Runtime 编辑后调用）。
    /// 编辑模式（F1）下默认<strong>不</strong>调用 <see cref="ApplyCamera"/>，避免落笔反复刷新时相机被居中/缩放；
    /// 需要时传入 <paramref name="applyCameraDuringEditMode"/> 为 true（例如打开关卡、开始测试）。
    /// </summary>
    public bool RefreshFromTilemaps(bool applyCameraDuringEditMode = false) =>
        TryBootstrapLevel(applyCameraDuringEditMode);

    /// <summary> 仅校验当前 Tilemap 能否进入游玩，不修改盘面或相机。 </summary>
    public bool TryValidateLevelForPlaytest(out string technicalError)
    {
        technicalError = null;
        var assets = Assets;
        if (assets == null)
        {
            technicalError = "TileAssetSettings missing.";
            return false;
        }

        if (groundTilemap == null || objectsTilemap == null)
        {
            technicalError = "Ground or Objects Tilemap is not assigned.";
            return false;
        }

        if (assets.GoalCompletedTile == null)
        {
            technicalError = "GoalCompletedTile missing.";
            return false;
        }

        return SokobanRuntimeState.TryFromTilemaps(
            groundTilemap,
            objectsTilemap,
            assets.WallBaseTiles,
            assets.WallCapTile,
            assets.FloorTiles,
            assets.GoalUncompletedTile,
            assets.GoalCompletedTile,
            assets.PlayerTile,
            assets.BoxTile,
            out _,
            out technicalError);
    }

    bool TryBootstrapLevel(bool applyCameraDuringEditMode)
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
                assets.GoalCompletedTile,
                assets.PlayerTile,
                assets.BoxTile,
                out _state,
                out var err))
        {
            // 编辑中落笔会反复 Refresh，半成品地图不刷屏；开始测试等显式操作（applyCameraDuringEditMode）要给出原因。
            if (!RuntimeTilemapEditPainter.IsEditMode || applyCameraDuringEditMode)
                Debug.LogWarning("[Sokoban] " + err, this);
            _state = null;
            return false;
        }

        SokobanRuntimeState.ApplyWallBaseAndCap(groundTilemap, assets.WallBaseTiles, assets.WallCapTile);
        SyncTilemapsFromState(assets);

        if (centerCameraOnStart
            && (!RuntimeTilemapEditPainter.IsEditMode || applyCameraDuringEditMode))
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
        {
            _hasMapWorldBounds = false;
            _cameraFollowPlayer = false;
            return;
        }

        _mapWorldBounds = world;
        _hasMapWorldBounds = true;

        var pad = Mathf.Max(0f, orthographicPadding);
        if (fitOrthographicSize)
        {
            var halfH = world.extents.y + pad;
            var halfW = world.extents.x + pad;
            var fitSize = Mathf.Max(halfH, halfW / Mathf.Max(0.0001f, cam.aspect));
            cam.orthographicSize = Mathf.Min(fitSize, maxOrthographicSize);
        }

        _cameraFollowPlayer = followPlayerWhenMapExceedsView
            && SokobanOrthographicCameraUtility.MapExceedsOrthographicView(cam, world, pad);

        if (_cameraFollowPlayer && _state != null)
            FocusCameraOnPlayer(cam);
        else
        {
            var p = cam.transform.position;
            cam.transform.position = new Vector3(world.center.x, world.center.y, p.z);
        }

        if (_hasMapWorldBounds)
            ClampCameraToMapBounds(cam);
    }

    void LateUpdate()
    {
        if (!_cameraFollowPlayer || _state == null || groundTilemap == null)
            return;

        if (RuntimeTilemapEditPainter.IsEditMode)
            return;

        var cam = targetCamera != null ? targetCamera : Camera.main;
        if (cam == null || !cam.orthographic)
            return;

        FocusCameraOnPlayer(cam);
    }

    void FocusCameraOnPlayer(Camera cam)
    {
        var playerWorld = groundTilemap.GetCellCenterWorld(_state.Player);
        var p = cam.transform.position;
        cam.transform.position = new Vector3(playerWorld.x, playerWorld.y, p.z);
        ClampCameraToMapBounds(cam);
    }

    void ClampCameraToMapBounds(Camera cam)
    {
        if (!_hasMapWorldBounds || groundTilemap == null)
            return;

        var planeZ = groundTilemap.transform.position.z;
        var pad = Mathf.Max(0f, orthographicPadding);
        var b = _mapWorldBounds;
        SokobanOrthographicCameraUtility.ClampPositionToWorldBounds(
            cam,
            b.min.x + pad,
            b.max.x - pad,
            b.min.y + pad,
            b.max.y - pad,
            planeZ);
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

        if (RuntimeTilemapEditPainter.IsEditMode)
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

        AudioManager.PlayOneShot("pop_up", AudioManager.AudioGroup.SFX);

        SyncTilemapsFromState(assets);

        if (_cameraFollowPlayer)
            FocusCameraOnPlayer(targetCamera != null ? targetCamera : Camera.main);

        if (_state.IsWin() && !_winCompleteUiShown)
        {
            _winCompleteUiShown = true;
            Debug.Log("[Sokoban] Level complete.", this);
            GameSceneManager.Instance?.RegisterUnlockedNextLevelAfterWin();
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
