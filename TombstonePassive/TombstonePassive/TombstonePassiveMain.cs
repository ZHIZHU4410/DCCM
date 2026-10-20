#nullable disable

using System;
using dc;
using dc.en;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using dc.tool.weap;

namespace TombstonePassive
{
    /// <summary>
    /// 墓碑（Tombstone）被动灾厄模组
    /// ================================
    /// 需求：**不再攻击**；带上墓碑后，**每移动一格**就以英雄为中心触发一次"灾厄"。
    ///
    /// 原版机制（GamePseudocode/dc.tool.weap/Tombstone.cs）：
    ///   第三下平 A（<c>hitFromWeapon</c> 的 <c>isLastCycle()</c> 分支）命中后，
    ///   在以**被命中敌人**为圆心、<c>props.range = 12</c> 格为半径的范围内，
    ///   给每个敌人挂上 <c>affectTombstone</c> 状态并调 <c>doGlas(0, mobs)</c>
    ///   —— 于是每个敌人头顶落一块墓碑，砸中后额外施加 affect 8 并继续向周围扩散。
    ///
    /// 本模组做的事：
    ///   · 每帧看英雄的**格子坐标**（<c>hero.cx / hero.cy</c>），一跨格就触发一次；
    ///   · 触发时把上面那套逻辑的圆心换成**英雄**（详见 TombstoneDoom.Trigger 的注释）；
    ///   · 完全不碰攻击流程 —— 平 A 照常，只是"不打也能触发"。
    ///
    /// 额外的配套修正（都是"不改就没法用"的那种）：
    ///   · **灾厄伤害不递减**：原版 <c>getDiminishingFactor(69, 1, limit=3, null)</c> 在
    ///     同一 5 秒窗口内第 3 次起直接返回 0（GamePseudocode/dc/Entity.cs:4133
    ///     <c>if (1 + 已用次数 >= absoluteMaxUses) return 0.0;</c>）。
    ///     那是给三连击设计的节流；改成每格触发后几乎永远是"0 伤害"，
    ///     所以这里拦截 key == 69 的调用，改成由配置里的「灾厄伤害系数」决定。
    /// </summary>
    public class TombstonePassiveMain : ModBase, IOnGameExit, IOnGameInit, IOnFrameUpdate, IOnHeroUpdate, IModMenu
    {
        /// <summary>异常日志限流。</summary>
        private int _errorCount;

        /// <summary>是否已经记录过上一格（第一次进关只记录、不触发）。</summary>
        private bool _hasLastCell;

        /// <summary>上一次触发/记录时英雄所在的格子。</summary>
        private int _lastCx, _lastCy;

        /// <summary>触发冷却剩余秒数。</summary>
        private double _cooldown;

        /// <summary>上一帧的 dt，用来识别"同一帧被回调两次"（与 MobBuffCombo 同做法）。</summary>
        private double _lastDt;
        private bool _hasDt;

        /// <summary>首帧诊断日志只打一条。</summary>
        private bool _diagLogged;

        public TombstonePassiveMain(ModInfo info) : base(info) { }

        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ---------------------------------------------------------------- 生命周期

        public override void Initialize()
        {
            base.Initialize();

            try { Hook_Entity.getDiminishingFactor += OnGetDiminishingFactor; }
            catch (Exception ex) { Logger.Error(ex, "[TombstonePassive] Hook_Entity.getDiminishingFactor 挂载失败"); }

            Write("[TombstonePassive] 已加载：带墓碑走路 = 每格自动灾厄（被动）");
        }

        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = DoomKeys.Config.Value;
                Write("[TombstonePassive] 开关状态 —— " +
                      $"总开关={cfg.EnableMod} 被动灾厄={cfg.EnablePassive} " +
                      $"不递减={cfg.EnableFullDamage}（系数 {cfg.DamageFactor:0.##}）" +
                      $"英雄落墓碑={cfg.EnableHeroTombstone} " +
                      $"半径={cfg.RadiusCells:0.#}格 最小间隔={cfg.MinIntervalSec:0.##}s");
                Write($"[TombstonePassive] 配置文件: {DoomKeys.Config.ConfigPath}" +
                      "（也可在游戏里 选项 → 模组 → 墓碑被动 直接改）");

                try { if (DoomKeys.Config.Value != null) DoomKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[TombstonePassive] 写配置失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Entity.getDiminishingFactor -= OnGetDiminishingFactor; } catch { }

            DoomFeatures.ResetHotkeyState();
            _hasLastCell = false;
            _cooldown = 0.0;
            _lastDt = 0.0;
            _hasDt = false;
            _diagLogged = false;

            Write("[TombstonePassive] 游戏退出，模组已卸载");
        }

        // ---------------------------------------------------------------- 每帧：格子检测

        /// <summary>
        /// 双通道驱动：<c>IOnFrameUpdate</c>（框架直接回调，不依赖 Hook 织入）
        /// + <c>IOnHeroUpdate</c>（跟着英雄更新走）。两者可能同一帧各回调一次，
        /// 用 <c>dt</c> 重复判断去重（MobBuffCombo 同做法）。
        /// </summary>
        void IOnFrameUpdate.OnFrameUpdate(double dt) => Tick(dt);

        void IOnHeroUpdate.OnHeroUpdate(double dt) => Tick(dt);

        private void Tick(double dt)
        {
            try { DoomFeatures.PollHotkeys(Write); } catch { }

            try
            {
                bool sameFrame = _hasDt && dt == _lastDt;
                _lastDt = dt;
                _hasDt = true;

                if (dt > 0.0 && !sameFrame && _cooldown > 0.0)
                {
                    _cooldown -= dt;
                    if (_cooldown < 0.0) _cooldown = 0.0;
                }

                TickPassive();
            }
            catch (Exception ex) { Fail("tick", ex); }
        }

        /// <summary>"每移动一格"的判定：英雄的格子坐标（<c>cx / cy</c>）发生变化就触发一次。</summary>
        private void TickPassive()
        {
            var cfg = DoomKeys.Config.Value;

            if (!DoomFeatures.IsOn(DoomFeature.Passive))
            {
                _hasLastCell = false;       // 关掉期间不累积，重新打开时以当前格为基准
                return;
            }

            Hero hero = null;
            try { hero = dc.pr.Game.Class.ME?.hero; } catch { }
            if (hero == null || hero.destroyed || hero.life <= 0)
            {
                _hasLastCell = false;
                return;
            }

            Tombstone tomb = TombstoneDoom.FindTombstone(hero);
            if (tomb == null)
            {
                _hasLastCell = false;       // 没带墓碑 → 不触发
                return;
            }

            // 第一次（刚进关 / 刚装上墓碑）：只记录基准格，不触发
            if (!_hasLastCell)
            {
                _hasLastCell = true;
                _lastCx = hero.cx;
                _lastCy = hero.cy;
                return;
            }

            // 没跨格 → 什么都不做
            if (hero.cx == _lastCx && hero.cy == _lastCy) return;

            int fromCx = _lastCx, fromCy = _lastCy;
            _lastCx = hero.cx;
            _lastCy = hero.cy;

            // 冷却中：格子已经记下了，等冷却结束后的下一格再触发
            if (_cooldown > 0.0) return;

            if (cfg.MinIntervalSec > 0.0) _cooldown = cfg.MinIntervalSec;

            int marked = TombstoneDoom.Trigger(hero, tomb, cfg.RadiusCells,
                                               DoomFeatures.IsOn(DoomFeature.HeroTombstone),
                                               Write);

            if (!_diagLogged)
            {
                _diagLogged = true;
                Write("[TombstonePassive] 首次被动灾厄 —— " +
                      $"起点格=({fromCx},{fromCy}) → 当前格=({hero.cx},{hero.cy}) " +
                      $"半径={cfg.RadiusCells:0.#}格 命中/标记={marked} 只 " +
                      $"（触发点 = 英雄身体中心）");
            }
        }

        // ---------------------------------------------------------------- 灾厄伤害：绕过"第 3 次归零"

        /// <summary>
        /// <c>Entity.getDiminishingFactor(key, fullPowerUses, absoluteMaxUses, overrideResetTime)</c>
        /// 是游戏的"重复命中伤害递减"机制，内部按 key 记次数：
        /// <code>
        /// count = 1 + diminishingReturns[key];
        /// if (count >= absoluteMaxUses) return 0.0;      // ← 直接归零
        /// </code>
        /// 墓碑灾厄用的 key 是 **69**（全工程仅此一处），所以这里只拦 69：
        /// 打开「灾厄伤害不递减」后，返回配置的伤害系数，既不归零也不受 5 秒窗口限制。
        /// 其它 key（其它武器/异常状态的递减）原样放行。
        /// </summary>
        private double OnGetDiminishingFactor(Hook_Entity.orig_getDiminishingFactor orig,
                                              Entity self, int key, int fullPowerUses,
                                              int absoluteMaxUses, double? overrideResetTime)
        {
            if (key == TombstoneDoom.DoomFactorKey && self is Hero
                && DoomFeatures.IsOn(DoomFeature.FullDamage))
            {
                try
                {
                    double f = DoomKeys.Config.Value.DamageFactor;
                    if (double.IsNaN(f) || f < 0.0) f = 0.0;
                    if (f > 5.0) f = 5.0;
                    return f;
                }
                catch { }
            }

            return orig(self, key, fullPowerUses, absoluteMaxUses, overrideResetTime);
        }

        // ---------------------------------------------------------------- 选项菜单（IModMenu）

        /// <summary>这一页在「选项 → 模组」里的名字。</summary>
        public string GetName() => "墓碑被动";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("TOMBSTONE PASSIVE 设置"));
                b.createScroller(0.0);

                foreach (var f in DoomFeatures.All)
                {
                    var feature = f;
                    bool on = DoomFeatures.RawGet(feature);
                    string key = DoomFeatures.KeyName(feature);
                    string hint = DoomFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(DoomFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !DoomFeatures.RawGet(feature);
                            DoomFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                var cfg = DoomKeys.Config.Value;

                b.addSliderWidget(
                    StringUtils.AsHaxeString("灾厄半径(格)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.RadiusCells = v;
                        try { DoomKeys.Config.Save(); } catch { }
                    },
                    cfg.RadiusCells,
                    Ref<double>.In(0.5),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(1.0),
                    Ref<double>.In(30.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("最小触发间隔(秒)"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.MinIntervalSec = v;
                        try { DoomKeys.Config.Save(); } catch { }
                    },
                    cfg.MinIntervalSec,
                    Ref<double>.In(0.05),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.0),
                    Ref<double>.In(3.0),
                    null,
                    Ref<int>.In(0));

                b.addSliderWidget(
                    StringUtils.AsHaxeString("灾厄伤害系数"),
                    (HlAction<double>)delegate (double v)
                    {
                        cfg.DamageFactor = v;
                        try { DoomKeys.Config.Save(); } catch { }
                    },
                    cfg.DamageFactor,
                    Ref<double>.In(0.05),
                    b.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.05),
                    Ref<double>.In(3.0),
                    null,
                    Ref<int>.In(0));

                b.updateScroller();
                Write($"[TombstonePassive] 选项菜单已建立：{DoomFeatures.All.Length} 个功能开关 + 3 条滑条");
            }
            catch (Exception ex)
            {
                Write($"[TombstonePassive] 建立选项菜单失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 工具

        private void Fail(string where, Exception ex)
        {
            if (_errorCount >= 5) return;
            _errorCount++;
            Write($"[TombstonePassive] {where} 出错（第 {_errorCount} 次，最多记 5 次）: {ex.Message}");
        }
    }
}
