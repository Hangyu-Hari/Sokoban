using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 将编辑网格内两层 Tilemap 存为 JSON / 从 JSON 读回并写入 Tilemap。
/// 仅写入「至少有一层有瓦片」的格子；空格子不写入。
/// 瓦片用 <see cref="TileAssetSettings"/> 中的稳定字符串键标识。
/// </summary>
[Serializable]
public sealed class SokobanSavedCell
{
    /// <summary> 相对 <c>bounds.xMin</c> 的列索引，范围 <c>0 .. boundsSizeX-1</c>。 </summary>
    public int x;
    /// <summary> 相对 <c>bounds.yMin</c> 的行索引。 </summary>
    public int y;
    /// <summary> 背景层键；空串表示该层无瓦片。 </summary>
    public string groundKey;
    /// <summary> 物体层键；空串表示该层无瓦片。 </summary>
    public string objectKey;
}

[Serializable]
public sealed class SokobanLevelSaveData
{
    public const float DefaultMaxOrthographicSize = 6.5f;

    public int formatVersion = 2;
    public int boundsXMin;
    public int boundsYMin;
    public int boundsZMin;
    public int boundsSizeX;
    public int boundsSizeY;
    public int boundsSizeZ;
    /// <summary> 大地图时的镜头正交 Size 上限；≤0 表示使用 <see cref="DefaultMaxOrthographicSize"/>。 </summary>
    public float maxOrthographicSize = DefaultMaxOrthographicSize;
    /// <summary> 仅包含有瓦片的格子（任一层非空）。 </summary>
    public SokobanSavedCell[] filledCells;

    public static float ResolveMaxOrthographicSize(float savedValue) =>
        savedValue > 0.01f ? savedValue : DefaultMaxOrthographicSize;
}

public static class SokobanLevelSaveFile
{
    /// <summary>
    /// 将 <paramref name="cellBounds"/> 内有瓦片的格子写入 <paramref name="fullPath"/>（JSON）；空格子不写入。
    /// </summary>
    public static bool TrySave(
        Tilemap ground,
        Tilemap objects,
        BoundsInt cellBounds,
        TileAssetSettings assets,
        string fullPath,
        float maxOrthographicSize,
        out string error)
    {
        error = null;
        if (ground == null || objects == null)
        {
            error = "Tilemap 引用为空。";
            return false;
        }

        if (assets == null)
        {
            error = "未找到 TileAssetSettings。";
            return false;
        }

        if (cellBounds.size.x <= 0 || cellBounds.size.y <= 0)
        {
            error = "网格宽高无效。";
            return false;
        }

        var z = cellBounds.zMin;
        var filled = new List<SokobanSavedCell>();

        for (var y = cellBounds.yMin; y < cellBounds.yMax; y++)
        {
            for (var x = cellBounds.xMin; x < cellBounds.xMax; x++)
            {
                var cell = new Vector3Int(x, y, z);
                var gt = ground.GetTile(cell);
                var ot = objects.GetTile(cell);
                if (gt == null && ot == null)
                    continue;

                if (!TryGetTileKey(gt, assets, out var gk, out var ge))
                {
                    error = ge;
                    return false;
                }

                if (!TryGetTileKey(ot, assets, out var ok, out var oe))
                {
                    error = oe;
                    return false;
                }

                filled.Add(new SokobanSavedCell
                {
                    x = x - cellBounds.xMin,
                    y = y - cellBounds.yMin,
                    groundKey = gk,
                    objectKey = ok,
                });
            }
        }

        var data = new SokobanLevelSaveData
        {
            formatVersion = 2,
            boundsXMin = cellBounds.xMin,
            boundsYMin = cellBounds.yMin,
            boundsZMin = cellBounds.zMin,
            boundsSizeX = cellBounds.size.x,
            boundsSizeY = cellBounds.size.y,
            boundsSizeZ = cellBounds.size.z,
            maxOrthographicSize = Mathf.Max(0.1f, maxOrthographicSize),
            filledCells = filled.ToArray(),
        };

        try
        {
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonUtility.ToJson(data, true);
            File.WriteAllText(fullPath, json);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        return true;
    }

    /// <summary>
    /// 读取 JSON 关卡：先清空 <paramref name="clearAndPlaceWithin"/> 内两层 Tilemap，再把文件中落在该范围内的格子写回。
    /// </summary>
    public static bool TryLoad(
        string fullPath,
        Tilemap ground,
        Tilemap objects,
        BoundsInt clearAndPlaceWithin,
        TileAssetSettings assets,
        out float maxOrthographicSize,
        out string error)
    {
        error = null;
        maxOrthographicSize = SokobanLevelSaveData.DefaultMaxOrthographicSize;
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
        {
            error = "文件不存在或路径为空。";
            return false;
        }

        if (ground == null || objects == null)
        {
            error = "Tilemap 引用为空。";
            return false;
        }

        if (assets == null)
        {
            error = "未找到 TileAssetSettings。";
            return false;
        }

        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        SokobanLevelSaveData data;
        try
        {
            data = JsonUtility.FromJson<SokobanLevelSaveData>(json);
        }
        catch (Exception ex)
        {
            error = "JSON 解析失败：" + ex.Message;
            return false;
        }

        if (data == null)
        {
            error = "关卡数据为空。";
            return false;
        }

        if (data.formatVersion != 2)
        {
            error = "不支持的 formatVersion：" + data.formatVersion + "（需要 2），或 JSON 无效。";
            return false;
        }

        maxOrthographicSize = SokobanLevelSaveData.ResolveMaxOrthographicSize(data.maxOrthographicSize);

        if (data.boundsSizeX <= 0 || data.boundsSizeY <= 0)
        {
            error = "文件中网格宽高无效。";
            return false;
        }

        var cells = data.filledCells;
        var staged = new List<(Vector3Int cell, TileBase g, TileBase o)>();
        if (cells != null)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                var entry = cells[i];
                var absX = data.boundsXMin + entry.x;
                var absY = data.boundsYMin + entry.y;
                var cell = new Vector3Int(absX, absY, clearAndPlaceWithin.zMin);
                if (!clearAndPlaceWithin.Contains(cell))
                    continue;

                if (!TryParseTileKey(entry.groundKey, assets, out var gTile, out var ge))
                {
                    error = ge;
                    return false;
                }

                if (!TryParseTileKey(entry.objectKey, assets, out var oTile, out var oe))
                {
                    error = oe;
                    return false;
                }

                staged.Add((cell, gTile, oTile));
            }
        }

        var zClear = clearAndPlaceWithin.zMin;
        for (var y = clearAndPlaceWithin.yMin; y < clearAndPlaceWithin.yMax; y++)
        {
            for (var x = clearAndPlaceWithin.xMin; x < clearAndPlaceWithin.xMax; x++)
            {
                var c = new Vector3Int(x, y, zClear);
                ground.SetTile(c, null);
                objects.SetTile(c, null);
            }
        }

        for (var i = 0; i < staged.Count; i++)
        {
            var s = staged[i];
            ground.SetTile(s.cell, s.g);
            objects.SetTile(s.cell, s.o);
        }

        return true;
    }

    /// <summary> 只读取 JSON 中的镜头设置（不改动 Tilemap）。 </summary>
    public static bool TryReadMaxOrthographicSize(string fullPath, out float maxOrthographicSize)
    {
        maxOrthographicSize = SokobanLevelSaveData.DefaultMaxOrthographicSize;
        if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
            return false;

        string json;
        try
        {
            json = File.ReadAllText(fullPath);
        }
        catch
        {
            return false;
        }

        SokobanLevelSaveData data;
        try
        {
            data = JsonUtility.FromJson<SokobanLevelSaveData>(json);
        }
        catch
        {
            return false;
        }

        if (data == null || data.formatVersion != 2)
            return false;

        maxOrthographicSize = SokobanLevelSaveData.ResolveMaxOrthographicSize(data.maxOrthographicSize);
        return true;
    }

    static bool TryParseTileKey(string key, TileAssetSettings assets, out TileBase tile, out string error)
    {
        tile = null;
        error = null;
        if (string.IsNullOrEmpty(key))
            return true;

        if (key == "wallCap")
        {
            tile = assets.WallCapTile;
            if (tile == null)
            {
                error = "键 wallCap 对应瓦片未配置。";
                return false;
            }

            return true;
        }

        if (key == "goalUncompleted")
        {
            tile = assets.GoalUncompletedTile;
            if (tile == null)
            {
                error = "键 goalUncompleted 对应瓦片未配置。";
                return false;
            }

            return true;
        }

        if (key == "goalCompleted")
        {
            tile = assets.GoalCompletedTile;
            if (tile == null)
            {
                error = "键 goalCompleted 对应瓦片未配置。";
                return false;
            }

            return true;
        }

        if (key == "player")
        {
            tile = assets.PlayerTile;
            if (tile == null)
            {
                error = "键 player 对应瓦片未配置。";
                return false;
            }

            return true;
        }

        if (key == "box")
        {
            tile = assets.BoxTile;
            if (tile == null)
            {
                error = "键 box 对应瓦片未配置。";
                return false;
            }

            return true;
        }

        const string wallPrefix = "wallBase:";
        if (key.StartsWith(wallPrefix, StringComparison.Ordinal))
        {
            if (!int.TryParse(key.Substring(wallPrefix.Length), out var idx))
            {
                error = "无效的 wallBase 键：" + key;
                return false;
            }

            var arr = assets.WallBaseTiles;
            if (arr == null || idx < 0 || idx >= arr.Length || arr[idx] == null)
            {
                error = "wallBase 索引越界或未配置：" + key;
                return false;
            }

            tile = arr[idx];
            return true;
        }

        const string floorPrefix = "floor:";
        if (key.StartsWith(floorPrefix, StringComparison.Ordinal))
        {
            if (!int.TryParse(key.Substring(floorPrefix.Length), out var idx))
            {
                error = "无效的 floor 键：" + key;
                return false;
            }

            var arr = assets.FloorTiles;
            if (arr == null || idx < 0 || idx >= arr.Length || arr[idx] == null)
            {
                error = "floor 索引越界或未配置：" + key;
                return false;
            }

            tile = arr[idx];
            return true;
        }

        error = "未知的瓦片键：" + key;
        return false;
    }

    static bool TryGetTileKey(TileBase tile, TileAssetSettings assets, out string key, out string error)
    {
        key = "";
        error = null;
        if (tile == null)
            return true;

        var walls = assets.WallBaseTiles;
        if (walls != null)
        {
            for (var i = 0; i < walls.Length; i++)
            {
                if (walls[i] == tile)
                {
                    key = "wallBase:" + i;
                    return true;
                }
            }
        }

        if (assets.WallCapTile == tile)
        {
            key = "wallCap";
            return true;
        }

        var floors = assets.FloorTiles;
        if (floors != null)
        {
            for (var i = 0; i < floors.Length; i++)
            {
                if (floors[i] == tile)
                {
                    key = "floor:" + i;
                    return true;
                }
            }
        }

        if (assets.GoalUncompletedTile == tile)
        {
            key = "goalUncompleted";
            return true;
        }

        if (assets.GoalCompletedTile == tile)
        {
            key = "goalCompleted";
            return true;
        }

        if (assets.PlayerTile == tile)
        {
            key = "player";
            return true;
        }

        if (assets.BoxTile == tile)
        {
            key = "box";
            return true;
        }

        error = "无法序列化瓦片（不在 TileAssetSettings 中）：" + tile.name;
        return false;
    }
}
