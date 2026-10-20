#nullable disable

using System;
using dc;
using dc.en;
using dc.tool;

namespace ThrowingKnifeOverhaul
{
    /// <summary>
    /// 飞刀的"锁定追击"。
    ///
    /// 原版飞刀其实**只在出手那一刻**瞄一次：<c>ThrowingKnife.onExecute</c> 里用
    /// <c>TargetHelper</c> 选出目标，把 <c>atan2(目标 - 出手点)</c> 当角度
    /// 塞进 <c>new Bullet(hero, atk, ang, 1.2, "smallArrow")</c> —— 之后子弹就沿直线飞。
    /// 而且那套选目标里有 <c>filterBySight(...)</c>，**墙后的敌人会被排除**。
    ///
    /// 所以这里自己实现"飞行中持续修正航向"：
    ///   · 目标：每一帧用游戏自己的 <c>TargetHelper</c> 重新找最近的怪
    ///     （<b>刻意不调 filterBySight</b>，这样隔墙也能锁）；
    ///   · 转向：把速度矢量 (dx, dy) 朝目标中心限速转一点、**速率保持不变**
    ///     —— 与游戏自带 <c>HomingSoul.fixedUpdate</c>
    ///     （GamePseudocode/dc.en.bu/HomingSoul.cs:64-122）完全同思路：
    ///        cur  = atan2(dy + bdy, dx + bdx)
    ///        want = atan2(目标中心y - 自己中心y, 目标中心x - 自己中心x)   // 中心都减 hei*0.5
    ///        turn = normalizeAngle(want - cur) 再夹到 [-上限, +上限]
    ///        dx = cos(cur + turn) * speed ; dy = sin(cur + turn) * speed
    ///     原版把上限写死成 2π/3，这里换成可调值（默认 20°/帧）。
    ///
    /// 注意：子弹的位移就是靠 <c>Entity.dx/dy</c> 每帧积分出来的
    /// （<c>Bullet.shootFromWeapon</c> 也是读 <c>dx/dy</c> 算角度的），
    /// 所以改这两个字段就是真的改航向，而不是只改贴图朝向。
    /// </summary>
    internal static class KnifeHoming
    {
        /// <summary>1 格 = 24 像素。</summary>
        public const double CasePx = 24.0;

        /// <summary>对一枚飞刀做一帧的索敌转向。找不到目标就保持原航向。</summary>
        public static void Steer(Bullet b, double turnDegPerFrame, double searchCells)
        {
            if (b == null || b.destroyed) return;

            double vx = b.dx + b.bdx;
            double vy = b.dy + b.bdy;
            double speed = System.Math.Sqrt(vx * vx + vy * vy);
            if (!(speed > 1e-6)) return;                 // 静止/刚出生的子弹不动它

            Entity target = FindTarget(b, searchCells);
            if (target == null || target.destroyed || target.life <= 0) return;

            // 目标与自己的"身体中心"像素坐标（与 HomingSoul 取法一致）
            double tx = ((double)target.cx + target.xr) * CasePx;
            double ty = ((double)target.cy + target.yr) * CasePx - target.hei * 0.5;
            double sx = ((double)b.cx + b.xr) * CasePx;
            double sy = ((double)b.cy + b.yr) * CasePx - b.hei * 0.5;

            double cur = System.Math.Atan2(vy, vx);
            double want = System.Math.Atan2(ty - sy, tx - sx);

            double turn = NormalizeAngle(want - cur);
            double maxTurn = DegreeToRadian(turnDegPerFrame);
            if (turn > maxTurn) turn = maxTurn;
            else if (turn < -maxTurn) turn = -maxTurn;

            double ang = cur + turn;
            b.dx = System.Math.Cos(ang) * speed;
            b.dy = System.Math.Sin(ang) * speed;
        }

        /// <summary>
        /// 就近找一个怪当追踪目标。直接用游戏自己的 TargetHelper：
        /// <c>_team.get_targetHelper()</c> 返回本队（英雄队）的 helper，currents 由 Team 维护，
        /// 内容正是敌方目标列表（与 BleedAxeOverhaul / LanternOverhaul / ThrowableStuff 用的同一套）。
        ///
        /// 刻意**不调** <c>filterBySight</c> —— 飞刀穿墙，隔墙索敌才一致。
        /// 也刻意不用 <c>closestTo</c>/<c>importantMob</c> 那套打分，只要"最近的怪"，
        /// 因为飞刀是一次性弹丸，不需要 Boss 权重。
        /// </summary>
        public static Entity FindTarget(Bullet b, double searchCells)
        {
            if (b == null) return null;

            Team team = b._team;
            if (team == null) return null;

            if (searchCells <= 0.0) searchCells = 16.0;

            TargetHelper helper = team.get_targetHelper().init(b);
            helper.filterByDistance(searchCells, null, null);
            helper.filterUntargetables();
            helper.removeBreakables();
            helper.@is(dc.en.Mob.Class, 10.0);          // 怪物加权：只锁怪，不锁可破坏物

            Entity best = helper.getBest();
            return (best is dc.en.Mob) ? best : null;   // 范围内没怪就不锁
        }

        /// <summary>把角度归一化到 [-π, π]，用于算最短转向方向。</summary>
        public static double NormalizeAngle(double a)
        {
            while (a < -System.Math.PI) a += System.Math.PI * 2.0;
            while (a > System.Math.PI) a -= System.Math.PI * 2.0;
            return a;
        }

        private static double DegreeToRadian(double deg) => deg * System.Math.PI / 180.0;
    }
}
