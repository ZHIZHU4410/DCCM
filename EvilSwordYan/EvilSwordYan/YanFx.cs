#nullable disable

using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.hl.types;
using dc.tool;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Utilities;

using SysMath = System.Math;

namespace EvilSwordYan
{
    /// <summary>
    /// 「苍」吸附 + 表现层（仿 GojoLimitless 的 GojoUtil / GojoFx，去掉伤害部分）。
    ///
    /// 提供三类能力：
    ///   1) 目标筛选：关卡敌人快照 + 合法敌人过滤（与 SlowOrbBlackHole /
    ///      IndulgenceWallPierce 一致，避免打到墙里的怪 / 已死的怪 / 队友）；
    ///   2) 吸附：沿单位向量把敌人往英雄方向拽（走 bdx/bdy，Boss/精英抗性照常生效）；
    ///   3) 表现：用**原版已有**的 FX 函数叠出"苍"的青色领域 + 贯穿光带 + 火花，
    ///      与武器自带的 HUOA~D 火焰特效叠在一起（蓝 + 火，观感更靓）。
    /// </summary>
    public static class YanFx
    {
        /// <summary>1 格 = 24 像素。</summary>
        public const double TILE_PX = 24.0;

        /// <summary>Entity.hei 是像素，/48 得到竖直中心偏移。</summary>
        private const double HEI_HALF_TILES = 48.0;

        // ================= 配色（0xRRGGBB） =================

        /// <summary>「苍」主色：青蓝。</summary>
        public const int Blue = 0x1560FF;

        /// <summary>亮青白（描边 / 贯穿光带）。</summary>
        public const int Cyan = 0x2FE0FF;

        /// <summary>接近白的核心高光。</summary>
        public const int Flash = 0xAEF4FF;

        // ================= 坐标 =================

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

        public static double DistancePx(Entity a, Entity b)
        {
            if (!CenterPx(a, out double ax, out double ay)) return double.MaxValue;
            if (!CenterPx(b, out double bx, out double by)) return double.MaxValue;
            double dx = ax - bx;
            double dy = ay - by;
            return SysMath.Sqrt(dx * dx + dy * dy);
        }

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

        public static double FacingDir(Entity e)
        {
            try { return e != null && e.dir < 0 ? -1.0 : 1.0; }
            catch { return 1.0; }
        }

        // ================= 目标筛选 =================

        /// <summary>关卡里的敌人快照（命中会改动实体表，必须先快照再处理）。</summary>
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

        /// <summary>合法敌人过滤（存活 / 可标记 / 敌对 / 可探测 / 可命中）。</summary>
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

        // ================= 吸附（不含伤害） =================

        /// <summary>Boss / 精英受到的牵引要额外打折。</summary>
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

        /// <summary>按目标类型取重量系数：Boss/精英 0.4，其余 1.0。</summary>
        public static double WeightFactorFor(Mob mob)
        {
            if (!IsHeavy(mob)) return 1.0;
            return 0.4;
        }

        /// <summary>
        /// 沿单位向量推/拉一个实体（走 bdx/bdy 凹凸位移，
        /// Boss / 精英的 bumpResistance、撞墙判定、动画状态机全部照常工作）。
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

                const double cap = 8.0;      // 单次速度上限（格/帧）
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
        /// 「苍」吸附主入口：把 radiusTiles 内的敌人往英雄方向拽（**不造成任何伤害**）。
        /// 越远的敌人拉得越狠（不然远处的怪根本拽不过来），返回被牵引的数量。
        /// </summary>
        public static int PullEnemies(Entity hero, double radiusTiles, double power)
        {
            if (hero == null || power <= 0.0) return 0;

            double radiusPx = SysMath.Max(1.0, radiusTiles) * TILE_PX;
            if (!CenterPx(hero, out double hx, out double hy)) return 0;

            List<Mob> mobs = SnapshotEnemies(hero);
            int pulled = 0;
            foreach (Mob mob in mobs)
            {
                if (mob == null || mob.destroyed || mob.life <= 0) continue;

                double dist = DistancePx(hero, mob);
                if (double.IsNaN(dist) || dist > radiusPx) continue;
                if (!CenterPx(mob, out double mx, out double my)) continue;
                if (!Normalize(hx - mx, hy - my, out double nx, out double ny)) continue;

                double falloff = 1.0 + (dist / radiusPx);          // 外圈 ×2，贴脸 ×1
                PushAway(mob, nx, ny, power * falloff, WeightFactorFor(mob));
                pulled++;
            }
            return pulled;
        }

        // ================= 穿墙：忽略墙体的攻击范围判定 =================

        /// <summary>
        /// "无视墙体"的攻击范围判定：原版 Weapon.canHit 因为距离/碰撞几何判定失败时，
        /// 这里只用**攻击框尺寸 + 英雄朝向偏移**做矩形判定（不做任何墙体/视线检查），
        /// 于是隔着墙的敌人也能被砍到。
        /// </summary>
        public static bool InAttackAreaIgnoringWalls(Weapon w, Entity e, Area area)
        {
            if (w == null || e == null) return false;

            Hero hero;
            try
            {
                hero = w.owner;
                if (hero == null || hero.destroyed || hero.life <= 0) return false;
            }
            catch { return false; }

            // 没传 area 就取当前连击段的攻击框
            if (area == null)
            {
                try
                {
                    dynamic areas = w.areas;
                    int cyc = w.get_cycle();
                    if (areas != null && cyc >= 0 && cyc < (int)areas.length)
                    {
                        area = (Area)areas.getDyn(cyc);
                    }
                }
                catch { }
            }
            if (area == null) return false;

            try
            {
                if (!CenterPx(hero, out double hx, out double hy)) return false;
                if (!CenterPx(e, out double ex, out double ey)) return false;

                double dir = FacingDir(hero);
                double cx = hx + area.x * dir;                       // 攻击框中心（像素）
                double halfW = SysMath.Max(8.0, area.widPx * 0.5);
                double halfH = SysMath.Max(8.0, area.heiPx * 0.5);

                double rad = 0.0;
                try { rad = e.radius; } catch { }

                return SysMath.Abs(ex - cx) <= halfW + rad
                       && SysMath.Abs(ey - hy) <= halfH + rad;
            }
            catch
            {
                return false;
            }
        }

        // ================= 特效（全部走原版 FX 函数） =================

        private static Fx FxOf(Entity e)
        {
            try { return e?._level?.fx; }
            catch { return null; }
        }

        /// <summary>「苍」的青色球状领域：内外两层时间扭曲球。</summary>
        public static void BlueField(Entity src, double xPx, double yPx, double radiusTiles)
        {
            Fx fx = FxOf(src);
            if (fx == null) return;

            double r = SysMath.Max(0.5, radiusTiles) * TILE_PX;
            try { fx.timeDistorsionStart(xPx, yPx, r, Blue); } catch { }
            try { fx.timeDistorsionEnd(xPx, yPx, r, Cyan); } catch { }
            try { fx.timeDistorsionStart(xPx, yPx, r * 0.45, Flash); } catch { }
        }

        /// <summary>朝英雄面朝方向的贯穿光带（青色 + 亮青白）。</summary>
        public static void BeamLine(Entity src, double xPx, double yPx, double rangePx, double dir)
        {
            Fx fx = FxOf(src);
            if (fx == null) return;

            int d = dir >= 0.0 ? 1 : -1;
            double len = SysMath.Max(1.0, rangePx);
            try { fx.longHitLine(xPx, yPx, d, len, Blue, Cyan); } catch { }
            try { fx.longHitLine(xPx, yPx + 8.0, d, len * 0.7, Flash, Flash); } catch { }
        }

        /// <summary>命中点火花：以命中位置为中心的一圈小爆发。</summary>
        public static void ImpactSparks(Entity src, double xPx, double yPx, double radiusTiles)
        {
            Fx fx = FxOf(src);
            if (fx == null) return;

            double r = SysMath.Max(0.5, radiusTiles) * TILE_PX;
            try { fx.smallIceExplosion(xPx, yPx, Cyan); } catch { }

            const int steps = 6;
            for (int i = 0; i < steps; i++)
            {
                double a = 6.283185307179586 * i / steps;
                try
                {
                    fx.smallIceExplosion(xPx + SysMath.Cos(a) * r, yPx + SysMath.Sin(a) * r, Blue);
                }
                catch { break; }
            }
        }

        /// <summary>被牵引的敌人身上的小标记（让"吸"看得见）。</summary>
        public static void PullMark(Entity src, Mob mob)
        {
            Fx fx = FxOf(src);
            if (fx == null || mob == null) return;
            if (!CenterPx(mob, out double mx, out double my)) return;
            try { fx.smallIceExplosion(mx, my, Flash); } catch { }
        }

        /// <summary>C# string → hashlink String。</summary>
        public static dc.String Hs(string s) => new HashlinkString(s).AsHaxe<dc.String>();
    }
}
