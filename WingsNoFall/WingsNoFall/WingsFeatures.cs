using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WingsNoFall
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum WingsFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>翅膀生效期间不再自动下落（没按"下"就绝不往下掉）。</summary>
        NoFall,

        /// <summary>按住"上"可以无限爬升（突破原版的悬停高度上限）。</summary>
        FreeAscend,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名"集中在一张表里（与 DamageAuraBoost 的 AuraFeatures 同一套做法）。
    ///
    /// 用法（调用点）：
    ///
    ///     if (!WingsFeatures.IsOn(WingsFeature.NoFall)) return;
    ///
    /// **不要**直接去读 `WingsConfig.EnableXxx` —— 那样会绕过总开关。
    ///
    /// 总开关语义：
    ///   · `EnableMod = false` → `IsOn()` 对**除 Mod 自己以外**的一切返回 false；
    ///   · 但 Mod 自己始终可切换（热键 + 菜单复选框都照常），否则就没法从游戏里开回来。
    /// </summary>
    public static class WingsFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly WingsFeature[] All =
        {
            WingsFeature.Mod,
            WingsFeature.NoFall,
            WingsFeature.FreeAscend,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(WingsFeature f) => f switch
        {
            WingsFeature.Mod => "总开关",
            WingsFeature.NoFall => "不再自动下落",
            WingsFeature.FreeAscend => "按住“上”自由爬升",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(WingsFeature f) => f switch
        {
            WingsFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            WingsFeature.NoFall => "飞行中只要不按方向下，就不会被拽回悬停高度",
            WingsFeature.FreeAscend => "原版飞不过悬停高度；打开后按住“上”想飞多高飞多高",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(WingsFeature f)
        {
            try
            {
                var c = WingsKeys.Config.Value;
                return f switch
                {
                    WingsFeature.Mod => c.EnableMod,
                    WingsFeature.NoFall => c.EnableNoFall,
                    WingsFeature.FreeAscend => c.EnableFreeAscend,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(WingsFeature f, bool on)
        {
            try
            {
                var c = WingsKeys.Config.Value;
                switch (f)
                {
                    case WingsFeature.Mod: c.EnableMod = on; break;
                    case WingsFeature.NoFall: c.EnableNoFall = on; break;
                    case WingsFeature.FreeAscend: c.EnableFreeAscend = on; break;
                }
                WingsKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(WingsFeature f)
        {
            try
            {
                var c = WingsKeys.Config.Value;
                return f switch
                {
                    WingsFeature.Mod => c.KeyToggleMod,
                    WingsFeature.NoFall => c.KeyToggleNoFall,
                    WingsFeature.FreeAscend => c.KeyToggleFreeAscend,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。**统一入口** —— 调用点一律用它，别直接读配置字段，
        /// 否则总开关就形同虚设。
        ///
        /// 总开关特例：`WingsFeature.Mod` 自己不受总开关影响（否则关掉就再也开不回来了）。
        /// </summary>
        public static bool IsOn(WingsFeature f)
        {
            bool own = RawGet(f);
            if (f == WingsFeature.Mod) return own;

            try { if (!WingsKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        /// <summary>每个热键上一次的按下状态（边沿检测，按住不会连发）。</summary>
        private static readonly Dictionary<WingsFeature, bool> _wasDown = new();

        /// <summary>
        /// 每帧调用（放在 IOnHeroUpdate 里）：检查所有已绑定的热键，按下就切换对应功能。
        /// 边沿检测 —— 只在"刚按下"那一帧动作。
        /// </summary>
        public static void PollHotkeys(Action<string> log)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = WingsKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在"刚按下"那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[WingsNoFall] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态，避免"上一关按着、下一关一进来就触发"。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
