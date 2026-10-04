using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace WingsNoFall
{
    /// <summary>
    /// 乌鸦之翼（Wings / Ailes de Corbeau）「不再自动下落」的持久化配置
    /// （存成 coremod/config/WingsNoFall.json，可手改；也可在 游戏内「选项 → 模组 → 乌鸦之翼强化」里改）。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母：A ~ Z   · 数字：0 ~ 9   · 功能键：F1 ~ F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · **默认全部留空 = 不绑键**（避免和游戏本体 / 其它模组撞键），想用自己填
    /// </summary>
    public class WingsConfig
    {
        // ---------------------------------------------------------------- 总开关
        //
        // EnableMod 一关，**所有功能全部停用**（恢复成原版：超过悬停高度就会被拽回去），
        // 但它自己仍然可用（热键 + 选项菜单里的复选框），否则就没法从游戏里开回来。

        /// <summary>总开关：关掉 = 整个模组静默（恢复原版下落行为）。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        /// <summary>
        /// 不再自动下落：翅膀生效期间，只要没有按"下"，就绝不产生向下的速度
        /// （原版会把你往"脚下地面往上 3 格"的悬停高度拽，飞高一点就掉）。
        /// </summary>
        public bool EnableNoFall = true;
        public string KeyToggleNoFall = "";

        /// <summary>
        /// 按住"上"自由爬升：原版爬升被 targetCy（悬停高度）卡死，飞不高；
        /// 打开后按住"上"就会以 AscendSpeed 的速度一直往上飞，想多高就多高。
        /// </summary>
        public bool EnableFreeAscend = true;
        public string KeyToggleFreeAscend = "";

        /// <summary>
        /// 自由爬升速度（单位与实体 dy 相同；Hero 起跳冲量是 -0.4，所以 0.3 已经很快了）。
        /// 选项菜单里有滑条可调；0.05 ~ 2.0。
        /// </summary>
        public double AscendSpeed = 0.3;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码的解析（与 DamageAuraBoost 的 AuraKeys 同一套做法）。</summary>
    public static class WingsKeys
    {
        public static Config<WingsConfig> Config { get; } = new Config<WingsConfig>("WingsNoFall");

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
