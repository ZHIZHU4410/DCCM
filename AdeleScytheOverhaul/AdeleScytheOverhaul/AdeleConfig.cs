using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace AdeleScytheOverhaul
{
    /// <summary>
    /// 死亡镰刀强化的持久化配置（存成 coremod/config/AdeleScytheOverhaul.json，可手改）。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母：A ~ Z   · 数字：0 ~ 9   · 功能键：F1 ~ F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · **默认全部留空 = 不绑键**（避免和游戏本体 / 其它模组撞键），想用自己填
    /// </summary>
    public class AdeleConfig
    {
        // ---------------------------------------------------------------- 总开关
        //
        // EnableMod 一关，**所有功能全部停用**（数值还原成原版 + 只打第一段 + 不穿墙），
        // 但它自己仍然可用（热键 + 选项菜单里的复选框），否则就没法从游戏里开回来。
        //
        // 调用点统一用 `AdeleFeatures.IsOn(AdeleFeature.Xxx)` 判断，别直接读这里的字段。

        /// <summary>总开关：关掉 = 整个模组静默（数值恢复原版、连击恢复原版、不再穿墙）。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        /// <summary>只保留最后一击：连击序号永远钉在最后一段（等于原版 LastCycleSpam 词条的效果）。</summary>
        public bool EnableLastHit = true;
        public string KeyToggleLastHit = "";

        /// <summary>范围 ×3（最后一段的攻击框，数据补丁写进 res.pak，本开关在运行时切回原版）。</summary>
        public bool EnableRange = true;
        public string KeyToggleRange = "";

        /// <summary>伤害 ×3（最后一段的 power，数据补丁写进 res.pak，本开关在运行时切回原版）。</summary>
        public bool EnableDamage = true;
        public string KeyToggleDamage = "";

        /// <summary>攻速 ×3（走原版 Weapon.prepare 的 attackSpeed，即游戏的"攻击速度"倍率）。</summary>
        public bool EnableAttackSpeed = true;
        public string KeyToggleAttackSpeed = "";

        /// <summary>
        /// 取消攻击后摇：把 strikeChain[3].lockCtrlAfter（原版 0.37s）抹成 0，
        /// 于是 <c>OldSkill.execute()</c> 走 <c>hero.cancelSkillControlLock()</c>
        /// 而不是 <c>hero.lockControlFromSkill(0.37)</c> —— 挥完立刻能再挥，感受不到攻击间隔。
        /// 原版那个 0.37s 就是"攻击间隔"的主要来源（它不受攻速倍率影响）。
        /// </summary>
        public bool EnableNoRecovery = true;
        public string KeyToggleNoRecovery = "";

        /// <summary>无视墙体：命中判定只算攻击框几何，跳过墙体/视线/门与地形阻挡。</summary>
        public bool EnableWallPierce = true;
        public string KeyToggleWallPierce = "";

        /// <summary>刀光特效跟着放大（纯视觉，不影响判定；默认开启，让"范围 ×3"看得出来）。</summary>
        public bool EnableFxScale = true;
        public string KeyToggleFxScale = "";

        /// <summary>刀光放大倍率（选项菜单里是滑条「刀光倍率」）。</summary>
        public double FxScale = 3.0;

        /// <summary>
        /// 攻速倍率（选项菜单里是滑条「攻速倍率」）。
        /// 直接乘在 <c>Weapon.prepare(attackSpeed)</c> 上，也就是游戏自己的攻击速度倍率；
        /// 同时作为"追加判定间隔 props.tick"的除数。
        /// 后摇已经被「取消攻击后摇」抹平，这里再调大就是进一步压缩出招动画本身的时间。
        /// </summary>
        public double SpeedMult = 3.0;

        /// <summary>
        /// 刀光水平微调（像素）。正数 = 人物朝向的**前方**（精灵挂在被朝向镜像的英雄精灵下）。
        ///
        /// 默认值 43 是**实测**出来的：对 res/atlas/fxWeapon.atlas + fxWeapon-*.png 做
        /// 「按 alpha 加权的像素质心」（不是包围盒中心 —— 镰刀月牙的包围盒中心明显偏上偏后），
        /// 得到原版 FXAtkReaperToolD 的可见视觉质心平均落在精灵原点身后 43px、上方 60px。
        /// 模组逐帧做了"保持原版构图"的补偿（用包围盒中心抵消缩放），
        /// 再加上这两个常量偏移，放大后的刀光**视觉中心**就落回人物身上
        /// （14 个可见帧的质心离散只有 ±16px，所以每一帧看都在人物身上）。
        /// 想让特效整体往后/往前挪 → 调这两条滑条；
        /// 想让特效保持原版构图（只是变大）→ 把两条都拉到 0。
        /// </summary>
        public double FxOffsetX = 43.0;

        /// <summary>刀光垂直微调（像素）。正数 = 向下；默认 60 同上（把视觉中心拉回人物）。</summary>
        public double FxOffsetY = 60.0;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码的解析（与 DamageAuraBoost 的 AuraKeys 同一套做法）。</summary>
    public static class AdeleKeys
    {
        public static Config<AdeleConfig> Config { get; } = new Config<AdeleConfig>("AdeleScytheOverhaul");

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
