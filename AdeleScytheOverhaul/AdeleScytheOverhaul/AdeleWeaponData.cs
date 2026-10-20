#nullable disable

using System;
using dc.hl.types;
using dc.tool;
using dc.tool.skill;

namespace AdeleScytheOverhaul
{
    /// <summary>
    /// 死亡镰刀数值的"双份值"管理：把 CDB 记录（<c>wInfos.strikeChain[3]</c>）在
    /// **原版值** 与 **强化值** 之间来回切，并同步刷新运行时攻击框 <c>areas[3]</c>。
    ///
    /// 为什么要运行时同步（而不是只靠 res.pak 数据补丁）：
    ///   · res.pak 里的 <c>Assets/data.cdb_/weapon/AdeleScythe.json</c> 只提供"强化后"的数值，
    ///     它是模组的默认状态，改不了；
    ///   · 选项菜单里的「范围 ×3 / 伤害 ×3 / 攻速 ×3」开关要能**当场**切回原版，
    ///     所以要在运行时改这份共享记录（与 DamageAuraBoost 的 SyncStats 同一套路）。
    ///
    /// 三个坑（都是本仓库踩过的）：
    ///   1. <c>wInfos</c> 是 **CDB 共享记录**（`Data.Class.weapon` 里那一条），
    ///      改它等于改"所有死亡镰刀" —— 这正是我们要的；但因此写入必须幂等/可逆。
    ///   2. <c>strikeChain[i]</c> 是 Haxe 匿名结构（<c>HashlinkTIndex(0)</c> 的 HaxeVirtual），
    ///      **不能用 `as virtual_xxx_` 取**（ForgeQualityForge 里记过这个坑：转换恒为 null、静默失效）。
    ///      这里一律用 `dynamic` 直读/直写字段。
    ///   3. <c>areas</c> 是武器**构造时**从 strikeChain 建好的快照，
    ///      只改 strikeChain 不会立刻影响已经拿在手上的那把刀 ——
    ///      所以每次攻击前（Weapon.prepare）都要按当前 strikeChain 重算一次 areas[3]。
    /// </summary>
    internal static class AdeleWeaponData
    {
        /// <summary>最后一段（最后一击）在 strikeChain 里的下标。AdeleScythe 共 4 段（0~3）。</summary>
        public const int LastCycle = 3;

        // ===== 原版数值（v35 模板 data.cdb → weapon 表 item == AdeleScythe → strikeChain[3]）=====
        //   这些常量只用于"切回原版"，改游戏版本后要跟着模板核对一次。
        public const double VanillaPower = 80.0;
        public const double VanillaWidth = 8.5;      // shape 0 = 圆：半径 = width * 24 / 2 = 102 px
        public const double VanillaHeight = 6.5;     // 圆形判定不用 height，仅为切回时保持原样
        public const double VanillaOffsetX = -3.7;   // 判定圆心相对英雄的横向偏移（格）
        public const double VanillaOffsetY = -1.0;   // 纵向偏移（格）
        public const double VanillaTick = 6.0;       // 追加判定的间隔（帧）

        /// <summary>
        /// 攻击后摇（秒）。原版 strikeChain[3].lockCtrlAfter = 0.37。
        ///
        /// 它去哪儿了（GamePseudocode/dc.tool.skill/OldSkill.cs）：
        ///   · <c>useWeaponSkillInfos(inf)</c> → <c>setTimers(inf.charge, inf.coolDown, inf.lockCtrlAfter)</c>
        ///     → <c>lockControlsAfterUseS = 0.37</c>
        ///     ★ 这一步只在**武器构造时**跑一次，所以光改 CDB 不会立刻生效，
        ///       必须同时把运行中的技能对象的 <c>lockControlsAfterUseS</c> 也改掉。
        ///   · <c>execute()</c> → <c>if (0 &lt; lockControlsAfterUseS) hero.lockControlFromSkill(0.37)</c>；
        ///     否则走 <c>hero.cancelSkillControlLock()</c>（完全不锁）。
        ///   也就是"挥完这一刀之后 0.37 秒不能动" —— 这就是能明显感觉到的攻击间隔。
        /// </summary>
        public const double VanillaLockCtrlAfter = 0.37;

        /// <summary>去掉后摇时的取值（0 → execute() 走 cancelSkillControlLock()）。</summary>
        public const double NoRecoveryLockCtrlAfter = 0.0;

        // ===== 倍率（与 patch_adele_data.py 里的常量必须一致）=====
        public const double RangeMult = 3.0;         // 范围 ×3
        public const double PowerMult = 3.0;         // 伤害 ×3
        public const double SpeedMult = 3.0;         // 攻速 ×3（同时也是追判间隔的除数）

        /// <summary>当前已经写进 CDB 记录的状态（仅用于日志/排查，不参与判断）。</summary>
        private static int _writtenMask = -1;

        /// <summary>把状态打包成掩码（仅用于日志/排查）。</summary>
        private static int MaskOf(bool range, bool damage, bool fastTick, bool noRecovery)
        {
            int m = 0;
            if (range) m |= 1;
            if (damage) m |= 2;
            if (fastTick) m |= 4;
            if (noRecovery) m |= 8;
            return m;
        }

        /// <summary>
        /// 每次攻击前调用（挂在 <c>Hook_Weapon.prepare</c> 上）。
        ///
        /// **每次都无条件重写一遍**（而不是"变了才写"）：
        ///   · 一份 CDB 记录只有几个字段，写一次的开销可以忽略（每次挥刀才一次）；
        ///   · 换来的是**自愈**：不论记录被谁（重载 CDB / 换关 / 别的模组）改回强化值，
        ///     下一次挥刀都会重新对齐到当前开关状态，不会出现"开关是关的、数值却是强化的"。
        /// </summary>
        public static void Sync(Weapon w, bool range, bool damage, bool fastTick, bool noRecovery, double tickDiv)
        {
            if (w == null || w.wInfos == null) return;

            WriteStrikeChain(w, range, damage, fastTick, noRecovery, tickDiv);
            _writtenMask = MaskOf(range, damage, fastTick, noRecovery);

            // 后摇是"技能对象上的一份拷贝"，构造时从 CDB 抄过来，之后不会再读 CDB ——
            // 所以必须单独把运行中的技能也改掉，否则开关要重新装备武器才生效。
            // 只改最后一击那一段（与数据补丁改的 strikeChain[3] 对应），
            // 其余段保持各自的原版后摇，开关关掉时能原样还回去。
            ApplyLockAfter(w, noRecovery ? NoRecoveryLockCtrlAfter : VanillaLockCtrlAfter);

            // 按当前 strikeChain 刷新运行时攻击框：
            //   · 数值刚被切换 → 立刻生效（不用重新装备武器）
            //   · 刚换关/刚捡到这把刀 → areas 本来就已经是构造时算好的，重算一次也无害
            RebuildArea(w, LastCycle);
        }

        /// <summary>把后摇写进运行中的武器技能（只改最后一击那一段，其余段保持原版）。</summary>
        private static void ApplyLockAfter(Weapon w, double lockAfter)
        {
            ArrayObj skills = w.skills;
            if (skills == null) return;
            if (LastCycle < 0 || LastCycle >= skills.length) return;

            WeaponSkill sk = skills.getDyn(LastCycle) as WeaponSkill;
            if (sk != null) sk.lockControlsAfterUseS = lockAfter;
        }

        /// <summary>把强化值 / 原版值写进共享的 CDB 记录。</summary>
        private static void WriteStrikeChain(Weapon w, bool range, bool damage, bool fastTick, bool noRecovery, double tickDiv)
        {
            ArrayObj chain = w.wInfos.strikeChain;
            if (chain == null || chain.length <= LastCycle) return;

            dynamic inf = chain.getDyn(LastCycle);
            if (inf == null) return;

            // --- 攻击框尺寸 ---
            dynamic area = inf.area;
            if (area != null)
            {
                area.width = range ? VanillaWidth * RangeMult : VanillaWidth;
                area.height = range ? VanillaHeight * RangeMult : VanillaHeight;
                area.offsetCaseX = range ? VanillaOffsetX * RangeMult : VanillaOffsetX;
                area.offsetCaseY = range ? VanillaOffsetY * RangeMult : VanillaOffsetY;
            }

            // --- 攻击后摇（0 = 取消，挥完立刻能再挥）---
            inf.lockCtrlAfter = noRecovery ? NoRecoveryLockCtrlAfter : VanillaLockCtrlAfter;

            // --- 伤害（power 是 Array<Float>，原版武器都只有 1 个元素）---
            double power = damage ? VanillaPower * PowerMult : VanillaPower;
            dynamic pw = inf.power;
            if (pw != null && pw.length > 0)
            {
                pw.setDyn(0, (object)power);
            }
            else
            {
                inf.power = (object)power;      // 兜底：本武器实际不会走到
            }

            // --- 追加判定的间隔（跟着攻速一起变快；除数就是菜单里的攻速倍率）---
            dynamic props = inf.props;
            if (props != null && props.tick != null)
            {
                double div = tickDiv > 0.0 ? tickDiv : 1.0;
                props.tick = (double?)(fastTick ? VanillaTick / div : VanillaTick);
            }
        }

        /// <summary>
        /// 按 <c>wInfos.strikeChain[cycle].area</c> 重算这一段的运行时 <c>Area</c>。
        /// 写法严格照抄原版 <c>_Weapon.__inst_construct__</c>（GamePseudocode/dc.tool/_Weapon.cs:365-419）
        /// 加上 <c>Area</c> 构造函数本身的行为（GamePseudocode/dc.tool/_Area.cs:18-43）：
        ///
        ///   shape 0（圆）：原版传 <c>Area(width * 0.5 * 24, null)</c>，
        ///     而 <c>_Area.__inst_construct__</c> 在 hpx == null 时会把 **widPx 与 heiPx 都设成
        ///     传入值 × 2**（也就是 widPx 存的是"直径"，= width * 24）。★ 这一步很容易漏，
        ///     漏了半径就只剩一半（本来 ×3 会变成实际只有 ×1.5）。
        ///   shape 1（矩形）：原版传 <c>Area(width * 24, height * 24)</c>，原样存入 widPx / heiPx。
        ///
        ///   两种情况最后都是 <c>setRelativePos(owner, widPx * 0.5 + offsetCaseX * 24, offsetCaseY * 24)</c>
        ///   —— 注意这里用的是**存入之后**的 widPx。
        ///
        /// 只重算被模组改过的那一段（strikeChain[3]），其余段落的 Area 保持原版构造结果不动。
        /// </summary>
        private static void RebuildArea(Weapon w, int cycle)
        {
            ArrayObj chain = w.wInfos.strikeChain;
            ArrayObj areas = w.areas;
            if (chain == null || areas == null || w.owner == null) return;
            if (cycle < 0 || cycle >= chain.length || cycle >= areas.length) return;

            dynamic inf = chain.getDyn(cycle);
            if (inf == null) return;

            Area ar = areas.getDyn(cycle) as Area;
            if (ar == null) return;

            dynamic a = inf.area;
            if (a == null) return;

            double width = (double)a.width;
            double height = (double)a.height;
            int shape = (int)a.shape;
            double offX = (double)a.offsetCaseX;
            double offY = (double)a.offsetCaseY;

            if (shape == 0)
            {
                // 圆：单独构造时传的是"半径像素"，Area 内部翻倍存成直径
                double widOrRadiusPx = width * 0.5 * AdeleHitBox.CasePx;
                ar.widPx = widOrRadiusPx * 2.0;
                ar.heiPx = widOrRadiusPx * 2.0;
                ar.isCircle = true;
            }
            else
            {
                ar.widPx = width * AdeleHitBox.CasePx;
                ar.heiPx = height * AdeleHitBox.CasePx;
                ar.isCircle = false;
            }

            ar.setRelativePos(w.owner,
                              ar.widPx * 0.5 + offX * AdeleHitBox.CasePx,
                              offY * AdeleHitBox.CasePx);
        }

        /// <summary>换局 / 卸载时清掉记录的状态（仅日志用；<see cref="Sync"/> 本身是无条件重写的，不依赖它）。</summary>
        public static void ResetWrittenState() => _writtenMask = -1;
    }
}
