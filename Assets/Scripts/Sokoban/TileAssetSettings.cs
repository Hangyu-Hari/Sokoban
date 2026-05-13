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
    [Tooltip("背景层：空目标点（该格还没有箱子）。")]
    [SerializeField] TileBase goalUncompletedTile;
    [Tooltip("已完成目标点")]
    [SerializeField] TileBase goalCompletedTile;
    [SerializeField] TileBase playerTile;
    [SerializeField] TileBase boxTile;

    public TileBase[] WallBaseTiles => wallBaseTiles;
    public TileBase WallCapTile => wallCapTile;
    public TileBase[] FloorTiles => floorTiles;
    public TileBase GoalUncompletedTile => goalUncompletedTile;
    public TileBase GoalCompletedTile => goalCompletedTile;
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
