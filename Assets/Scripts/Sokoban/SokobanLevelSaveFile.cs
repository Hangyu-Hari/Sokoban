using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 将编辑网格内两层 Tilemap 存为 JSON（仅保存；读取后续再做）。
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
    public int formatVersion = 2;
    public int boundsXMin;
    public int boundsYMin;
    public int boundsZMin;
    public int boundsSizeX;
    public int boundsSizeY;
    public int boundsSizeZ;
    /// <summary> 仅包含有瓦片的格子（任一层非空）。 </summary>
    public SokobanSavedCell[] filledCells;
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
