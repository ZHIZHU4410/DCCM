#nullable disable

using System;
using dc;
using dc.en;
using dc.hl.types;
using dc.pr;
using dc.tool;
using dc.tool.weap;
using ModCore.Utilities;

namespace TombstonePassive
{
    /// <summary>
    /// 「灾厄」触发核心 —— 把原版**第三下平 A** 的那套逻辑，原样搬到"以英雄为中心"的被动上。
    ///
    /// 原版第三下干了什么（GamePseudocode/dc.tool.weap/Tombstone.cs，hitFromWeapon 的 isLastCycle 分支）：
    /// <code>
    /// double r = get_curSkillInf().props.range - 0.5;          // strikeChain[2].props.range = 12 格
    /// fx.radiusExplosion(x, y, r, 5587165, null);              // ① 半径爆环（以命中点为心）
    /// var targetMobs = [];
    /// foreach (Mob mob in hero._team.opponentMobsIterator) {    // ② 遍历所有敌对 mob
    ///     if (mob == e) continue;                               //    命中目标本身不算
    ///     if (dist(mob, e) &lt;= props.range * 24) {                //    半径内
    ///         targetMobs.push(mob);
    ///         mob.customStatuses.remove("affectTombstone");     //    ③ 挂"灾厄"状态图标
    ///         mob.refreshIcons();
    ///         mob.customStatuses.push("affectTombstone");
    ///         mob.refreshIcons();
    ///     }
    /// }
    /// doGlas(0, targetMobs);                                    // ④ 每个目标头顶落墓碑
    /// </code>
    ///
    /// 我们只把"中心"从**被命中的敌人**换成**英雄自己**，其余 ①②③④ 全部照抄。
    ///
    /// ④ 的 <c>doGlas(wave, targetMobs)</c>（Tombstone.cs:575）会对每个目标：
    ///   · 用 <c>hero.getDiminishingFactor(69, 1, item.props.limit, null)</c> 算伤害比例；
    ///   · 在目标头顶 <c>props.height</c> 格处生成 <c>TombstoneEntity</c>（doomedMob = 该目标）；
    ///   · 墓碑落地 → <c>glasDamage()</c> → 对目标 <c>_AttackUtils.hit</c> + <c>setAffectS(8, duration3)</c>；
    ///   · 再 <c>doWeaponGlas()</c> 连锁到它周围的怪（这就是"灾厄扩散"）。
    /// </summary>
    internal static class TombstoneDoom
    {
        /// <summary>1 格 = 24 像素。</summary>
        public const double CasePx = 24.0;

        /// <summary>
        /// <c>getDiminishingFactor</c> 的 key。原版墓碑灾厄用的就是 69
        /// （Tombstone.cs:631 <c>hero2.getDiminishingFactor(69, 1, absoluteMaxUses, null)</c>），
        /// 全工程只有这一处用 69，所以拦截它是安全的。
        /// </summary>
        public const int DoomFactorKey = 69;

        /// <summary>原版 <c>fx.radiusExplosion</c> 用的颜色（Tombstone.cs:1090）。</summary>
        public const int DoomFxColor = 5587165;

        /// <summary>原版给"被灾厄标记"的怪挂的 customStatus 名字（用于图标/提示）。</summary>
        public const string DoomStatus = "affectTombstone";

        /// <summary>找不到武器数据时的兜底半径（格），等于 v35 的 strikeChain[2].props.range。</summary>
        public const double DefaultRadiusCells = 12.0;

        // ================================================================ 装备查找

        /// <summary>
        /// 找出英雄**主手/副手**里装备的墓碑。
        /// 先走 <c>HeroWeaponsManager.findInMain(Tombstone.Class)</c>，不行再扫一遍 <c>mainWeapons</c>。
        /// </summary>
        public static Tombstone FindTombstone(Hero hero)
        {
            if (hero == null || hero.destroyed) return null;

            try
            {
                dc.tool.hero.HeroWeaponsManager wm = hero.weaponsManager;
                if (wm == null) return null;

                Weapon w = wm.findInMain(Tombstone.Class);
                if (w is Tombstone t) return t;

                ArrayObj mains = wm.mainWeapons;
                if (mains != null)
                {
                    for (int i = 0; i < mains.length; i++)
                    {
                        if (mains.getDyn(i) is Tombstone t2) return t2;
                    }
                }
            }
            catch { }

            return null;
        }

        // ================================================================ 触发

        /// <summary>
        /// 以英雄为中心触发一次灾厄。
        /// 返回被标记/被砸的目标数量（0 = 附近没怪，只放了视觉爆环）。
        /// </summary>
        public static int Trigger(Hero hero, Tombstone tomb, double radiusCells, bool heroTombstone, Action<string> log)
        {
            if (hero == null || hero.destroyed || hero.life <= 0) return 0;
            if (tomb == null || tomb.destroyed) return 0;

            Level level = hero._level;
            if (level == null) return 0;

            if (radiusCells <= 0.0) radiusCells = DefaultRadiusCells;

            // ---- 英雄身体中心（像素），与原版对"命中点"的取法一致 ----
            double x = ((double)hero.cx + hero.xr) * CasePx;
            double y = ((double)hero.cy + hero.yr) * CasePx - hero.hei * 0.5;

            // ---- ① 半径爆环：原版 r = props.range - 0.5，durationMax 传 null 时内部默认 2.0 ----
            try
            {
                Fx fx = level.fx;
                if (fx != null)
                {
                    double durationMax = 2.0;      // 与 Fx.radiusExplosion 里"durationMax 为空"的分支一致
                    fx.radiusExplosion(x, y, radiusCells - 0.5, DoomFxColor, ref durationMax);
                }
            }
            catch { }

            // ---- ② 收集半径内的敌对 mob ----
            // 完全照抄原版的遍历方式：hero._team.opposingTeams → 每个敌方 Team 的 asMobs，
            // 再按"身体中心像素距离 <= range*24"筛（Tombstone.cs:1142-1194 就是这个逻辑）。
            //
            // 为什么不用 TargetHelper：它的 currents 里存的是 virtual_e_out_score_ {e, score, out}
            // 这种包装（getBest 也只返回 .e 一个），取"全部目标"要拆包 + 判 @out，既绕又容易踩雷。
            ArrayObj targets = null;
            int marked = 0;

            try
            {
                Team team = hero._team;
                ArrayObj opposing = team?.opposingTeams;
                if (opposing != null)
                {
                    double radiusPx = radiusCells * CasePx;

                    for (int ti = 0; ti < opposing.length; ti++)
                    {
                        Team enemyTeam = opposing.getDyn(ti) as Team;
                        if (enemyTeam == null) continue;

                        ArrayObj mobs = enemyTeam.asMobs;
                        if (mobs == null) continue;

                        for (int mi = 0; mi < mobs.length; mi++)
                        {
                            Mob m = mobs.getDyn(mi) as Mob;
                            if (m == null || m.destroyed || m.life <= 0) continue;
                            if (DistancePx(m, hero) > radiusPx) continue;   // 半径外 → 不是"周围"

                            if (targets == null)
                            {
                                // 注意：Hashlink 的动态数组必须用 ArrayUtils.CreateDyn() 建，
                                // 直接 new ArrayObj() 会在 VM 里崩（不是 .NET 异常，抓不住）。
                                targets = (ArrayObj)ArrayUtils.CreateDyn().array;
                            }

                            targets.push((object)m);
                            MarkDoomed(m);
                            marked++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                try { log?.Invoke($"[TombstonePassive] 收集目标失败: {ex.Message}"); } catch { }
            }

            // ---- ④ 落墓碑：原版 doGlas(0, targetMobs) ----
            if (targets != null && targets.length > 0)
            {
                try { tomb.doGlas(0, targets); }
                catch (Exception ex)
                {
                    try { log?.Invoke($"[TombstonePassive] doGlas 失败: {ex.Message}"); } catch { }
                }
            }

            // ---- 附加视觉：英雄脚下也砸一块墓碑（默认关）----
            if (heroTombstone)
            {
                try { SpawnHeroTombstone(level, hero, tomb); }
                catch (Exception ex)
                {
                    try { log?.Invoke($"[TombstonePassive] 英雄处墓碑失败: {ex.Message}"); } catch { }
                }
            }

            return marked;
        }

        /// <summary>两实体"身体中心"之间的像素距离（与原版 Tombstone.cs:1153-1177 的取法一致）。</summary>
        private static double DistancePx(Entity a, Entity b)
        {
            double ax = ((double)a.cx + a.xr) * CasePx;
            double ay = ((double)a.cy + a.yr) * CasePx - a.hei * 0.5;
            double bx = ((double)b.cx + b.xr) * CasePx;
            double by = ((double)b.cy + b.yr) * CasePx - b.hei * 0.5;

            double dx = ax - bx;
            double dy = ay - by;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>③ 给怪挂上"灾厄"状态图标（原版是先 remove 再 push，等于把图标挪到末尾）。</summary>
        private static void MarkDoomed(Mob m)
        {
            try
            {
                ArrayObj cs = m.customStatuses;
                if (cs == null) return;

                dc.String key = StringUtils.AsHaxeString(DoomStatus);
                cs.remove(key);
                m.refreshIcons();
                cs.push(key);
                m.refreshIcons();
            }
            catch { }
        }

        /// <summary>
        /// 在英雄位置砸一块墓碑（纯视觉，<c>doomedMob = null</c> 的构造分支：
        /// 会自由落体、落地碎掉、放 <c>weapon_tombstone_hit3.wav</c>，但不结算伤害）。
        ///
        /// 两个坑（MobBuffCombo 踩过）：
        ///   · <c>modelNum</c> 不能是 0 —— initGfx 用 "Weapon_Tombstone" + (modelNum*10) 拼帧名，
        ///     0 拼不出帧 → spr == null → 后面必崩；
        ///   · Hero 不能当 <c>doomedMob</c>（glasDamage 会把它当 Mob 处理并 kill()）。
        /// </summary>
        private static void SpawnHeroTombstone(Level level, Hero hero, Tombstone tomb)
        {
            int modelNum = ResolveModelNum();

            double dmgRatioValue = 0.0;
            int waveValue = -1;

            InventItem item = null;
            try { item = tomb.item; } catch { }

            TombstoneEntity stone = new TombstoneEntity(
                level, hero.cx, hero.cy, item, hero, modelNum, hero.dir,
                null,
                // 注意：dc.tool 里有个【非泛型】的 Ref 类，会和 HaxeProxy.Runtime.Ref<T> 撞名，
                // 所以这里必须写全名。
                HaxeProxy.Runtime.Ref<double>.From(ref dmgRatioValue),
                HaxeProxy.Runtime.Ref<int>.From(ref waveValue),
                null);

            stone.modelNum = modelNum;      // 必须先设，initGfx 靠它拼帧名
            try { stone.init(); }
            catch { return; }

            if (stone.spr == null)
            {
                try { stone.destroy(); } catch { }
                return;
            }

            stone.cy = hero.cy - 2;         // 从头顶两格掉下来
            stone.yr = hero.yr;
            stone.hasGravity = true;
            stone.killOnLand = true;
        }

        /// <summary>墓碑外观编号：原版 <c>item.props.count = 5</c> → modelNum 取 1..4。</summary>
        private static int ResolveModelNum()
        {
            int count = 5;
            try
            {
                dynamic row = Data.Class.item.byId.get(StringUtils.AsHaxeString("Tombstone"));
                if (row != null && row.props != null && row.props.count != null)
                {
                    int c = (int)row.props.count;
                    if (c >= 2) count = c;
                }
            }
            catch { }

            int max = count - 1;
            if (max < 1) max = 1;
            return Random.Shared.Next(1, max + 1);
        }
    }
}
