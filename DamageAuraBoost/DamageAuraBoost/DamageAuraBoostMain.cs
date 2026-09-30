#nullable disable

using dc;
using dc.en;
using dc.pow;
using dc.tool.atk;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.IO;

namespace DamageAuraBoost
{
    /// <summary>
    /// DamageAura（撕裂光环）强化模组
    /// ==============================
    /// 【数值部分】由 res.pak 数据补丁实现（patch_aura_data.py 生成）：
    ///   范围 distance ×2、攻速 tick ÷4（dps ×4 抵消，单次伤害不变）、持续时间 ×4、冷却 12s → 4s。
    ///
    /// 【连击部分】本代码实现 —— 参考 CollectorSpin 的触发方式：
    ///   原版 P_DmgKill（连击）变异只在 Mob.onDirectHitFromHero（英雄直接命中）时触发，
    ///   而光环攻击带"远程"标记 tag 14 会被排除，所以原版光环击杀/命中不触发连击。
    ///   修复：光环命中 Mob 时移除 tag 14，使其成为"直接命中"：
    ///     - 装备了连击变异 → 走原版 onDirectHitFromHero 链路（UI + 叠层 + 伤害加成全原版）
    ///     - 未装备变异     → 复刻 Mob.cs 的 P_DmgKill 处理（叠 affect 132 + 右上角连击 UI）
    ///
    /// 【总开关】仿 ChronoBlade 的 ChronoFeatures：
    ///   总开关（AuraFeature.Mod）一关 → 数值还原成原版 + 连击不再触发；
    ///   开关状态存在 coremod/config/DamageAuraBoost.json，
    ///   可在 游戏内「选项 → 模组 → 撕裂光环强化」里切换，或配置热键（默认不绑键）。
    /// </summary>
    public class DamageAuraBoostMain : ModBase, IOnGameExit, IOnAfterLoadingAssets, IOnHeroUpdate, IOnGameInit, IModMenu
    {
        private const string ItemId = "DamageAura";

        // ===== 原版数值（游戏 v35 模板 data.cdb，用于总开关关闭时还原）=====
        private const double VanillaDistance = 5.0;
        private const double VanillaTick = 0.1;
        private const double VanillaDps = 77.0;
        private const double VanillaDuration = 3.7;
        private const double VanillaCastCd = 12.0;

        // ===== 强化数值缓存（首次从 res.pak 应用后的物品数据里读取，避免与脚本常量重复维护）=====
        private double _boostDistance = 10.0;
        private double _boostTick = 0.025;
        private double _boostDps = 308.0;
        private double _boostDuration = 14.8;
        private double _boostCastCd = 4.0;
        private bool _boostCaptured;

        /// <summary>当前已应用的数值状态（null = 还没应用过；用于检测开关变化）。</summary>
        private bool? _statsAppliedState;

        /// <summary>P_DmgKill 变异使用的 affect 编号（连击层数）。</summary>
        private const int ComboAffectId = 132;

        /// <summary>光环攻击结算中标记（精确判定命中来自光环）。</summary>
        private bool _auraTicking;

        /// <summary>连击触发次数（日志采样用，避免刷屏）。</summary>
        private int _comboHits;

        public DamageAuraBoostMain(ModInfo info) : base(info) { }

        /// <summary>同时写控制台与模组日志文件（logs\log_latest.log）。</summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        public override void Initialize()
        {
            base.Initialize();
            try { Hook_DamageAura.fixedUpdate += OnAuraFixedUpdate; }
            catch (Exception ex) { Logger.Error(ex, "[DamageAuraBoost] Hook_DamageAura.fixedUpdate 挂载失败"); }
            try { Hook_Entity.applyAttackResult += OnApplyAttackResult; }
            catch (Exception ex) { Logger.Error(ex, "[DamageAuraBoost] Hook_Entity.applyAttackResult 挂载失败"); }
            Logger.Information("[DamageAuraBoost] 已加载: 数值=res.pak 数据补丁, 光环命中触发连击(P_DmgKill)");
        }

        /// <summary>资源加载完成：手动加载 mod 自带的 res.pak（数据补丁）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(DamageAuraBoostMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[DamageAuraBoost] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[DamageAuraBoost] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[DamageAuraBoost] res.pak 加载失败");
            }
        }

        /// <summary>开局自检：写下当前开关状态，并把默认配置落盘（第一次运行时生成可编辑的 json）。</summary>
        void IOnGameInit.OnGameInit()
        {
            LogConfig();
        }

        private void LogConfig()
        {
            try
            {
                var cfg = AuraKeys.Config.Value;
                Logger.Information(
                    "[DamageAuraBoost] 开关状态 —— " +
                    $"总开关={cfg.EnableMod} 数值强化={cfg.EnableStats} 连击={cfg.EnableCombo}");
                Logger.Information(
                    $"[DamageAuraBoost] 配置文件: {AuraKeys.Config.ConfigPath}" +
                    "（也可在游戏的 选项 → 模组 → 撕裂光环强化 菜单里直接改）");

                try { AuraKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[DamageAuraBoost] 写配置失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 每帧
        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 热键切换（默认一个都没绑；只在"刚按下"那一帧动作）
            try { AuraFeatures.PollHotkeys(Write); } catch { }

            // 数值开关 ↔ 物品数据 同步（开关变化时即时生效，新施放的光环读取新数值）
            SyncStats();
        }

        // ---------------------------------------------------------------- 数值开关

        /// <summary>
        /// 把物品数据的数值切到"强化值"或"原版值"。
        /// 强化值首次从 res.pak 应用后的数据里读取缓存；原版值用常量（运行时拿不到模板 cdb）。
        /// 幂等：状态没变就直接返回；CDB 还没就绪就下一帧再试。
        /// </summary>
        private void SyncStats()
        {
            bool want = AuraFeatures.IsOn(AuraFeature.Stats);
            if (_statsAppliedState == want) return;

            dynamic itemData = Data.Class.item.byId.get(ToHaxeString(ItemId));
            if (itemData == null || itemData.props == null) return;

            try
            {
                dynamic props = itemData.props;

                if (!_boostCaptured)
                {
                    // 首次：记下 res.pak 应用后的强化值（读不到就沿用字段默认值）
                    try
                    {
                        _boostDistance = (double)props.distance;
                        _boostTick = (double)props.tick;
                        dynamic dps0 = props.dps;
                        if (dps0 != null && dps0.length > 0) _boostDps = (double)dps0.getDyn(0);
                        _boostDuration = (double)props.duration;
                        _boostCastCd = (double)itemData.castCD;
                    }
                    catch { }
                    _boostCaptured = true;
                }

                if (want)
                {
                    ApplyStats(props, itemData, _boostDistance, _boostTick, _boostDps, _boostDuration, _boostCastCd);
                }
                else
                {
                    ApplyStats(props, itemData, VanillaDistance, VanillaTick, VanillaDps, VanillaDuration, VanillaCastCd);
                }

                _statsAppliedState = want;
                Logger.Information(
                    $"[DamageAuraBoost] 数值{(want ? "强化" : "还原")}: " +
                    $"distance={props.distance} tick={props.tick} duration={props.duration} castCD={itemData.castCD}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[DamageAuraBoost] 数值切换失败");
            }
        }

        private static void ApplyStats(dynamic props, dynamic itemData,
                                       double distance, double tick, double dps,
                                       double duration, double castCd)
        {
            props.distance = distance;
            props.tick = tick;
            dynamic dpsArr = props.dps;
            if (dpsArr != null && dpsArr.length > 0)
            {
                dpsArr.setDyn(0, dps);
            }
            props.duration = duration;
            itemData.castCD = castCd;
        }

        // ---------------------------------------------------------------- 连击触发（P_DmgKill 效果）

        /// <summary>光环结算期间置标记（连击功能关闭时不介入）。</summary>
        private void OnAuraFixedUpdate(Hook_DamageAura.orig_fixedUpdate orig, DamageAura self)
        {
            if (!AuraFeatures.IsOn(AuraFeature.Combo))
            {
                orig(self);
                return;
            }

            bool prev = _auraTicking;
            _auraTicking = true;
            try
            {
                orig(self);
            }
            finally
            {
                _auraTicking = prev;
            }
        }

        /// <summary>
        /// 光环命中 Mob：
        /// 1) 移除远程标记 tag 14 → 攻击变为"英雄直接命中" → 原版 Mob.onDirectHitFromHero
        ///    被调用（与 CollectorSpin 一致），装备连击变异时原版链路完整生效（UI/叠层/伤害）。
        /// 2) 未装备变异时，复刻 Mob.cs 的 P_DmgKill 处理：叠层 + 刷新窗口 + 右上角连击 UI。
        /// </summary>
        private void OnApplyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            bool comboOn = AuraFeatures.IsOn(AuraFeature.Combo);
            bool isAuraHit = comboOn
                             && _auraTicking
                             && self is Mob
                             && !self.destroyed
                             && self._level != null
                             && self._team != null
                             && self._team == self._level.teamMob;
            if (isAuraHit)
            {
                try { attack.setTag(14, false); } catch { }
            }
            orig(self, attack);

            if (isAuraHit && attack.source is Hero hero
                && hero.life > 0 && !hero.destroyed
                && !hero.inventory.hasItem(ToHaxeString("P_DmgKill")))
            {
                try
                {
                    TriggerDmgKillCombo(hero);
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "[DamageAuraBoost] 连击模拟失败");
                }
            }
        }

        /// <summary>复刻 Mob.cs onDirectHitFromHero 中 P_DmgKill 的处理：每次直接命中叠一层。</summary>
        private void TriggerDmgKillCombo(Hero hero)
        {
            dynamic itemData = Data.Class.item.byId.get(ToHaxeString("P_DmgKill"));
            if (itemData == null) return;

            double duration = (double)itemData.props.duration;   // 2.5s
            double one = 1.0;
            hero.setAffectS(ComboAffectId, duration, Ref<double>.From(ref one), null);
            hero.resetAllAffectToTime(ComboAffectId, duration);

            int count = hero.countAffect(ComboAffectId);
            double prct = (double)itemData.props.prct;
            double scaling = (double)itemData.commonProps.customScaling;
            double bonus = (prct + scaling * (double)hero.getRelevantPerkTier(ToHaxeString("P_DmgKill"))) * count;

            dynamic hud = hero._level?.game?.hud;
            if (hud != null && hud.comboCount != null)
            {
                if (count == 0)
                {
                    hud.comboCount.reset();
                }
                else
                {
                    hud.comboCount.setValue(count, 1.0 + bonus);
                }
            }

            _comboHits++;
            if (_comboHits % 10 == 1)
            {
                Logger.Information($"[DamageAuraBoost] 连击层数={count}, 加成={bonus:P1}, hud存在={hud != null}, comboCount存在={(hud != null && hud.comboCount != null)}");
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        // ---------------------------------------------------------------- 选项菜单（IModMenu）
        //
        // 模仿 ChronoBlade：游戏的「选项 → 模组 → 撕裂光环强化」这一页不是自动生成的，
        // 而是在 BuildMenu() 里自己 addToggleWidget 建出来（开关值写回配置并落盘）。

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "撕裂光环强化";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("DAMAGE AURA BOOST 设置"));
                b.createScroller(0.0);

                // 每个功能一个复选框（总开关排最前），用 AuraFeatures.All 循环生成
                foreach (var f in AuraFeatures.All)
                {
                    var feature = f;                       // 闭包捕获：别直接用循环变量
                    bool on = AuraFeatures.RawGet(feature);
                    string key = AuraFeatures.KeyName(feature);
                    string hint = AuraFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(AuraFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !AuraFeatures.RawGet(feature);
                            AuraFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                b.updateScroller();
                Write($"[DamageAuraBoost] 选项菜单已建立：{AuraFeatures.All.Length} 个功能开关");
            }
            catch (Exception ex)
            {
                Write($"[DamageAuraBoost] 建立选项菜单失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_DamageAura.fixedUpdate -= OnAuraFixedUpdate;
            Hook_Entity.applyAttackResult -= OnApplyAttackResult;
            AuraFeatures.ResetHotkeyState();
            Logger.Information("[DamageAuraBoost] 游戏退出，模组已卸载");
        }
    }
}
