using ModCore.Storage;

namespace TridentDash;

/// <summary>
/// 持久化配置：coremod/config/TridentDash.json
/// （也可在游戏内「选项 → 模组 → 深渊三叉戟·常驻冲锋」里改）
/// </summary>
public class TridentDashConfig
{
    /// <summary>总开关：关掉 = 完全恢复原版三叉戟。</summary>
    public bool EnableMod = true;

    /// <summary>
    /// 只打"蓄力冲锋"那一击（cycle 1 = AtkTridentC，也就是一直往前冲的那一下）。
    /// 关掉就回到原版三段连招。
    /// </summary>
    public bool OnlyChargeAttack = true;

    /// <summary>随时可出手：isReady / canStartChargeEarly 恒 true。</summary>
    public bool AlwaysReady = true;

    /// <summary>
    /// 攻击一次 = 无限冲刺：冲刺不会因为帧数用完而结束，
    /// 只有翻滚（或换下武器 / 死亡）才会停下来。
    /// </summary>
    public bool InfiniteDash = true;

    /// <summary>翻滚打断冲刺。</summary>
    public bool InterruptOnRoll = true;

    /// <summary>随时转向：冲锋中可随时改变方向。</summary>
    public bool TurnAnyTime = true;

    /// <summary>冲刺期间按原版 props.tick 节奏结算伤害。</summary>
    public bool AutoDamage = true;

    /// <summary>冲刺速度倍率；基准 = 原版 props.speed（0.5 格/帧）。</summary>
    public double DashSpeedMul = 1.0;

    /// <summary>起步爆发倍率：刚出发那几帧按 巡航速度 × 这个值 冲，读起来像"弹射"。</summary>
    public double LaunchMul = 2.4;

    /// <summary>起步爆发持续帧数（之后缓出到巡航速度）。</summary>
    public int LaunchFrames = 9;

    /// <summary>冲刺拖尾（引擎自带的 fx.dash，原版怪物冲刺用的同一个）。</summary>
    public bool UseTrail = true;

    /// <summary>每隔几帧生成一次拖尾。越小越密、越吃粒子。</summary>
    public int TrailInterval = 3;

    /// <summary>起步冲击波。</summary>
    public bool StartBurst = true;

    /// <summary>击杀叠加冲刺动量：每杀一个怪，冲刺速度永久小幅提升（期间衰减）。</summary>
    public double KillSpeedBoost = 0.12;

    /// <summary>击杀动量上限（0.6 = 最多快 60%）。</summary>
    public double KillBoostMax = 0.6;

    /// <summary>击杀动量每帧衰减系数（越接近 1 保持越久）。</summary>
    public double KillBoostDecay = 0.985;

    /// <summary>冲刺时无视墙体（切 CollisionMode.IgnoreWalls，冲刺结束还原）。</summary>
    public bool IgnoreWalls = true;

    /// <summary>冲刺时获得护盾（每帧刷新 Hero 的无敌 affect）。</summary>
    public bool ShieldWhileDashing = true;

    /// <summary>护盾刷新时长（秒）；冲刺结束后会残留这么久再消失。</summary>
    public double ShieldSeconds = 0.25;

    /// <summary>「无限冲刺」关闭时的冲刺时长（帧）。</summary>
    public int DashFrames = 14;

    /// <summary>允许空中保持冲刺。</summary>
    public bool DashInAir = true;

    /// <summary>过场动画期间暂停冲刺推进。</summary>
    public bool PauseInCinematic = true;
}

/// <summary>配置入口（coremod/config/TridentDash.json）。</summary>
public static class TridentDashKeys
{
    public static Config<TridentDashConfig> Config { get; } = new("TridentDash");
}
