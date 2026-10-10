#nullable disable

using ModCore.Storage;

namespace HolyWaterOverhaul
{
    /// <summary>
    /// HolyWaterOverhaul 的持久化配置（落到 coremod/config/HolyWaterOverhaul.json）。
    ///
    /// ⚠ 关于"持续时间无上限"的取舍，见 <see cref="PoolDurationSeconds"/> 的注释：
    /// 关卡的地面区域效果是一个**只有 512 格的定长对象池**，占满之后再调用
    /// `addAreaAffectS` 会线性扫完整个池才放弃 —— 那才是卡顿的来源。
    /// 所以：**敌人在烧**这件事是无上限的（<see cref="BurnSeconds"/>），
    /// 而**地面火堆**的存活时间是个有限值（<see cref="PoolDurationSeconds"/>）。
    /// </summary>
    public class HolyWaterOverhaulConfig
    {
        /// <summary>总开关（关掉完全回原版）。</summary>
        public bool EnableMod = true;

        // ---------------------------------------------------------------- 普通：散射火堆

        /// <summary>普通词条一次丢几个火堆。原版是按落点附近的目标各建一个（通常 1~2 个）。</summary>
        public int PoolCount = 15;

        /// <summary>散射总宽度（格）。15 个火堆平均铺在这段宽度上。</summary>
        public double PoolSpreadTiles = 13.0;

        /// <summary>每个火堆都贴到该列的地面（`LevelMap.getGroundY`），避免斜坡上悬空或埋进地里。</summary>
        public bool PoolSnapToGround = true;

        /// <summary>
        /// 地面火堆 / 传奇雨柱的存活时间（秒）。这个值会写进 item 数据的 `props.duration`。
        ///
        /// ⚠ **不要设成 99999。** `Level.addAreaAffectS`（`GamePseudocode/dc.pr/Level.cs:7498`）
        /// 是这样找空位的：
        ///
        ///     do { ... } while (!(0.0 >= frames));      // 从 0 号开始线性扫，找 frames &lt;= 0 的空位
        ///     if (num2 >= arrayObj.length) return;      // 扫完都没空位 → 白扫一场直接返回
        ///
        /// 而 `areaAffects` 这个池在 `_Level.__inst_construct__` 里**只建了 512 个**
        /// （`_uid = 511` → 512 项）。地面火堆如果永不消失，池子被占满之后
        /// **每一次** `addAreaAffectS` 都要扫 512 项才发现没位置 —— 传奇雨一帧要建
        /// 几百根雨柱（圆盘半径 `props.distance × 7.2`），乘起来就是那一下卡顿。
        /// </summary>
        public double PoolDurationSeconds = 15.0;

        // ---------------------------------------------------------------- 传奇：全图灼烧

        /// <summary>传奇词条是否灼烧**整个地图**的所有敌人（而不是原版的"视野内"）。</summary>
        public bool LegendBurnWholeMap = true;

        /// <summary>全图灼烧的刷新间隔（秒）。原版 HolyRain 用的是 props.tick = 0.35。</summary>
        public double LegendTick = 0.35;

        /// <summary>
        /// 敌人身上灼烧的持续时间（秒）。走 `entity.setAffectS(88, ...)`，
        /// **不占那个 512 格的地面池**，所以这里可以放心给 99999（≈无上限）。
        /// </summary>
        public double BurnSeconds = 99999.0;

        /// <summary>
        /// 传奇雨圆盘的半径（格）。原版是 `props.distance(2) × 7.2 = 14.4 格`，
        /// 一帧要建 ≈615 根雨柱。全图灼烧由本模组自己那段负责，雨柱只是表现，
        /// 所以这里默认收到 5 格（≈81 根），把那一帧的开销削掉约 87%。
        /// </summary>
        public double RainDiscCells = 5.0;
    }

    public static class HolyWaterKeys
    {
        public static Config<HolyWaterOverhaulConfig> Config { get; } =
            new Config<HolyWaterOverhaulConfig>("HolyWaterOverhaul");
    }
}
