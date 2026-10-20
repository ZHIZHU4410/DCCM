using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace ThrowingKnifeOverhaul
{
    /// <summary>
    /// 飞刀（ThrowingKnife）强化的持久化配置（存成 coremod/config/ThrowingKnifeOverhaul.json，可手改）。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母：A ~ Z   · 数字：0 ~ 9   · 功能键：F1 ~ F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · **默认全部留空 = 不绑键**（避免和游戏本体 / 其它模组撞键），想用自己填
    /// </summary>
    public class KnifeConfig
    {
        // ---------------------------------------------------------------- 总开关
        //
        // EnableMod 一关，**所有功能全部停用**（恢复原版手感），
        // 但它自己仍然可用（热键 + 选项菜单里的复选框），否则就没法从游戏里开回来。
        //
        // 调用点统一用 `KnifeFeatures.IsOn(KnifeFeature.Xxx)` 判断，别直接读这里的字段。

        /// <summary>总开关：关掉 = 整个模组静默（飞刀回到原版）。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        /// <summary>飞刀无视墙体（Bullet.ignoreWalls/ignoreOneWays + 跳过撞墙处理）。</summary>
        public bool EnableWallPierce = true;
        public string KeyToggleWallPierce = "";

        /// <summary>攻速 ×N（出招动画/蓄力 ×N，武器冷却 ÷N）。</summary>
        public bool EnableAttackSpeed = true;
        public string KeyToggleAttackSpeed = "";

        /// <summary>锁定追击：飞刀每帧朝最近敌人限速转向。</summary>
        public bool EnableHoming = true;
        public string KeyToggleHoming = "";

        /// <summary>追击射程 ×N（原版 maxDist 只有 5.5 格，不放大追不到人）。</summary>
        public bool EnableHomingRange = true;
        public string KeyToggleHomingRange = "";

        /// <summary>无限飞刀（每次投掷前把弹药补满；原版只有 8 把，攻速 ×3 会瞬间打空）。</summary>
        public bool EnableInfiniteAmmo = true;
        public string KeyToggleInfiniteAmmo = "";

        // ---------------------------------------------------------------- 可调数值（菜单滑条）

        /// <summary>
        /// 攻速倍率。同时作用在两处：
        ///   · <c>Weapon.prepare(attackSpeed)</c> —— 蓄力/出招动画（原版 charge 0.1s）；
        ///   · 武器技能的 <c>cooldownSpeedMul</c> —— 原版 <c>coolDown 0.25s</c> 是按
        ///     `coolDownF -= tmod * getCooldownSpeed()` 递减的，**不吃 attackSpeed**，
        ///     不单独放大这一项的话，射速会被那 0.25s 卡住（最多 4 发/秒）。
        /// </summary>
        public double AttackSpeedMult = 3.0;

        /// <summary>追击转向速度（度/帧）。20°/帧 ≈ 9 帧转 180°；越大越"贴脸"。</summary>
        public double HomingTurnDegPerFrame = 20.0;

        /// <summary>追击射程倍率（乘在飞刀的 Bullet.maxDist 上；原版 5.5 格 → 默认 13.75 格）。</summary>
        public double RangeMult = 2.5;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码的解析（与 AdeleScytheOverhaul 的 AdeleKeys 同一套做法）。</summary>
    public static class KnifeKeys
    {
        public static Config<KnifeConfig> Config { get; } = new Config<KnifeConfig>("ThrowingKnifeOverhaul");

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
