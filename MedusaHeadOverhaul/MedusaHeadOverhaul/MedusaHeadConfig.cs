using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace MedusaHeadOverhaul
{
    /// <summary>
    /// MedusaHead（美杜莎的头）加强 —— 配置
    /// （存成 coremod/config/MedusaHeadOverhaul.json，可手改）。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母 A~Z / 数字 0~9 / 功能键 F1~F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · 默认全部留空 = 不绑键
    /// </summary>
    public class MedusaConfig
    {
        // ---------------------------------------------------------------- 开关

        /// <summary>总开关：关掉 = 美杜莎的头回到原版。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        /// <summary>爆炸范围 × N（只在爆炸那一刻改 area.width，滚距不受影响）。</summary>
        public bool EnableRadius = true;
        public string KeyToggleRadius = "";

        /// <summary>翻滚距离 × N（改 itemInf.props.range；默认 1.0 = 原版）。</summary>
        public bool EnableRollRange = true;
        public string KeyToggleRollRange = "";

        /// <summary>石化时长 × N（拦 setAffectS 的 affect 46）。</summary>
        public bool EnablePetrify = true;
        public string KeyTogglePetrify = "";

        /// <summary>Boss 也能被石化：无视目标对 affect 46 的抗性。</summary>
        public bool EnableBossPetrify = true;
        public string KeyToggleBossPetrify = "";

        /// <summary>爆炸无视墙体：爆炸后用"纯距离"再补一遍石化，让隔墙的敌人也吃到。</summary>
        public bool EnableWallIgnore = true;
        public string KeyToggleWallIgnore = "";

        // ---------------------------------------------------------------- 数值（菜单滑条）

        /// <summary>
        /// 爆炸范围倍数（默认 5）。
        /// 原版：strikeChain[1].area.width = 10 格（直径），
        /// trigger() 里半径 px = 0.5 * area.width * 24 = 120px = 5 格。
        /// 所以 ×5 → 半径 25 格（差不多半个房间）。
        /// </summary>
        public double RadiusMultiplier = 5.0;

        /// <summary>翻滚距离倍数（默认 1.0 = 原版 20 格）。嫌扔太远就往下调。</summary>
        public double RollRangeMultiplier = 1.0;

        /// <summary>
        /// 石化时长倍数（默认 10）。
        /// 原版：onHit 里 <c>e.setAffectS(46, get_curSkillInf().props.duration)</c>，
        /// 投掷段（strikeChain[0]）的 props.duration = 2.5s → ×10 = 25s。
        /// 注意这个值只乘"挂到目标身上的 affect 时长"，**不会**影响武器 CD
        /// （CD 用的是 strikeChain[1].props.duration，故意没动）。
        /// </summary>
        public double PetrifyMultiplier = 10.0;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码解析。</summary>
    public static class MedusaKeys
    {
        public static Config<MedusaConfig> Config { get; } = new Config<MedusaConfig>("MedusaHeadOverhaul");

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
        public static int Resolve(string name, int fallback)
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

            if (s.StartsWith("F", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(s.Substring(1), out int fn) && fn >= 1 && fn <= 12)
            {
                return 0x70 + (fn - 1);
            }

            if (int.TryParse(s, out int dec) && dec > 0 && dec < 256) return dec;

            return fallback;
        }
    }
}
