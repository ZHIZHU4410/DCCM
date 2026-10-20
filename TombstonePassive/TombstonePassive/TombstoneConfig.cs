using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace TombstonePassive
{
    /// <summary>
    /// 墓碑（Tombstone）被动灾厄的持久化配置
    /// （存成 coremod/config/TombstonePassive.json，可手改）。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母 A~Z / 数字 0~9 / 功能键 F1~F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · 默认全部留空 = 不绑键
    /// </summary>
    public class DoomConfig
    {
        // ---------------------------------------------------------------- 开关

        /// <summary>总开关：关掉 = 整个模组静默（墓碑回到原版）。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        /// <summary>
        /// 被动灾厄：带着墓碑**每移动一格**就以英雄为中心触发一次灾厄
        /// （不再需要打第三下平 A）。
        /// </summary>
        public bool EnablePassive = true;
        public string KeyTogglePassive = "";

        /// <summary>
        /// 灾厄伤害不递减。
        ///
        /// 原版墓碑第三下用的是 <c>hero.getDiminishingFactor(69, 1, props.limit=3, null)</c>
        /// （GamePseudocode/dc/Entity.cs:4105），它有个硬性规则：
        /// <c>if (1 + 已用次数 >= absoluteMaxUses) return 0.0;</c>
        /// ——也就是**同一个 5 秒窗口内第 3 次起伤害直接归零**。
        /// 这个机制是为"三连击"设计的；改成每格触发后，几乎每次都是第 3 次以后 → 伤害恒为 0。
        /// 打开本开关后，key=69 的递减因子直接由「灾厄伤害系数」决定（不动原版其它递减）。
        /// </summary>
        public bool EnableFullDamage = true;
        public string KeyToggleFullDamage = "";

        /// <summary>视觉附加项：英雄脚下也砸一块墓碑（默认关，否则走路会刷一地墓碑）。</summary>
        public bool EnableHeroTombstone = false;
        public string KeyToggleHeroTombstone = "";

        // ---------------------------------------------------------------- 数值（菜单滑条）

        /// <summary>
        /// 灾厄半径（格）。原版第三下用的是
        /// <c>get_curSkillInf().props.range</c> = strikeChain[2].props.range = **12 格**。
        /// 这里默认就用原版数值，方便对齐手感。
        /// </summary>
        public double RadiusCells = 12.0;

        /// <summary>
        /// 最小触发间隔（秒）。0 = 严格"每移动一格就触发一次"；
        /// 拉大可以降频（也顺带把灾厄伤害密度压下来）。
        /// </summary>
        public double MinIntervalSec = 0.0;

        /// <summary>
        /// 灾厄伤害系数（只在「灾厄伤害不递减」打开时生效）。
        /// 1.0 = 原版满伤害（墓碑 item.props.power = 160 × 1.0）；
        /// 因为改成被动后触发频率高得多，默认给 0.5。
        /// </summary>
        public double DamageFactor = 0.5;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码解析。</summary>
    public static class DoomKeys
    {
        public static Config<DoomConfig> Config { get; } = new Config<DoomConfig>("TombstonePassive");

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
