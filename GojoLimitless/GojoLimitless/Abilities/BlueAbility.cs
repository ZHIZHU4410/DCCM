#nullable disable
using System;
using System.Collections.Generic;
using dc.en;

using SysMath = System.Math;

namespace GojoLimitless.Abilities
{
    /// <summary>
    /// 术式顺转「苍」（Cursed Technique Lapse: Blue）—— 默认 F5。
    ///
    /// 定位：**控制招，不是输出招**。
    ///   · 伤害低（默认 150），半径大（12 格），冷却短（3 秒）
    ///   · 吸附力度大：越远的敌人拉得越狠（不然远处的怪根本拽不过来）
    ///   · 内圈把敌人直接叠到英雄身上，方便接「赤」或平砍
    ///
    /// Boss / 精英的吸附额外乘 <c>BossPullPushFactor</c>（默认 0.4），
    /// 再叠加原版 <c>getBumpResistanceFactor()</c>，所以 Boss 只会被轻微拖动。
    ///
    /// 连招：用「苍」之后 <c>ComboWindow</c>（默认 2.5 秒）内用「赤」，会接出虚式「茈」。
    /// </summary>
    public sealed class BlueAbility : GojoAbilityBase
    {
        public override string Id => "blue";
        public override string DisplayName => "术式顺转·苍";

        protected override AbilityCfg CfgOf(Configs c) => c.Blue;

        protected override bool Activate(Hero hero, double dt)
        {
            Configs cfg = Cfg.V;
            AbilityCfg a = cfg.Blue;

            double radiusTiles = SysMath.Max(1.0, a.Radius);
            double radiusPx = radiusTiles * GojoUtil.TILE_PX;

            GojoUtil.CenterPx(hero, out double hx, out double hy);

            // ---- 特效：以英雄为圆心的青色领域 ----
            GojoFx.Burst(hero, hx, hy, radiusTiles, GojoPalette.Blue);
            GojoFx.BeamLine(hero, hx, hy, radiusPx, GojoPalette.Blue, 1);

            // ---- 吸 ----
            List<Mob> mobs = GojoUtil.SnapshotEnemies(hero);
            double power = SysMath.Max(0.0, a.Power);
            double dmg = SysMath.Max(0.0, a.Damage);

            int pulled = 0;
            int hit = 0;

            foreach (Mob mob in mobs)
            {
                if (mob.destroyed || mob.life <= 0) continue;

                double dist = GojoUtil.DistancePx(hero, mob);
                if (double.IsNaN(dist) || dist > radiusPx) continue;

                GojoUtil.CenterPx(mob, out double mx, out double my);
                if (!GojoUtil.Normalize(hx - mx, hy - my, out double nx, out double ny)) continue;

                // 越远吸得越狠：外圈 ×2，贴脸 ×1
                double falloff = 1.0 + (dist / radiusPx);
                double weight = GojoUtil.WeightFactorFor(mob);

                GojoUtil.PushAway(mob, nx, ny, power * falloff, weight);
                pulled++;

                if (dmg > 0.0 && GojoUtil.DealDamage(hero, mob, dmg, heroScaling: a.UseHeroScaling)) hit++;
            }

            Log.Hud($"苍  牵引 {pulled}  命中 {hit}  半径{radiusTiles:0.#}格  力度{power:0.##}");
            return true;
        }
    }
}
