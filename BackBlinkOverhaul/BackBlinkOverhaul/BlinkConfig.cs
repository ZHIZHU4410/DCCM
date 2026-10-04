using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace BackBlinkOverhaul
{
    /// <summary>
    /// BackBlink（位移）改造的持久化配置，存成 coremod/config/BackBlinkOverhaul.json，可手改。
    ///
    /// 热键写成"按键名"字符串：
    ///   · 单个字母：A ~ Z   · 数字：0 ~ 9   · 功能键：F1 ~ F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · **默认全部留空 = 不绑键**（避免和游戏本体 / 其它模组撞键），想用自己填
    /// </summary>
    public class BlinkConfig
    {
        // ---------------------------------------------------------------- 总开关

        /// <summary>总开关：关掉 = 整个模组静默（传送 / 冲击波 / 连锁全部停用，数值恢复原版）。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        // ---------------------------------------------------------------- 1. 基础传送机制

        /// <summary>1.1.1 无视墙体传送。</summary>
        public bool EnableWallPierce = true;
        public string KeyToggleWallPierce = "";

        /// <summary>1.1.2 无视距离传送（锁敌距离放到极大，整个关卡任意敌人）。</summary>
        public bool EnableInfiniteRange = true;
        public string KeyToggleInfiniteRange = "";

        /// <summary>1.1.3 锁定任意敌人（不限"近距离"）。关掉 = 回到原版的距离内锁敌。</summary>
        public bool EnableAnyEnemy = true;
        public string KeyToggleAnyEnemy = "";

        /// <summary>1.1.4 落点判定与安全位置修正（落点被墙堵住时自动挪到最近的可站立格）。</summary>
        public bool EnableLandingFix = true;
        public string KeyToggleLandingFix = "";

        /// <summary>1.1.5 无目标时的处理：true = 朝面向方向盲闪一段；false = 什么也不做（保留原版 0.5s 小 CD）。</summary>
        public bool EnableFallbackBlink = true;
        public string KeyToggleFallbackBlink = "";

        /// <summary>盲闪（没有锁定到敌人）时是否也结算落地冲击波。</summary>
        public bool ShockwaveOnFallbackBlink = false;

        /// <summary>盲闪距离（格）。</summary>
        public double FallbackBlinkCells = 6.0;

        // ---------------------------------------------------------------- 1.2 冷却与充能

        /// <summary>1.2.1 移除冷却。关掉后按 <see cref="CooldownMult"/> 乘原版冷却。</summary>
        public bool EnableNoCooldown = true;
        public string KeyToggleNoCooldown = "";

        /// <summary>1.2.2 无充能限制 / 无限释放（释放后立刻清空冷却与充能）。</summary>
        public bool EnableInfiniteCharges = true;
        public string KeyToggleInfiniteCharges = "";

        /// <summary>冷却倍率（仅在 <see cref="EnableNoCooldown"/> = false 时有意义）。1.0 = 原版。</summary>
        public double CooldownMult = 1.0;

        /// <summary>1.2.4 防止无限连按导致性能问题：两次传送之间的最小间隔（秒），0 = 不限制。</summary>
        public double MinCastIntervalS = 0.08;

        /// <summary>同一帧内最多结算的连锁次数（递归保护）。</summary>
        public int MaxChainPerFrame = 8;

        // ---------------------------------------------------------------- 1.3 视觉表现

        /// <summary>1.3.1 传送路径彩色拖尾。</summary>
        public bool EnableTrail = true;
        public string KeyToggleTrail = "";

        /// <summary>1.3.2 拖尾颜色（#RRGGBB）。</summary>
        public string TrailColor = "#8A2BE2";

        /// <summary>拖尾粗细。</summary>
        public double TrailThickness = 6.0;

        /// <summary>拖尾透明度 0~1。</summary>
        public double TrailAlpha = 0.85;

        /// <summary>1.3.3 残影 / 粒子 / 起点终点特效。</summary>
        public bool EnableAfterImage = true;
        public string KeyToggleAfterImage = "";

        /// <summary>残影颜色（#RRGGBB）。</summary>
        public string AfterImageColor = "#FFFFFF";

        /// <summary>残影存在时间（秒）。</summary>
        public double AfterImageDurationS = 0.25;

        /// <summary>1.3.4 传送方向与速度感表现（慢动作 + 速度残影 + 冲刺线）。</summary>
        public bool EnableSpeedFeel = true;
        public string KeyToggleSpeedFeel = "";

        /// <summary>慢动作倍率（越小越慢）。</summary>
        public double SlowMoScale = 0.18;

        /// <summary>慢动作时长（秒）。</summary>
        public double SlowMoDurationS = 0.1;

        // ---------------------------------------------------------------- 1.4 音效与反馈

        /// <summary>1.4.1 / 1.4.2 / 1.4.3 音效与震屏总开关。</summary>
        public bool EnableFeedback = true;
        public string KeyToggleFeedback = "";

        /// <summary>传送音效资源路径（res 内的路径）。</summary>
        public string TeleportSfx = "sfx/active/active_phaser.wav";

        /// <summary>落地音效资源路径。</summary>
        public string LandingSfx = "sfx/active/active_phaser.wav";

        /// <summary>音效音量。</summary>
        public double SfxVolume = 0.8;

        /// <summary>1.4.3 屏幕震动强度（0 = 不震）。</summary>
        public double ShakePower = 4.0;

        /// <summary>1.4.3 屏幕震动时长（秒）。</summary>
        public double ShakeDurationS = 0.12;

        // ---------------------------------------------------------------- 2. 落地冲击波 Blink Strike

        /// <summary>2.1 落地冲击波总开关。</summary>
        public bool EnableShockwave = true;
        public string KeyToggleShockwave = "";

        /// <summary>4.2.1 冲击波伤害系数（乘在物品 props.power 上）。</summary>
        public double ShockwaveDamageMult = 1.0;

        /// <summary>4.2.2 冲击波半径（格）。默认 2 格 —— 贴脸爆发，不做大范围清场。</summary>
        public double ShockwaveRadius = 2.0;

        /// <summary>2.2.2 基于暴虐属性缩放（额外加成系数，0 = 不加成）。</summary>
        public double BrutalityScaling = 0.25;

        /// <summary>2.2.3 基于战术属性缩放（额外加成系数，0 = 不加成）。</summary>
        public double TacticScaling = 0.25;

        /// <summary>2.3.2 冲击波是否穿墙（true = 只要在半径内就命中，不做视线判定）。</summary>
        public bool ShockwavePierceWall = true;

        /// <summary>2.3.3 对 Boss 的伤害倍率。</summary>
        public double BossDamageMult = 0.35;

        /// <summary>2.3.3 对精英的伤害倍率。</summary>
        public double EliteDamageMult = 0.7;

        /// <summary>2.3.4 对护盾 / 无敌帧的处理：强制给落地冲击波挂上 IgnoreGlobalShield。
        ///
        /// 物品数据里已经加了 `{ affix: "IgnoreGlobalShield" }` 这条传说词条，
        /// 但原版的 legendAffixes 只在**传说品质**的实例上才生效。
        /// 打开这个开关就一直有效（走原版 `atk.addAffix`，与 KingsSpear 同一条链路）。</summary>
        public bool ForceIgnoreGlobalShield = true;
        public string KeyToggleIgnoreShield = "";

        /// <summary>2.2.4 冲击波伤害使用的 tag（默认为 0 = 无附加标记）。可用于和词条 / 暴击联动。</summary>
        public int ShockwaveTag = 0;

        /// <summary>2.4.1 冲击波特效颜色（#RRGGBB）。</summary>
        public string ShockwaveColor = "#8A2BE2";

        // ---------------------------------------------------------------- 便捷功能（参考 chuansong+shengcheng 的 T 键）

        /// <summary>便捷热键总开关：用 T 键在脚下召唤一个 AllUp（全属性提升）卷轴。</summary>
        public bool EnableScrollHotkey = true;
        public string KeyToggleScrollHotkey = "";

        /// <summary>召唤卷轴用的按键名（默认 T，与 chuansong+shengcheng 一致）。</summary>
        public string SpawnScrollKey = "T";

        /// <summary>召唤的卷轴 id（游戏 Consumable 表里的条目）。</summary>
        public string SpawnScrollId = "AllUp";

        /// <summary>两次召唤之间的最小间隔（秒），防止按住连发刷物品。</summary>
        public double SpawnScrollIntervalS = 0.35;

        /// <summary>召唤时卷轴的抛出方向（-1 向后 / 0 原地落下 / 1 向前）。</summary>
        public double SpawnScrollDir = 0.0;

        // ---------------------------------------------------------------- 3. 连锁传送 Chain Blink

        /// <summary>3. 连锁传送总开关。</summary>
        public bool EnableChain = true;
        public string KeyToggleChain = "";

        /// <summary>3.1.3 连锁时间窗口（秒）。在窗口内击杀敌人 → 再次免费释放。</summary>
        public double ChainWindowS = 2.0;

        /// <summary>3.4.4 连锁上限（0 = 无上限）。</summary>
        public int ChainMaxStacks = 5;

        /// <summary>3.4.2 连续击杀奖励：每层连锁给冲击波伤害的加成（乘算，0.15 = +15%）。</summary>
        public double ChainDamageBonusPerStack = 0.15;

        /// <summary>3.4.4 连锁伤害衰减：超过此层数后每层衰减系数（0.9 = 每层 ×0.9）。</summary>
        public double ChainDamageFalloff = 0.9;

        /// <summary>3.5.1 可连锁状态提示（HUD + 特效）。</summary>
        public bool EnableChainHud = true;
        public string KeyToggleChainHud = "";
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码的解析（与 DamageAuraBoost 的 AuraKeys 同一套做法）。</summary>
    public static class BlinkKeys
    {
        public static Config<BlinkConfig> Config { get; } = new Config<BlinkConfig>("BackBlinkOverhaul");

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
