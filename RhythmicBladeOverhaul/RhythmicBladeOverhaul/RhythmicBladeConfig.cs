using ModCore.Storage;

namespace RhythmicBladeOverhaul;

/// <summary>
/// 持久化配置：coremod/config/RhythmicBladeOverhaul.json
/// （也可在游戏内「选项 → 模组 → 节奏之刃·节奏打击」里改）
/// </summary>
public class RhythmicBladeConfig
{
    /// <summary>总开关：关掉 = 完全恢复原版节奏之刃。</summary>
    public bool EnableMod = true;

    /// <summary>
    /// 固定倍率：判定范围与特效都乘这个值（默认 3.0）。
    ///
    /// 原版节奏打击的判定半径是 6 格（getRealCurArea 里硬编码 new Area(144.0, null)），
    /// 乘 3 之后就是 18 格半径的 360° 圆形打击。
    /// </summary>
    public double Multiplier = 3.0;

    /// <summary>
    /// 无视墙体。节奏打击是以主角为圆心的 360° 圆形打击，
    /// 开启后命中只算"圆心到目标的距离"，不再做原版那条 Bresenham 墙体检测。
    /// </summary>
    public bool EnableWallPierce = true;

    /// <summary>伤害也跟着倍率涨（最终伤害 × Multiplier）。</summary>
    public bool ScaleDamage = true;

    /// <summary>特效也跟着倍率涨（把 fxProps.fxScale 设成倍率）。</summary>
    public bool ScaleEffect = true;

    /// <summary>
    /// 特效缩放微调（默认 1.0）。觉得特效比实际判定范围大就调小（0.8），小就调大（1.2）。
    /// </summary>
    public double EffectScaleTweak = 1.0;
}

/// <summary>配置入口（coremod/config/RhythmicBladeOverhaul.json）。</summary>
public static class RhythmicBladeKeys
{
    public static Config<RhythmicBladeConfig> Config { get; } = new("RhythmicBladeOverhaul");
}
