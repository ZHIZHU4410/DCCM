#nullable disable
using System;
using System.Collections.Generic;
using dc.en;

using SysMath = System.Math;

namespace GojoLimitless.Abilities
{
    /// <summary>
    /// 领域展开「无量空处」（Domain Expansion: Unlimited Void）—— 默认 F9。
    ///
    /// 原作里无量空处会把对手拖进"无限的信息"里：感官被灌爆，什么都做不了。
    /// 这里做成一个持续数秒的关闭式领域：
    ///
    ///   * 领域内所有敌人被 **定身**（原版 <c>affectRoot</c> = 15，藤蔓那一套"不能动"）
    ///   * 移速被压到 <c>SlowMultiplier</c>（默认 0.20，走 SlowAura 的 hook）
    ///   * 一直被轻轻拽向领域中心
    ///
    /// **内外两圈**（<c>Radius</c> = 外圈，<c>InnerRadius</c> = 内圈，默认 22 / 7 格）：
    ///   * **外圈**（&gt; 内圈半径）：只吸附 + 定身 + 减速，**不造成任何伤害**，
    ///     并且用 <c>OuterPullMultiplier</c> 倍的力度把敌人往中心拽；
    ///   * **内圈**（≤ 内圈半径）：才开始吃每 <c>TickInterval</c> 秒一跳的伤害。
    ///
    /// 领域期间**英雄**获得无敌（原版 affect 5，和 <c>Hero.toggleFullInvincibility</c> 同一个 id）。
    /// </summary>
    public sealed class UnlimitedVoidAbility : GojoAbilityBase
    {
        public override string Id => "domain";
        public override string DisplayName => "领域展开·无量空处";

        protected override AbilityCfg CfgOf(Configs c) => c.Domain;

        /// <summary>
        /// 定身用的 affect id。
        ///
        /// ⚠️ **原来是 28，那是错的** —— 28 根本不在游戏的影响表里，实际效果是给怪物
        /// 挂了个别的东西（实机表现为"怪物身上出现了护盾"）。
        /// 正确的定身 id 是 **15**：见 <c>GamePseudocode/dc/Entity.cs</c> 的影响名表
        /// <c>case 15: "affectRoot"</c>（按 id 顺序解析出来的：
        /// 8=affectStun、15=affectRoot、23=affectFrost、46=affectPetrify …）。
        /// </summary>
        private const int RootAffectId = 15;

        /// <summary>
        /// 英雄无敌用的 affect id。
        ///
        /// ⚠️ **原来是 48，也是错的**。原版自己开无敌是这么写的：
        /// <code>
        /// // GamePseudocode/dc.en/Hero.cs:13640  toggleFullInvincibility(bool on)
        /// hero.setAffectS(5, 99999.0, ref ..., null);   // ← 5 才是无敌
        /// </code>
        /// 所以我们用同一个 id（5），只是把时长换成领域持续时间的一小段。
        /// </summary>
        private const int InvincibleAffectId = 5;

        /// <summary>定身/迟滞的刷新间隔（秒）。</summary>
        private const double FieldRefresh = 0.12;

        /// <summary>领域剩余持续时间。</summary>
        private static double _remaining;
        private static double _tickTimer;
        private static double _fieldTimer;
        private static double _elapsed;

        /// <summary>领域是否正在进行（HUD / 特效用）。</summary>
        public static bool IsActive => _remaining > 0.0;

        /// <summary>领域剩余时间。</summary>
        public static double Remaining => _remaining > 0.0 ? _remaining : 0.0;

        protected override bool Activate(Hero hero, double dt)
        {
            Configs cfg = Cfg.V;
            DomainCfg a = cfg.Domain;

            GojoUtil.CenterPx(hero, out double hx, out double hy);

            double radiusTiles = SysMath.Max(2.0, a.Radius);
            double duration = SysMath.Max(0.5, a.Duration);

            _remaining = duration;
            _elapsed = 0.0;
            _tickTimer = 0.0;
            _fieldTimer = 0.0;

            // 开场：紫色领域 + 无敌
            GojoFx.Burst(hero, hx, hy, radiusTiles, GojoPalette.Void);
            GojoFx.BeamLine(hero, hx, hy, radiusTiles * GojoUtil.TILE_PX, GojoPalette.Purple, 1);
            if (a.Invincible)
            {
                GojoUtil.StackAffect(hero, InvincibleAffectId, duration * 0.6, 1.0);
            }

            // 立刻定身一次，别让敌人有反应时间
            ApplyField(hero, radiusTiles, true);

            // 氛围层：全屏紫色 + 晕影 + 虚空环 + 裂纹 + 扫光 + 开场闪光。
            // 会一直画到领域结束（见 OnUpdate 的 GojoDomainFx.Update / End）。
            if (a.DomainFx) GojoDomainFx.Begin(hero, duration);

            Log.Info($"领域展开「无量空处」—— 外圈 {radiusTiles:0.#} 格（只吸附不伤害）/ "
                     + $"内圈 {a.InnerRadius:0.#} 格（造成伤害），持续 {duration:0.#}s");
            return true;
        }

        protected override void OnUpdate(Hero hero, double dt)
        {
            if (_remaining <= 0.0)
            {
                // 领域已经结束（或被关掉）—— 确保氛围层收干净
                GojoDomainFx.End();
                return;
            }

            if (!GojoHub.AbilitiesActive)
            {
                _remaining = 0.0;
                GojoDomainFx.End();
                return;
            }

            Configs cfg = Cfg.V;
            DomainCfg a = cfg.Domain;

            _remaining -= dt;
            _elapsed += dt;

            double radiusTiles = SysMath.Max(2.0, a.Radius);

            _fieldTimer += dt;
            if (_fieldTimer >= FieldRefresh)
            {
                _fieldTimer = 0.0;
                ApplyField(hero, radiusTiles, false);
            }

            double tick = SysMath.Max(0.1, a.TickInterval);
            _tickTimer += dt;
            if (_tickTimer >= tick)
            {
                _tickTimer -= tick;
                ApplyTickDamage(hero, radiusTiles, a.Damage);
            }

            // 氛围层：每帧重画（开场闪光 / 脉动 / 虚空环 / 扫光都靠时间推进）
            if (a.DomainFx) GojoDomainFx.Update(hero, dt);

            if (_remaining <= 0.0)
            {
                Log.Debug("无量空处结束");
                _remaining = 0.0;
                GojoDomainFx.End();      // 领域结束 → 环境立刻恢复
            }
        }

        /// <summary>
        /// 外圈力场：定身 + 迟滞 + **向心拉扯**（不掉血）。
        ///
        /// <paramref name="outerTiles"/> 是外圈半径。外圈**只负责把敌人拽进来**，
        /// 伤害全部交给内圈（见 <see cref="ApplyTickDamage"/>）。
        /// 越靠外的敌人拽得越狠，否则远处的怪根本进不了内圈。
        /// </summary>
        private static void ApplyField(Hero hero, double outerTiles, bool opening)
        {
            double outerPx = outerTiles * GojoUtil.TILE_PX;
            double innerPx = InnerRadiusPx();

            Configs cfg = Cfg.V;
            double pullPower = SysMath.Max(0.0, cfg.Domain.Power);
            double outerMul = SysMath.Max(1.0, cfg.Domain.OuterPullMultiplier);
            double slowMul = cfg.Domain.SlowMultiplier;
            double linger = cfg.Infinity != null ? cfg.Infinity.SlowLinger : 0.30;

            List<Mob> mobs = GojoUtil.SnapshotEnemies(hero);
            GojoUtil.CenterPx(hero, out double hx, out double hy);

            int caught = 0;
            int dragged = 0;
            foreach (Mob mob in mobs)
            {
                if (mob.destroyed || mob.life <= 0) continue;

                double dist = GojoUtil.DistancePx(hero, mob);
                if (double.IsNaN(dist) || dist > outerPx) continue;

                GojoUtil.StackAffect(mob, RootAffectId, FieldRefresh * 3.0, 1.1);
                // 减速走 hook（见 SlowAura）—— 原版 affect 133 是解冻计数器，做不到减速
                SlowAura.Mark(mob, slowMul, SysMath.Max(FieldRefresh * 3.0, linger));

                if (pullPower > 0.0)
                {
                    // 外圈的敌人要拽得更狠（内圈已经很近了，正常力度就够）
                    bool outside = dist > innerPx;
                    double mult = outside ? outerMul : 1.0;

                    GojoUtil.CenterPx(mob, out double mx, out double my);
                    if (GojoUtil.Normalize(hx - mx, hy - my, out double nx, out double ny))
                    {
                        GojoUtil.PushAway(mob, nx, ny, pullPower * mult, GojoUtil.WeightFactorFor(mob));
                    }
                    if (outside) dragged++;
                }
                caught++;
            }

            if (opening)
            {
                Log.Debug($"无量空处锁定 {caught} 个敌人（其中 {dragged} 个在外圈，正在被拽入内圈）");
            }
        }

        /// <summary>内圈半径（像素），并做合法性钳制（必须 &lt; 外圈）。</summary>
        private static double InnerRadiusPx()
        {
            Configs cfg = Cfg.V;
            double inner = cfg.Domain.InnerRadius;
            double outer = cfg.Domain.Radius;

            if (double.IsNaN(inner) || inner <= 0.0) inner = 7.0;
            // 内圈不可能比外圈还大 —— 兜底钳到外圈的 90%
            if (inner >= outer) inner = SysMath.Max(1.0, outer * 0.9);

            return inner * GojoUtil.TILE_PX;
        }

        /// <summary>
        /// 内圈伤害：**只打进了内圈的敌人**。
        ///
        /// 这是"内外圈"机制的关键 —— 外圈只是把敌人吸过来，不造成任何伤害；
        /// 敌人被拽进 <c>InnerRadius</c>（默认 7 格）之后才开始吃每跳伤害。
        /// </summary>
        private static void ApplyTickDamage(Hero hero, double outerTiles, double dmgPerTick)
        {
            double innerPx = InnerRadiusPx();
            double dmg = SysMath.Max(0.0, dmgPerTick);
            if (dmg <= 0.0) return;

            List<Mob> mobs = GojoUtil.SnapshotEnemies(hero);
            int hit = 0;
            foreach (Mob mob in mobs)
            {
                if (mob.destroyed || mob.life <= 0) continue;

                // ⚠️ 判的是**内圈**，不是外圈：外圈不造成伤害
                double dist = GojoUtil.DistancePx(hero, mob);
                if (double.IsNaN(dist) || dist > innerPx) continue;

                if (GojoUtil.DealDamage(hero, mob, dmg, heroScaling: Cfg.V.Domain.UseHeroScaling)) hit++;
            }

            if (hit > 0)
            {
                double tick = SysMath.Max(0.1, Cfg.V.Domain.TickInterval);
                Log.Debug($"无量空处第 {(int)(_elapsed / tick)} 跳，内圈命中 {hit}");
            }
        }

        public override void Reset()
        {
            base.Reset();
            _remaining = 0.0;
            _tickTimer = 0.0;
            _fieldTimer = 0.0;
            _elapsed = 0.0;
            GojoDomainFx.Reset();
        }

        public static void Shutdown()
        {
            _remaining = 0.0;
            GojoDomainFx.Reset();
        }
    }
}
