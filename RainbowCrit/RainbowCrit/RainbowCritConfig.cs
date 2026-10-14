using ModCore.Storage;

namespace RainbowCrit;

/// <summary>
/// 持久化配置，存成 coremod/config/RainbowCrit.json，可手改，也可在游戏内
/// 「选项 → 模组 → 全局彩色暴击」里切换。
/// </summary>
public class RainbowCritConfig
{
    /// <summary>总开关：关掉 = 完全恢复原版伤害数字。</summary>
    public bool EnableMod = true;

    /// <summary>
    /// true（默认）= 只美化「玩家打出的」暴击数字（含玩家的陷阱 / 宠物 / 召唤物造成的暴击）。
    /// false       = 任何暴击都变彩色（怪物暴击玩家时，玩家头上飘的也是彩虹字）。
    /// </summary>
    public bool PlayerCritsOnly = true;

    /// <summary>
    /// true = 不接管暴击数字，完全走原版路径（调试 / 开关前后对比用）。
    /// false（默认）= 暴击数字显示为 Hotline Miami 风格的彩虹字。
    /// </summary>
    public bool DisableRainbow = false;
}

/// <summary>配置入口（coremod/config/RainbowCrit.json）。</summary>
public static class RainbowCritKeys
{
    public static Config<RainbowCritConfig> Config { get; } = new("RainbowCrit");
}
