#nullable disable
using System;
using dc;
using dc.en;
using dc.haxe.ds;
using dc.hl.types;
using dc.tool.atk;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;

using SysMath = System.Math;

namespace GojoLimitless
{
    /// <summary>
    /// 通用工具：坐标换算、目标筛选、伤害投递、affect 简化封装。
    ///
    /// 目标筛选条件与仓库里其它模组（SlowOrbBlackHole / ClusterBombClearMap /
    /// IndulgenceWallPierce）保持一致，避免打到墙里的怪 / 已死的怪 / 队友。
    /// </summary>
    public static class GojoUtil
    {
        /// <summary>1 格 = 24 像素。</summary>
        public const double TILE_PX = 24.0;

        /// <summary>Entity.hei 是像素，/48 得到竖直中心偏移（和 SlowOrbBlackHole 一致）。</summary>
        private const double HEI_HALF_TILES = 48.0;

        // ------------------------------------------------------------ 坐标

        /// <summary>实体中心（像素）。</summary>
        public static bool CenterPx(Entity e, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            if (e == null) return false;
            try
            {
                x = ((double)e.cx + e.xr) * TILE_PX;
                y = ((double)e.cy + e.yr - e.hei / HEI_HALF_TILES) * TILE_PX;
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>两实体中心距离（像素）。</summary>
        public static double DistancePx(Entity a, Entity b)
        {
            if (!CenterPx(a, out double ax, out double ay)) return double.MaxValue;
            if (!CenterPx(b, out double bx, out double by)) return double.MaxValue;
            double dx = ax - bx;
            double dy = ay - by;
            return SysMath.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>实体朝向的单位向量（攻击方向）。</summary>
        public static void Facing(Entity e, out double fx, out double fy)
        {
            fx = 1.0;
            fy = 0.0;
            if (e == null) return;
            try { fx = e.dir >= 0 ? 1.0 : -1.0; } catch { }
        }

        /// <summary>归一化向量（零向量返回 false）。</summary>
        public static bool Normalize(double x, double y, out double nx, out double ny)
        {
            double len = SysMath.Sqrt(x * x + y * y);
            if (len < 1e-6 || double.IsNaN(len))
            {
                nx = 0.0;
                ny = 0.0;
                return false;
            }
            nx = x / len;
            ny = y / len;
            return true;
        }

        /// <summary>目标是否落在"以 origin 为顶点、朝 fwd 方向、总张角 spreadRad 的锥形"内。</summary>
        public static bool InCone(double ox, double oy, double fx, double fy,
                                  double tx, double ty, double rangePx, double spreadRad)
        {
            double dx = tx - ox;
            double dy = ty - oy;
            if (dx * dx + dy * dy > rangePx * rangePx) return false;
            if (!Normalize(dx, dy, out double nx, out double ny)) return false;

            double dot = nx * fx + ny * fy;
            if (dot < 0.0) return false;
            // dot >= cos(spread/2)
            return dot >= SysMath.Cos(spreadRad * 0.5);
        }

        // ------------------------------------------------------------ 目标筛选

        /// <summary>关卡里的敌人快照（命中会改动实体表，必须先快照再打）。</summary>
        public static List<Mob> SnapshotEnemies(Entity owner)
        {
            var list = new List<Mob>();
            if (owner == null) return list;

            ArrayObj entities;
            try { entities = owner._level?.entities; }
            catch { return list; }
            if (entities == null) return list;

            int len = entities.length;
            for (int i = 0; i < len; i++)
            {
                object raw;
                try { raw = entities.getDyn(i); }
                catch { break; }

                if (raw is not Mob mob) continue;
                if (!IsValidEnemy(owner, mob)) continue;
                list.Add(mob);
            }
            return list;
        }

        /// <summary>
        /// 与 SlowOrbBlackHole.IsValidTarget 完全一致的合法敌人过滤。
        ///
        /// ⚠️ 整个判断体包在 try 里：这几个原版方法（<c>_targetable</c> /
        /// <c>canBeDetected</c> / <c>canBeHitBy</c>）在特殊实体上有可能抛，
        /// 而调用点（迟滞领域每 0.1 秒扫一遍实体表）如果在循环中途抛出去，
        /// 就会退化成"每帧一次 Console.WriteLine"的日志洪水 + 帧时间抖动。
        /// 这里吞掉并当作"不是合法目标"。
        /// </summary>
        public static bool IsValidEnemy(Entity owner, Mob mob)
        {
            if (owner == null || mob == null) return false;
            try
            {
                if (mob.destroyed || mob.life <= 0) return false;
                if (!mob._targetable) return false;
                if (!owner.isOpponent(mob)) return false;
                if (!mob.canBeDetected()) return false;
                if (!mob.canBeHitBy(owner)) return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ------------------------------------------------------------ 伤害

        /// <summary>
        /// 英雄当前的"等级"（三个属性等级之和），喂给 <c>AttackData.sourceTier</c>。
        ///
        /// 原版 <c>_AttackUtils.cs:129</c> 的伤害缩放是这么分叉的：
        /// <code>
        /// double num = atk.useHeroScaling
        ///     ? _Const.scaleHeroValueToTier(dmg, atk.sourceTier)   // 吃英雄属性
        ///     : _Const.scaleMobValueToTier(dmg, atk.sourceTier);   // 吃怪物层级
        /// </code>
        /// 所以只要把 <c>useHeroScaling</c> 打开、再把英雄的层级传进去，
        /// 招式伤害就会跟着升级卷轴一起涨（和原版武器同一套公式）。
        /// </summary>
        public static int HeroTier(Hero hero)
        {
            if (hero == null) return 1;
            try
            {
                int sum = 0;
                try { sum += hero.brutalityTier; } catch { }
                try { sum += hero.tacticTier; } catch { }
                try { sum += hero.survivalTier; } catch { }
                return sum > 0 ? sum : 1;      // 0 级时给 1，避免退化
            }
            catch { return 1; }
        }

        /// <summary>
        /// 走原版攻击管线打一发伤害（击杀计数 / 细胞 / 掉落 / 死亡流程全部正常）。
        ///
        /// <paramref name="pushX"/>/<paramref name="pushY"/> 是单位向量，配合
        /// <paramref name="pushPower"/> 可以顺带把敌人推开（原版 bump 抗性照常生效）。
        ///
        /// <paramref name="heroScaling"/> = true 时伤害跟着**升级卷轴**一起涨
        /// （走 <c>scaleHeroValueToTier</c>），false 时是固定值（走 <c>scaleMobValueToTier</c>）。
        /// </summary>
        public static bool DealDamage(Hero hero, Mob mob, double dmg,
                                      double pushX = 0.0, double pushY = 0.0, double pushPower = 0.0,
                                      int tier = 0, bool heroScaling = true)
        {
            if (hero == null || mob == null || mob.destroyed || mob.life <= 0) return false;
            try
            {
                AttackData atk = AttackUtils.Class.createFromHero.Invoke(hero, (dynamic)dmg, null);
                atk.useHeroScaling = heroScaling;
                // 吃英雄加成就传英雄层级；否则保持原来的固定值语义
                atk.sourceTier = heroScaling
                    ? (tier > 0 ? tier : HeroTier(hero))
                    : (tier > 0 ? tier : 1);
                AttackUtils.Class.hit.Invoke(atk, mob);

                if (pushPower > 0.0 && (pushX != 0.0 || pushY != 0.0))
                {
                    PushAway(mob, pushX, pushY, pushPower);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "DealDamage 失败");
                return false;
            }
        }

        // ------------------------------------------------------------ 位移

        /// <summary>
        /// 沿单位向量推/拉一个实体。
        ///
        /// 走 <c>bdx/bdy</c>（凹凸位移）而不是直接改坐标：这样 Boss / 精英的
        /// <c>bumpResistance</c>、撞墙判定、动画状态机全部照常工作。
        ///
        /// <paramref name="resistFactor"/> 是在原版抗性之外**再乘**的一道系数，
        /// 用来额外压 Boss 的牵引/击退（调用点一般传 <see cref="WeightFactorFor"/>）。
        /// </summary>
        public static void PushAway(Mob mob, double nx, double ny, double power, double resistFactor = 1.0)
        {
            if (mob == null || mob.destroyed) return;
            try
            {
                double resist = 1.0;
                try { resist = 1.0 - mob.getBumpResistanceFactor(); } catch { }
                if (resist <= 0.0) return;

                double f = power * resist * resistFactor;
                if (double.IsNaN(f) || double.IsInfinity(f) || f == 0.0) return;

                // 单次给的速度上限：「赤」的击飞需要更大的瞬时速度才看得出来，
                // 8 格/帧已经远超正常击退，再高就纯属飞天了。
                double cap = 8.0;
                double vx = mob.bdx + nx * f;
                double vy = mob.bdy + ny * f;
                if (vx > cap) vx = cap; else if (vx < -cap) vx = -cap;
                if (vy > cap) vy = cap; else if (vy < -cap) vy = -cap;

                mob.bdx = vx;
                mob.bdy = vy;
            }
            catch { }
        }

        /// <summary>
        /// 目标是不是 Boss / 精英 —— 它们受到的牵引与击退要额外打折。
        ///
        /// <c>dc.en.mob.Boss</c> 是原版所有 Boss 的基类，<c>Mob.elite</c> 是精英标记。
        /// </summary>
        public static bool IsHeavy(Mob mob)
        {
            if (mob == null) return false;
            try
            {
                if (mob.elite) return true;
                return mob is dc.en.mob.Boss;
            }
            catch { return false; }
        }

        /// <summary>按目标类型取"重量系数"：Boss/精英用配置值（默认 0.4），其余 1.0。</summary>
        public static double WeightFactorFor(Mob mob)
        {
            if (!IsHeavy(mob)) return 1.0;
            try
            {
                double f = Cfg.V.BossPullPushFactor;
                if (double.IsNaN(f) || f < 0.0) return 0.0;
                if (f > 1.0) return 1.0;
                return f;
            }
            catch { return 1.0; }
        }

        /// <summary>沿水平方向给击退（正 = 向右）。</summary>
        public static void PushHorizontal(Mob mob, double dxSign, double power)
        {
            PushAway(mob, dxSign >= 0.0 ? 1.0 : -1.0, 0.0, power);
        }

        // ------------------------------------------------------------ affect

        /// <summary>
        /// 刷新一条 affect 到指定层数。
        ///
        /// ⚠️ **层数是由 <paramref name="value"/> 决定的，不是调用次数**：
        /// <c>Entity.setAffectS</c> 里是
        /// <c>num15 = (int)val; while (num2 &lt; num15 - 1) { push 同一条 }</c>。
        /// 传 <c>value = 1.0</c> 只会得到 1 层，循环调用也不会叠上去
        /// （affect 133 甚至还有"桶非空就直接 return"的早退）。
        /// 所以这里清空之后**只调一次**，把层数塞进 value。
        ///
        /// 注意这条规则只对"按层数算效果"的 affect（如 133 解冻层）成立；
        /// 对 28（定身）这类 <c>DifferentValue</c> 分组的 affect，
        /// 传 1 层即可，<paramref name="value"/> 是它的效果数值。
        /// </summary>
        public static void ApplyAffectStacks(Entity target, int affectId, int stacks, double sec, double value)
        {
            if (target == null || target.destroyed || target.life <= 0) return;
            if (stacks <= 0 || sec <= 0.0) return;

            try
            {
                target.removeAllAffects(affectId);
                for (int i = 0; i < stacks; i++)
                {
                    double v = value;
                    target.setAffectS(affectId, sec, ref v, null);
                }
            }
            catch { }
        }

        /// <summary>只叠一层（不清空）。定身 / 无敌这类"存在即生效"的 affect 用这个。</summary>
        public static void StackAffect(Entity target, int affectId, double sec, double value)
        {
            if (target == null || target.destroyed || target.life <= 0) return;
            try
            {
                double v = value;
                target.setAffectS(affectId, sec, ref v, null);
            }
            catch { }
        }

        // ------------------------------------------------------------ 字符串

        /// <summary>C# string → hashlink String（游戏 API 只认后者）。</summary>
        public static dc.String Hs(string s) => new HashlinkString(s).AsHaxe<dc.String>();

        /// <summary>从物品 id 判断是不是本模组的「Limitless」（兼容 hashlink 的 "id=Xxx" 形式）。</summary>
        public static bool ItemIdIs(object rawId, string expected)
        {
            if (rawId == null) return false;
            string s = rawId.ToString();
            if (string.IsNullOrEmpty(s)) return false;
            int eq = s.IndexOf('=');
            if (eq >= 0) s = s.Substring(eq + 1);
            s = s.Trim().Trim('"', '\'', ' ');
            return string.Equals(s, expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>取 InventItem 的物品 id（拿不到返回 null）。</summary>
        public static string ItemId(dc.tool.InventItem item)
        {
            if (item == null) return null;
            try
            {
                dynamic data = item._itemData;
                object id = data.id;
                string s = id?.ToString();
                if (string.IsNullOrEmpty(s)) return null;
                int eq = s.IndexOf('=');
                if (eq >= 0) s = s.Substring(eq + 1);
                return s.Trim().Trim('"', '\'', ' ');
            }
            catch
            {
                return null;
            }
        }
    }
}
