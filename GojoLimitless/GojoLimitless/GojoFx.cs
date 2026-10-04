#nullable disable
using System;
using dc;
using dc.en;

using SysMath = System.Math;

namespace GojoLimitless
{
    /// <summary>
    /// 招式表现层 —— 只调用**原版已有**的 FX 函数，不引入任何新贴图/粒子资源。
    ///
    /// 用到的三个原版特效：
    ///   * <c>Fx.timeDistorsionStart</c>  —— 时之守护者那圈"时间扭曲"球，正好是个领域/球体
    ///   * <c>Fx.longHitLine</c>          —— 一条贯穿型命中光带，拿来做「茈」的主光束
    ///   * <c>Fx.smallIceExplosion</c>    —— 小型爆发（配合光带做"撞点"）
    ///
    /// 全部包了 try/catch：特效挂掉绝不能让招式不生效。
    /// </summary>
    public static class GojoFx
    {
        private static Fx FxOf(Entity e)
        {
            try { return e?._level?.fx; }
            catch { return null; }
        }

        /// <summary>以某点为中心的球形领域爆发（用的就是原版时间扭曲那套粒子）。</summary>
        public static void Burst(Entity src, double xPx, double yPx, double radiusTiles, int color)
        {
            Fx fx = FxOf(src);
            if (fx == null) return;

            double r = SysMath.Max(0.5, radiusTiles) * GojoUtil.TILE_PX;
            try { fx.timeDistorsionStart(xPx, yPx, r, color); } catch { }
            try { fx.timeDistorsionEnd(xPx, yPx, r, color); } catch { }
        }

        /// <summary>一条水平贯穿光带（方向 = dir 的正负）。</summary>
        public static void BeamLine(Entity src, double xPx, double yPx, double rangePx, int color, int dir)
        {
            Fx fx = FxOf(src);
            if (fx == null) return;

            try { fx.longHitLine(xPx, yPx, dir >= 0 ? 1 : -1, SysMath.Max(1.0, rangePx), color, color); }
            catch { }
        }

        /// <summary>「茈」的紫色贯穿光束：光带 + 沿路径的连续爆发。</summary>
        public static void PurpleLance(Entity src, double xPx, double yPx, double fxDir, double fyDir, double rangePx)
        {
            Fx fx = FxOf(src);
            if (fx == null) return;

            int dir = fxDir >= 0.0 ? 1 : -1;
            try { fx.longHitLine(xPx, yPx, dir, SysMath.Max(1.0, rangePx), GojoPalette.Purple, GojoPalette.SixEyes); }
            catch { }

            // 沿光束每隔一段距离点一个爆发
            const int steps = 6;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double bx = xPx + fxDir * rangePx * t;
                double by = yPx + fyDir * rangePx * t;
                try { fx.smallIceExplosion(bx, by, GojoPalette.Purple); } catch { break; }
            }

            try
            {
                fx.timeDistorsionStart(xPx + fxDir * rangePx * 0.35,
                                       yPx + fyDir * rangePx * 0.35,
                                       rangePx * 0.35, GojoPalette.Void);
            }
            catch { }
        }

        /// <summary>湮灭一发子弹时的小爆点。</summary>
        public static void BulletErased(Entity src, double xPx, double yPx, int color)
        {
            Fx fx = FxOf(src);
            if (fx == null) return;
            try { fx.smallIceExplosion(xPx, yPx, color); } catch { }
        }
    }
}
