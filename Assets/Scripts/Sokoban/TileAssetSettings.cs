using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 全局瓦片引用
/// </summary>
[DisallowMultipleComponent]
public sealed class TileAssetSettings : MonoBehaviour
{
    static TileAssetSettings _instance;

    public static TileAssetSettings Instance => _instance;

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

    public TileBase[] WallBaseTiles => wallBaseTiles;
    public TileBase WallCapTile => wallCapTile;
    public TileBase[] FloorTiles => floorTiles;
    public TileBase GoalTile => goalTile;
    public TileBase PlayerTile => playerTile;
    public TileBase BoxTile => boxTile;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }
}
