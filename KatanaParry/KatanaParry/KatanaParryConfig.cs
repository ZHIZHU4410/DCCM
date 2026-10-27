using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace KatanaParry
{
    /// <summary>
    /// 武士刀（Katana）前两刀带盾反的配置
    /// （存成 coremod/config/KatanaParry.json，可手改）。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母 A~Z / 数字 0~9 / 功能键 F1~F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · 默认全部留空 = 不绑键
    /// </summary>
    public class ParryConfig
    {
        // ---------------------------------------------------------------- 开关

        /// <summary>总开关：关掉 = 整个模组静默（武士刀回到原版）。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        /// <summary>前两刀盾反：武士刀第 1、2 段挥砍期间，敌人打过来会被盾反。</summary>
        public bool EnableParry = true;
        public string KeyToggleParry = "";

        /// <summary>
        /// 反击：把攻击来源（近战怪）击退 + 眩晕。
        /// 对应原版 <c>BaseShield.applyStunAndBumpFromParry()</c>：
        /// 速度减半 → 反向小击退 → <c>setAffectS(8, 眩晕秒数)</c>（8 = stun）。
        /// </summary>
        public bool EnableStun = true;
        public string KeyToggleStun = "";

        /// <summary>盾反反馈：光效 + "Parade !" 文字 + 格挡音效。</summary>
        public bool EnableFx = true;
        public string KeyToggleFx = "";

        /// <summary>
        /// 反击伤害：成功盾反后对**近战来源**打一次"来自英雄"的攻击
        /// （对应原版 <c>BaseShield.shieldCounterAttack</c>：打 tag 13/25/2 后 <c>_AttackUtils.hit</c>）。
        /// </summary>
        public bool EnableCounter = true;
        public string KeyToggleCounter = "";

        /// <summary>弹幕打回去：远程弹丸盾反时把它掉头射回来源（而不是直接消掉）。</summary>
        public bool EnableReflect = true;
        public string KeyToggleReflect = "";

        /// <summary>
        /// 酒桶 / 炸弹弹回：盾反窗口内，附近的**敌方**炸弹（<c>Grenade</c>）与酒桶
        /// （<c>ExplosiveBarrel</c> / <c>LauncherExplosiveBarrel</c>）会被"打回去" ——
        /// 炸弹走原版 <c>Grenade.counter()</c> 造 <c>RepeledGrenade</c>；
        /// 酒桶改成英雄阵营 + <c>setActivatedByHero(true)</c> 并掉头飞回。
        /// </summary>
        public bool EnableBounce = true;
        public string KeyToggleBounce = "";

        // ---------------------------------------------------------------- 数值（菜单滑条）

        /// <summary>
        /// 盾反窗口（秒）。每次第 1/2 段挥砍开始时打开，持续这么久。
        /// 原版武士刀前两段的单段耗时 = charge 0.2 + lockCtrlAfter 0.2 = 0.4s，
        /// 所以默认 0.45 大致覆盖整段挥砍。
        /// </summary>
        public double ParryWindowSec = 0.45;

        /// <summary>反击眩晕时长（秒）。</summary>
        public double StunSec = 1.0;

        /// <summary>
        /// **一级**盾反伤害 = 15（完美格挡带暴击 → 30，因为暴击是基础伤害的 2×）。
        ///
        /// `_AttackData._create(hero, power, tier)` 只是把 tier 记进 `sourceTier`，
        /// 真正的"随卷轴成长"发生在 `_AttackUtils.hit → updateDamages` 里按 `sourceTier` 缩放
        /// （每级约 ×1.15），所以这里填**一级数值**，卷轴越多伤害越高。
        /// </summary>
        public double ParryDamage = 15.0;

        /// <summary>弹幕盾反的伤害系数（默认 0.75 = 75%）：打回去那一发 = 盾反伤害 × 该系数。</summary>
        public double ReflectDamageRatio = 0.75;

        /// <summary>酒桶/炸弹的"盾反范围"（格）：窗口内这个距离内的敌方炸弹/酒桶会被打回去。</summary>
        public double BounceRadiusCells = 2.5;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码解析。</summary>
    public static class ParryKeys
    {
        public static Config<ParryConfig> Config { get; } = new Config<ParryConfig>("KatanaParry");

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
