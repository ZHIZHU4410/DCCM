#nullable disable
using System;
using System.Collections.Generic;
using dc.en;

using SysMath = System.Math;

namespace GojoLimitless.Abilities
{
    /// <summary>
    /// 虚式「茈」（Hollow Purple）—— 默认 F7，或用「苍 + 赤」连招自动接出。
    ///
    /// 定位：**大招**。长冷却、超高伤害、前方锥形贯穿。
    ///   · 半宽由 <c>Radius / ComboHalfWidthRatio</c> 换算成张角：
    ///     配置里的 Radius 就是射程，锥形半宽 = Radius × 0.17（26 格射程 → 约 4.5 格半宽）
    ///   · 冷却默认 14 秒
    ///
    /// 命中判定用 <see cref="GojoUtil.InCone"/>。
    /// </summary>
    public sealed class PurpleAbility : GojoAbilityBase
    {
        public override string Id => "purple";
        public override string DisplayName => "虚式·茈";

        protected override AbilityCfg CfgOf(Configs c) => c.Purple;

        /// <summary>锥形半宽 = 射程 × 这个比例（0.17 → 26 格射程约 4.4 格半宽）。</summary>
        private const double HalfWidthRatio = 0.17;

        private bool _comboFlag;
        private double _comboStamp = -999.0;

        /// <summary>连招（苍+赤）是否可以接茈：冷却是唯一门槛。</summary>
        public bool CanFireInternally => Enabled && Ready && GojoHub.AbilitiesActive;

        /// <summary>由「赤」调用的连招释放（跳过按键判定，冷却照常计）。</summary>
        public bool FireCombo(Hero hero, double dt)
        {
            if (hero == null) return false;
            if (!CanFireInternally) return false;

            bool fired = false;
            try { fired = Activate(hero, dt); }
            catch (Exception ex) { Log.Exception(ex, DisplayName + " 连招释放失败"); }

            if (fired)
            {
                _comboFlag = true;
                _comboStamp = _now;
                TriggerCooldown();
                Log.Debug($"{DisplayName} 连招释放（冷却 {CooldownSeconds:0.##}s）");
            }
            return fired;
        }

        protected override bool Activate(Hero hero, double dt)
        {
            bool isCombo = _comboFlag && (_now - _comboStamp) < 0.5;
            _comboFlag = false;

            Configs cfg = Cfg.V;
            AbilityCfg a = cfg.Purple;

            double rangeTiles = SysMath.Max(2.0, a.Radius);
            double rangePx = rangeTiles * GojoUtil.TILE_PX;
            double halfWidth = SysMath.Max(0.5, rangeTiles * HalfWidthRatio);
            // 半宽(格) / 射程(格) → 半角 → 总张角
            double spreadRad = 2.0 * SysMath.Atan2(halfWidth, rangeTiles);

            GojoUtil.CenterPx(hero, out double hx, out double hy);
            GojoUtil.Facing(hero, out double fx, out double fy);

            // ---- 额外一层：原地变大的原版死亡之球 ----
            // ⚠️ **先 Spawn**：先发起的实体先进场景，绘制时就在**下面** ——
            //    这就是需求里的"在现有特效的下面一并出现"。
            GojoOrbFx.Spawn(hero);

            // ---- 特效：紫色贯穿光束 + 爆发（盖在死亡之球之上）----
            GojoFx.Burst(hero, hx, hy, rangeTiles * 0.6, GojoPalette.Purple);
            GojoFx.PurpleLance(hero, hx, hy, fx, fy, rangePx);

            // ---- 抹除 ----
            List<Mob> mobs = GojoUtil.SnapshotEnemies(hero);
            double dmg = SysMath.Max(0.0, a.Damage);
            double push = SysMath.Max(0.0, a.Power);

            int hit = 0;
            foreach (Mob mob in mobs)
            {
                if (mob.destroyed || mob.life <= 0) continue;

                GojoUtil.CenterPx(mob, out double mx, out double my);
                if (!GojoUtil.InCone(hx, hy, fx, fy, mx, my, rangePx, spreadRad)) continue;

                if (!GojoUtil.Normalize(mx - hx, my - hy, out double nx, out double ny)) continue;

                if (dmg > 0.0 && GojoUtil.DealDamage(hero, mob, dmg, heroScaling: a.UseHeroScaling)) hit++;
                if (push > 0.0)
                {
                    GojoUtil.PushAway(mob, nx, ny, push, GojoUtil.WeightFactorFor(mob));
                }
            }

            Log.Hud($"茈{(isCombo ? "（苍+赤连招）" : "")}  贯穿 {hit}  射程{rangeTiles:0.#}格  伤害{dmg:0}");
            return true;
        }

        /// <summary>
        /// 每帧推进死亡之球的"变大 → 消失"。
        ///
        /// 放在 <c>OnUpdate</c> 里（由 <see cref="GojoHub.Tick"/> 每帧调），
        /// 所以即使不在放招期间也能正常长大 / 收掉。
        /// </summary>
        protected override void OnUpdate(Hero hero, double dt)
        {
            GojoOrbFx.Update(dt);
        }

        public override void Reset()
        {
            base.Reset();
            _comboFlag = false;
            _comboStamp = -999.0;
            GojoOrbFx.Reset();
        }
    }
}
