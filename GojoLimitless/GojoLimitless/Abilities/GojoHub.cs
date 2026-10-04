#nullable disable
using System;
using System.Collections.Generic;
using dc.en;

namespace GojoLimitless.Abilities
{
    /// <summary>
    /// 招式总调度：
    ///   * 注册全部招式（苍 / 赤 / 茈 / 无量空处 / 无下限被动）
    ///   * 每帧：刷按键 → 刷新道具门槛 → 派发 TryActivate + Update
    ///   * 换关、换英雄时重置全部状态
    ///
    /// 「无下限」是被动（没有按键），也挂在这里一起 Update —— 一处就能看全所有状态。
    /// </summary>
    public static class GojoHub
    {
        /// <summary>当前是否允许放招（总开关；不再依赖任何自建道具）。</summary>
        public static bool AbilitiesActive { get; private set; }

        /// <summary>剩余强制开启时间（秒）；&lt;= 0 表示没开。默认模式下无实际作用。</summary>
        public static double ForceTimer { get; private set; }

        /// <summary>连招在窗口内时的提示文本（HUD 用）；空串 = 没有提示。</summary>
        public static string ComboHint { get; private set; } = "";

        private static readonly List<GojoAbilityBase> All = new();

        /// <summary>全部招式（只读，HUD / 菜单遍历用）。</summary>
        public static IReadOnlyList<GojoAbilityBase> Abilities => All;

        private static InfinityAbility _infinity;
        private static bool _loggedActive;

        /// <summary>强制开启 / 模组菜单的键码（0 = 没绑）。</summary>
        private static int _forceKey = 0x4F;   // O
        private static int _menuKey = 0x79;    // F10

        private static double _lastDt = 1.0 / 60.0;

        // ------------------------------------------------------------ 初始化

        public static void Initialize()
        {
            All.Clear();
            _infinity = new InfinityAbility();
            All.Add(_infinity);
            All.Add(new BlueAbility());
            All.Add(new RedAbility());
            All.Add(new PurpleAbility());
            All.Add(new UnlimitedVoidAbility());

            ReloadKeys();
            Log.Info("招式已注册: " + DescribeAll());
        }

        /// <summary>
        /// 配置改动后重新解析按键。
        ///
        /// v7 起按键回到"模组自己存键名"：这里把键名解析成键码并**加进跟踪集**，
        /// 保证菜单/配置里改完键当帧就能用。
        /// </summary>
        public static void ReloadKeys()
        {
            UiCfg ui = Cfg.V.Ui;
            _forceKey = GojoKeys.Bind(ui != null ? ui.ForceEnableKey : "O", 0x4F /* O */);
            _menuKey = GojoKeys.Bind(ui != null ? ui.MenuKey : "F10", 0x79);

            for (int i = 0; i < All.Count; i++)
            {
                try { Input.EnsureKey(All[i].TriggerKey); } catch { }
            }
        }

        /// <summary>强制开启键的键码。</summary>
        public static int ForceKey => _forceKey;

        /// <summary>模组覆盖层菜单键的键码。</summary>
        public static int MenuKey => _menuKey;

        public static void EnableForce(double seconds)
        {
            ForceTimer = seconds;
            Log.Info($"强制开启 {seconds:0}s —— 无需「Limitless」道具");
        }

        public static void DisableForce()
        {
            ForceTimer = 0.0;
        }

        public static bool ForceActive => ForceTimer > 0.0;

        // ------------------------------------------------------------ 每帧

        /// <summary>
        /// 每帧调度。
        ///
        /// <paramref name="inputAlreadyPolled"/> = true 时跳过 <see cref="Input.Poll"/>
        /// （菜单打开时由菜单自己 Poll 一次，避免同一帧刷两遍把边沿吃掉）。
        /// </summary>
        public static void Tick(Hero hero, double dt, bool inputAlreadyPolled = false)
        {
            if (dt <= 0.0 || double.IsNaN(dt) || dt > 1.0) dt = 1.0 / 60.0;
            _lastDt = dt;

            if (!inputAlreadyPolled) Input.Poll();

            UiCfg ui = Cfg.V.Ui;

            if (Input.Pressed(_forceKey))
            {
                if (ForceTimer > 0.0) { DisableForce(); Log.Info("强制开启已关闭"); }
                else EnableForce(ui != null ? ui.ForceEnableSeconds : 180.0);
            }

            if (ForceTimer > 0.0) ForceTimer -= dt;


            // ⚠️ 门槛**不在这里刷**了 —— 移到 <see cref="UpdateGateAlways"/>，
            //    由主循环每帧无条件调用（包括 hero 为 null / 已销毁的帧）。
            //    原来放在这里，只要 hero 有若干帧是 null，门槛就永远停在 false，
            //    HUD 显示 [OFF]、所有招式被 !Gated 挡掉 —— 实机踩过的坑。

            for (int i = 0; i < All.Count; i++)
            {
                try { All[i].TryActivate(hero, dt); }
                catch (Exception ex) { Log.Exception(ex, All[i].DisplayName + " TryActivate 异常"); }
            }

            for (int i = 0; i < All.Count; i++)
            {
                try { All[i].Update(hero, dt); }
                catch (Exception ex) { Log.Exception(ex, All[i].DisplayName + " Update 异常"); }
            }

            UpdateComboHint();
        }

        /// <summary>连招提示：苍之后窗口内 → "F6 接 茈"。</summary>
        private static void UpdateComboHint()
        {
            try
            {
                BlueAbility blue = Find<BlueAbility>();
                PurpleAbility purple = Find<PurpleAbility>();
                if (blue == null || purple == null) { ComboHint = ""; return; }

                double window = Cfg.V.ComboWindow;
                if (window <= 0.0 || !purple.CanFireInternally)
                {
                    ComboHint = "";
                    return;
                }

                double since = blue.SinceLastUse;
                if (since >= 0.0 && since <= window)
                {
                    int redKey = 0;
                    RedAbility red = Find<RedAbility>();
                    if (red != null) redKey = red.TriggerKey;
                    ComboHint = GojoKeys.Name(redKey) + " -> " + purple.DisplayName;
                }
                else
                {
                    ComboHint = "";
                }
            }
            catch { ComboHint = ""; }
        }

        /// <summary>
        /// 刷新总开关门槛。**主循环每帧无条件调用**（和 hero 有没有值无关）。
        ///
        /// 为什么不放进 <see cref="Tick"/>：<c>Tick</c> 只在 hero 非 null 时才被调用，
        /// 而换关 / 读档 / 死亡瞬间 hero 会有若干帧是 null 或已销毁。门槛一旦在那里刷新，
        /// <c>ResetAll()</c> 把它置 false 之后就再也没机会被刷回 true ——
        /// 表现就是 HUD 一直 `[OFF]`、所有招式被 `!Gated` 挡掉。
        /// </summary>
        public static void UpdateGateAlways()
        {
            try
            {
                UiCfg ui = Cfg.V.Ui;
                bool active = ui == null || ui.ModEnabled;

                // ⚠️ 用"和上一帧比"判断状态变化，别用 _loggedActive ——
                //    ResetAll() 会把两边都清成 false，一旦不同步，
                //    "重新激活"这条日志就永远不再出现（排查时会被误导）。
                if (active != AbilitiesActive)
                {
                    Log.Info(active
                        ? "无下限已激活（按键即用）"
                        : "无下限关闭（模组总开关关掉了）");
                }
                _loggedActive = active;
                AbilitiesActive = active;
            }
            catch (Exception ex)
            {
                Log.Warn("刷新总开关失败: " + ex.Message);
            }
        }

        public static void ResetAll()
        {
            for (int i = 0; i < All.Count; i++)
            {
                try { All[i].Reset(); } catch { }
            }
            InfinityAbility.ResetAll();
            Input.Reset();
            _loggedActive = false;
            ForceTimer = 0.0;
            AbilitiesActive = false;
            ComboHint = "";
        }

        /// <summary>关闭所有持续效果（离开关卡 / 退出游戏时）。</summary>
        public static void Shutdown()
        {
            try { InfinityAbility.Shutdown(); } catch { }
            try { UnlimitedVoidAbility.Shutdown(); } catch { }
        }

        public static InfinityAbility Infinity => _infinity;

        /// <summary>按类型取已注册的招式（连招判定 / HUD 用）。</summary>
        public static T Find<T>() where T : class, IGojoAbility
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] is T t) return t;
            }
            return null;
        }

        private static string DescribeAll()
        {
            var parts = new List<string>();
            for (int i = 0; i < All.Count; i++)
            {
                GojoAbilityBase a = All[i];
                int vk = a.TriggerKey;
                string key = vk > 0 ? GojoKeys.Name(vk) : "被动";
                parts.Add($"{a.DisplayName}[{key}]");
            }
            return string.Join(" ", parts);
        }
    }
}
