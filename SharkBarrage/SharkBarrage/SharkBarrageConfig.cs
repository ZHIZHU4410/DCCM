#nullable disable

using ModCore.Storage;

namespace SharkBarrage
{
    /// <summary>
    /// SharkBarrage 的持久化配置（落到 coremod/config/SharkBarrage.json，可手改）。
    ///
    /// 原版 Shark（Maw of the Deep / 深渊之口）的三下连击：
    ///   第 1 下 普通挥砍（strikeChain[0] / AtkSharkA）
    ///   第 2 下 普通挥砍 + 震屏破地（strikeChain[1] / AtkSharkB）
    ///   第 3 下 get_cycle() >= 2 → 播 AtkSharkC_NOSHARK 并 throwShark()（strikeChain[2] / AtkSharkC）
    /// 传奇词缀 TripleBullets 让 throwShark() 里的 num 从 1 变 3。
    /// </summary>
    public class SharkBarrageConfig
    {
        /// <summary>总开关。</summary>
        public bool EnableMod = true;

        /// <summary>
        /// 每次攻击都直接丢鲨鱼（= 删掉前两下平a）。
        /// 实现方式是把这把武器的 <c>get_cycle()</c> 恒定为 2，
        /// 于是 <c>Shark.onExecute()</c> 永远走"第三下"那条分支。
        /// </summary>
        public bool AlwaysThrow = true;

        // ---------------------------------------------------------------- 数量

        /// <summary>普通词条一次丢几只。原版 1。</summary>
        public int SharkCount = 20;

        /// <summary>传奇词缀 <c>TripleBullets</c> 一次丢几只。原版 3。</summary>
        public int LegendSharkCount = 40;

        // ---------------------------------------------------------------- 范围

        /// <summary>普通词条的飞行距离倍数。原版 maxDist = 11 格 × 24 = 264px。</summary>
        public double RangeMult = 2.0;

        /// <summary>传奇词条的飞行距离倍数。</summary>
        public double LegendRangeMult = 5.0;

        /// <summary>鲨鱼是否无视墙体（原版 <c>Bullet.ignoreWalls</c>，默认 false）。</summary>
        public bool IgnoreWalls = true;

        /// <summary>
        /// 扇形总角度（度）。以瞄准角为中心左右铺开。
        ///
        /// 原版 3 只时是每只 0.15rad ≈ 8.6°，总共 17°；20/40 只沿用那个间距会散成一整个圆，
        /// 所以改成"总角度固定，只数越多每只越密"。
        /// 20 只铺 60° 时每只只差 3.2°，出膛一大坨糊在一起 → 默认给到 150°（每只 7.9°）。
        /// </summary>
        public double FanAngleDeg = 150.0;

        /// <summary>是否播枪口特效（原版每只都放一次，20 只叠起来太吵，这里只放一次）。</summary>
        public bool PlayMuzzleFx = true;
    }

    public static class SharkKeys
    {
        public static Config<SharkBarrageConfig> Config { get; } = new Config<SharkBarrageConfig>("SharkBarrage");
    }
}
