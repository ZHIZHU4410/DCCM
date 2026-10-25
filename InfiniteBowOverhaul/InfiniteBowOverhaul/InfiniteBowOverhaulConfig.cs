using ModCore.Storage;

namespace InfiniteBowOverhaul;

/// <summary>
/// 持久化配置：coremod/config/InfiniteBowOverhaul.json
/// （也可在游戏内「选项 → 模组 → 无尽弓 · 环绕箭」里改）
/// </summary>
public class InfiniteBowOverhaulConfig
{
    /// <summary>总开关：关掉 = 完全恢复原版无尽弓。</summary>
    public bool EnableMod = true;

    /// <summary>
    /// 每次射击的箭数（默认 10）。
    /// 原版 InfiniteBow.shoot() 只会造 1 支箭（BaseBow.shoot 不造箭），
    /// 所以这里由模组补齐剩下的 9 支，每支都走独立轨迹。
    /// </summary>
    public int ArrowCount = 10;

    /// <summary>
    /// 最大环绕半径（格）。原版 DamageAura 的 props.distance = 5 格，两倍即 10 格。
    /// 箭不会都贴在这个外圈上 —— 每支箭自己的轨道半径在
    /// [InnerRadiusFrac, 1.0] × 这个值 之间随机。
    /// </summary>
    public double OrbitRadiusCells = 10.0;

    /// <summary>
    /// 内侧半径占比（0~1）。0.3 = 每支箭的轨道半径在最大半径的 30%~100% 之间随机，
    /// 这样箭会散布在整个范围里，而不是全挤在最外圈。
    /// </summary>
    public double InnerRadiusFrac = 0.30;

    /// <summary>
    /// 二级摆动幅度占比（0~1）。在主轨道上再叠加一个小圆，让轨迹不闭合、
    /// 看起来像"电子云"那样无规则。0 = 纯圆轨道。
    /// </summary>
    public double WobbleFrac = 0.18;

    /// <summary>环绕角速度下限（弧度/秒）。</summary>
    public double SpinMin = 1.2;

    /// <summary>环绕角速度上限（弧度/秒）。每支箭在这区间里随机取一个，方向也随机。</summary>
    public double SpinMax = 2.8;

    /// <summary>同时环绕的箭矢上限（防止无限累积把帧数拖死）。超出上限的新箭按原版飞行并自然消失。</summary>
    public int MaxOrbitingBullets = 80;

    /// <summary>箭矢环绕时不消耗射程（打不到敌人就一直留着）。</summary>
    public bool KeepAlive = true;

    /// <summary>箭矢无视墙体。</summary>
    public bool IgnoreWalls = true;

    /// <summary>
    /// 箭矢在过场动画里不被清掉/不掉落。
    /// 原版 _GameCinematic 会 destroy 所有 destroyOnCinematic 的子弹。
    /// </summary>
    public bool SurviveCinematic = true;

    /// <summary>攻速倍率（引擎的 Weapon.get_attackSpeed）。</summary>
    public double AttackSpeedMul = 3.0;

    /// <summary>任意箭矢都能暴击（原版只有 cycle 2 的最后一射能暴击）。</summary>
    public bool CanCrit = true;

    /// <summary>暴击倍率。</summary>
    public double CritMul = 1.5;
}

/// <summary>配置入口（coremod/config/InfiniteBowOverhaul.json）。</summary>
public static class InfiniteBowOverhaulKeys
{
    public static Config<InfiniteBowOverhaulConfig> Config { get; } = new("InfiniteBowOverhaul");
}
