using System;

/// <summary> 将关卡校验的技术错误转为编辑器 UI 上的简短中文提示。 </summary>
public static class PlaytestStartUserMessages
{
    public const string DefaultHint = "点击按钮测试关卡";
    public const string TestingHint = "测试中......";
    public const string SnapshotFailed = "无法读取当前地图";
    public const string StartFailed = "无法开始测试";

    public static string FromTechnicalError(string technicalError)
    {
        if (string.IsNullOrEmpty(technicalError))
            return StartFailed;

        if (technicalError.Contains("No player tile", StringComparison.OrdinalIgnoreCase))
            return "未检测到玩家";

        if (technicalError.Contains("exactly one player", StringComparison.OrdinalIgnoreCase))
            return "需要恰好一名玩家";

        if (technicalError.Contains("ground is wall/void", StringComparison.OrdinalIgnoreCase))
            return "玩家或箱子不能放在墙上";

        if (technicalError.Contains("Unknown ground tile", StringComparison.OrdinalIgnoreCase))
            return "地面使用了无法识别的图块";

        if (technicalError.Contains("Objects 层", StringComparison.OrdinalIgnoreCase)
            || technicalError.Contains("未识别的 Tile", StringComparison.OrdinalIgnoreCase))
            return "物体层只能放置玩家和箱子";

        if (technicalError.Contains("Goal Uncompleted", StringComparison.OrdinalIgnoreCase)
            || technicalError.Contains("目标画在了 Objects", StringComparison.OrdinalIgnoreCase))
            return "目标请画在地面层";

        if (technicalError.Contains("Ground or Objects Tilemap", StringComparison.OrdinalIgnoreCase))
            return "地图层未配置";

        if (technicalError.Contains("TileAssetSettings", StringComparison.OrdinalIgnoreCase)
            || technicalError.Contains("GoalCompletedTile", StringComparison.OrdinalIgnoreCase)
            || technicalError.Contains("Inspector 指定", StringComparison.OrdinalIgnoreCase))
            return "瓦片资源未配置完整";

        return StartFailed;
    }
}
