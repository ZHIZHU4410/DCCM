using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DamageAuraBoost
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum AuraFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>数值强化（范围 / 攻速 / 持续时间 / 冷却）。</summary>
        Stats,

        /// <summary>光环命中触发连击（P_DmgKill）效果。</summary>
        Combo,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名"集中在一张表里（仿 ChronoBlade 的 ChronoFeatures）。
    ///
    /// 用法（调用点）：
    ///
    ///     if (!AuraFeatures.IsOn(AuraFeature.Combo)) return;
    ///
    /// **不要**直接去读 `AuraConfig.EnableXxx` —— 那样会绕过总开关。
    ///
    /// 总开关语义：
    ///   · `EnableMod = false` → `IsOn()` 对**除 Mod 自己以外**的一切返回 false；
    ///   · 但 Mod 自己始终可切换（热键 + 菜单复选框都照常），否则就没法从游戏里开回来。
    ///
    /// 热键：每个功能一个 `KeyToggleXxx` 配置，**默认全部留空（不绑键）**；
    ///       空字符串 = 不监听，所以默认不会和游戏本体 / 其它模组撞键。
    /// </summary>
    public static class AuraFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly AuraFeature[] All =
        {
            AuraFeature.Mod,
            AuraFeature.Stats,
            AuraFeature.Combo,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(AuraFeature f) => f switch
        {
            AuraFeature.Mod => "总开关",
            AuraFeature.Stats => "数值强化（范围/攻速/持续/冷却）",
            AuraFeature.Combo => "光环命中触发连击",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(AuraFeature f) => f switch
        {
            AuraFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            AuraFeature.Stats => "范围×2 / 攻速×4 / 持续×4 / 冷却4秒（重新施放光环后生效）",
            AuraFeature.Combo => "命中叠连击层数 + 右上角连击 UI + 伤害加成",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(AuraFeature f)
        {
            try
            {
                var c = AuraKeys.Config.Value;
                return f switch
                {
                    AuraFeature.Mod => c.EnableMod,
                    AuraFeature.Stats => c.EnableStats,
                    AuraFeature.Combo => c.EnableCombo,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(AuraFeature f, bool on)
        {
            try
            {
                var c = AuraKeys.Config.Value;
                switch (f)
                {
                    case AuraFeature.Mod: c.EnableMod = on; break;
                    case AuraFeature.Stats: c.EnableStats = on; break;
                    case AuraFeature.Combo: c.EnableCombo = on; break;
                }
                AuraKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(AuraFeature f)
        {
            try
            {
                var c = AuraKeys.Config.Value;
                return f switch
                {
                    AuraFeature.Mod => c.KeyToggleMod,
                    AuraFeature.Stats => c.KeyToggleStats,
                    AuraFeature.Combo => c.KeyToggleCombo,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。**统一入口** —— 调用点一律用它，别直接读配置字段，
        /// 否则总开关就形同虚设。
        ///
        /// 总开关特例：`AuraFeature.Mod` 自己不受总开关影响（否则关掉就再也开不回来了）。
        /// </summary>
        public static bool IsOn(AuraFeature f)
        {
            bool own = RawGet(f);
            if (f == AuraFeature.Mod) return own;

            try { if (!AuraKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        /// <summary>每个热键上一次的按下状态（边沿检测，按住不会连发）。</summary>
        private static readonly Dictionary<AuraFeature, bool> _wasDown = new();

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
                    int vk = AuraKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在"刚按下"那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[DamageAuraBoost] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态，避免"上一关按着、下一关一进来就触发"。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
