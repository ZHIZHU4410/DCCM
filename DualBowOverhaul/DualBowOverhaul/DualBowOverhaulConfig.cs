using ModCore.Storage;

namespace DualBowOverhaul;

/// <summary>
/// 持久化配置：coremod/config/DualBowOverhaul.json
/// （也可在游戏内「选项 → 模组 → 多槽弓」里改）
/// </summary>
public class DualBowOverhaulConfig
{
    /// <summary>总开关：关掉 = 完全恢复原版多槽弓（3 箭、不能暴击、不追踪）。</summary>
    public bool EnableMod = true;

    /// <summary>
    /// 每次射击的箭数（默认 10）。
    /// 原版 CDB 是 count = 3；带 DoubleBullets 词条时会 ×2，
    /// 模组会自动把基础值减半来保证最终恰好是这个数。
    /// </summary>
    public int ArrowCount = 10;

    /// <summary>无限弹药（原版只有 21 发，10 箭一射只够两次）。</summary>
    public bool InfiniteAmmo = true;

    /// <summary>可以暴击（原版这个武器带 NoCritical 标签）。</summary>
    public bool CanCrit = true;

    /// <summary>暴击倍率（原版没写 critMul，取 1.5）。</summary>
    public double CritMul = 1.5;

    /// <summary>箭矢无视墙体（Bullet.ignoreWalls）。</summary>
    public bool IgnoreWalls = true;

    /// <summary>追踪敌人。</summary>
    public bool Homing = true;

    /// <summary>追踪飞行速度（格/帧）。原版箭速大约就是 1.0。</summary>
    public double HomingSpeed = 1.0;

    /// <summary>
    /// 瞄准敌人"身体中心"而不是脚底。
    /// Entity 的锚点是脚底中心，中心 = cy+yr - hei/2。
    /// </summary>
    public bool AimAtCenter = true;

    /// <summary>
    /// 攻速倍率（默认 3.0）。
    /// 走的是引擎自己的攻速接口：WeaponSkill.getCastSpeed() = base.getCastSpeed() * weapon.get_attackSpeed()，
    /// 所以动画速度、蓄力速度会一起变快。
    /// </summary>
    public double AttackSpeedMul = 3.0;
}

/// <summary>配置入口（coremod/config/DualBowOverhaul.json）。</summary>
public static class DualBowOverhaulKeys
{
    public static Config<DualBowOverhaulConfig> Config { get; } = new("DualBowOverhaul");
}
