#nullable disable
using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.hxd.fs;
using dc.tool;
using dc.tool.atk;
using dc.tools.pak;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;

namespace GojoLimitless
{
    /// <summary>
    /// 《咒术回战》五条悟「无下限」模组。
    ///
    /// ============================ 能力清单 ============================
    ///  无下限（被动）      Infinity        —— 屏障吸收一切来犯攻击 + 迟滞领域 + 湮灭弹幕
    ///  术式顺转·苍         F5              —— 大范围强牵引（吸向自身）+ 低伤害
    ///  术式反转·赤         F6              —— 大范围强斥力 + 击飞 + 中伤害
    ///  虚式·茈             F7 / 苍→赤连招  —— 前方长距贯穿必杀
    ///  领域展开·无量空处   F9              —— 大范围定身 + 持续伤害 + 短暂无敌
    ///  强制开启（测试）    F8              —— 保留，但默认"按键即用"时无意义
    ///  模组菜单            F10             —— 全部数值/按键/开关，实时生效
    /// =================================================================
    ///
    /// 驱动方式：
    ///   * <see cref="IOnFrameUpdate"/>  —— 模组框架直接回调（挂的是 <c>dc.Boot.update</c>），
    ///     **游戏暂停时也会跑**。菜单（打开时会真暂停游戏）、原生选项页的按键捕获、
    ///     HUD 全放这里。
    ///   * **不实现** <c>IOnHeroUpdate</c>：ModCore 在 <c>IOnFrameUpdate</c> 里本来就会
    ///     转发一次 <c>IOnHeroUpdate</c>（而且不判暂停），两个都实现等于同一帧跑两遍。
    ///
    /// 配置入口有两个，都读写同一份 <c>coremod/config/GojoLimitless.json</c>：
    ///   * 游戏原生「选项 → 模组 → GojoLimitless」（<see cref="IModMenu"/>）—— 推荐
    ///   * 游戏内 <c>F10</c> 覆盖层菜单（<see cref="GojoMenu"/>）—— 不暂停也能开
    ///
    /// 屏障拦截选在 <c>Hook_Entity.applyAttackResult</c>（和 DamageAuraBoost /
    /// AutoParry / Katana 同一入口）：**原函数一进来就读 finalDmg**，
    /// 所以在这一层把 finalDmg 清零就等于"这一击根本没到"。
    ///
    /// **不含任何自建道具 / res.pak**：招式纯靠按键触发，全部复用游戏原有的
    /// affect / FX / 攻击管线，不新增也不修改任何游戏资源。
    /// </summary>
    public class GojoLimitlessMain : ModBase,
        IOnGameExit,
        IOnAfterLoadingAssets,
        IOnFrameUpdate,
        IOnHeroUpdate,
        IModMenu
    {
        private Hero _lastHero;
        private int _levelStarts;

        // 原生选项页（IModMenu）
        private static readonly GojoModMenu _optionsPage = new();

        public GojoLimitlessMain(ModInfo info) : base(info) { }

        // ------------------------------------------------------------ 原生选项菜单

        /// <summary>游戏「选项 → 模组」里那一页的名字。</summary>
        public string GetName() => "GojoLimitless";

        /// <summary>
        /// 框架在玩家打开「选项 → 模组 → GojoLimitless」时回调这里。
        /// 具体控件在 <see cref="GojoModMenu"/> 里建。
        /// </summary>
        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                _optionsPage.BuildMenu(options);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "构建原生选项菜单失败");
            }
        }

        // ------------------------------------------------------------ 生命周期

        public override void Initialize()
        {
            base.Initialize();
            Log.Attach(Logger);

            Cfg.Reload();
            if (Cfg.PatchedOnLoad)
            {
                Log.Info("配置已补齐缺失字段 / 迁移版本（见 coremod/config/GojoLimitless.json）");
            }

            try { Hook_Entity.applyAttackResult += OnApplyAttackResult; }
            catch (Exception ex) { Log.Exception(ex, "Hook_Entity.applyAttackResult 挂载失败"); }

            // 迟滞领域：压低敌人的移动速度乘数（原版 affect 133 做不到，见 SlowAura 注释）
            try { Hook_Mob.getMoveSpeedMul += Abilities.SlowAura.HookGetMoveSpeedMul; }
            catch (Exception ex) { Log.Exception(ex, "Hook_Mob.getMoveSpeedMul 挂载失败"); }

            // 新英雄（开新局 / 换角色）时复位：和 KillAttackSpeed / DeathRevive 同一入口
            try { Hook_Hero.init += OnHeroInit; }
            catch (Exception ex) { Log.Exception(ex, "Hook_Hero.init 挂载失败"); }

            Abilities.GojoHub.Initialize();

            UiCfg ui = Cfg.V.Ui;
            Log.Info("已加载 —— " + DescribeKeys());
            Log.Info($"菜单键 {(ui != null ? ui.MenuKey : "F10")}"
                     + " / 强制开启键 " + (ui != null ? ui.ForceEnableKey : "O")
                     + "；按键是模组自己轮询的，可在菜单或配置里改");
        }

        private static string DescribeKeys()
        {
            var sb = new System.Text.StringBuilder();
            var list = Abilities.GojoHub.Abilities;
            for (int i = 1; i < list.Count; i++)     // [0] 是无下限被动
            {
                if (sb.Length > 0) sb.Append(" / ");
                int vk = list[i].TriggerKey;
                sb.Append(list[i].DisplayName).Append('(')
                  .Append(vk > 0 ? GojoKeys.Name(vk) : "未绑定").Append(')');
            }
            return sb.ToString();
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Entity.applyAttackResult -= OnApplyAttackResult; } catch { }
            try { Hook_Mob.getMoveSpeedMul -= Abilities.SlowAura.HookGetMoveSpeedMul; } catch { }
            try { Hook_Hero.init -= OnHeroInit; } catch { }
            try { GojoMenu.Reset(); } catch { }
            try { GojoHud.Reset(); } catch { }
            try { Abilities.GojoHub.Shutdown(); } catch { }
            try { Abilities.GojoHub.ResetAll(); } catch { }
            Log.Info("游戏退出，模组已卸载");
        }

        /// <summary>
        /// 加载完成。
        ///
        /// 现在**什么都不用挂**：道具已按需求彻底删除，所以没有 res.pak、
        /// 没有 cdb 数据补丁，也就不会有"pak 加载失败"这一类降级路径。
        /// 留着这个回调是为了把当前配置状态打一行日志，方便自检。
        /// </summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                UiCfg ui = Cfg.V.Ui;
                int count = Abilities.GojoHub.Abilities.Count - 1;   // 减去无下限被动
                Log.Info("资源阶段完成 —— 本模组不含任何 res.pak / 数据补丁（道具已删除）");
                Log.Info($"招式 {count} 个，HUD {(ui == null || ui.HudEnabled ? "开" : "关")}，"
                         + $"总开关 {(ui == null || ui.ModEnabled ? "开" : "关")}");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "资源阶段日志失败");
            }
        }

        // ------------------------------------------------------------ 英雄

        private void OnHeroInit(Hook_Hero.orig_init orig, Hero self)
        {
            orig(self);
            try
            {
                Abilities.InfinityAbility.LogSummary();
                Abilities.GojoHub.ResetAll();
                try { GojoHud.Reset(); } catch { }
                try { GojoMenu.Reset(); } catch { }

                Cfg.Reload();
                Abilities.GojoHub.ReloadKeys();
                Abilities.InfinityAbility.InvalidateReserve();

                _lastHero = self;
                _levelStarts++;
                Log.Info($"新英雄（第 {_levelStarts} 次）—— 招式 / 储量 / HUD 已复位，配置已重载");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "英雄初始化处理失败");
            }
        }

        // ------------------------------------------------------------ 每帧

        /// <summary>
        /// 模组框架的心跳：**暂停时也会回调**。
        ///
        /// 菜单（打开时会真暂停游戏）、原生选项页的按键捕获、HUD 全挂在这里。
        /// 不再实现 <c>IOnHeroUpdate</c>：ModCore 在 <c>IOnFrameUpdate</c> 里本来就
        /// 会转发一次 <c>IOnHeroUpdate</c>（而且**不判暂停**），两个都实现会导致同一帧
        /// 跑两遍 —— 菜单的 flash/重复键计时会按 2×dt 走，还得靠 dt 相等去重（那个去重
        /// 会吞掉按键）。只留这一条，逻辑干净、按键不会被吞。
        /// </summary>
        void IOnFrameUpdate.OnFrameUpdate(double dt) => TickAll(dt);

        /// <summary>
        /// 额外挂一条 <see cref="ModCore.Events.Interfaces.Game.Hero.IOnHeroUpdate"/>。
        ///
        /// 为什么加：本模组原来**故意不实现**它（怕和 IOnFrameUpdate 重复跑一帧）。
        /// 但仓库里按键工作正常的 <c>ChronoBlade</c> 恰恰是在 <c>IOnHeroUpdate</c> 里读键的
        /// （`ChronoBladeMod.cs:703` 起），而 DCCM 一帧里两个回调的**先后顺序**会决定
        /// "游戏按键状态表"那一帧是否已经刷新。多挂一条可以让输入轮询落在更靠后的时机，
        /// 代价只是重复回调 —— 而 <c>TickAll</c> 里本来就有 `Input.Generation` 去重，
        /// 同一帧的第二次进来只会走 <see cref="RenderOnly"/>。
        /// </summary>
        void ModCore.Events.Interfaces.Game.Hero.IOnHeroUpdate.OnHeroUpdate(double dt) => TickAll(dt);

        private void TickAll(double dt)
        {
            try
            {
                if (dt <= 0.0 || double.IsNaN(dt) || dt > 1.0) dt = 1.0 / 60.0;

                // ---- 门槛：每帧无条件刷（和 hero 无关）----
                //
                // ⚠️ 必须在下面那条早退**之前**：换关 / 读档 / 死亡瞬间 hero 会有若干帧是
                //    null 或已销毁，而 ResetAll() 会把门槛置 false。如果门槛只在
                //    GojoHub.Tick（需要 hero 非 null）里刷，那它就永远刷不回 true ——
                //    实机表现就是 HUD 一直 [OFF]、所有招式被 !Gated 挡掉。
                Abilities.GojoHub.UpdateGateAlways();

                Hero hero = ResolveHero();
                if (!IsUsableHero(hero))
                {
                    WarnHeroOnce(hero);
                    return;
                }
                _heroWarned = false;

                // ---- 英雄换了（换关 / 死亡 / 新局）→ 复位一次 ----
                if (!ReferenceEquals(hero, _lastHero))
                {
                    _lastHero = hero;
                    Abilities.GojoHub.ResetAll();
                    Cfg.Reload();
                    Abilities.GojoHub.ReloadKeys();
                    try { GojoHud.Reset(); } catch { }
                    try { GojoMenu.Reset(); } catch { }
                    try { GojoOrbFx.Reset(); } catch { }
                    try { GojoDomainFx.Reset(); } catch { }
                    Log.Info("检测到新英雄对象，全部状态已复位");
                }

                // ============================================================
                // 招式派发 + HUD：**每次回调都执行**
                //
                // ⚠️⚠️ 这里绝对不能再加"一帧一次"的早退。
                //
                // 原来 `GojoHub.Tick` 和 `GojoHud.Update` 都放在世代去重之后，而
                // IOnFrameUpdate / IOnHeroUpdate 两个回调里只有**第二个**能通过去重 ——
                // 一旦那个回调因为任何原因没来（或英雄判定在那一帧失败），
                // 招式整段就被跳过，表现就是"按了键毫无反应"。
                //
                // 而招式自己用 `_keyWasDown` 做边沿（见 GojoAbilityBase.TryActivate），
                // 重复调用是幂等的，不会重复放招。所以这里可以放心每帧跑。
                // ============================================================

                // 菜单键 / 覆盖层菜单要用到 JustPressed，所以先确保本帧采样过一次
                if (Input.Generation == _lastInputGeneration)
                {
                    Input.Poll();
                    _lastInputGeneration = Input.Generation;
                }

                int menuKey = Abilities.GojoHub.MenuKey;
                if (Input.Pressed(menuKey))
                {
                    GojoMenu.Toggle(hero);
                    Log.Info(GojoMenu.IsOpen ? "菜单已打开（游戏已暂停）" : "菜单已关闭");
                    return;
                }

                if (GojoMenu.IsOpen)
                {
                    GojoMenu.Update(hero, dt);
                    return;
                }

                // 原生选项页的按键行刷新（内部幂等）
                GojoModMenu.Tick(dt);

                // ⚠️ 这里**故意不判 IsGamePaused()**（见下方长注释）
                Abilities.GojoHub.Tick(hero, dt, inputAlreadyPolled: true);

                UiCfg ui = Cfg.V.Ui;
                if (ui == null || ui.HudEnabled) GojoHud.Update(hero);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "每帧调度异常");
            }
        }

        private long _lastInputGeneration = -1;

        /// <summary>是否已经因为"英雄不可用"警告过一次（避免刷屏）。</summary>
        private bool _heroWarned;

        /// <summary>
        /// 取"当前真正在用的英雄"。**优先用游戏自己的 <c>dc.pr.Game.ME.hero</c>**。
        ///
        /// ============================ 为什么不用 ModCore.HeroInstance ============================
        /// 原来只用 <c>ModCore.Modules.Game.Instance.HeroInstance</c>，实机日志证明它
        /// **经常返回一个已 destroyed 的旧对象** —— 于是主循环里那条
        /// <c>if (hero == null || hero.destroyed) return;</c> 一直命中，
        /// 后面所有东西（招式派发、HUD 更新）全被跳过：
        /// 表现就是"按键探针有输出，但 [Tick] 和 HUD 创建一条都没有"。
        ///
        /// 仓库里按键工作正常的模组都用游戏自己的引用：
        ///   · <c>ZoomVisionMain.cs:100-102</c> → <c>dc.pr.Game.Class.ME</c> + <c>game.hero</c>
        ///   · <c>ChronoBladeMod.cs</c> → 同样先看 <c>HeroInstance</c>，但判定用的是
        ///     <c>h != null &amp;&amp; !h.destroyed &amp;&amp; h._level != null</c>
        /// 所以这里两条路都试，并把"有 _level 的"排在前面。
        /// </summary>
        private static Hero ResolveHero()
        {
            // 1) 游戏自己的当前英雄（最可靠）
            try
            {
                Hero h = dc.pr.Game.Class.ME?.hero;
                if (IsUsableHero(h)) return h;
            }
            catch { }

            // 2) ModCore 的实例（游戏引用拿不到时兜底）
            try
            {
                Hero h = ModCore.Modules.Game.Instance?.HeroInstance;
                if (IsUsableHero(h)) return h;
            }
            catch { }

            return null;
        }

        /// <summary>英雄能不能用：非空、没销毁、还活着、并且已经挂上了关卡。</summary>
        private static bool IsUsableHero(Hero h)
        {
            if (h == null) return false;
            try
            {
                if (h.destroyed) return false;
                if (h.life <= 0) return false;
                if (h._level == null) return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 英雄不可用时打一条（每秒最多一次），把**四个判定的原始值**都摊开 ——
        /// 这样下次一眼就能看出是"拿不到引用"还是"被某个条件挡住"。
        /// </summary>
        private void WarnHeroOnce(Hero h)
        {
            if (_heroWarned) return;

            long now = Environment.TickCount64;
            if (now - _heroWarnAt < 2000) return;
            _heroWarnAt = now;
            _heroWarned = true;

            string gameHero = "取不到";
            try
            {
                Hero g = dc.pr.Game.Class.ME?.hero;
                gameHero = g == null ? "null"
                    : $"destroyed={g.destroyed} life={g.life} level={(g._level != null ? "有" : "null")}";
            }
            catch (Exception ex) { gameHero = "异常:" + ex.GetType().Name; }

            string mcHero = "取不到";
            try
            {
                Hero m = ModCore.Modules.Game.Instance?.HeroInstance;
                mcHero = m == null ? "null"
                    : $"destroyed={m.destroyed} life={m.life} level={(m._level != null ? "有" : "null")}";
            }
            catch (Exception ex) { mcHero = "异常:" + ex.GetType().Name; }

            Log.Warn($"英雄不可用 → 本帧跳过招式与 HUD。Game.hero[{gameHero}] HeroInstance[{mcHero}] "
                     + $"传入=[{(h == null ? "null" : "非null")}]");
        }

        private long _heroWarnAt;

        /// <summary>游戏是否处于暂停（用来避免在原版暂停菜单里乱放招）。</summary>
        private static bool IsGamePaused()
        {
            try
            {
                dc.pr.Game game = dc.pr.Game.Class.ME;
                return game != null && game.paused;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ 无下限屏障

        /// <summary>
        /// 拦截打向英雄的攻击。
        ///
        /// <c>Entity.applyAttackResult</c> 的执行顺序是：
        ///   life&gt;0 检查 → lastAtkData 赋值 → <b>读 a.finalDmg</b> → 各种减伤/affect 处理
        /// 所以在 orig 之前把 finalDmg 清零 = 这一击完全没造成伤害，
        /// 而且**照常放行 orig**，让 Hero.applyAttackResult（activeSkillsManager 等）
        /// 仍然能看到"有人打过我"。
        /// </summary>
        private void OnApplyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData a)
        {
            if (self is Hero hero && a != null)
            {
                try
                {
                    bool hostile = a.source == null || !ReferenceEquals(a.source, hero);
                    if (hostile)
                    {
                        Abilities.InfinityAbility.TryAbsorb(hero, a, a.finalDmg);
                    }
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, "无下限拦截失败（放行原逻辑）");
                }
            }

            orig(self, a);
        }

    }
}
