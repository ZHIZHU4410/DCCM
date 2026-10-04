#nullable disable
using System;
using System.Collections.Generic;

namespace GojoLimitless
{
    /// <summary>
    /// 配置里的按键名 → Win32 虚拟键码。
    ///
    /// ============================ 为什么回到"硬绑" ============================
    /// v5 试过"直接读原版动作绑定（<c>dc.tool.BindingProfiles</c>）"：实机下**读得到
    /// 但按键检测不动**（诊断日志里所有动作恒为"松开"），排查成本高于收益。
    /// 所以回到**模组自己存键名 + 直接轮询 <c>GetAsyncKeyState</c>** ——
    /// 这条链路在 v1~v4 和仓库里其它模组上都是验证过可用的。
    ///
    /// 支持：
    ///   · 单个字母 A~Z / 数字 0~9
    ///   · 功能键 F1~F12
    ///   · 命名键 Space / Tab / Enter / Esc / Shift / Ctrl / Alt /
    ///            Left / Right / Up / Down / Home / End / PageUp / PageDown /
    ///            Insert / Delete / NumPad0~9 /
    ///            Minus / Equals / LeftBracket / RightBracket / Backslash /
    ///            Semicolon / Quote / Comma / Period / Slash / Backquote
    ///   · 直接写十六进制码 "0x51"
    ///   · 空字符串 = 不绑定
    /// </summary>
    public static class GojoKeys
    {
        // ---------------------------------------------------------------- 常用虚拟键码

        public const int Space = 0x20;
        public const int Tab = 0x09;
        public const int Enter = 0x0D;
        public const int Escape = 0x1B;
        public const int Shift = 0x10;
        public const int Ctrl = 0x11;
        public const int Alt = 0x12;
        public const int Left = 0x25;
        public const int Up = 0x26;
        public const int Right = 0x27;
        public const int Down = 0x28;
        public const int Back = 0x08;

        /// <summary>
        /// 死亡细胞里默认空着、适合拿来当模组热键的键（用户实测可用）：
        /// <c>J K L U I O H</c>，再加上 F1~F12 那一片。
        /// </summary>
        public const string HelpText =
            "推荐 J / K / L / U / I / O / H 或 F1-F12；也支持 A-Z、0-9、Space、方向键、标点、0xNN";

        private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SPACE"] = Space,
            ["TAB"] = Tab,
            ["ENTER"] = Enter,
            ["RETURN"] = Enter,
            ["ESC"] = Escape,
            ["ESCAPE"] = Escape,
            ["SHIFT"] = Shift,
            ["CTRL"] = Ctrl,
            ["CONTROL"] = Ctrl,
            ["ALT"] = Alt,
            ["BACKSPACE"] = Back,
            ["LEFT"] = Left,
            ["UP"] = Up,
            ["RIGHT"] = Right,
            ["DOWN"] = Down,
            ["INSERT"] = 0x2D,
            ["DELETE"] = 0x2E,
            ["DEL"] = 0x2E,
            ["HOME"] = 0x24,
            ["END"] = 0x23,
            ["PAGEUP"] = 0x21,
            ["PAGEDOWN"] = 0x22,
            ["MINUS"] = 0xBD,
            ["EQUALS"] = 0xBB,
            ["LEFTBRACKET"] = 0xDB,
            ["RIGHTBRACKET"] = 0xDD,
            ["BACKSLASH"] = 0xDC,
            ["SEMICOLON"] = 0xBA,
            ["QUOTE"] = 0xDE,
            ["COMMA"] = 0xBC,
            ["PERIOD"] = 0xBE,
            ["SLASH"] = 0xBF,
            ["BACKQUOTE"] = 0xC0,
            ["NUMPAD0"] = 0x60,
            ["NUMPAD1"] = 0x61,
            ["NUMPAD2"] = 0x62,
            ["NUMPAD3"] = 0x63,
            ["NUMPAD4"] = 0x64,
            ["NUMPAD5"] = 0x65,
            ["NUMPAD6"] = 0x66,
            ["NUMPAD7"] = 0x67,
            ["NUMPAD8"] = 0x68,
            ["NUMPAD9"] = 0x69,
        };

        /// <summary>解析按键名。失败返回 <paramref name="fallback"/>。</summary>
        public static int Parse(string name, int fallback)
        {
            if (string.IsNullOrWhiteSpace(name)) return fallback;
            string s = name.Trim().ToUpperInvariant();

            // 十六进制码 0xNN
            if (s.Length >= 3 && s.StartsWith("0X", StringComparison.Ordinal)
                && int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out int hex))
            {
                return hex;
            }

            // 单字符 A-Z / 0-9
            if (s.Length == 1)
            {
                char c = s[0];
                if (c >= 'A' && c <= 'Z') return c;
                if (c >= '0' && c <= '9') return c;
            }

            // F1~F12
            if (s.Length >= 2 && s[0] == 'F'
                && int.TryParse(s.Substring(1), out int fn) && fn >= 1 && fn <= 12)
            {
                return 0x70 + (fn - 1);
            }

            if (Named.TryGetValue(s, out int vk)) return vk;
            return fallback;
        }

        /// <summary>解析"可为空"的按键名：空串返回 0（= 不绑定）。</summary>
        public static int ParseOptional(string name) => Parse(name, 0);

        /// <summary>虚拟键码 → 显示名（HUD / 菜单里用，和配置里能写的名字一致）。</summary>
        public static string Name(int vk)
        {
            if (vk <= 0) return "-";
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x70 + 1);
            if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);

            switch (vk)
            {
                case Space: return "Space";
                case Tab: return "Tab";
                case Enter: return "Enter";
                case Escape: return "Esc";
                case Back: return "Back";
                case Shift: return "Shift";
                case Ctrl: return "Ctrl";
                case Alt: return "Alt";
                case Left: return "Left";
                case Up: return "Up";
                case Right: return "Right";
                case Down: return "Down";
            }

            foreach (var kv in Named)
            {
                if (kv.Value == vk && kv.Key.Length > 1) return kv.Key;
            }
            return "0x" + vk.ToString("X2");
        }

        /// <summary>
        /// 解析配置里的键名，**顺带把这个键加进 <see cref="Input"/> 的跟踪集**并返回键码。
        /// 统一走这个入口，就不会出现"配置改了但那个键没被轮询"的问题。
        /// </summary>
        public static int Bind(string name, int fallback)
        {
            int vk = Parse(name, fallback);
            if (vk > 0) Input.EnsureKey(vk);
            return vk;
        }
    }
}
