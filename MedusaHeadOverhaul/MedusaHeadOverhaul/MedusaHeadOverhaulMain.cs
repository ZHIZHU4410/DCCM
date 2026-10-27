#nullable disable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using dc;
using dc.en;
using dc.en.bu;
using dc.hl.types;
using dc.tool;
using dc.tool.weap;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Utilities;

// Haxe 匿名结构在代理里是超长类型名，这里起别名。
using ItemInfos = Hashlink.Virtuals.virtual_ambiantDesc_castCD_cellCost_commonProps_dlc_droppable_gameplayDesc_group_icon_id_legendAffixes_moneyCost_name_props_synergy_tags_tier1_tier2_;
using ItemProps = Hashlink.Virtuals.virtual_ang_aoeDuration_bonus_buff_bump_castTime_chance_color_color2_cooldown_count_debuff_distance_dotDps_dps_dps2_duration_duration2_duration3_effectCD_effectCharge_frict_height_item2_life_limit_lock_max_maxNumberOfMarks_min_mob_offsetX_offsetY_power_power2_power3_prct_prct2_prct3_range_size_speed_speed2_tick_triggerOnHit_uses_width_;
using SkillInfo = Hashlink.Virtuals.virtual_animId_animSpd_area_breachBonus_canCrit_charge_coolDown_critMul_dynamicCharge_earlyCombo_fxId_fxProps_glowColor_hitFrame_lockCtrlAfter_onionSkinFrame_onionSkinOffX_power_props_sfxCharge_sfxHit_sfxProps_sfxRelease_;
using AreaInfo = Hashlink.Virtuals.virtual_height_offsetCaseX_offsetCaseY_shape_width_;

namespace MedusaHeadOverhaul
{
    /// <summary>
    /// MedusaHead（美杜莎的头）加强 —— v2
    /// ================================
    /// 原版：扔出去 → 碰到敌人 / 再按一次攻击 → 爆炸（范围伤害 + 击退），命中的敌人被**石化**（affect 46）。
    ///
    /// 本模组四件事（全部运行时完成，不改 data.cdb）：
    ///
    /// 1) **爆炸范围 ×N**
    ///    半径在 `MedusaHeadBullet.trigger()` 里算（MedusaHeadBullet.cs:1284-1292）：
    ///    <code>
    ///    double width = aoeSkillInf.area.width;   // strikeChain[1].area.width = 10 格
    ///    double radiusPx = 0.5 * width * 24.0;    // = 120px = 5 格
    ///    Area area = new Area(radiusPx, null);
    ///    </code>
    ///    ⚠ v1 的坑：在 `prepare` 里**持久**改了 `area.width`，结果只有"翻滚距离"变长了、爆炸没变化 ——
    ///    因为 `aoeSkillInf` 与 `wInfos.strikeChain[1]` 是同一个共享对象，改它会牵动别的逻辑。
    ///    v2 改成：**只在这颗头爆炸的那一刻**把值临时改大，`orig` 跑完立刻**还原**。
    ///    这样爆炸半径变大，滚距和其它逻辑一点不受影响。
    ///
    /// 2) **翻滚距离可控**（默认 ×1 = 原版 20 格）
    ///    投掷距离来自 `itemInf.props.range`（构造 `MedusaHeadBullet` 时读的），
    ///    所以在 `Hook_Weapon.prepare` 里按"原始值 × N"设一次（幂等，不会滚雪球）。
    ///    "距离不需要那么长" → 拉回 1.0 就是原版；想更短就 0.6 之类。
    ///
    /// 3) **石化时长 ×N**
    ///    石化只有一个施加点：`MedusaHead` 的 onHit（MedusaHead.cs:40-72）
    ///    <code>e.setAffectS(46, get_curSkillInf().props.duration, ...)</code>
    ///    所以拦 `Hook_Entity.setAffectS`，对 affect 46 把秒数乘 N。
    ///    刻意不改 `props.duration` 数据 —— 同一份数据还被用来算武器 CD（MedusaHead.cs:322）。
    ///
    /// 4) **爆炸无视墙体**
    ///    `trigger()` 的伤害循环本身是"纯距离"判定（没有视线/墙体检查），
    ///    但**石化**不一定能覆盖到隔墙的敌人。所以爆炸结束后再补一遍：
    ///    以爆炸点为中心、按（放大后的）半径遍历 `level.entities` 里的敌对 `Mob`，
    ///    纯距离命中就补一次 affect 46 —— 隔墙也照样石化。
    /// </summary>
    public class MedusaHeadOverhaulMain : ModBase, IOnGameInit, IOnGameExit, IOnFrameUpdate, IModMenu
    {
        /// <summary>affect 46 = 石化（petrify），见 Entity.cs:10462 的 "affectPetrify"。</summary>
        private const int AffectPetrify = 46;

        /// <summary>area 对象身份 → 原始 width（防止重复乘）。</summary>
        private readonly Dictionary<int, double> _baseAreaWidth = new();

        /// <summary>item props 对象身份 → 原始 range。</summary>
        private readonly Dictionary<int, double> _baseRollRange = new();

        private int _radiusLogCount;
        private int _rollLogCount;
        private int _petrifyLogCount;
        private int _wallLogCount;
        private int _errorCount;

        public MedusaHeadOverhaulMain(ModInfo info) : base(info) { }

        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ---------------------------------------------------------------- 生命周期

        public override void Initialize()
        {
            base.Initialize();

            try { Hook_Weapon.prepare += OnWeaponPrepare; }
            catch (Exception ex) { Logger.Error(ex, "[MedusaHeadOverhaul] Hook_Weapon.prepare 挂载失败"); }

            try { Hook_MedusaHeadBullet.trigger += OnBulletTrigger; }
            catch (Exception ex) { Logger.Error(ex, "[MedusaHeadOverhaul] Hook_MedusaHeadBullet.trigger 挂载失败"); }

            try { Hook_Entity.setAffectS += OnSetAffectS; }
            catch (Exception ex) { Logger.Error(ex, "[MedusaHeadOverhaul] Hook_Entity.setAffectS 挂载失败"); }

            Write("[MedusaHeadOverhaul] 已加载：美杜莎的头 —— 爆炸范围 ×N / 石化时长 ×N / Boss 石化 / 无视墙体");
        }

        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = MedusaKeys.Config.Value;
                Write("[MedusaHeadOverhaul] 开关状态 —— " +
                      $"总开关={cfg.EnableMod} 爆炸范围={cfg.EnableRadius}(×{cfg.RadiusMultiplier:0.##}) " +
                      $"翻滚距离={cfg.EnableRollRange}(×{cfg.RollRangeMultiplier:0.##}) " +
                      $"石化={cfg.EnablePetrify}(×{cfg.PetrifyMultiplier:0.##}) " +
                      $"Boss石化={cfg.EnableBossPetrify} 无视墙体={cfg.EnableWallIgnore}");
                Write($"[MedusaHeadOverhaul] 配置文件: {MedusaKeys.Config.ConfigPath}" +
                      "（也可在游戏里 选项 → 模组 → 美杜莎的头 直接改）");

                try { if (MedusaKeys.Config.Value != null) MedusaKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[MedusaHeadOverhaul] 写配置失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Weapon.prepare -= OnWeaponPrepare; } catch { }
            try { Hook_MedusaHeadBullet.trigger -= OnBulletTrigger; } catch { }
            try { Hook_Entity.setAffectS -= OnSetAffectS; } catch { }

            MedusaFeatures.ResetHotkeyState();
            _baseAreaWidth.Clear();
            _baseRollRange.Clear();
            _radiusLogCount = 0;
            _rollLogCount = 0;
            _petrifyLogCount = 0;
            _wallLogCount = 0;
            _errorCount = 0;

            Write("[MedusaHeadOverhaul] 游戏退出，模组已卸载");
        }

        void IOnFrameUpdate.OnFrameUpdate(double dt)
        {
            try { MedusaFeatures.PollHotkeys(Write); } catch { }
        }

        // ---------------------------------------------------------------- ② 翻滚距离（默认原版）

        private void OnWeaponPrepare(Hook_Weapon.orig_prepare orig, Weapon self, double attackSpeed)
        {
            orig(self, attackSpeed);

            try
            {
                if (self is MedusaHead medusa) PatchRollRange(medusa);
            }
            catch (Exception ex) { Fail("roll", ex); }
        }

        /// <summary>
        /// 投掷距离 = `itemInf.props.range`（MedusaHeadBullet 构造时读它当 maxDist）。
        /// 按"原始值 × N"设，幂等：反复 prepare / 换武器都不会越乘越大。
        /// </summary>
        private void PatchRollRange(MedusaHead weapon)
        {
            if (!MedusaFeatures.IsOn(MedusaFeature.RollRange)) return;

            ItemInfos infos = weapon.itemInf;
            if (infos == null) return;

            ItemProps props = infos.props;
            if (props == null) return;

            double? cur = props.range;
            if (cur == null || cur.Value <= 0.0) return;

            double mul = RollRangeMultiplierNow();

            int key = RuntimeHelpers.GetHashCode(props);
            if (!_baseRollRange.TryGetValue(key, out double baseRange))
            {
                baseRange = cur.Value;
                _baseRollRange[key] = baseRange;
            }

            double target = baseRange * mul;
            if (System.Math.Abs(cur.Value - target) > 1e-6) props.range = (double?)target;

            if (_rollLogCount < 4)
            {
                _rollLogCount++;
                Write($"[MedusaHeadOverhaul] 翻滚距离：range {baseRange:0.##} → {target:0.##} 格（×{mul:0.##}）");
            }
        }

        private static double RollRangeMultiplierNow()
        {
            double m = MedusaKeys.Config.Value.RollRangeMultiplier;
            if (double.IsNaN(m) || m <= 0.0) m = 1.0;
            if (m > 5.0) m = 5.0;
            return m;
        }

        // ---------------------------------------------------------------- ①④ 爆炸范围 + 无视墙体

        /// <summary>
        /// 这颗头爆炸时：临时把 AoE 半径放大 → 跑原版 trigger() → **立刻还原** → 再补一遍隔墙石化。
        /// </summary>
        private void OnBulletTrigger(Hook_MedusaHeadBullet.orig_trigger orig, MedusaHeadBullet self)
        {
            SkillInfo aoe = null;
            AreaInfo area = null;
            double baseWidth = 0.0;
            double radiusCells = 0.0;
            bool patched = false;

            try
            {
                if (self != null)
                {
                    aoe = self.aoeSkillInf;
                    area = aoe?.area;

                    if (area != null && area.width > 0.0)
                    {
                        baseWidth = area.width;
                        radiusCells = baseWidth * 0.5;      // 半径（格）= 直径 / 2

                        if (MedusaFeatures.IsOn(MedusaFeature.Radius))
                        {
                            double target = baseWidth * RadiusMultiplierNow();
                            if (System.Math.Abs(area.width - target) > 1e-6)
                            {
                                area.width = target;        // 只对这一次爆炸生效
                                patched = true;
                                radiusCells = target * 0.5;

                                if (_radiusLogCount < 4)
                                {
                                    _radiusLogCount++;
                                    Write($"[MedusaHeadOverhaul] 爆炸范围：area.width {baseWidth:0.###} → {target:0.###} 格" +
                                          $"（×{RadiusMultiplierNow():0.##}，半径 {radiusCells:0.##} 格）");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Fail("radius", ex); }

            try
            {
                orig(self);
            }
            finally
            {
                // ★ 还原：v1 就是因为没还原，才连带把翻滚距离改长
                if (patched)
                {
                    try { area.width = baseWidth; } catch { }
                }
            }

            try
            {
                if (MedusaFeatures.IsOn(MedusaFeature.WallIgnore)) ThroughWallPetrify(self, radiusCells);
            }
            catch (Exception ex) { Fail("wall", ex); }
        }

        /// <summary>
        /// 爆炸后按"纯距离"再补一遍石化：半径内所有敌对 Mob（**隔着墙也算**）都挂 affect 46。
        ///
        /// 说明：原版 `trigger()` 的伤害循环本身就是纯距离判定、没有视线/墙体检查，
        /// 所以这里补的是"石化覆盖" —— 让隔墙的敌人也吃到石化，而不是只覆盖视线内。
        /// </summary>
        private void ThroughWallPetrify(MedusaHeadBullet bullet, double radiusCells)
        {
            if (bullet == null || bullet.destroyed) return;
            if (radiusCells <= 0.0) return;

            dc.pr.Level level = bullet._level;
            if (level == null) return;

            ArrayObj all = level.entities;
            if (all == null) return;

            Entity owner = bullet.owner;
            if (owner == null || owner.destroyed) return;

            double bx = ((double)bullet.cx + bullet.xr) * 24.0;
            double by = ((double)bullet.cy + bullet.yr) * 24.0 - bullet.hei * 0.5;
            double radiusPx = radiusCells * 24.0;

            // 石化基础时长取"武器当前技能"的 props.duration（投掷段 = 2.5s），
            // 之后还会被 setAffectS 钩子乘 N。
            double sec = 0.0;
            try
            {
                MedusaHead w = bullet.weaponMedusaHead;
                SkillInfo cinf = w?.get_curSkillInf();
                if (cinf?.props != null)
                {
                    double? d = cinf.props.duration;
                    if (d != null && d.Value > 0.0) sec = d.Value;
                }
            }
            catch { }
            if (sec <= 0.0) sec = 2.5;

            int found = 0;
            int done = 0;
            int n = all.length;

            for (int i = 0; i < n; i++)
            {
                Mob mob;
                try { mob = all.getDyn(i) as Mob; } catch { continue; }
                if (mob == null || mob.destroyed || mob.life <= 0) continue;
                if (!owner.isOpponent(mob)) continue;
                if (!NearPx(mob, bx, by, radiusPx)) continue;

                found++;
                try
                {
                    double v = 0.0;
                    mob.setAffectS(AffectPetrify, sec, Ref<double>.From(ref v), null);
                    done++;
                }
                catch { }
            }

            if (found > 0 && _wallLogCount < 5)
            {
                _wallLogCount++;
                Write($"[MedusaHeadOverhaul] 无视墙体：半径 {radiusCells:0.##} 格内敌人 {found} 个，补石化 {done} 个" +
                      $"（基础 {sec:0.##}s，之后按 ×N 放大）");
            }
        }

        private static bool NearPx(Entity e, double hx, double hy, double radiusPx)
        {
            double ex = ((double)e.cx + e.xr) * 24.0;
            double ey = ((double)e.cy + e.yr) * 24.0 - e.hei * 0.5;
            double dx = ex - hx;
            double dy = ey - hy;
            return dx * dx + dy * dy <= radiusPx * radiusPx;
        }

        private static double RadiusMultiplierNow()
        {
            double m = MedusaKeys.Config.Value.RadiusMultiplier;
            if (double.IsNaN(m) || m <= 0.0) m = 5.0;
            if (m > 20.0) m = 20.0;
            return m;
        }

        // ---------------------------------------------------------------- ③ 石化时长 / Boss 石化

        /// <summary>
        /// 拦所有 affect 施加；只有 affect 46（石化）特殊处理：
        ///   · 时长 ×N；
        ///   · 目标对该 affect 有抗性时用 `ignoreResist: true` 强推（Boss 普遍 resist=1.0）。
        /// </summary>
        private void OnSetAffectS(Hook_Entity.orig_setAffectS orig, Entity self, int x,
                                  double sec, Ref<double> val, bool? ignoreResist)
        {
            if (x != AffectPetrify || self == null || self.destroyed || sec <= 0.0)
            {
                orig(self, x, sec, val, ignoreResist);
                return;
            }

            try
            {
                if (MedusaFeatures.IsOn(MedusaFeature.Petrify))
                {
                    double mul = PetrifyMultiplierNow();
                    double before = sec;
                    sec *= mul;

                    if (_petrifyLogCount < 5)
                    {
                        _petrifyLogCount++;
                        Write($"[MedusaHeadOverhaul] 石化时长 {before:0.##}s → {sec:0.##}s（×{mul:0.##}）目标={self.GetType().Name}");
                    }
                }

                if (MedusaFeatures.IsOn(MedusaFeature.BossPetrify))
                {
                    double resist = 0.0;
                    try { resist = self.getAffectResist((int?)AffectPetrify); } catch { }

                    if (resist > 0.0)
                    {
                        ignoreResist = true;

                        if (_petrifyLogCount < 8)
                        {
                            _petrifyLogCount++;
                            Write($"[MedusaHeadOverhaul] 石化抗性 resist={resist:0.##} → 强制石化（无视抗性）目标={self.GetType().Name} 时长={sec:0.##}s");
                        }
                    }
                }
            }
            catch (Exception ex) { Fail("petrify", ex); }

            orig(self, x, sec, val, ignoreResist);
        }

        private static double PetrifyMultiplierNow()
        {
            double m = MedusaKeys.Config.Value.PetrifyMultiplier;
            if (double.IsNaN(m) || m <= 0.0) m = 10.0;
            if (m > 100.0) m = 100.0;
            return m;
        }

        // ---------------------------------------------------------------- 选项菜单（IModMenu）

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "美杜莎的头";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("MEDUSA HEAD 设置"));
                b.createScroller(0.0);

                foreach (var f in MedusaFeatures.All)
                {
                    var feature = f;
                    bool on = MedusaFeatures.RawGet(feature);
                    string key = MedusaFeatures.KeyName(feature);
                    string hint = MedusaFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(MedusaFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !MedusaFeatures.RawGet(feature);
                            MedusaFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                var cfg = MedusaKeys.Config.Value;

                b.addSliderWidget(
                    StringUtils.AsHaxeString("爆炸范围倍数"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.RadiusMultiplier = v;
                        try { MedusaKeys.Config.Save(); } catch { }
                    },
                    cfg.RadiusMultiplier,
                    Ref<double>.In(0.5),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.5),
                    Ref<double>.In(20.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("翻滚距离倍数"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.RollRangeMultiplier = v;
                        try { MedusaKeys.Config.Save(); } catch { }
                    },
                    cfg.RollRangeMultiplier,
                    Ref<double>.In(0.1),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.1),
                    Ref<double>.In(5.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("石化时长倍数"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.PetrifyMultiplier = v;
                        try { MedusaKeys.Config.Save(); } catch { }
                    },
                    cfg.PetrifyMultiplier,
                    Ref<double>.In(1.0),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(1.0),
                    Ref<double>.In(50.0),
                    null,
                    Ref<int>.In(0));

                b.updateScroller();
                Write($"[MedusaHeadOverhaul] 选项菜单已建立：{MedusaFeatures.All.Length} 个功能开关 + 3 条滑条");
            }
            catch (Exception ex)
            {
                Write($"[MedusaHeadOverhaul] 建立选项菜单失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 工具

        private void Fail(string where, Exception ex)
        {
            if (_errorCount >= 5) return;
            _errorCount++;
            Write($"[MedusaHeadOverhaul] {where} 出错（第 {_errorCount} 次，最多记 5 次）: {ex.Message}");
        }
    }
}
