using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace DamageAuraBoost
{
    /// <summary>
    /// 撕裂光环强化的持久化配置（存成 coremod/config/DamageAuraBoost.json，可手改）。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母：A ~ Z   · 数字：0 ~ 9   · 功能键：F1 ~ F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · **默认全部留空 = 不绑键**（避免和游戏本体 / 其它模组撞键），想用自己填
    /// </summary>
    public class AuraConfig
    {
        // ---------------------------------------------------------------- 总开关
        //
        // EnableMod 一关，**所有功能全部停用**（数值还原成原版 + 不再触发连击），
        // 但它自己仍然可用（热键 + 选项菜单里的复选框），否则就没法从游戏里开回来。
        //
        // 调用点统一用 `AuraFeatures.IsOn(AuraFeature.Xxx)` 判断，别直接读这里的字段。

        /// <summary>总开关：关掉 = 整个模组静默（数值恢复原版，连击不再触发）。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        /// <summary>
        /// 数值强化：范围 ×2 / 攻速 ×4（单次伤害不变）/ 持续时间 ×4 / 冷却 12s → 4s。
        /// 数值来自 res.pak 数据补丁，本开关在运行时把物品数据切回原版值（重新施放光环后生效）。
        /// </summary>
        public bool EnableStats = true;
        public string KeyToggleStats = "";

        /// <summary>光环命中触发 P_DmgKill（连击）效果（叠层 + 右上角连击 UI + 伤害加成）。</summary>
        public bool EnableCombo = true;
        public string KeyToggleCombo = "";
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码的解析（与 ChronoBlade 的 ChronoKeys 同一套做法）。</summary>
    public static class AuraKeys
    {
        public static Config<AuraConfig> Config { get; } = new Config<AuraConfig>("DamageAuraBoost");

        private static readonly Dictionary<string, int> Special = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = 0x20,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Return"] = 0x0D,
            ["Escape"] = 0x1B,
            ["Shift"] = 0x10,
            ["Ctrl"] = 0x11,
            ["Control"] = 0x11,
            ["Alt"] = 0x12,
            ["LeftBracket"] = 0xDB,
            ["RightBracket"] = 0xDD,
            ["Semicolon"] = 0xBA,
            ["Quote"] = 0xDE,
            ["Comma"] = 0xBC,
            ["Period"] = 0xBE,
            ["Slash"] = 0xBF,
            ["Backslash"] = 0xDC,
            ["Minus"] = 0xBD,
            ["Equals"] = 0xBB,
            ["Backquote"] = 0xC0,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27,
        };

        /// <summary>把配置里的按键名解析成虚拟键码；无法识别时返回 fallback。</summary>
        public static int Resolve(string? name, int fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            string s = name.Trim();

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out int hex))
            {
                return hex;
            }

            if (Special.TryGetValue(s, out int vk)) return vk;

            if (s.Length == 1)
            {
                char c = char.ToUpperInvariant(s[0]);
                if (c >= 'A' && c <= 'Z') return c;
                if (c >= '0' && c <= '9') return c;
            }

            if ((s.StartsWith("F", StringComparison.OrdinalIgnoreCase))
                && int.TryParse(s.Substring(1), out int fn) && fn >= 1 && fn <= 12)
            {
                return 0x70 + (fn - 1);
            }

            if (int.TryParse(s, out int dec) && dec > 0 && dec < 256) return dec;

            return fallback;
        }
    }
}
