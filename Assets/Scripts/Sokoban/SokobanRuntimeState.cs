using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Runtime sokoban state
/// </summary>
public sealed class SokobanRuntimeState
{
    readonly BoundsInt _bounds;
    readonly bool[] _wall;
    readonly bool[] _goal;
    readonly HashSet<Vector3Int> _boxes = new();

    public BoundsInt Bounds => _bounds;
    public Vector3Int Player { get; private set; }

    public IReadOnlyCollection<Vector3Int> Boxes => _boxes;

    SokobanRuntimeState(BoundsInt bounds, bool[] wall, bool[] goal, Vector3Int player, IEnumerable<Vector3Int> boxes)
    {
        _bounds = bounds;
        _wall = wall;
        _goal = goal;
        Player = player;
        foreach (var b in boxes)
            _boxes.Add(b);
    }

    int ToIndex(Vector3Int c)
    {
        int w = _bounds.size.x;
        return (c.x - _bounds.xMin) + (c.y - _bounds.yMin) * w;
    }

    public bool InBounds(Vector3Int c) => _bounds.Contains(c);

    public bool IsWall(Vector3Int c) => !InBounds(c) || _wall[ToIndex(c)];

    public bool IsGoal(Vector3Int c) => InBounds(c) && _goal[ToIndex(c)];

    /// <summary>Floor or goal (not wall).</summary>
    public bool IsWalkable(Vector3Int c) => InBounds(c) && !_wall[ToIndex(c)];

    public bool IsWin()
    {
        for (int y = _bounds.yMin; y < _bounds.yMax; y++)
        {
            for (int x = _bounds.xMin; x < _bounds.xMax; x++)
            {
                var c = new Vector3Int(x, y, 0);
                if (IsGoal(c) && !_boxes.Contains(c))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Single-step move: pushes at most one box. Returns true if the player moved.</summary>
    public bool TryMove(Vector3Int delta)
    {
        if (delta.x != 0 && delta.y != 0)
            return false;

        var next = Player + delta;
        if (!IsWalkable(next))
            return false;

        if (!_boxes.Contains(next))
        {
            Player = next;
            return true;
        }

        var beyond = next + delta;
        if (!IsWalkable(beyond) || _boxes.Contains(beyond))
            return false;

        _boxes.Remove(next);
        _boxes.Add(beyond);
        Player = next;
        return true;
    }

    static bool TileInList(IReadOnlyList<TileBase> list, TileBase t)
    {
        if (t == null || list == null)
            return false;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == t)
                return true;
        }

        return false;
    }

    static bool NonEmptyTileList(TileBase[] list, string listName, out string error)
    {
        error = null;
        if (list == null || list.Length == 0)
        {
            error = $"请在 Inspector 为「{listName}」至少指定 1 个 Tile。";
            return false;
        }

        for (var i = 0; i < list.Length; i++)
        {
            if (list[i] == null)
            {
                error = $"「{listName}」第 {i + 1} 项为空，请删掉空槽或拖入 Tile。";
                return false;
            }
        }

        return true;
    }

    static TileBase PickBaseTileStable(int x, int y, TileBase[] wallBaseTiles)
    {
        var h = (uint)(x * 374761393 + y * 668265263);
        return wallBaseTiles[(int)(h % (uint)wallBaseTiles.Length)];
    }

    /// <summary>
    /// 读关后刷新地面墙贴图：正下方（cell 的 Y-1）仍是墙则用顶墙；否则用底墙（多种里保留已有或按坐标稳定选一种）。
    /// </summary>
    public static void ApplyWallBaseAndCap(Tilemap ground, TileBase[] wallBaseTiles, TileBase wallCapTile)
    {
        if (!ground || wallBaseTiles == null || wallBaseTiles.Length == 0 || !wallCapTile)
            return;

        bool IsWallTile(TileBase t) => TileInList(wallBaseTiles, t) || t == wallCapTile;

        var bounds = ground.cellBounds;
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                var t = ground.GetTile(cell);
                if (!IsWallTile(t))
                    continue;

                var below = cell + Vector3Int.down;
                var belowTile = ground.GetTile(below);
                var wallBelow = IsWallTile(belowTile);
                if (wallBelow)
                {
                    if (t != wallCapTile)
                        ground.SetTile(cell, wallCapTile);
                    continue;
                }

                if (TileInList(wallBaseTiles, t))
                    continue;

                ground.SetTile(cell, PickBaseTileStable(x, y, wallBaseTiles));
            }
        }
    }

    /// <summary>
    /// Ground: wall / floor / goal only. Objects: player / crate only (empty allowed).
    /// Unpainted ground cells are treated as walls.
    /// 多种底墙、单一顶墙在逻辑里都是墙；多种地板都是可走平地（非目标）。
    /// </summary>
    public static bool TryFromTilemaps(
        Tilemap ground,
        Tilemap objects,
        TileBase[] wallBaseTiles,
        TileBase wallCapTile,
        TileBase[] floorTiles,
        TileBase goalTile,
        TileBase playerTile,
        TileBase boxTile,
        out SokobanRuntimeState state,
        out string error)
    {
        state = null;
        error = null;

        if (!ground || !objects)
        {
            error = "Ground or Objects Tilemap is not assigned.";
            return false;
        }

        if (!wallCapTile || !goalTile || !playerTile || !boxTile)
        {
            error = "请在 Inspector 指定：顶墙、目标、玩家、箱子，以及底墙列表、地板列表。";
            return false;
        }

        if (!NonEmptyTileList(wallBaseTiles, "底墙 Wall Base Tiles", out error))
            return false;
        if (!NonEmptyTileList(floorTiles, "地板 Floor Tiles", out error))
            return false;

        bool IsWallGround(TileBase gt) => TileInList(wallBaseTiles, gt) || gt == wallCapTile;

        var g = ground.cellBounds;
        var o = objects.cellBounds;
        int x0 = Mathf.Min(g.xMin, o.xMin);
        int y0 = Mathf.Min(g.yMin, o.yMin);
        int x1 = Mathf.Max(g.xMax, o.xMax);
        int y1 = Mathf.Max(g.yMax, o.yMax);
        var b = new BoundsInt(x0, y0, 0, x1 - x0, y1 - y0, 1);

        int w = b.size.x;
        int len = w * b.size.y;
        var wall = new bool[len];
        var goal = new bool[len];

        Vector3Int player = default;
        var boxes = new List<Vector3Int>();
        int playerCount = 0;

        bool IdxWall(int x, int y) => wall[(x - b.xMin) + (y - b.yMin) * w];
        void SetWall(int x, int y, bool v) => wall[(x - b.xMin) + (y - b.yMin) * w] = v;
        void SetGoal(int x, int y, bool v) => goal[(x - b.xMin) + (y - b.yMin) * w] = v;

        for (int y = b.yMin; y < b.yMax; y++)
        {
            for (int x = b.xMin; x < b.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                var gt = ground.GetTile(cell);

                if (IsWallGround(gt))
                {
                    SetWall(x, y, true);
                    SetGoal(x, y, false);
                }
                else if (TileInList(floorTiles, gt))
                {
                    SetWall(x, y, false);
                    SetGoal(x, y, false);
                }
                else if (gt == goalTile)
                {
                    SetWall(x, y, false);
                    SetGoal(x, y, true);
                }
                else if (gt == null)
                {
                    SetWall(x, y, true);
                    SetGoal(x, y, false);
                }
                else
                {
                    error = $"Unknown ground tile at {cell}: {gt.name}. 地面只允许：底墙列表里任一种、顶墙、地板列表里任一种、目标，或留空（虚空当墙）。";
                    return false;
                }
            }
        }

        for (int y = b.yMin; y < b.yMax; y++)
        {
            for (int x = b.xMin; x < b.xMax; x++)
            {
                var cell = new Vector3Int(x, y, 0);
                var ot = objects.GetTile(cell);
                if (ot == null)
                    continue;

                if (IdxWall(x, y))
                {
                    error = $"Object tile at {cell} but ground is wall/void.";
                    return false;
                }

                if (ot == playerTile)
                {
                    player = cell;
                    playerCount++;
                }
                else if (ot == boxTile)
                {
                    boxes.Add(cell);
                }
                else
                {
                    error =
                        $"Objects 层在 {cell} 发现了未识别的 Tile「{ot.name}」。这一层只能放「玩家」和「箱子」。\n" +
                        "目标点、地板、墙都必须画在 Background（地面）层；目标格上有人或箱子时：Background 画目标，Objects 只叠玩家或箱子，不要把目标画在 Objects 上。";
                    if (ot == goalTile)
                        error += "\n（该 Tile 与 Inspector 里的 Goal 相同：你把目标画在了 Objects 层，请删改到 Background。）";
                    else if (TileInList(floorTiles, ot))
                        error += "\n（该 Tile 在地板列表里：应只在 Background 使用。）";
                    else if (TileInList(wallBaseTiles, ot) || ot == wallCapTile)
                        error += "\n（该 Tile 属于墙：应只在 Background 使用。）";

                    return false;
                }
            }
        }

        if (playerCount != 1)
        {
            error = playerCount == 0
                ? "No player tile on the Objects layer."
                : "There must be exactly one player tile on the Objects layer.";
            return false;
        }

        state = new SokobanRuntimeState(b, wall, goal, player, boxes);
        return true;
    }
}
