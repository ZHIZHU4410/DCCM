#nullable disable
using System;
using System.Collections.Generic;
using dc;
using dc.h2d;
using dc.ui;
using HaxeProxy.Runtime;
using ModCore.Menu;
using ModCore.Utilities;

namespace GojoLimitless
{
    /// <summary>
    /// 游戏原生「选项 → 模组 → GojoLimitless」设置页。
    ///
    /// 实现方式和仓库里的 <c>ZoomVision</c> / <c>CameraMod</c> / <c>EchoVision</c> 完全一致：
    /// 实现 <see cref="IModMenu"/>，框架就会在模组菜单里开一页并回调 <see cref="BuildMenu"/>。
    ///
    /// 页面结构：
    ///   1. 总开关
    ///   2. 无下限（开关 / 半径 / 储量 / 回充 / 迟滞 / 湮灭 / 光圈）
    ///   3. 苍 / 赤 / 茈 / 领域（开关 / 冷却 / 伤害 / 半径 / 力度 / 持续）
    ///   4. 战斗（Boss 系数 / 连招窗口）
    ///   5. 按键（6 行，只显示原版动作的当前绑定；改键去原版「选项 - 控制」）
    ///   6. HUD（开关 / 位置 / 行高）
    ///
    /// ============================ 按键 ============================
    /// 配置里的 <c>Key</c> 存的是**键名**（<c>"J"</c> / <c>"F10"</c> / <c>"Space"</c> 之类，
    /// 见 <see cref="GojoKeys"/>）。本页可以**直接改键**：点一行 → 按下一个非修饰键即可。
    /// </summary>
    public class GojoModMenu : IModMenu
    {
        public string GetName() => "GojoLimitless";

        /// <summary>按键行 widget。</summary>
        private static readonly List<OptionWidget> _keyRows = new();
        private static readonly List<Func<string>> _keyGetters = new();
        private static readonly List<OptionWidget> _allRows = new();

        // ---------------------------------------------------------------- 按键捕获状态

        /// <summary>是否正在等玩家按键。</summary>
        public static bool KeyCapturing { get; private set; }

        /// <summary>
        /// 进入捕获后先忽略这么久 —— 原版"激活控件"和"确认"都是回车，
        /// 不缓冲的话那一次回车会被立刻当成新绑定。
        /// </summary>
        private const double CaptureWarmupSeconds = 0.35;

        private static double _captureWarmup;

        /// <summary>是否已经观察到"所有键都松开过"（之后才开始收键）。</summary>
        private static bool _captureSawRelease;

        /// <summary>要排除的键（激活控件的那一次按键）。</summary>
        private static int _captureIgnoreKey;

        private static string _capturingLabel;
        private static Action<string> _capturingSet;

        private const int VkEscape = 0x1B;
        private const int VkBack = 0x08;
        private const int VkEnter = 0x0D;
        private const int VkShift = 0x10;
        private const int VkCtrl = 0x11;
        private const int VkAlt = 0x12;

        /// <summary>主循环每帧调用（选项菜单是暂停状态，只有 IOnFrameUpdate 还在跑）。</summary>
        public static void Tick(double dt)
        {
            // 键位行的副标题每帧刷新
            RefreshKeyRows();

            if (!KeyCapturing) return;

            if (_captureWarmup > 0.0) _captureWarmup -= dt;

            // 必须等所有键都松开过一次，否则"激活控件"那一下还按着的 Enter 会被吃掉
            if (!_captureSawRelease)
            {
                if (Input.HeldCount == 0 && _captureWarmup <= 0.0) _captureSawRelease = true;
                else return;
            }

            int vk = Input.FirstPressed();
            if (vk == 0) return;

            if (vk == VkEscape || vk == VkBack)
            {
                Log.Info($"取消重绑定 {_capturingLabel}");
                EndCapture();
                return;
            }

            if (vk == _captureIgnoreKey) return;

            if (vk == VkShift || vk == VkCtrl || vk == VkAlt)
            {
                Log.Info($"忽略修饰键 {GojoKeys.Name(vk)}，请按一个非修饰键");
                return;
            }

            string name = GojoKeys.Name(vk);
            try { _capturingSet?.Invoke(name); } catch (Exception ex) { Log.Warn("写入按键失败: " + ex.Message); }
            Cfg.Save();
            Abilities.GojoHub.ReloadKeys();
            Log.Info($"重绑定 {_capturingLabel} = {name}（键码 {vk}）");
            EndCapture();
            RefreshKeyRows();
        }

        private static void BeginCapture(string label, Action<string> set, int ignoreKey)
        {
            KeyCapturing = true;
            _captureWarmup = CaptureWarmupSeconds;
            _captureSawRelease = false;
            _captureIgnoreKey = ignoreKey;
            _capturingLabel = label;
            _capturingSet = set;
        }

        private static void EndCapture()
        {
            KeyCapturing = false;
            _capturingSet = null;
            _capturingLabel = null;
            _captureSawRelease = false;
        }

        // ---------------------------------------------------------------- 页面

        public void BuildMenu(dc.ui.Options options)
        {
            OptionsBase ob = options;

            // 每次重建都清掉旧引用（原版会重新构建整页）
            _keyRows.Clear();
            _keyGetters.Clear();
            _keyLabels.Clear();
            _keyMainTexts.Clear();
            _keySubTexts.Clear();
            _allRows.Clear();

            ob.title.set_text(StringUtils.AsHaxeString("GOJOLIMITLESS 设置"));

            // 和游戏自带的设置页一致：左右留白 + 80% 宽度
            ob.createScroller(0.0);
            try
            {
                dc.Main main = dc.Main.Class?.ME;
                double ps = main != null ? main.pixelScale : 3.0;
                if (ps < 0.1) ps = 3.0;

                int stageW = dc.libs.Process.Class.CUSTOM_STAGE_WIDTH;
                if (stageW <= 0) stageW = 1920;

                ob.scrollerFlow.set_paddingLeft((int)(stageW * 0.1) + (int)(ps * 40.0));
                int w = (int)(stageW * 0.8);
                ob.scrollerFlow.set_minWidth((int?)w);
                ob.scrollerFlow.set_maxWidth((int?)w);
            }
            catch { }

            Configs c = Cfg.V;

            // ============================================================ 总开关
            AddToggle(ob, "启用 GojoLimitless",
                      "整个模组的总开关；关掉后所有招式与被动都停用",
                      () => Cfg.V.Ui.ModEnabled, v => Cfg.V.Ui.ModEnabled = v);

            // ============================================================ 无下限
            ob.addSeparator(StringUtils.AsHaxeString("无下限（被动）"), ob.scrollerFlow);
            AddToggle(ob, "启用无下限", "屏障吸收 · 迟滞领域 · 湮灭弹幕",
                      () => Cfg.V.Infinity.Enabled, v => Cfg.V.Infinity.Enabled = v);
            AddSlider(ob, "屏障半径（格）", "敌人进入这个范围会被迟滞、子弹会被湮灭",
                      () => Cfg.V.Infinity.Radius, v => Cfg.V.Infinity.Radius = v, 0.5, 0.5, 40.0);
            AddSlider(ob, "咒力储量上限", "被打时按伤害扣；扣光就挡不住",
                      () => Cfg.V.Infinity.Reserve, v => Cfg.V.Infinity.Reserve = v, 20.0, 1.0, 100000.0);
            AddSlider(ob, "储量 / 属性等级", "三个属性等级之和再乘这个数加进上限",
                      () => Cfg.V.Infinity.ReservePerLevel, v => Cfg.V.Infinity.ReservePerLevel = v, 2.0, 0.0, 10000.0);
            AddSlider(ob, "储量回充 / 秒", "小怪连续输出基本无伤、Boss 大伤害能打穿的关键",
                      () => Cfg.V.Infinity.RegenPerSecond, v => Cfg.V.Infinity.RegenPerSecond = v, 10.0, 0.0, 100000.0);
            AddSlider(ob, "单次吸收上限", "单次最多吃掉储量的百分比（防止一发被吸干）",
                      () => Cfg.V.Infinity.MaxDrainPerHit, v => Cfg.V.Infinity.MaxDrainPerHit = v, 0.05, 0.05, 1.0);
            AddSlider(ob, "破裂停顿（秒）", "被打穿后的充能停顿",
                      () => Cfg.V.Infinity.BreakPenalty, v => Cfg.V.Infinity.BreakPenalty = v, 0.1, 0.0, 10.0);
            AddSlider(ob, "破裂期回充倍率", "停顿期间回充降到这个比例",
                      () => Cfg.V.Infinity.BreakRegenFactor, v => Cfg.V.Infinity.BreakRegenFactor = v, 0.05, 0.0, 1.0);
            AddSlider(ob, "迟滞最低移速倍率", "贴脸时的移速（0.25 = 只剩 25%）",
                      () => Cfg.V.Infinity.SlowMinMultiplier, v => Cfg.V.Infinity.SlowMinMultiplier = v, 0.05, 0.02, 1.0);
            AddSlider(ob, "迟滞尾巴（秒）", "敌人离开屏障后多久恢复正常速度",
                      () => Cfg.V.Infinity.SlowLinger, v => Cfg.V.Infinity.SlowLinger = v, 0.05, 0.0, 5.0);
            AddToggle(ob, "湮灭敌方子弹", "飞进屏障的敌方子弹直接消失",
                      () => Cfg.V.Infinity.EraseBullets, v => Cfg.V.Infinity.EraseBullets = v);
            AddToggle(ob, "显示呼吸光圈", "英雄身上的青色呼吸光圈",
                      () => Cfg.V.Infinity.AuraFx, v => Cfg.V.Infinity.AuraFx = v);
            AddSlider(ob, "光圈呼吸幅度", "0 = 静止；1 = 半径与透明度 ±20%",
                      () => Cfg.V.Infinity.AuraBreath, v => Cfg.V.Infinity.AuraBreath = v, 0.1, 0.0, 3.0);
            AddSlider(ob, "低咒力变红阈值", "储量低于这个比例时光圈转红",
                      () => Cfg.V.Infinity.AuraLowReserveRatio, v => Cfg.V.Infinity.AuraLowReserveRatio = v, 0.05, 0.0, 1.0);

            // ============================================================ 四个招式
            AddAbility(ob, "术式顺转·苍", "控制招：大范围把敌人拽过来，伤害低",
                       () => Cfg.V.Blue, 0.5, 60.0, 1.0, 40.0, 0.0, 100000.0, 0.05, 8.0);
            AddAbility(ob, "术式反转·赤", "击飞招：贴身一圈掀飞，伤害中",
                       () => Cfg.V.Red, 0.5, 60.0, 1.0, 40.0, 0.0, 100000.0, 0.05, 8.0);
            AddAbility(ob, "虚式·茈", "大招：面向锥形贯穿，长冷却高伤害",
                       () => Cfg.V.Purple, 0.5, 60.0, 2.0, 80.0, 0.0, 1000000.0, 0.05, 8.0);

            ob.addSeparator(StringUtils.AsHaxeString("领域展开·无量空处"), ob.scrollerFlow);
            AddToggle(ob, "启用领域展开", "大范围定身 + 持续伤害",
                      () => Cfg.V.Domain.Enabled, v => Cfg.V.Domain.Enabled = v);
            AddSlider(ob, "冷却（秒）", "两次施放之间的间隔",
                      () => Cfg.V.Domain.Cooldown, v => Cfg.V.Domain.Cooldown = v, 0.5, 0.5, 300.0);
            AddSlider(ob, "每跳伤害", "每 TickInterval 秒造成一跳",
                      () => Cfg.V.Domain.Damage, v => Cfg.V.Domain.Damage = v, 50.0, 0.5, 100000.0);
            AddSlider(ob, "外圈半径（格）", "领域覆盖范围；外圈只吸附不造成伤害",
                      () => Cfg.V.Domain.Radius, v => Cfg.V.Domain.Radius = v, 1.0, 2.0, 60.0);
            AddSlider(ob, "内圈半径（格）", "进了这个圈才开始掉血（必须小于外圈）",
                      () => Cfg.V.Domain.InnerRadius, v => Cfg.V.Domain.InnerRadius = v, 0.5, 1.0, 60.0);
            AddSlider(ob, "外圈吸附倍率", "外圈把敌人往里拽的额外力度（越大吸得越快）",
                      () => Cfg.V.Domain.OuterPullMultiplier, v => Cfg.V.Domain.OuterPullMultiplier = v, 0.1, 1.0, 10.0);
            AddSlider(ob, "力度", "把敌人拉向中心的强度",
                      () => Cfg.V.Domain.Power, v => Cfg.V.Domain.Power = v, 0.05, 0.0, 8.0);
            AddSlider(ob, "持续（秒）", "领域持续时间",
                      () => Cfg.V.Domain.Duration, v => Cfg.V.Domain.Duration = v, 0.5, 0.5, 60.0);
            AddSlider(ob, "领域移速倍率", "领域内敌人的移速（0.20 = 只剩 20%）",
                      () => Cfg.V.Domain.SlowMultiplier, v => Cfg.V.Domain.SlowMultiplier = v, 0.05, 0.02, 1.0);
            AddSlider(ob, "伤害跳间隔（秒）", "两跳之间的间隔",
                      () => Cfg.V.Domain.TickInterval, v => Cfg.V.Domain.TickInterval = v, 0.05, 0.05, 5.0);
            AddToggle(ob, "领域内无敌", "施放期间给英雄短暂无敌（原版 affect 5）",
                      () => Cfg.V.Domain.Invincible, v => Cfg.V.Domain.Invincible = v);
            AddToggle(ob, "领域氛围特效", "全屏紫色 + 虚空环 + 裂纹 + 扫光，持续整个领域；晃眼可关",
                      () => Cfg.V.Domain.DomainFx, v => Cfg.V.Domain.DomainFx = v);

            // ============================================================ 战斗
            ob.addSeparator(StringUtils.AsHaxeString("战斗"), ob.scrollerFlow);
            AddSlider(ob, "Boss 牵引 / 击退系数", "Boss 与精英受到的位移额外乘这个数",
                      () => Cfg.V.BossPullPushFactor, v => Cfg.V.BossPullPushFactor = v, 0.05, 0.0, 1.0);
            AddSlider(ob, "连招窗口（秒）", "苍之后多久内按赤会接出茈",
                      () => Cfg.V.ComboWindow, v => Cfg.V.ComboWindow = v, 0.25, 0.0, 10.0);

            // ============================================================ 按键（只读显示）
            ob.addSeparator(StringUtils.AsHaxeString("按键（点一行 → 按下一个键即可改绑；推荐 J/K/L/U/I/O/H）"), ob.scrollerFlow);
            AddKey(ob, "苍", () => Cfg.V.Blue.Key, v => Cfg.V.Blue.Key = v);
            AddKey(ob, "赤", () => Cfg.V.Red.Key, v => Cfg.V.Red.Key = v);
            AddKey(ob, "茈", () => Cfg.V.Purple.Key, v => Cfg.V.Purple.Key = v);
            AddKey(ob, "领域展开", () => Cfg.V.Domain.Key, v => Cfg.V.Domain.Key = v);
            AddKey(ob, "强制开启（测试用）", () => Cfg.V.Ui.ForceEnableKey, v => Cfg.V.Ui.ForceEnableKey = v);
            AddKey(ob, "打开本模组菜单（覆盖层）", () => Cfg.V.Ui.MenuKey, v => Cfg.V.Ui.MenuKey = v);

            // ============================================================ 界面
            ob.addSeparator(StringUtils.AsHaxeString("界面"), ob.scrollerFlow);
            AddToggle(ob, "显示 HUD", "左上角咒力条 / 冷却条 / 连招提示",
                      () => Cfg.V.Ui.HudEnabled, v => Cfg.V.Ui.HudEnabled = v);
            AddSlider(ob, "HUD X（横向位置）", "越大越靠右",
                      () => Cfg.V.Ui.HudX, v => Cfg.V.Ui.HudX = v, 4.0, -2000.0, 4000.0);
            AddSlider(ob, "HUD Y（纵向位置）", "越大越靠下（原版左上角 UI 大约占到 260）",
                      () => Cfg.V.Ui.HudY, v => Cfg.V.Ui.HudY = v, 4.0, -2000.0, 4000.0);
            AddSlider(ob, "HUD 行高", "每一行的基准高度",
                      () => Cfg.V.Ui.HudLineHeight, v => Cfg.V.Ui.HudLineHeight = v, 1.0, 8.0, 40.0);
            AddSlider(ob, "HUD 行距倍率", "把每行之间的距离拉开（1.0 = 刚好等于行高）",
                      () => Cfg.V.Ui.HudRowSpacing, v => Cfg.V.Ui.HudRowSpacing = v, 0.05, 0.5, 3.0);
            AddSlider(ob, "HUD 文字大小", "整体缩放（1.0 = 原尺寸）",
                      () => Cfg.V.Ui.HudScale, v => Cfg.V.Ui.HudScale = v, 0.05, 0.4, 3.0);
            AddToggle(ob, "HUD 显示日志", "在 HUD 下面叠加最近几条运行日志（自检用）",
                      () => Cfg.V.Ui.HudShowLog, v => Cfg.V.Ui.HudShowLog = v);
            AddSlider(ob, "HUD 日志行数", "显示最近几条日志",
                      () => (double)Cfg.V.Ui.HudLogLines,
                      v => Cfg.V.Ui.HudLogLines = (int)System.Math.Round(v), 1.0, 0.0, 12.0);
            AddToggle(ob, "详细日志", "把命中 / 减速 / 屏障次数写进日志",
                      () => Cfg.V.Ui.VerboseLog, v => Cfg.V.Ui.VerboseLog = v);

            ob.updateScroller();

            // 全部建完之后刷新一遍按键行（显示当前绑定）
            RefreshKeyRows();

            Log.Info($"原生选项菜单已构建（{_allRows.Count} 个控件，{_keyRows.Count} 个键位行）");
            _ = c;
        }

        // ---------------------------------------------------------------- 控件工厂

        private static void AddAbility(OptionsBase ob, string title, string hint,
                                       Func<AbilityCfg> cfg,
                                       double cdStep, double cdMax,
                                       double radStep, double radMax,
                                       double dmgStep, double dmgMax,
                                       double powStep, double powMax)
        {
            ob.addSeparator(StringUtils.AsHaxeString(title + " —— " + hint), ob.scrollerFlow);
            AddToggle(ob, title + " 启用", "关掉后这一招不响应按键",
                      () => cfg().Enabled, v => cfg().Enabled = v);
            AddSlider(ob, title + " 冷却（秒）", "两次施放之间的间隔",
                      () => cfg().Cooldown, v => cfg().Cooldown = v, cdStep, 0.05, cdMax);
            AddSlider(ob, title + " 伤害（基础值）", "会随升级卷轴放大；关掉下面的开关则固定",
                      () => cfg().Damage, v => cfg().Damage = v, dmgStep, 0.0, dmgMax);
            AddToggle(ob, title + " 伤害吃卷轴加成", "打开后伤害随三个属性等级之和放大（同原版武器）",
                      () => cfg().UseHeroScaling, v => cfg().UseHeroScaling = v);
            AddSlider(ob, title + " 半径 / 射程（格）", "作用范围（茈是射程，锥形宽度按比例算）",
                      () => cfg().Radius, v => cfg().Radius = v, radStep, 1.0, radMax);
            AddSlider(ob, title + " 力度", "牵引 / 击退强度",
                      () => cfg().Power, v => cfg().Power = v, powStep, 0.0, powMax);
            AddSlider(ob, title + " 持续（秒）", "瞬发招式无意义，领域用它",
                      () => cfg().Duration, v => cfg().Duration = v, 0.5, 0.0, 60.0);
        }

        private static void AddToggle(OptionsBase ob, string label, string hint,
                                      Func<bool> get, Action<bool> set)
        {
            try
            {
                bool snapshot = get();
                OptionWidget w = ob.addToggleWidget(
                    StringUtils.AsHaxeString(label),
                    StringUtils.AsHaxeString(hint),
                    (HlFunc<bool>)(() =>
                    {
                        bool now = !get();
                        set(now);
                        Cfg.Save();
                        return now;
                    }),
                    new Ref<bool>(ref snapshot),
                    ob.scrollerFlow);

                if (w != null) _allRows.Add(w);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "创建开关控件失败: " + label);
            }
        }

        /// <summary>
        /// 一个数值滑条。
        ///
        /// ⚠️ <c>addSliderWidget</c> 的 <c>button</c> 参数是**精灵名前缀**，不是说明文字！
        /// 反编译 <c>OptionsBase.addSliderWidget</c> 可以看到它做的事是：
        ///
        /// <code>
        /// if (button == null) button = "sliderButton";      // 默认值
        /// obj  = "" + button + "Off";                        // → "sliderButtonOff"
        /// obj2 = "" + button + "On";                         // → "sliderButtonOn"
        /// ...
        /// new HSprite(Assets.Class.ui, obj, ...);            // 从 ui 图集里取这张图
        /// </code>
        ///
        /// 之前把中文说明当成 <c>button</c> 传进去，于是它去图集里找
        /// <c>"敌人进入这个范围会被迟滞、子弹会被湮灭Off"</c> 这张精灵 ——
        /// 找不到 → hashlink 抛 "Uncaught hashlink exception"，**每一个滑条都没建出来**
        /// （这就是"滑条无法滑动"：屏幕上根本没有滑条，只剩开关和分隔线）。
        ///
        /// <c>hint</c> 只能拼进标题文字里（原版滑条没有副标题位置）。
        /// </summary>
        private static void AddSlider(OptionsBase ob, string label, string hint,
                                      Func<double> get, Action<double> set,
                                      double step, double min, double max)
        {
            try
            {
                // ⚠️ 步长**不能是 0**：原版滑条拿它做离散步进，传 0 会让滑条完全拖不动
                //    （实机反馈："伤害改成 0 之后就拖不动滑条了"—— 因为伤害那格传的是 0.0）。
                //    这里兜一个正步长，并按量级自动放大，免得 0~100000 的区间一步只动 0.01。
                double safeStep = step;
                if (safeStep <= 0.0) safeStep = 1.0;
                if (max > 0.0 && safeStep < max / 200.0) safeStep = max / 200.0;

                double snapshot = get();
                double stepRef = safeStep;
                bool showPercent = false;
                bool showRaw = true;
                double minRef = min;
                double maxRef = max;
                int padding = 0;

                // 原版滑条没有副标题，所以把说明折进标题后面
                string title = string.IsNullOrEmpty(hint) ? label : label + " — " + hint;

                OptionWidget w = ob.addSliderWidget(
                    StringUtils.AsHaxeString(title),
                    (HlAction<double>)(v =>
                    {
                        set(v);
                        Cfg.Save();
                    }),
                    snapshot,
                    ref stepRef,
                    ob.scrollerFlow,
                    ref showPercent,
                    ref showRaw,
                    ref minRef,
                    ref maxRef,
                    null,            // ← button：必须 null（=用默认精灵 "sliderButton"）
                    ref padding);

                if (w != null) _allRows.Add(w);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "创建滑条控件失败: " + label);
            }
        }

        /// <summary>
        /// 一个键位行：点一下进入**按键捕获**，下一个按下的（非修饰）键就是新绑定。
        ///
        /// v7 起按键回到"模组自己存键名"（<see cref="GojoKeys"/>），所以这里可以真正改键。
        /// 推荐用死亡细胞里默认空着的 J / K / L / U / I / O / H 或 F1~F12。
        ///
        /// ⚠️ 踩过的几个坑（动这段之前先看）：
        ///
        /// 1. <c>dc.h2d.HtmlText.set_text</c> 会走 <c>_Xml_.parse(text)</c> ——
        ///    **文本被当成 XML**，出现 <c>&lt;</c> 会解析失败。所以文案里不能有尖括号
        ///    （原来写的 <c>&lt;&lt; 请按下一个键 &gt;&gt;</c> 是炸弹）。
        /// 2. 文字节点不能"猜下标"。实机日志里 <c>副标题节点=无</c> 说明按位置取是错的。
        ///    现在仍然**按初始文案反查**：建完之后哪条 Text 的文字等于初始副标题，
        ///    哪条就是副标题 —— 这个判据不依赖树结构。
        /// 3. 刷新不靠我自己每帧遍历，而是挂到原版控件的 <c>onUpdate</c> 上
        ///    （<c>OptionsBase</c> 每帧会遍历所有 widget 调它，见 OptionsBase.cs:23652-23667）。
        /// 4. **激活控件那一次的回车不能当成新绑定** —— 单击是"确认键"触发的，
        ///    所以要记下它并排除，并要求所有键先松开过一次（见 <see cref="Tick"/>）。
        /// </summary>
        private static void AddKey(OptionsBase ob, string label, Func<string> get, Action<string> set)
        {
            try
            {
                int offset = 0;
                OptionWidget w = ob.addSimpleWidget(
                    StringUtils.AsHaxeString(label),
                    StringUtils.AsHaxeString(SubTextIdle(get())),
                    (HlAction)(() =>
                    {
                        try
                        {
                            // 记下"激活控件的那一次按键"，捕获逻辑会把它排除
                            int ignore = Input.FirstPressed();
                            BeginCapture(label, set, ignore != 0 ? ignore : VkEnter);
                            Log.Info($"等待新按键：{label}（按一个非修饰键；Esc / Backspace 取消）");
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("进入按键捕获失败: " + ex.Message);
                        }
                    }),
                    ref offset,
                    ob.scrollerFlow);

                if (w == null)
                {
                    Log.Warn("按键控件创建返回 null: " + label);
                    return;
                }

                int rowIndex = _keyRows.Count;
                _allRows.Add(w);
                _keyRows.Add(w);
                _keyGetters.Add(get);
                _keyLabels.Add(label);

                // 建完之后按"初始副标题内容"反查节点
                var texts = new List<dc.h2d.Text>(4);
                try { CollectTexts(w, texts, 0); } catch { }

                dc.h2d.Text sub = null;
                dc.h2d.Text main = null;
                for (int i = 0; i < texts.Count; i++)
                {
                    string cur = TextOf(texts[i]);
                    if (cur == SubTextIdle(get())) { sub = texts[i]; continue; }
                    if (cur == label && main == null) main = texts[i];
                }
                if (main == null && texts.Count > 0) main = texts[0];
                if (sub == null && texts.Count > 1) sub = texts[texts.Count - 1];

                _keyMainTexts.Add(main);
                _keySubTexts.Add(sub);

                Log.Info($"按键行已建：{label}  共找到 {texts.Count} 条文字，"
                         + $"主标题={(main != null ? "有" : "无")}  副标题={(sub != null ? "有" : "无")}");

                // 原版每帧会调 onUpdate —— 借它来保持副标题
                try
                {
                    w.onUpdate = (HlAction)(() => RefreshOne(rowIndex));
                }
                catch (Exception ex)
                {
                    Log.Warn("挂 onUpdate 失败（按键行不会自动刷新）: " + ex.Message);
                }

                RefreshOne(rowIndex);
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "创建按键控件失败: " + label);
            }
        }

        private static string TextOf(dc.h2d.Text t)
        {
            try { return t?.text?.ToString() ?? ""; }
            catch { return ""; }
        }

        /// <summary>
        /// 副标题文案。
        ///
        /// <code>
        ///   正在等按键 → 输入要绑定的按键…（Esc 取消）
        ///   没绑       → 未绑定（点这里按一个键）
        ///   正常       → 当前 A（键码 65）
        /// </code>
        /// </summary>
        private static string SubTextIdle(string keyName)
        {
            if (KeyCapturing)
            {
                return "输入要绑定的按键…（Esc / Backspace 取消）";
            }

            int vk = GojoKeys.Parse(keyName, 0);
            if (vk <= 0)
            {
                return string.IsNullOrWhiteSpace(keyName)
                    ? "未绑定（点这里，再按一个键）"
                    : "按键名无效（当前 \"" + keyName + "\"）；" + GojoKeys.HelpText;
            }

            return "当前 " + GojoKeys.Name(vk) + "（键码 " + vk + "）；点这里可改键";
        }

        /// <summary>刷新单个键位行（原版每帧回调 + 我们主动调）。</summary>
        private static void RefreshOne(int index)
        {
            if (index < 0 || index >= _keySubTexts.Count) return;

            // 记录"原版还在调 onUpdate"这个事实 —— 用来检测选项页已经关掉
            _lastRowUpdateTick = Environment.TickCount64;

            string actionName = "";
            try
            {
                if (index < _keyGetters.Count)
                {
                    Func<string> g = _keyGetters[index];
                    if (g != null) actionName = g() ?? "";
                }
            }
            catch { }

            // 副标题内容由 GojoKeys 从配置算出来，所以改完键下一帧就更新
            SetTextIfChanged(index < _keySubTexts.Count ? _keySubTexts[index] : null,
                             SubTextIdle(actionName));
        }

        /// <summary>上一次原版调用 onUpdate 的时间（毫秒，单调）。</summary>
        private static long _lastRowUpdateTick;

        /// <summary>
        /// 选项页是不是已经关掉了。
        ///
        /// ⚠️ 原来是给"按键捕获"收尾用的：玩家按 Esc 时**原版会直接关掉整个选项页**，
        /// 我们收不到任何"取消"回调，捕获状态就永远挂着不动。现在不捕获了，
        /// 这里只保留"原版已经多久没刷新控件"的判定，超时就记一条日志（避免刷屏）。
        /// </summary>
        private const long PageGoneMillis = 700;

        /// <summary>"页面已关闭"这条日志只记一次；页面重新有刷新后重新武装。</summary>
        private static bool _pageGoneLogged;

        private static void CheckPageStillOpen()
        {
            if (_lastRowUpdateTick == 0) return;

            long now = Environment.TickCount64;
            if (now - _lastRowUpdateTick < PageGoneMillis)
            {
                _pageGoneLogged = false;
                return;
            }

            if (_pageGoneLogged) return;
            _pageGoneLogged = true;
            Log.Info("选项页已关闭 —— 停止刷新按键行（重新打开页面会重建）");
        }

        /// <summary>只在内容变了的时候 set_text，并让这一行重新排版（否则改动要等下次进页面才看见）。</summary>
        private static void SetTextIfChanged(dc.h2d.Text node, string text)
        {
            if (node == null || text == null) return;

            // set_text 会当 XML 解析，尖括号先清掉
            text = GojoMenu.Sanitize(text);

            try
            {
                if (node.text != null && node.text.ToString() == text) return;
            }
            catch { }

            try { node.set_text(StringUtils.AsHaxeString(text)); } catch { }

            // ⚠️ 关键：set_text 只改了文字，容器不会自动重新排版 ——
            //    不 reflow 的话改动要等下次进页面才看得到（实机反馈就是"要重新进才能看到"）。
            try
            {
                if (node.parent is Flow pf) pf.reflow();
            }
            catch { }
        }

        /// <summary>
        /// 深度优先收集控件里**全部** <c>dc.h2d.Text</c>。
        ///
        /// ⚠️ 之前只遍历一层 <c>children</c>，结果主标题找得到、副标题永远是 null
        /// （实机日志：<c>按键行已建：苍  主标题节点=有  副标题节点=无</c>）——
        /// 所以那 6 行文字从来没被改过，"重绑定没有反馈"就是这个原因。
        /// 原版把文字包在子 Flow 里，必须递归下去。
        /// </summary>
        private static void CollectTexts(dc.h2d.Object node, List<dc.h2d.Text> outList, int depth)
        {
            if (node == null || outList == null || depth > 6) return;

            if (node is dc.h2d.Text t)
            {
                outList.Add(t);
                return;
            }

            dc.hl.types.ArrayObj kids;
            try { kids = node.children; }
            catch { return; }
            if (kids == null) return;

            for (int i = 0; i < kids.length; i++)
            {
                object raw;
                try { raw = kids.getDyn(i); }
                catch { break; }
                if (raw is dc.h2d.Object child) CollectTexts(child, outList, depth + 1);
            }
        }

        /// <summary>给 AddKey 用：记录顺序里的显示名 / 主副标题节点。</summary>
        private static readonly List<string> _keyLabels = new();
        private static readonly List<dc.h2d.Text> _keyMainTexts = new();
        private static readonly List<dc.h2d.Text> _keySubTexts = new();

        /// <summary>
        /// 刷新全部按键行。
        ///
        /// 平常由原版控件的 <c>onUpdate</c> 逐行驱动（<see cref="AddKey"/> 里挂的），
        /// 这里每帧兜底刷一遍 —— 玩家在原版「选项 - 控制」里改键后立刻就能看到。
        /// </summary>
        private static void RefreshKeyRows()
        {
            for (int i = 0; i < _keySubTexts.Count; i++) RefreshOne(i);
        }
    }
}
