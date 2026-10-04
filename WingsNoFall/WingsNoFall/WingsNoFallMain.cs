#nullable disable

using dc;
using dc.en;
using dc.hl.types;
using dc.hxd;
using dc.hxd.res;
using dc.pow;
using dc.tool;
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

namespace WingsNoFall
{
    /// <summary>
    /// 乌鸦之翼（内部 ID <c>Wings</c>，法文名 "Ailes de Corbeau"）——「飞在天上不再自动下落」模组
    /// =====================================================================================
    /// 【原版机制（GamePseudocode/dc.pow/Wings.cs + _Wings.cs）】
    ///   _Wings.__inst_construct__ 做了这些事：
    ///     · 给英雄挂 affect 61（飞行）与 13（动画），时长 = props.duration（原版 15 秒）
    ///     · hero.airControl()、resetCooldownForItem、慢动作 + 闪电特效 + 羽翼音效
    ///     · 监听 clean / jump / touchGround / land / attackDealt / diveAttackLand 等信号
    ///   每帧 Wings.fixedUpdate() 负责"悬停"，核心就三行（见 Wings.cs 6971~6992 / 9006~9435）：
    ///     · 用 getGroundY(cx, cy) / getCeilY(cx, cy) 算出目标高度 targetCy
    ///       —— 悬停时是"脚下地面往上 3 格"，按下方向下时是"地面往下 1 格"，
    ///          另外有些分支会直接把 targetCy 设成当前高度（原地悬停）；
    ///     · 然后写垂直速度：
    ///           dy = clamp((targetCy - (cy + yr)) * 24 / props.limit, -1, 1)
    ///           if (cy &lt; targetCy &amp;&amp; 没有 cd16) dy *= 0.33
    ///       也就是一条"朝 targetCy 收敛"的弹簧；高于目标高度时只按 0.33 倍慢慢往下拽。
    ///   ⇒ **玩家一旦飞得比悬停高度高（持续爬升 / 跳跃 / 地形落差），就会被这条弹簧拽回去**，
    ///     这就是需求里说的"超过一定高度会下落"。
    ///   另一条相关分支（Wings.cs 8166~8191）：
    ///     · 按住"方向上/下"里的那一个方向且不在悬停 → 直接 removeAllAffects(61) + dy = 1 掉下来。
    ///       本模组用"affect 61 还在不在"来识别"玩家按了方向下、原版已经取消飞行"，
    ///       所以**方向下依旧百分百走原版**，一个字节都不改。
    ///
    /// 【本模组的做法】
    ///   1) 代码侧（本文件，主要手段）
    ///      Hook Wings.fixedUpdate：先让原版跑完（它会算好 targetCy 并把 dy 写好），
    ///      然后按玩家真实方向输入修一次 dy：
    ///        · 按住"方向下" → **立刻 return**，一切交给原版（取消飞行 / 下落）；
    ///        · 按住"方向上" → 打开「自由爬升」时强制 dy = -AscendSpeed（想飞多高飞多高）；
    ///        · 其它情况     → 打开「不再自动下落」时把向下的 dy 归零（绝不被拽下去）。
    ///      方向判定复刻原版 Wings.fixedUpdate 的读法：ControllerAccess.get_bindings()
    ///      的 primary/secondary/third（键盘，dc.hxd.Key）+ padA/padB/padC（手柄），
    ///      动作码 12 = 方向下、10 = 方向上，并跟随 invertPlayerMovements 反转。
    ///      ★ 即使方向码判断反了也不会出错：原版"方向下"那一支会先把 affect 61 移除，
    ///        而本模组发现翅膀已经不在了，就什么都不做（见 HasWingsAffect）。
    ///   2) 数据侧（res.pak，patch_wings_data.py 生成）
    ///      props.limit 48 → 120：把那条弹簧的"满速距离"从 2 格拉长到 5 格，
    ///      近距离往回拽的力度同比变柔和（兜底：万一代码侧钩子没挂上，也只是缓慢漂移）。
    ///      数值分工与 DamageAuraBoost 一致 —— 数据管数值、代码管逻辑。
    ///
    /// 【真机验证要点】
    ///   · 用翅膀飞高以后松手 → 应该停在原地（不再慢慢往下沉）；
    ///   · 飞行中按住"方向下" → 依旧和原版一样往下 / 取消飞行；
    ///   · 飞行中按住"方向上" → 可以一直往上升（原版被悬停高度卡住）。
    /// </summary>
    public class WingsNoFallMain : ModBase, IOnGameExit, IOnAfterLoadingAssets, IOnHeroUpdate, IOnGameInit, IModMenu
    {
        /// <summary>翅膀的 CDB id（item 表）。</summary>
        private const string ItemId = "Wings";

        /// <summary>_Wings 构造函数挂的飞行 affect（setAffectS(61, duration)）；onEnd 时移除。</summary>
        private const int WingsAffectId = 61;

        // ---------------- 原版 Wings.fixedUpdate 里用的动作码 ----------------
        //
        // 这两个是 get_bindings().primary/secondary/third/padX 的下标：
        //   动作码 12 → 读出来的垂直输入是 -1（方向下，原版据此取消飞行并下落）
        //   动作码 10 → 读出来的垂直输入是 +1（方向上，原版据此悬停 / 爬升）

        /// <summary>动作码：方向上。</summary>
        private const int ActionUp = 10;

        /// <summary>动作码：方向下。</summary>
        private const int ActionDown = 12;

        /// <summary>爬升速度滑条的范围 / 步长。</summary>
        private const double AscendSpeedMin = 0.05;
        private const double AscendSpeedMax = 2.0;
        private const double AscendSpeedStep = 0.05;

        /// <summary>
        /// 默认爬升速度（配置读不到时的兜底）。
        /// 参照物：Hero 起跳冲量是 dy = -0.4，本模组默认 0.3 —— 比一次跳跃初速略慢，
        /// 但因为是持续速度，实际爬升非常干脆。想要更快/更慢都在选项菜单滑条里调。
        /// </summary>
        private const double AscendSpeedDefault = 0.3;

        // ===== 数据侧数值（必须与 patch_wings_data.py 的 LIMIT 保持一致）=====

        /// <summary>原版 data.cdb 里 Wings 的 props.limit（= 2 格）。</summary>
        private const double VanillaLimit = 48.0;

        /// <summary>本模组 res.pak 里 Wings 的 props.limit（= 5 格，弹簧更柔和）。</summary>
        private const double BoostLimit = 120.0;

        /// <summary>挂点连续这么多帧没触发就启用 IOnHeroUpdate 兜底（约 0.08 秒）。</summary>
        private const int FramesBeforeFallback = 5;

        /// <summary>日志采样计数（避免每帧刷屏）。</summary>
        private int _logCounter;

        /// <summary>挂点是否已经挂上（Hook_Wings 事件 / CreateHook 二选一）。</summary>
        private bool _hooked;

        /// <summary>
        /// 距上一次 Wings.fixedUpdate 挂点被调用的帧数。
        /// 挂点正常触发时它一直是 0，IOnHeroUpdate 的兜底就完全不介入（避免重复修正）；
        /// 万一挂点"注册成功但从不触发"（ChronoBlade 记录过这种情况），
        /// 5 帧之后兜底自动接管。
        /// </summary>
        private int _framesSinceHook = int.MaxValue / 2;

        /// <summary>低层挂点句柄（只有在 Hook_Wings 事件不可用时才会用到）。</summary>
        private HashlinkHooks.HookHandle _lowLevelHook;

        /// <summary>
        /// 低层 CreateHook 用的委托。**签名必须和原函数完全一致**，
        /// 不能写成 object / Delegate，否则 HashlinkHookManager.CreateAdaptDelegate 会崩游戏
        /// （踩坑记录见 ChronoBlade / ChronoWeaponFactory 的注释）。
        /// </summary>
        public delegate void orig_Wings_fixedUpdate(Wings self);

        public WingsNoFallMain(ModInfo info) : base(info) { }

        /// <summary>同时写控制台与模组日志文件（logs\log_latest.log）。</summary>
        internal void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { Logger.Information(msg); } catch { }
        }

        // ==================================================================
        // 生命周期
        // ==================================================================

        public override void Initialize()
        {
            base.Initialize();

            // 首选：框架为 Wings 生成的 Hook 事件（与 DamageAuraBoost 挂 Hook_DamageAura.fixedUpdate 同一套）
            try
            {
                Hook_Wings.fixedUpdate += OnWingsFixedUpdate;
                _hooked = true;
                Logger.Information("[WingsNoFall] 已 Hook Hook_Wings.fixedUpdate");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[WingsNoFall] Hook_Wings.fixedUpdate 挂载失败: {ex.Message}");
            }

            // 备选：低层 CreateHook（类名按 hashlink 的包路径写法，逐个试）
            if (!_hooked)
            {
                foreach (var typeName in new[] { "pow.Wings", "dc.pow.Wings", "Wings" })
                {
                    try
                    {
                        // 处理器直接以方法组传入（不要强转成 orig_ 委托，那不是同一个委托类型）
                        _lowLevelHook = HashlinkHooks.Instance.CreateHook(
                            typeName, "fixedUpdate", OnWingsFixedUpdateLowLevel);
                        _lowLevelHook.Enable();
                        _hooked = true;
                        Logger.Information($"[WingsNoFall] 已用 CreateHook 挂上 {typeName}.fixedUpdate");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"[WingsNoFall] CreateHook {typeName}.fixedUpdate 失败: {ex.Message}");
                    }
                }
            }

            // 都失败 → 退化成 IOnHeroUpdate 每帧兜底（晚一帧生效，效果一样）
            if (!_hooked)
            {
                Logger.Warning("[WingsNoFall] 两种挂点都失败，改用 IOnHeroUpdate 每帧兜底修正 dy");
            }

            Logger.Information("[WingsNoFall] 已加载：乌鸦之翼(Wings) 飞行时不再自动下落；" +
                               "按住“方向下”保留原版下落；按住“方向上”可自由爬升");
        }

        /// <summary>资源加载完成：手动加载 mod 自带的 res.pak（limit 数据补丁）。</summary>
        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(typeof(WingsNoFallMain).Assembly.Location) ?? "";
                string pakPath = System.IO.Path.Combine(dir, "res.pak");
                if (System.IO.File.Exists(pakPath))
                {
                    FsPak.Instance.FileSystem.loadPak(ToHaxeString(pakPath));
                    Logger.Information($"[WingsNoFall] res.pak 已加载: {pakPath}");
                }
                else
                {
                    Logger.Warning($"[WingsNoFall] 未找到 res.pak: {pakPath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WingsNoFall] res.pak 加载失败");
            }
        }

        /// <summary>开局自检：写下当前开关状态 + res.pak 数据补丁是否生效，并把默认配置落盘。</summary>
        void IOnGameInit.OnGameInit()
        {
            try
            {
                var cfg = WingsKeys.Config.Value;
                Logger.Information(
                    "[WingsNoFall] 开关状态 —— " +
                    $"总开关={cfg.EnableMod} 不再下落={cfg.EnableNoFall} 自由爬升={cfg.EnableFreeAscend} " +
                    $"爬升速度={cfg.AscendSpeed:0.00}");
                Logger.Information(
                    $"[WingsNoFall] 配置文件: {WingsKeys.Config.ConfigPath}" +
                    "（也可在游戏的 选项 → 模组 → 乌鸦之翼(Wings) 菜单里直接改）");
                try { WingsKeys.Config.Save(); } catch { }

                LogDataPatch();
            }
            catch (Exception ex)
            {
                Logger.Warning($"[WingsNoFall] 写配置失败: {ex.Message}");
            }
        }

        /// <summary>把 CDB 里 Wings 的 limit / duration 打出来，确认 res.pak 数据补丁真的生效了。</summary>
        private void LogDataPatch()
        {
            try
            {
                dynamic item = Data.Class.item.byId.get(ToHaxeString(ItemId));
                if (item == null)
                {
                    Logger.Warning($"[WingsNoFall] CDB 里找不到 item/{ItemId}");
                    return;
                }

                Logger.Information(
                    $"[WingsNoFall] item/{ItemId} 数据：limit={item.props.limit}（原版 48 / 本模组 120）、" +
                    $"duration={item.props.duration}、castCD={item.castCD}");
            }
            catch (Exception ex)
            {
                Logger.Warning($"[WingsNoFall] 读取 item/{ItemId} 数据失败: {ex.Message}");
            }
        }

        // ==================================================================
        // 挂点
        // ==================================================================

        /// <summary>
        /// Hook_Wings.fixedUpdate：先跑原版（它会把 targetCy 与 dy 都算好），再按玩家输入修正 dy。
        /// </summary>
        private void OnWingsFixedUpdate(Hook_Wings.orig_fixedUpdate orig, Wings self)
        {
            orig(self);
            _framesSinceHook = 0;
            try { CorrectWingsVelocity(self); } catch (Exception ex) { Logger.Error(ex, "[WingsNoFall] 修正失败"); }
        }

        /// <summary>低层 CreateHook 版本（签名必须与 orig_Wings_fixedUpdate 一致）。</summary>
        private void OnWingsFixedUpdateLowLevel(orig_Wings_fixedUpdate orig, Wings self)
        {
            orig(self);
            _framesSinceHook = 0;
            try { CorrectWingsVelocity(self); } catch (Exception ex) { Logger.Error(ex, "[WingsNoFall] 修正失败"); }
        }

        /// <summary>
        /// 每帧热键轮询 + 数据开关同步 + 兜底修正。
        /// 只有在 Wings.fixedUpdate 挂点连续 5 帧没被调用（注册成功但从不触发 / 注册失败）时，
        /// 才在这里补一次修正。
        /// </summary>
        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            try { WingsFeatures.PollHotkeys(Write); } catch { }

            // 数据开关 ↔ CDB 数值 同步（与 DamageAuraBoost 的 SyncStats 同一套做法）：
            // 关掉「不再自动下落」/ 关掉总开关时，把 limit 还原成原版 48，避免模组关不干净。
            try { SyncWingsLimit(); } catch { }

            if (_framesSinceHook < int.MaxValue / 2) _framesSinceHook++;
            if (_hooked && _framesSinceHook <= FramesBeforeFallback) return;

            try
            {
                Hero hero = Game.Instance.HeroInstance;
                if (hero == null || hero.destroyed) return;
                CorrectWingsVelocityForHero(hero, null);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WingsNoFall] 兜底修正失败");
            }
        }

        // ==================================================================
        // 数据侧：limit 开关同步（和 DamageAuraBoost 的 SyncStats 一个套路）
        // ==================================================================

        /// <summary>当前已应用的 limit 状态（null = 还没应用过；用于检测开关变化）。</summary>
        private bool? _limitAppliedState;

        /// <summary>
        /// 把 CDB 里 item/Wings 的 props.limit 切到"本模组值 120"或"原版值 48"。
        ///
        /// 为什么要在这里切：res.pak 是全局生效的，光靠数据补丁的话
        /// 把「不再自动下落」关掉（甚至关掉总开关）也回不到原版手感。
        /// 幂等：状态没变就直接返回；CDB 还没就绪就下一帧再试。
        /// </summary>
        private void SyncWingsLimit()
        {
            bool want = WingsFeatures.IsOn(WingsFeature.NoFall);
            if (_limitAppliedState == want) return;

            dynamic item = Data.Class.item.byId.get(ToHaxeString(ItemId));
            if (item == null || item.props == null) return;

            try
            {
                item.props.limit = want ? BoostLimit : VanillaLimit;
                _limitAppliedState = want;
                Logger.Information(
                    $"[WingsNoFall] 数据 limit = {item.props.limit}（{(want ? "本模组 120" : "原版 48")}）");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[WingsNoFall] limit 切换失败");
            }
        }

        // ==================================================================
        // 核心：修正垂直速度
        // ==================================================================

        /// <summary>
        /// 对"原版刚跑完的" Wings 实例做一次垂直速度修正。
        /// 判定顺序很重要：**方向下优先 return**，保证原版的往下完全不受影响。
        /// </summary>
        private void CorrectWingsVelocity(Wings self)
        {
            if (self == null || self.destroyed) return;
            if (!WingsFeatures.IsOn(WingsFeature.Mod)) return;

            Hero hero = self.owner as Hero;
            if (hero == null || hero.destroyed) return;

            int? targetCy = null;
            try { targetCy = self.targetCy; } catch { }

            CorrectWingsVelocityForHero(hero, targetCy);
        }

        private void CorrectWingsVelocityForHero(Hero hero, int? targetCy)
        {
            if (hero == null || hero.destroyed || hero.life <= 0) return;
            if (hero._level == null) return;

            // 翅膀还在生效吗？(_Wings 构造函数挂 affect 61，onEnd / 取消飞行时移除)
            if (!HasWingsAffect(hero)) return;

            int vertical = ReadVerticalInput(hero);

            // ★ 按住"方向下" → 一个字节都不改，保留原版的往下（原版此时会取消飞行并下落）
            if (vertical < 0) return;

            bool noFall = WingsFeatures.IsOn(WingsFeature.NoFall);
            bool freeAscend = WingsFeatures.IsOn(WingsFeature.FreeAscend);

            if (vertical > 0 && freeAscend)
            {
                // 按住"上"：强制向上。原版被 targetCy（悬停高度）卡住飞不高，这里直接接管垂直速度。
                double speed = CurrentAscendSpeed();
                if (hero.dy > -speed)
                {
                    hero.dy = -speed;
                    LogSample($"[WingsNoFall] 自由爬升 dy={hero.dy:0.###} cy={hero.cy} targetCy={targetCy}");
                }
                return;
            }

            if (noFall && hero.dy > 0.0)
            {
                // 没有按"下"，但原版给了向下的速度（被悬停高度那根弹簧拽回去了）→ 归零
                double before = hero.dy;
                hero.dy = 0.0;
                LogSample($"[WingsNoFall] 取消自动下落 dy {before:0.###} -> 0 cy={hero.cy} targetCy={targetCy}");
            }
        }

        /// <summary>翅膀的飞行 affect 是否还挂在英雄身上。</summary>
        private static bool HasWingsAffect(Hero hero)
        {
            try { return hero.countAffect(WingsAffectId) > 0; }
            catch { return false; }
        }

        private static double CurrentAscendSpeed()
        {
            try
            {
                double s = WingsKeys.Config.Value.AscendSpeed;
                if (s < AscendSpeedMin) s = AscendSpeedMin;
                if (s > AscendSpeedMax) s = AscendSpeedMax;
                return s;
            }
            catch { return AscendSpeedDefault; }
        }

        private void LogSample(string msg)
        {
            _logCounter++;
            if (_logCounter % 120 != 1) return;      // 约 2 秒一条，避免刷屏
            try { Logger.Information(msg); } catch { }
        }

        // ==================================================================
        // 方向输入：复刻原版 Wings.fixedUpdate 的读法
        // ==================================================================

        /// <summary>
        /// 读玩家当前的垂直方向输入：
        ///   -1 = 方向下（原版 Wings 里动作码 12），
        ///   +1 = 方向上（动作码 10），
        ///    0 = 没按 / 输入被锁。
        /// 与 _Wings 一样跟随 invertPlayerMovements（反向操作）开关。
        /// </summary>
        private static int ReadVerticalInput(Hero hero)
        {
            try
            {
                ControllerAccess access = hero.controller;
                if (access == null) return 0;
                if (access.manualLock) return 0;

                Controller controller = access.parent;
                if (controller == null) return 0;
                if (controller.isLocked) return 0;

                // 被别的 ControllerAccess 独占（过场 / 剧情）时不介入
                if (ExclusiveMismatch(controller, access)) return 0;

                // 原版: num3 = (!invert) ? 1 : -1;  动作码 12 -> -num3, 动作码 10 -> +num3
                double sign = Main.Class.ME.options.invertPlayerMovements ? -1.0 : 1.0;

                if (IsActionDown(controller, ActionDown)) return (int)(-1.0 * sign);
                if (IsActionDown(controller, ActionUp)) return (int)(1.0 * sign);
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>controller.exclusiveId 存在且不是自己 → 输入被抢走了。</summary>
        private static bool ExclusiveMismatch(Controller controller, ControllerAccess access)
        {
            try
            {
                dc.String exclusive = controller.exclusiveId;
                if (exclusive == null) return false;

                dc.String mine = access.id;
                string a = exclusive.ToString() ?? "";
                string b = mine?.ToString() ?? "";
                return a != b;
            }
            catch { return false; }
        }

        /// <summary>
        /// 某个动作码此刻是否按下（键盘看 primary/secondary/third，手柄看 padA/padB/padC）。
        /// 与原版 Wings.fixedUpdate 的判定完全同源。
        /// </summary>
        private static bool IsActionDown(Controller controller, int actionCode)
        {
            BindingProfiles b = controller.get_bindings();
            if (b == null) return false;

            if (AnyKeyboardDown(controller, b.primary, actionCode)) return true;
            if (AnyKeyboardDown(controller, b.secondary, actionCode)) return true;
            if (AnyKeyboardDown(controller, b.third, actionCode)) return true;

            if (AnyPadDown(controller, b.padA, actionCode)) return true;
            if (AnyPadDown(controller, b.padB, actionCode)) return true;
            if (AnyPadDown(controller, b.padC, actionCode)) return true;

            return false;
        }

        private static bool AnyKeyboardDown(Controller controller, ArrayBytes_Int bindings, int actionCode)
        {
            int key = ReadBinding(bindings, actionCode);
            if (key < 0) return false;

            try
            {
                if ((controller.mode & Controller.Class.ENABLE_KEY) == 0) return false;
                return Key.Class.isDown.Invoke(key);
            }
            catch { return false; }
        }

        private static bool AnyPadDown(Controller controller, ArrayBytes_Int bindings, int actionCode)
        {
            int key = ReadBinding(bindings, actionCode);
            if (key < 0) return false;

            try { return controller.padIsDown(key); }
            catch { return false; }
        }

        /// <summary>
        /// 读 bindings[i]（int 数组）。原版是 PseudocodeHelper.ReadMem&lt;int&gt;(bytes, i &lt;&lt; 2)，
        /// 这里用同一套裸内存读法（与 SeismicStompSlam 读 map.collisions 一致）。
        /// </summary>
        private static unsafe int ReadBinding(ArrayBytes_Int bindings, int index)
        {
            if (bindings == null) return -1;
            if (index < 0 || index >= bindings.length) return -1;

            IntPtr bytes = bindings.bytes;
            if (bytes == IntPtr.Zero) return -1;

            return *(int*)((byte*)bytes.ToPointer() + (index << 2));
        }

        // ==================================================================
        // 选项菜单（IModMenu）—— 三个复选框 + 一个爬升速度滑条
        // ==================================================================

        public string GetName() => "乌鸦之翼(Wings)";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var b = (dc.ui.OptionsBase)options;

                ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("WINGS NO FALL 设置"));
                b.createScroller(0.0);

                // 每个功能一个复选框（总开关排最前），用 WingsFeatures.All 循环生成
                foreach (var f in WingsFeatures.All)
                {
                    var feature = f;                       // 闭包捕获：别直接用循环变量
                    bool on = WingsFeatures.RawGet(feature);
                    string key = WingsFeatures.KeyName(feature);
                    string hint = WingsFeatures.Hint(feature);
                    if (!string.IsNullOrWhiteSpace(key)) hint += $"（热键 {key}）";

                    b.addToggleWidget(
                        StringUtils.AsHaxeString(WingsFeatures.Label(feature)),
                        StringUtils.AsHaxeString(hint),
                        (HlFunc<bool>)delegate
                        {
                            bool now = !WingsFeatures.RawGet(feature);
                            WingsFeatures.Set(feature, now);
                            return now;
                        },
                        new Ref<bool>(ref on),
                        b.scrollerFlow);
                }

                // 爬升速度滑条（写法与 SeismicStompSlam 的 Hero size 完全一致）
                b.addSliderWidget(
                    StringUtils.AsHaxeString("爬升速度"),
                    (HlAction<double>)delegate (double v)
                    {
                        try
                        {
                            WingsKeys.Config.Value.AscendSpeed = v;
                            WingsKeys.Config.Save();
                        }
                        catch { }
                    },
                    CurrentAscendSpeed(),
                    Ref<double>.In(AscendSpeedStep),
                    b.scrollerFlow,
                    Ref<bool>.In(false),                 // showPercent
                    Ref<bool>.In(true),                  // showRawValue
                    Ref<double>.In(AscendSpeedMin),
                    Ref<double>.In(AscendSpeedMax),
                    null,
                    Ref<int>.In(0));

                b.updateScroller();
                Write($"[WingsNoFall] 选项菜单已建立：{WingsFeatures.All.Length} 个功能开关 + 爬升速度滑条");
            }
            catch (Exception ex)
            {
                Write($"[WingsNoFall] 建立选项菜单失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            try { Hook_Wings.fixedUpdate -= OnWingsFixedUpdate; } catch { }
            try { _lowLevelHook?.Disable(); } catch { }
            _lowLevelHook = null;

            WingsFeatures.ResetHotkeyState();
            Logger.Information("[WingsNoFall] 游戏退出，模组已卸载");
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }
    }
}
