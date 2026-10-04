#nullable disable
using System;
using System.Collections.Generic;
using dc.en;

using SysMath = System.Math;

namespace GojoLimitless.Abilities
{
    /// <summary>
    /// 术式反转「赤」（Reversal: Red）—— 默认 F6。
    ///
    /// 定位：**击飞 + 中等伤害**。
    ///   · 伤害比「苍」高一点（默认 220），冷却同长
    ///   · 推力很大（默认 2.2，约「苍」的两倍）：贴身的敌人会被直接掀飞出去
    ///   · 击飞是"径向 + 轻微上抛"：<c>bdy</c> 额外加一点，看起来是掀起来而不是贴地滑
    ///
    /// Boss / 精英 同样乘 <c>BossPullPushFactor</c>。
    ///
    /// 连招：先用「苍」（F5），<c>ComboWindow</c> 内按「赤」，不打「赤」而是直接接出「茈」。
    /// 如果「茈」还在冷却，就退回普通「赤」。
    /// </summary>
    public sealed class RedAbility : GojoAbilityBase
    {
        public override string Id => "red";
        public override string DisplayName => "术式反转·赤";

        protected override AbilityCfg CfgOf(Configs c) => c.Red;

        /// <summary>击飞时额外附加的向上分量（格/帧）。</summary>
        private const double LiftAssist = 0.18;

        protected override bool Activate(Hero hero, double dt)
        {
            // ---- 连招判定：苍之后紧接着赤 → 茈 ----
            BlueAbility blue = GojoHub.Find<BlueAbility>();
            PurpleAbility purple = GojoHub.Find<PurpleAbility>();

            double comboWindow = Cfg.V.ComboWindow;
            if (blue != null && purple != null && comboWindow > 0.0 && purple.CanFireInternally
                && blue.SinceLastUse >= 0.0 && blue.SinceLastUse <= comboWindow)
            {
                bool comboFired = purple.FireCombo(hero, dt);
                if (comboFired)
                {
                    Log.Info($"「苍」+「赤」→ 虚式「茈」！（苍于 {blue.SinceLastUse:0.##}s 前释放）");
                    return true;
                }
            }

            Configs cfg = Cfg.V;
            AbilityCfg a = cfg.Red;

            double radiusTiles = SysMath.Max(1.0, a.Radius);
            double radiusPx = radiusTiles * GojoUtil.TILE_PX;

            GojoUtil.CenterPx(hero, out double hx, out double hy);

            // ---- 特效 ----
            GojoFx.Burst(hero, hx, hy, radiusTiles, GojoPalette.Red);
            GojoFx.BeamLine(hero, hx, hy, radiusPx, GojoPalette.Red, -1);

            // ---- 斥 ----
            List<Mob> mobs = GojoUtil.SnapshotEnemies(hero);
            double power = SysMath.Max(0.0, a.Power);
            double dmg = SysMath.Max(0.0, a.Damage);

            int pushed = 0;
            int hit = 0;
            int launched = 0;

            foreach (Mob mob in mobs)
            {
                if (mob.destroyed || mob.life <= 0) continue;

                double dist = GojoUtil.DistancePx(hero, mob);
                if (double.IsNaN(dist) || dist > radiusPx) continue;

                GojoUtil.CenterPx(mob, out double mx, out double my);
                if (!GojoUtil.Normalize(mx - hx, my - hy, out double nx, out double ny)) continue;

                double weight = GojoUtil.WeightFactorFor(mob);

                // 贴身吃满，外圈递减
                double falloff = 1.0 - 0.5 * (dist / radiusPx);
                GojoUtil.PushAway(mob, nx, ny, power * falloff, weight);

                // 击飞：非 Boss 额外给一点向上分量，看起来是"掀起来"
                if (weight >= 1.0)
                {
                    try
                    {
                        double vy = mob.bdy + LiftAssist;
                        if (vy > 8.0) vy = 8.0;
                        mob.bdy = vy;
                        launched++;
                    }
                    catch { }
                }

                pushed++;

                if (dmg > 0.0 && GojoUtil.DealDamage(hero, mob, dmg, heroScaling: a.UseHeroScaling)) hit++;
            }

            Log.Hud($"赤  斥退 {pushed}  命中 {hit}  掀飞 {launched}  半径{radiusTiles:0.#}格  力度{power:0.##}");
            return true;
        }
    }
}
