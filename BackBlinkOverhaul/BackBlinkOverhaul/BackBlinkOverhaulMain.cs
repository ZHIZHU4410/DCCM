#nullable disable

using dc;
using dc.en;
using dc.pow;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero.activeSkills;
using dc.tool.hero;
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
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BackBlinkOverhaul
{
    using Mob = dc.en.Mob;
    using Math = System.Math;
    /// <summary>
    /// BackBlink（位移 / Déphasage）全面改造模组
    /// ==========================================
    /// 原版行为：向"近距离"（props.distance = 11 格）内最近的敌人背后传送一次，
    ///           冷却 2 秒，落地只对目标打一发基础伤害。
    ///
    /// 本模组把它改成"闪现突袭"技能：
    ///
    ///   1. 基础传送机制改造
    ///      1.1.1 无视墙体（Hook_TargetHelper.filterBySight 返回 true → 原版寻路一定成立）
    ///      1.1.2 无视距离（res.pak 把 props.distance 改成 9999）
    ///      1.1.3 锁定任意敌人（放弃原版 DecisionHelper，自己在 Level.entities 里取最近敌人）
    ///      1.1.4 落点判定与安全位置修正（IsFree + 环形搜索 + safeTpTo(ignoreColl: true)）
    ///      1.1.5 无目标时的处理（Hook_HeroActiveSkillsManager.startCooldownForItem 里
    ///            捕获原版"没找到目标"的 0.5s 冷却信号 → 朝面向方向盲闪）
    ///      1.2   移除冷却 / 无充能（Hook startCooldownForItem / resetCooldownForItem）
    ///      1.3   拖尾 / 残影 / 慢动作 / 起点终点特效
    ///      1.4   音效 / 屏幕震动 / 命中反馈
    ///   2. 落地冲击波 Blink Strike
    ///      2.1   传送到达瞬间、以落点为圆心触发一次
    ///      2.2   走原版攻击管线（词条 / 暴击 / 背刺 / 处决全部自动联动）
    ///      2.3   半径 / 穿墙 / Boss 精英衰减 可配置
    ///   3. 连锁传送 Chain Blink
    ///      3.1   连锁窗口内击杀 → 叠一层"连锁强化"
    ///      3.3   传送后加标记，击杀时检测标记
    ///      3.4   传送 → 击杀 → 强化 → 再传送 的循环（无冷却设定下用"强化窗口 / 叠加层数"表达）
    ///
    /// 所有开关都在 coremod/config/BackBlinkOverhaul.json，
    /// 也可以在游戏内「选项 → 模组 → 位移·闪现突袭」里切换。
    /// </summary>
    public class BackBlinkOverhaulMain : ModBase, IOnGameExit, IOnAfterLoadingAssets, IOnHeroUpdate, IOnGameInit, IModMenu
    {
        // ---------------------------------------------------------------- 原版常量（总开关关闭时用来还原）

        private const string ItemId = BlinkCore.ItemId;
        private const double VanillaCastCd = 2.0;
        private const double VanillaDistance = 11.0;
        private const double VanillaPower = 70.0;

        private double _vanillaCastCd = VanillaCastCd;
        private double _vanillaDistance = VanillaDistance;
        private double _modCastCd = 0.0;
        private double _modDistance = 9999.0;
        private bool _captured;
        private bool? _statsAppliedState;

        // ---------------------------------------------------------------- 运行时状态

        /// <summary>是否处于"我们的传送结算中"（用于 Hook_TargetHelper 放行视线判定）。</summary>
        internal static bool BlinkActive;

        /// <summary>重入保护：防止连锁递归 / 无限连按。</summary>
        private int _depth;
        private const int MaxDepth = 6;

        /// <summary>1.2.4 上一次传送的时间（秒），用于最小施放间隔。</summary>
        private double _lastCastTime = -999.0;

        /// <summary>3. 连锁状态。</summary>
        private int _chainStacks;
        private double _chainEndTime = -999.0;

        /// <summary>3.3 连锁标记：本次传送挂上的"可连锁"标记的到期时间（按实体 uid 记录）。</summary>
        private readonly Dictionary<int, double> _chainMarks = new();

        /// <summary>3. 连锁是否处于启用状态（缓存在一次传送里，避免重复读配置）。</summary>
        private bool _chainMarksActive;

        /// <summary>本帧已经结算过的连锁次数（3.2.4 防止无限递归）。</summary>
        private int _frameChainCount;
        private double _frameStamp = -1.0;

        private int _castCount;

        // ---------------------------------------------------------------- 便捷热键（T 键召唤卷轴）

        /// <summary>T 键上一帧的按下状态（边沿检测，按住不连发）。</summary>
        private bool _scrollKeyDown;

        /// <summary>上一次召唤卷轴的游戏时间（秒），用于最小间隔保护。</summary>
        private double _lastScrollSpawnTime = -999.0;

        private int _scrollSpawnCount;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public BackBlinkOverhaulMain(ModInfo info) : base(info) { }

        /// <summary>同时写控制台与模组日志文件（logs\log_latest.log）。</summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ================================================================ 生命周期

        public override void Initialize()
        {
            base.Initialize();

            Try("Hook_BeheadedActiveSkillsManager.useSkillItem", () =>
                Hook_BeheadedActiveSkillsManager.useSkillItem += OnUseSkillItem);
            Try("Hook_HeroActiveSkillsManager.startCooldownForItem", () =>
                Hook_HeroActiveSkillsManager.startCooldownForItem += OnStartCooldownForItem);
            Try("Hook_HeroActiveSkillsManager.canUseActiveSkill", () =>
                Hook_HeroActiveSkillsManager.canUseActiveSkill += OnCanUseActiveSkill);
            Try("Hook_TargetHelper.filterBySight", () =>
                Hook_TargetHelper.filterBySight += OnFilterBySight);
            Try("Hook_Entity.applyAttackResult", () =>
                Hook_Entity.applyAttackResult += OnApplyAttackResult);
            Try("Hook_Mob.onDie", () =>
                Hook_Mob.onDie += OnMobDie);

            Logger.Information("[BackBlinkOverhaul] 已加载：无视墙/无视距离/无冷却 + 落地冲击波 + 连锁传送");
        }

        private void Try(string what, Action act)
        {
            try { act(); }
            catch (Exception ex) { Logger.Error(ex, $"[BackBlinkOverhaul] 挂载失败: {what}"); }
        }

        /// <summary>资源加载完成：手动加载 mod 自带的 res.pak（数据补丁 + 自定义 sfx）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(BackBlinkOverhaulMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(BlinkCore.Hx(pakPath));
                    Logger.Information($"[BackBlinkOverhaul] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[BackBlinkOverhaul] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BackBlinkOverhaul] res.pak 加载失败");
            }
        }

        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = BlinkKeys.Config.Value;
                Logger.Information(
                    "[BackBlinkOverhaul] 开关状态 —— " +
                    $"总开关={cfg.EnableMod} 穿墙={cfg.EnableWallPierce} 无距={cfg.EnableInfiniteRange} " +
                    $"任意敌人={cfg.EnableAnyEnemy} 落点修正={cfg.EnableLandingFix} 盲闪={cfg.EnableFallbackBlink}");
                Logger.Information(
                    "[BackBlinkOverhaul] 冷却/充能 —— " +
                    $"无冷却={cfg.EnableNoCooldown} 无充能={cfg.EnableInfiniteCharges} 冷却倍率={cfg.CooldownMult} 最小间隔={cfg.MinCastIntervalS}s");
                Logger.Information(
                    "[BackBlinkOverhaul] 冲击波 —— " +
                    $"启用={cfg.EnableShockwave} 系数={cfg.ShockwaveDamageMult} 半径={cfg.ShockwaveRadius}格 " +
                    $"穿墙={cfg.ShockwavePierceWall} Boss衰减={cfg.BossDamageMult} 精英衰减={cfg.EliteDamageMult} " +
                    $"无视护盾={cfg.ForceIgnoreGlobalShield}");
                Logger.Information(
                    "[BackBlinkOverhaul] 连锁 —— " +
                    $"启用={cfg.EnableChain} 窗口={cfg.ChainWindowS}s 上限={cfg.ChainMaxStacks} 每层加成={cfg.ChainDamageBonusPerStack:P0}");
                Logger.Information(
                    $"[BackBlinkOverhaul] 配置文件: {BlinkKeys.Config.ConfigPath}" +
                    "（也可在游戏的 选项 → 模组 → 位移·闪现突袭 菜单里直接改）");

                try { BlinkKeys.Config.Save(); } catch { }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[BackBlinkOverhaul] 写配置失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Try("卸载 useSkillItem", () => Hook_BeheadedActiveSkillsManager.useSkillItem -= OnUseSkillItem);
            Try("卸载 startCooldownForItem", () => Hook_HeroActiveSkillsManager.startCooldownForItem -= OnStartCooldownForItem);
            Try("卸载 canUseActiveSkill", () => Hook_HeroActiveSkillsManager.canUseActiveSkill -= OnCanUseActiveSkill);
            Try("卸载 filterBySight", () => Hook_TargetHelper.filterBySight -= OnFilterBySight);
            Try("卸载 applyAttackResult", () => Hook_Entity.applyAttackResult -= OnApplyAttackResult);
            Try("卸载 onDie", () => Hook_Mob.onDie -= OnMobDie);

            BlinkFeatures.ResetHotkeyState();
            _chainMarks.Clear();
            BlinkActive = false;
            Logger.Information("[BackBlinkOverhaul] 游戏退出，模组已卸载");
        }

        // ================================================================ 每帧

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            try
            {
                var hero = Game.Instance?.HeroInstance;
                if (hero != null) BlinkCore.UpdateGameTime(hero);
            }
            catch { }
            try { BlinkFeatures.PollHotkeys(Write); } catch { }
            try { PollScrollKey(); } catch { }
            try { SyncStats(); } catch { }
            try { TickChain(dt); } catch { }
        }

        // ================================================================ 便捷热键：T 键召唤卷轴
        //
        // 参考 chuansong+shengcheng：按 T 在脚下掉一个 AllUp（全属性提升）卷轴。
        // 这边做成可配置 + 可开关，并且用"边沿检测 + 最小间隔"两道防抖，
        // 避免按住 T 一直刷物品。

        /// <summary>轮询 T 键（边沿检测 + 最小间隔）。</summary>
        private void PollScrollKey()
        {
            var cfg = BlinkKeys.Config.Value;

            if (!BlinkFeatures.IsOn(BlinkFeature.ScrollHotkey))
            {
                _scrollKeyDown = false;
                return;
            }

            int vk = BlinkKeys.Resolve(cfg.SpawnScrollKey, 0x54);   // 默认 T
            if (vk <= 0) { _scrollKeyDown = false; return; }

            bool down;
            try { down = (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return; }

            bool was = _scrollKeyDown;
            _scrollKeyDown = down;

            if (!down || was) return;                              // 只在"刚按下"那一帧动作

            double interval = Math.Max(0.0, cfg.SpawnScrollIntervalS);
            double now = Now();
            if (interval > 0.0 && (now - _lastScrollSpawnTime) < interval) return;
            _lastScrollSpawnTime = now;

            SpawnScrollAtHero(cfg.SpawnScrollId, cfg.SpawnScrollDir);
        }

        /// <summary>
        /// 在英雄脚下生成一个消耗品（默认 AllUp = 全属性提升卷轴）。
        /// 与 chuansong+shengcheng 的 TriggerSpawnEvent 同一套写法：
        /// new InventItem(kind) → new ItemDrop(...) → init() → onDropAsLoot()。
        /// </summary>
        private void SpawnScrollAtHero(string itemId, double dir)
        {
            try
            {
                var hero = Game.Instance?.HeroInstance;
                if (hero == null || hero.destroyed || hero._level == null) return;
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    itemId = "AllUp";
                }

                var kind = new InventItemKind.Consumable(BlinkCore.Hx(itemId));
                var item = new InventItem(kind);

                bool inArmory = false;
                var drop = new dc.en.inter.ItemDrop(hero._level, hero.cx, hero.cy, item, true,
                                                    new HaxeProxy.Runtime.Ref<bool>(ref inArmory));
                drop.init();
                drop.onDropAsLoot();
                drop.dx = hero.dir * dir;

                _scrollSpawnCount++;
                if (_scrollSpawnCount <= 3 || _scrollSpawnCount % 10 == 0)
                {
                    Write($"[BackBlinkOverhaul] 召唤 {itemId} 卷轴 ×{_scrollSpawnCount}（位置 {hero.cx},{hero.cy}，按 {BlinkKeys.Config.Value.SpawnScrollKey}）");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"[BackBlinkOverhaul] 召唤卷轴失败: {ex.Message}");
            }
        }

        /// <summary>3.2.4 防止无限递归与性能保护：每帧重置连锁计数器。</summary>
        private void TickChain(double dt)
        {
            _frameChainCount = 0;

            var hero = Game.Instance?.HeroInstance;
            if (hero == null) { _chainMarks.Clear(); return; }

            double now = Now();

            // 3.1.3 连锁时间窗口过期 → 3.4.3 断链重置
            if (_chainStacks > 0 && now > _chainEndTime)
            {
                _chainStacks = 0;
                _chainEndTime = -999.0;
                if (BlinkFeatures.IsOn(BlinkFeature.ChainHud))
                {
                    BlinkCore.ShowChainHud(hero, 0, 0.0);
                }
            }
            else if (_chainStacks > 0 && BlinkFeatures.IsOn(BlinkFeature.ChainHud))
            {
                // 3.5.3 连杀计数 / UI 反馈（每帧刷新剩余窗口）
                BlinkCore.ShowChainHud(hero, _chainStacks, _chainEndTime - now);
            }

            // 3.3.4 标记消失与刷新规则：清掉过期标记
            if (_chainMarks.Count > 0)
            {
                List<int> expired = null;
                foreach (var kv in _chainMarks)
                {
                    if (now > kv.Value) (expired ??= new List<int>()).Add(kv.Key);
                }
                if (expired != null)
                {
                    foreach (var k in expired) _chainMarks.Remove(k);
                }
            }
        }

        private static double Now() => BlinkCore.GameTime;

        // ================================================================ 核心：施放技能

        /// <summary>
        /// 【入口】英雄释放主动技能。
        ///
        /// 流程：
        ///   1. 不是 BackBlink 或总开关关闭 → 原版照旧
        ///   2. 1.2.4 最小施放间隔 / 递归保护检查
        ///   3. <see cref="CaptureOrigin"/> 记录起点、决定目标、算出希望的落点
        ///   4. 调用 orig()：原版自己完成"转移到目标背后 + 转身 + 目标硬直 + 打标签"
        ///   5. <see cref="Finish"/> 结算落点修正 / 视觉 / 音效 / 冲击波 / 连锁
        /// </summary>
        private void OnUseSkillItem(Hook_BeheadedActiveSkillsManager.orig_useSkillItem orig,
                                    BeheadedActiveSkillsManager self, int id, InventItem i)
        {
            if (!BlinkFeatures.IsOn(BlinkFeature.Mod) || !IsBlinkItem(i))
            {
                orig(self, id, i);
                return;
            }

            if (_depth >= MaxDepth) { orig(self, id, i); return; }

            var hero = self?.hero ?? Game.Instance?.HeroInstance;
            if (hero == null || hero.destroyed || hero.life <= 0) { orig(self, id, i); return; }

            // 1.2.4 防止无限连按导致性能问题
            double cfgInterval = Math.Max(0.0, BlinkKeys.Config.Value.MinCastIntervalS);
            double now = Now();
            if (cfgInterval > 0.0 && (now - _lastCastTime) < cfgInterval)
            {
                return;   // 忽略这次输入（不进入原版，也不结算）
            }
            _lastCastTime = now;

            // 1.2.3 与连锁传送的兼容逻辑：本次是否处于"连锁强化窗口"
            int stacksAtCast = _chainStacks;

            // 3.3.4 标记消失与刷新规则：每次新的传送都重新开盘，不继承上次的标记
            _chainMarks.Clear();

            var cast = CaptureOrigin(hero, i, stacksAtCast);

            bool prevActive = BlinkActive;
            BlinkActive = true;
            _depth++;
            try
            {
                // 原版：转移到目标背后 + 处理目标硬直 / 打标签 / 播放原版音效
                orig(self, id, i);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BackBlinkOverhaul] 原版 BackBlink 执行异常");
            }
            finally
            {
                _depth--;
                BlinkActive = prevActive;
            }

            try
            {
                Finish(cast);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BackBlinkOverhaul] 传送结算失败");
            }
        }

        /// <summary>
        /// 决定目标与希望的落点。原版只在 props.distance 内锁敌，这里由我们自己选，
        /// 结果是"整关任意敌人"（1.1.2 / 1.1.3）。
        /// </summary>
        private BlinkCast CaptureOrigin(Hero hero, InventItem item, int stacks)
        {
            var cast = new BlinkCast
            {
                Hero = hero,
                Item = item,
                FromX = BlinkCore.PixelX(hero),
                FromY = BlinkCore.PixelY(hero),
                ChainStacks = stacks,
            };

            bool infinite = BlinkFeatures.IsOn(BlinkFeature.InfiniteRange);
            bool anyEnemy = BlinkFeatures.IsOn(BlinkFeature.AnyEnemy);

            // 1.1.3 锁定任意敌人（不限近距离）→ 实际上就等于无视距离
            Mob target = BlinkCore.FindNearestEnemy(hero, item, infinite || anyEnemy, out double dist);
            cast.Target = target;

            if (target != null && !target.destroyed)
            {
                cast.DistanceCells = dist;

                // 落点：目标背后（与原版一致：cx - dir * floor(radius/24)，y 用目标 yr）
                int back = -target.dir;
                double rCells = target.radius / BlinkCore.Cell;
                int tx = target.cx + back * (int)Math.Floor(rCells);
                int ty = target.cy;

                double xr = (back != 1) ? 0.05 : 0.95;
                cast.TargetX = ((double)tx + xr) * BlinkCore.Cell;
                cast.TargetY = ((double)ty + target.yr) * BlinkCore.Cell - target.hei * 0.5;
            }
            else
            {
                // 1.1.5 没目标 → 朝面向方向盲闪
                double cells = Math.Max(1.0, BlinkKeys.Config.Value.FallbackBlinkCells);
                int tx = hero.cx + hero.dir * (int)Math.Round(cells);
                int ty = hero.cy;
                double xr = (hero.dir != 1) ? 0.05 : 0.95;
                cast.TargetX = ((double)tx + xr) * BlinkCore.Cell;
                cast.TargetY = ((double)ty + hero.yr) * BlinkCore.Cell - hero.hei * 0.5;
            }

            return cast;
        }

        /// <summary>
        /// 【结算】原版已经把角色挪到了目标背后（或什么都没做）。这里负责：
        ///   · 读回真实落点
        ///   · 1.1.4 落点安全修正
        ///   · 1.1.5 传送失败 / 无目标时的处理
        ///   · 1.3 / 1.4 视觉与音效
        ///   · 2. 落地冲击波
        ///   · 3. 连锁传送
        /// </summary>
        private void Finish(BlinkCast cast)
        {
            var hero = cast.Hero;
            if (hero == null || hero.destroyed) return;

            cast.LandX = BlinkCore.PixelX(hero);
            cast.LandY = BlinkCore.PixelY(hero);

            // 判断原版到底有没有真的传送过去
            bool moved = Math.Abs(cast.LandX - cast.FromX) > 0.5 || Math.Abs(cast.LandY - cast.FromY) > 0.5;
            bool targeted = cast.Target != null && !cast.Target.destroyed;

            // 1.1.5 无目标 / 传送失败 → 由我们接管
            if (!moved)
            {
                BlinkCast blind = null;

                if (BlinkFeatures.IsOn(BlinkFeature.FallbackBlink))
                {
                    // 有目标：原版到不了（落点被墙堵死 / 穿墙后位置非法）→ 自己挪到目标背后的安全格
                    // 无目标：朝面向方向盲闪一段
                    blind = new BlinkCast
                    {
                        Hero = hero,
                        Item = cast.Item,
                        FromX = cast.FromX,
                        FromY = cast.FromY,
                        Target = cast.Target,
                        TargetX = cast.TargetX,
                        TargetY = cast.TargetY,
                        ChainStacks = cast.ChainStacks,
                    };

                    if (FallbackBlink(blind))
                    {
                        cast.LandX = blind.LandX;
                        cast.LandY = blind.LandY;
                    }
                    else
                    {
                        // 实在没地方落 → 传送失败：回原地并直接返回（不结算冲击波）
                        TryReturnToOrigin(cast);
                        TryClearCooldown(hero, cast.Item);
                        return;
                    }
                }
                else
                {
                    TryClearCooldown(hero, cast.Item);
                    return;
                }
            }

            // 1.1.4 落点判定与安全位置修正：
            //   被墙堵住时挪到最近的可站立格；开关关掉时只做最小兜底，避免角色被留在墙里。
            BlinkCore.SafeLanding(hero, cast.Target?.cx ?? hero.cx, cast.Target?.cy ?? hero.cy,
                                  BlinkFeatures.IsOn(BlinkFeature.LandingFix));
            if (BlinkCore.IsWall(hero._level?.map, hero.cx, hero.cy))
            {
                // 连修正都救不回来 → 回起点；再不行就放弃本次结算
                TryReturnToOrigin(cast);
                if (BlinkCore.IsWall(hero._level?.map, hero.cx, hero.cy))
                {
                    TryClearCooldown(hero, cast.Item);
                    return;
                }
            }
            cast.LandX = BlinkCore.PixelX(hero);
            cast.LandY = BlinkCore.PixelY(hero);

            // 1.1.1 穿墙标记（用于视觉提示；功能上由 filterBySight 放行实现）
            cast.PiercedWall = BlinkFeatures.IsOn(BlinkFeature.WallPierce);

            // 1.3 / 1.4 视觉与音效
            BlinkCore.PlayCastFx(cast);

            // 1.2 冷却与充能：无论有没有目标都保证能马上再按
            TryClearCooldown(hero, cast.Item);

            // 3.3.1 传送后添加激活标记（用于 3.3.3 击杀时检测标记）
            _chainMarksActive = BlinkFeatures.IsOn(BlinkFeature.Chain);
            if (_chainMarksActive && targeted)
            {
                MarkForChain(cast.Target);
            }

            // 2. 落地冲击波 Blink Strike
            bool doWave = BlinkFeatures.IsOn(BlinkFeature.Shockwave)
                          && (targeted || BlinkKeys.Config.Value.ShockwaveOnFallbackBlink);
            if (doWave)
            {
                var killed = new List<Mob>();
                int kills = BlinkCore.Shockwave(cast, killed, _chainMarksActive ? MarkForChain : null,
                                                BlinkFeatures.IsOn(BlinkFeature.IgnoreShield));

                // 3.1 击杀判定 → 3.2 重置逻辑（无冷却设定下用"刷新强化窗口 / 叠加层数"表达）
                if (BlinkFeatures.IsOn(BlinkFeature.Chain))
                {
                    OnBlinkKills(hero, killed, kills);
                }
                else
                {
                    _chainStacks = 0;
                }
            }
            else if (BlinkFeatures.IsOn(BlinkFeature.Chain))
            {
                OnBlinkKills(hero, null, 0);
            }

            _castCount++;
            if (_castCount % 25 == 1)
            {
                Write($"[BackBlinkOverhaul] 第 {_castCount} 次传送: 目标={(cast.Target == null ? "无(盲闪)" : "有")} " +
                      $"距离={cast.DistanceCells:F1}格 落点=({cast.LandX:F0},{cast.LandY:F0}) 连锁层数={_chainStacks}");
            }
        }

        /// <summary>
        /// 1.1.5 传送兜底：原版没把我们送过去时由这里接手。
        ///   · 有目标 → 在目标周围（优先背后）找一个能站的格子
        ///   · 无目标 → 朝面向方向盲闪一段
        /// 找不到落点就返回 false（= 传送失败）。
        /// </summary>
        private bool FallbackBlink(BlinkCast cast)
        {
            try
            {
                var hero = cast.Hero;
                if (hero == null || hero._level == null) return false;
                var map = hero._level.map;
                if (map == null) return false;

                var target = cast.Target;

                if (target != null && !target.destroyed)
                {
                    // 先按原版规则试"目标背后"，再向外扩一圈找同高度 / 上下邻格
                    int back = -target.dir;
                    int rCells = (int)Math.Floor(target.radius / BlinkCore.Cell);
                    int bx = target.cx + back * rCells;
                    int by = target.cy;

                    if (TryTeleportTo(hero, map, bx, by, back == 1 ? 0.95 : 0.05, target.yr))
                        return true;

                    // 扩展搜索：目标所在格周围 3 格内的空位
                    for (int r = 1; r <= 3; r++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            for (int dy = -r; dy <= r; dy++)
                            {
                                if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                                int tx = target.cx + dx;
                                int ty = target.cy + dy;
                                if (!BlinkCore.IsFree(hero, map, tx, ty)) continue;

                                // 朝向仍然对着目标，背刺手感保持一致
                                int d = (tx >= target.cx) ? -1 : 1;
                                if (TryTeleportTo(hero, map, tx, ty, d == 1 ? 0.95 : 0.05, target.yr))
                                    return true;
                            }
                        }
                    }
                    return false;
                }

                // 无目标：盲闪
                int dir = hero.dir >= 0 ? 1 : -1;
                int cells = (int)Math.Round(Math.Max(1.0, BlinkKeys.Config.Value.FallbackBlinkCells));
                for (int d = cells; d >= 1; d--)
                {
                    int tx = hero.cx + dir * d;
                    int ty = hero.cy;
                    if (TryTeleportTo(hero, map, tx, ty, dir == 1 ? 0.95 : 0.05, hero.yr))
                        return true;
                }
                return false;
            }
            catch { return false; }
        }

        private static bool TryTeleportTo(Hero hero, dc.level.LevelMap map, int tx, int ty, double xr, double yr)
        {
            try
            {
                if (!BlinkCore.IsFree(hero, map, tx, ty)) return false;
                hero.safeTpTo(tx, ty, xr, yr, true);
                return true;
            }
            catch { return false; }
        }

        /// <summary>传送失败时把角色送回原点，避免卡在墙里。</summary>
        private void TryReturnToOrigin(BlinkCast cast)
        {
            try
            {
                var hero = cast.Hero;
                if (hero == null || hero._level == null) return;
                var map = hero._level.map;
                int ox = (int)(cast.FromX / BlinkCore.Cell);
                int oy = (int)(cast.FromY / BlinkCore.Cell);
                if (BlinkCore.IsFree(hero, map, ox, oy))
                {
                    hero.safeTpTo(ox, oy, 0.5, hero.yr, true);
                    cast.LandX = BlinkCore.PixelX(hero);
                    cast.LandY = BlinkCore.PixelY(hero);
                }
            }
            catch { }
        }

        // ================================================================ 1.2 冷却与充能

        /// <summary>
        /// 1.2.1 / 1.2.2 移除冷却 + 无充能限制。
        ///
        /// 同时兼任 1.1.5 的"无目标"信号捕获：原版 <c>_BackBlink</c> 在没找到目标时
        /// 会调用 <c>hero.startCooldownForItem(item, 0.5)</c>，我们用 BlinkActive 标记
        /// 精确识别这个信号，然后立刻把它清掉。
        /// </summary>
        private void OnStartCooldownForItem(Hook_HeroActiveSkillsManager.orig_startCooldownForItem orig,
                                            HeroActiveSkillsManager self, InventItem item, double? overrideTime)
        {
            if (!BlinkFeatures.IsOn(BlinkFeature.Mod) || !IsBlinkItem(item))
            {
                orig(self, item, overrideTime);
                return;
            }

            var cfg = BlinkKeys.Config.Value;

            // 1.2.1 移除冷却（castCD = 0 → 原版算出来的就是 0）
            if (BlinkFeatures.IsOn(BlinkFeature.NoCooldown))
            {
                orig(self, item, (double?)0.0);
            }
            else
            {
                double mult = Math.Max(0.0, cfg.CooldownMult);
                double? ov = (overrideTime.HasValue && mult == 1.0)
                             ? overrideTime
                             : (double?)((overrideTime ?? 0.0) * mult);
                orig(self, item, ov);
            }

            // 1.2.2 无充能限制：立刻把冷却与充能清空
            if (BlinkFeatures.IsOn(BlinkFeature.InfiniteCharges))
            {
                try { self.resetCooldownForItem(item); } catch { }
            }
        }

        /// <summary>1.2.2 兜底：即使别处绕过 startCooldownForItem，也不让 BackBlink 被冷却拦住。</summary>
        private bool OnCanUseActiveSkill(Hook_HeroActiveSkillsManager.orig_canUseActiveSkill orig,
                                         HeroActiveSkillsManager self, int id)
        {
            bool result = orig(self, id);
            if (result) return true;

            try
            {
                if (!BlinkFeatures.IsOn(BlinkFeature.Mod)) return false;
                if (!BlinkFeatures.IsOn(BlinkFeature.NoCooldown) &&
                    !BlinkFeatures.IsOn(BlinkFeature.InfiniteCharges)) return false;

                // 原版返回 false 的常见原因是"充能 / 冷却没转好"。
                // 先确认这个 id 上装备的确实是 BackBlink，是的话就强行放行。
                var item = self?.hero?.inventory?.getActiveOn(id);
                if (!IsBlinkItem(item)) return false;

                // "MoveHero" 类的硬性限制（被定身）仍然尊重原版
                try { if (item.hasTag(BlinkCore.Hx("MoveHero")) && self.hero.moveBlocked()) return false; }
                catch { }

                return true;
            }
            catch { return false; }
        }

        /// <summary>把某个物品的冷却彻底清掉。</summary>
        private void TryClearCooldown(Hero hero, InventItem item)
        {
            try
            {
                if (!BlinkFeatures.IsOn(BlinkFeature.NoCooldown) &&
                    !BlinkFeatures.IsOn(BlinkFeature.InfiniteCharges)) return;
                var mgr = hero?.activeSkillsManager;
                if (mgr == null || item == null) return;
                mgr.resetCooldownForItem(item);
            }
            catch { }
        }

        // ================================================================ 1.1.1 无视墙体

        /// <summary>
        /// 只在我们自己的传送结算期间放行"视线判定"。
        ///
        /// 原版 <c>_BackBlink</c> 会用 <c>TargetHelper.filterBySight</c> 把"隔着墙看不见"的敌人
        /// 从候选列表里剔掉（chuanqiang / ThrowableStuff / LightningWhipBoost 都是这么穿的）。
        /// 这里直接不调用 orig，等价于"无视墙体"。
        /// </summary>
        private void OnFilterBySight(Hook_TargetHelper.orig_filterBySight orig,
                                     TargetHelper self, Entity otherSource, Ref<bool> ignoreOneWay, int? ignoreSpotType)
        {
            if (BlinkActive && BlinkFeatures.IsOn(BlinkFeature.WallPierce)) return;
            orig(self, otherSource, ignoreOneWay, ignoreSpotType);
        }

        // ================================================================ 3. 连锁传送

        /// <summary>3.3.1 传送后给目标挂上"可连锁"激活标记。</summary>
        private void MarkForChain(Mob m)
        {
            try
            {
                if (m == null) return;
                double window = Math.Max(0.2, BlinkKeys.Config.Value.ChainWindowS);
                _chainMarks[m.__uid] = Now() + window;
            }
            catch { }
        }

        /// <summary>
        /// 3.1.1 / 3.3.3 目标死亡判定（主路径）：直接挂在原版 <c>Mob.onDie</c> 上。
        ///
        /// 比"看 applyAttackResult 之后 life 有没有归零"更可靠 ——
        /// 死亡是原版自己判定的，我们只负责看这个死掉的敌人身上有没有本次传送留下的连锁标记。
        /// </summary>
        private void OnMobDie(Hook_Mob.orig_onDie orig, Mob self)
        {
            bool marked = false;
            try
            {
                marked = self != null
                         && BlinkFeatures.IsOn(BlinkFeature.Mod)
                         && BlinkFeatures.IsOn(BlinkFeature.Chain)
                         && _chainMarks.Count > 0
                         && _chainMarks.ContainsKey(self.__uid);
            }
            catch { }

            orig(self);

            if (!marked) return;

            try
            {
                // 3.1.4 击杀来源识别：本次传送标记过的敌人死亡 → 叠一层连锁
                GrantChainStack(self);
            }
            catch { }
        }

        /// <summary>3.3.3 击杀时检测标记（兜底路径：某些死法不走 Mob.onDie）。</summary>
        private void OnApplyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            int lifeBefore = 0;
            bool track = false;
            try
            {
                if (BlinkFeatures.IsOn(BlinkFeature.Mod)
                    && BlinkFeatures.IsOn(BlinkFeature.Chain)
                    && self is Mob m0 && !m0.destroyed
                    && m0._team != null && m0._level != null && m0._team == m0._level.teamMob
                    && _chainMarks.Count > 0 && _chainMarks.ContainsKey(m0.__uid))
                {
                    lifeBefore = m0.life;
                    track = true;
                }
            }
            catch { }

            orig(self, attack);

            if (!track) return;

            try
            {
                if (self is Mob m && lifeBefore > 0 && (m.life <= 0 || m.destroyed))
                {
                    // 3.1.4 击杀来源识别：攻击者必须是英雄（直接或间接）
                    bool fromHero = attack != null && (attack.source is Hero || attack.carrier is Hero);
                    if (fromHero)
                    {
                        GrantChainStack(m);
                    }
                }
            }
            catch { }
        }

        /// <summary>冲击波造成的击杀也会走一遍连锁。</summary>
        private void OnBlinkKills(Hero hero, List<Mob> killed, int kills)
        {
            try
            {
                if (killed != null && killed.Count > 0)
                {
                    foreach (var m in killed)
                    {
                        if (m == null) continue;
                        if (!_chainMarks.ContainsKey(m.__uid)) continue;
                        GrantChainStack(m);
                    }
                }
                else if (kills > 0)
                {
                    // 拿不到具体实体时按次数叠层（兜底）
                    for (int i = 0; i < kills; i++) AddChainStack();
                }
            }
            catch { }
        }

        private void GrantChainStack(Mob m)
        {
            try
            {
                var cfg = BlinkKeys.Config.Value;
                if (!BlinkFeatures.IsOn(BlinkFeature.Chain)) return;

                // 3.2.4 防止无限递归与性能保护：每帧连锁次数上限
                if (_frameStamp != Now())
                {
                    _frameStamp = Now();
                    _frameChainCount = 0;
                }
                if (_frameChainCount >= Math.Max(1, cfg.MaxChainPerFrame)) return;
                _frameChainCount++;

                _chainMarks.Remove(m.__uid);
                AddChainStack();
            }
            catch { }
        }

        private void AddChainStack()
        {
            var cfg = BlinkKeys.Config.Value;
            int max = Math.Max(0, cfg.ChainMaxStacks);

            // 3.4.4 连锁上限
            if (max > 0 && _chainStacks >= max)
            {
                // 到上限就只刷新窗口（3.2.3 刷新强化窗口）
                _chainEndTime = Now() + Math.Max(0.2, cfg.ChainWindowS);
            }
            else
            {
                _chainStacks++;
                _chainEndTime = Now() + Math.Max(0.2, cfg.ChainWindowS);
                // 3.5.2 重置特效（只打一圈光环，不重复整个传送特效）
                try
                {
                    var hero = Game.Instance?.HeroInstance;
                    if (hero != null) BlinkCore.PlayChainFx(hero, _chainStacks);
                }
                catch { }
            }

            if (BlinkFeatures.IsOn(BlinkFeature.ChainHud))
            {
                try
                {
                    var hero = Game.Instance?.HeroInstance;
                    if (hero != null) BlinkCore.ShowChainHud(hero, _chainStacks, _chainEndTime - Now());
                }
                catch { }
            }

            Write($"[BackBlinkOverhaul] 连锁 +1 → {_chainStacks} 层（窗口 {Math.Max(0, _chainEndTime - Now()):F2}s，" +
                  $"冲击波倍率 ×{BlinkCore.ChainDamageMult(_chainStacks, cfg):F2}）");
        }

        // ================================================================ 数值开关

        /// <summary>
        /// 把 BackBlink 物品数据在"强化值 / 原版值"之间切换。
        /// 强化值默认从 res.pak 应用后的数据里读取缓存；原版值用常量（运行时拿不到模板 cdb）。
        /// </summary>
        private void SyncStats()
        {
            bool want = BlinkFeatures.IsOn(BlinkFeature.Mod)
                        && (BlinkFeatures.IsOn(BlinkFeature.InfiniteRange) || BlinkFeatures.IsOn(BlinkFeature.AnyEnemy)
                            || BlinkFeatures.IsOn(BlinkFeature.NoCooldown));
            if (_statsAppliedState == want) return;

            dynamic itemData = Data.Class.item.byId.get(BlinkCore.Hx(ItemId));
            if (itemData == null || itemData.props == null) return;

            try
            {
                dynamic props = itemData.props;

                if (!_captured)
                {
                    try { _modDistance = Convert.ToDouble((object)props.distance); } catch { }
                    try { _modCastCd = Convert.ToDouble((object)itemData.castCD); } catch { }
                    _captured = true;
                    Logger.Information($"[BackBlinkOverhaul] 已缓存强化值: distance={_modDistance} castCD={_modCastCd}");
                }

                if (want)
                {
                    props.distance = _modDistance;
                    itemData.castCD = _modCastCd;
                }
                else
                {
                    props.distance = _vanillaDistance;
                    itemData.castCD = _vanillaCastCd;
                }

                _statsAppliedState = want;
                Logger.Information(
                    $"[BackBlinkOverhaul] 数值{(want ? "强化" : "还原")}: distance={props.distance} castCD={itemData.castCD}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[BackBlinkOverhaul] 数值切换失败");
            }
        }

        // ================================================================ 工具

        private static bool IsBlinkItem(InventItem item)
        {
            try
            {
                if (item == null) return false;

                // 1) 最可靠：物品数据（原版/模组都会把它缓存进 _itemData）
                dynamic data = item._itemData;
                if (data != null && data.id != null) return data.id.ToString() == BlinkCore.ItemId;

                // 2) 退而求其次：看 kind 里的 id 字符串
                //（Haxe 的 enum 字段在代理里叫 Param0；Index 是 enum 标签，不是 id）
                if (item.kind is InventItemKind.Active a)
                {
                    var id = a.Param0;
                    if (id != null && id.ToString() == BlinkCore.ItemId) return true;
                }

                return false;
            }
            catch { return false; }
        }

        // ================================================================ 选项菜单（IModMenu）

        public string GetName() => "位移·闪现突袭";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("BACKBLINK OVERHAUL 设置"));
                b.createScroller(0.0);

                foreach (var f in BlinkFeatures.All)
                {
                    var feature = f;                       // 闭包捕获：别直接用循环变量
                    bool on = BlinkFeatures.RawGet(feature);
                    string key = BlinkFeatures.KeyName(feature);
                    string hint = BlinkFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(BlinkFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !BlinkFeatures.RawGet(feature);
                            BlinkFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                b.updateScroller();
                Write($"[BackBlinkOverhaul] 选项菜单已建立：{BlinkFeatures.All.Length} 个功能开关");
            }
            catch (Exception ex)
            {
                Write($"[BackBlinkOverhaul] 建立选项菜单失败: {ex.Message}");
            }
        }
    }
}
