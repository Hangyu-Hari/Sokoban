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
