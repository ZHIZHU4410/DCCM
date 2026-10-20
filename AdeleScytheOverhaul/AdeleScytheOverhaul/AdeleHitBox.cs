#nullable disable

using System;
using dc;
using dc.en;
using dc.hl.types;
using dc.tool;

namespace AdeleScytheOverhaul
{
    /// <summary>
    /// 死亡镰刀的"无视墙体"命中判定。
    ///
    /// 背景（GamePseudocode/dc.tool/Weapon.cs:4044 `Weapon.canHit`）：
    ///   原版一次近战命中要过三层判定 ——
    ///     ① 存活/可被攻击等基础校验；
    ///     ② **攻击框几何**（当前连击段的 <c>Area</c> 与目标圆形碰撞半径的距离/相交）；
    ///     ③ **墙体 / 视线 / 地形阻挡**（目标格是否实心、两点之间逐格 Bresenham 走一遍
    ///        <c>fastSpots</c> 与 <c>collisions</c>，以及门 <c>Door</c>/<c>EtheralDoorSocle</c>
    ///        的 <c>sightCheckCase</c>）。
    ///   `AdeleScythe.checkAreahit`（最后一击的追加判定）里是同一套逻辑的第二份拷贝。
    ///
    ///   本文件只保留 ① + ②，**完全不碰 ③** —— 这就是"无视墙体"。
    ///   判定用的 <c>Area</c> 直接取运行时武器自己的 <c>areas[cycle]</c>
    ///   （由 <c>_Weapon.__inst_construct__</c> 从 <c>wInfos.strikeChain[cycle].area</c> 建出来，
    ///    也就是被数据补丁放大过 ×3 的那一份），所以"范围 ×3"和"无视墙体"是叠加的，
    ///   而不会像"直接 return true"那样把范围变成全图。
    ///
    /// 坐标系（与原版一致，全部换算成像素，1 格 = 24px）：
    ///   英雄中心： x = (cx + xr) * 24          y = (cy + yr) * 24 - hei * 0.5
    ///   目标中心： x = (e.cx + e.xr) * 24      y = (e.cy + e.yr) * 24 - e.hei * 0.5
    ///   攻击框位置：<c>Area.setRelativePos(owner, x, y)</c> 存的是"相对英雄的像素偏移"，
    ///              横向要再乘一次朝向 <c>dir</c>（原版：`num = heroPx + area.x * relativeTo.dir`）。
    /// </summary>
    internal static class AdeleHitBox
    {
        /// <summary>1 格 = 24 像素（水平/垂直相同）。</summary>
        public const double CasePx = 24.0;

        /// <summary>
        /// 当前连击段要用的运行时攻击框。
        /// 原版 <c>Weapon.canHit(e, null)</c> 取的就是 <c>areas[get_cycle()]</c>。
        /// </summary>
        public static Area AreaOf(Weapon self, int cycle)
        {
            if (self == null) return null;
            Area result;
            try
            {
                ArrayObj areas = self.areas;
                if (areas == null) return null;
                if (cycle < 0 || cycle >= areas.length) return null;

                // areas 是 Array<Area>，元素就是 dc.tool.Area 实例（不是 HaxeVirtual 匿名结构），
                // 所以 as 转换在这里是可靠的。
                result = areas.getDyn(cycle) as Area;
            }
            catch { return null; }
            return result;
        }

        /// <summary>
        /// 几何命中判定（不含任何墙体/视线检查）。
        /// <paramref name="cycle"/> 传当前连击序号；最后一击的追加判定传它自己的 previousCycle。
        /// </summary>
        public static bool Test(Weapon self, Entity e, int cycle)
        {
            if (self == null || e == null) return false;

            try
            {
                // ---------- ① 原版 canHit 开头的基础校验，逐条照抄 ----------
                if (self.destroyed || e.destroyed) return false;
                if (e.life <= 0) return false;

                Hero hero = self.owner;
                if (hero == null || hero.destroyed || hero.life <= 0) return false;

                if (!e.canBeHit()) return false;

                Area area = AreaOf(self, cycle);
                if (area == null) return false;

                // ---------- ② 几何：英雄与目标都换算成"身体中心像素坐标" ----------
                double heroPx = ((double)hero.cx + hero.xr) * CasePx;
                double heroPy = ((double)hero.cy + hero.yr) * CasePx - hero.hei * 0.5;

                double ex = ((double)e.cx + e.xr) * CasePx;
                double ey = ((double)e.cy + e.yr) * CasePx - e.hei * 0.5;

                // 攻击框中心（area.x / area.y 是相对英雄的像素偏移，横向乘朝向）
                double ax = heroPx + area.x * hero.dir;
                double ay = heroPy + area.y;

                if (area.isCircle)
                {
                    // 圆 vs 圆：原版 Weapon.cs 的 isCircle 分支
                    //   (areaCenter - eCenter)^2 <= (widPx * 0.5 + e.radius)^2
                    double reach = area.widPx * 0.5 + e.radius;
                    double dx = ax - ex;
                    double dy = ay - ey;
                    return dx * dx + dy * dy <= reach * reach;
                }

                // 矩形（AdeleScythe 最后一击是 shape 0 = 圆，这里是兜底，与最后一击无关）
                //   原版矩形分支两轴都用 e.radius * 0.75 做外扩
                double halfW = area.widPx * 0.5;
                double halfH = area.heiPx * 0.5;
                double slack = e.radius * 0.75;
                return System.Math.Abs(ax - ex) <= halfW + slack
                    && System.Math.Abs(ay - ey) <= halfH + slack;
            }
            catch
            {
                // 判定失败一律当作"打不到"，绝不让异常冒进原版的伤害流程
                return false;
            }
        }
    }
}
