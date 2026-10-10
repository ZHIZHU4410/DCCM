#nullable disable

using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace Sukuna
{
    /// <summary>
    /// 宿傩模组的持久化配置（存成 coremod/config/Sukuna.json，可手改；也可在游戏内
    /// 「选项 → 模组 → 宿傩·斩击」里切换）。
    ///
    /// 热键写成"按键名"字符串（与 DamageAuraBoost 的写法完全一致）：
    ///   · 单个字母：A ~ Z   · 数字：0 ~ 9   · 功能键：F1 ~ F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt / LeftBracket / RightBracket /
    ///              Semicolon / Quote / Comma / Period / Slash / Backslash / Minus / Equals /
    ///              Backquote / Up / Down / Left / Right
    ///   · 也支持十六进制虚拟键码，例如 "0x77"
    ///   · 开关热键默认**全部留空 = 不绑键**（避免和游戏本体 / 其它模组撞键）；
    ///     只有「伏魔御厨子」的释放键默认绑 V（否则领域没法在游戏里放出来）。
    /// </summary>
    public class SukunaConfig
    {
        // ---------------------------------------------------------------- 总开关
        //
        // EnableMod 一关 → 所有功能全部停用（不再追加斩击、不再能用领域），
        // 但它自己仍然可切换（热键 + 选项菜单里的复选框），否则关掉就开不回来了。
        //
        // 调用点统一用 `SukunaFeatures.IsOn(SukunaFeature.Xxx)` 判断，别直接读这里的字段。

        /// <summary>总开关：关掉 = 整个模组静默。</summary>
        public bool EnableMod = true;
        public string KeyToggleMod = "";

        // ---------------------------------------------------------------- 解 / 捌 / 伏魔御厨子

        /// <summary>「解」：女王细剑命中后，在目标周围随机位置追加多段斩击。</summary>
        public bool EnableDismantle = true;
        public string KeyToggleDismantle = "";

        /// <summary>「捌」：对精英 / 大型 / 高血量目标追加一条贯穿全场的斩线。</summary>
        public bool EnableCleave = true;
        public string KeyToggleCleave = "";

        /// <summary>「伏魔御厨子」：领域展开，范围内全体敌人被持续斩击。</summary>
        public bool EnableDomain = true;
        public string KeyToggleDomain = "";

        /// <summary>「伏魔御厨子」的释放键。留空 = 不放领域。</summary>
        public string KeyCastDomain = "V";

        /// <summary>
        /// 无视墙体：女王细剑的**挥砍判定**也走一遍"只按距离、不看遮挡"的兜底判定，
        /// 于是墙后的敌人照样吃斩击；斩击本体（queenStrike）本来就不做墙体检测。
        /// </summary>
        public bool EnableWallIgnore = true;
        public string KeyToggleWallIgnore = "";

        /// <summary>
        /// 只保留斩击伤害：吞掉挥砍本身那一次 _AttackUtils.hit（原版 hitFromWeapon 里的普通命中），
        /// 只留下 queenStrike 造的 ExtraDamage 那一下。
        /// </summary>
        public bool EnableSlashOnly = true;
        public string KeyToggleSlashOnly = "";

        /// <summary>
        /// 随机斩击角度：原版三段连击的角度是写死在 weapon 表的 props.angle 里的
        /// （1a 斜向下 0.5、2a 斜向上 -0.4、3a 水平 0），所以每一次挥砍的斩击方向都是固定的。
        /// 打开后每一刀都会在 <see cref="StrikeAngleRangeDeg"/> 范围内随机取角度。
        /// </summary>
        public bool EnableRandomAngle = true;
        public string KeyToggleRandomAngle = "";

        /// <summary>
        /// 随机角度的范围（度），以"这一刀原本的朝向"为中心。
        ///   360 = 完全随机（默认）；90 = 正向 ±45°；0 = 不随机（等于原版）。
        /// </summary>
        public double StrikeAngleRangeDeg = 360.0;

        // ---- 无视墙体的兜底判定范围（格）----

        /// <summary>身前能穿墙打到的距离（格）。24 像素 = 1 格。</summary>
        public double WallIgnoreRange = 10.0;

        /// <summary>穿墙判定允许的高度差（格）。</summary>
        public double WallIgnoreHeight = 4.0;

        // ---------------------------------------------------------------- 「解」参数

        /// <summary>每次命中追加的斩击段数（不含原版那一次）。</summary>
        public int DismantleSlashes = 8;

        /// <summary>追加斩击的间隔（秒）。必须 &gt; 0.1，否则会被原版 queenStrike 的目标冷却吃掉。</summary>
        public double DismantleInterval = 0.12;

        /// <summary>追加斩击的随机落点半径（像素）。</summary>
        public double DismantleRadius = 230.0;

        // ---------------------------------------------------------------- 「捌」参数

        /// <summary>斩线判定宽度（格）。原版 Queen.doCutLineAttack 用 width*24*0.5 + 目标半径 做命中判定。</summary>
        public double CleaveWidth = 1.2;

        /// <summary>命中后延迟多久放出「捌」（秒）。</summary>
        public double CleaveDelay = 0.18;

        /// <summary>血量不低于该值的目标会被判定为"强者"，吃一发「捌」。</summary>
        public int CleaveLifeThreshold = 260;

        // ---------------------------------------------------------------- 「伏魔御厨子」参数

        /// <summary>领域最终半径（像素）。24 像素 = 1 格。</summary>
        public double DomainRadius = 360.0;

        /// <summary>领域持续时间（秒）。</summary>
        public double DomainDuration = 6.0;

        /// <summary>领域每次结算的间隔（秒）。</summary>
        public double DomainInterval = 0.25;

        // ---------------------------------------------------------------- 「伏魔御厨子」领域视觉
        //
        // 复用原版「死亡球」(SlowOrb / dc.en.bu.Orb) 的外形：
        // 原版 Orb.postUpdate() 每 0.06 秒调一次 dc.Fx.orb(x, y, dx, radius, color)，
        // 在半径上撒一圈闪电状粒子，就是那颗紫蓝色死亡球。
        // 这里把同一个 Fx.orb 画在英雄身上、颜色改成红色、半径随时间从
        // DomainStartRadius 慢慢长到 DomainRadius —— 领域展开的观感。
        //
        // 另外：领域的**伤害半径也跟着一起长大**，所以是"边展开边斩"。

        /// <summary>死亡球颜色（0xRRGGBB 十六进制字符串，不带 #）。默认正红。</summary>
        public string DomainOrbColor = "FF2020";

        /// <summary>领域刚展开时的初始半径（像素）。</summary>
        public double DomainStartRadius = 60.0;

        /// <summary>从初始半径长到 DomainRadius 需要的时间（秒）。</summary>
        public double DomainGrowTime = 2.5;

        /// <summary>
        /// 领域展开的**缓入**指数（先慢后快）。
        /// radius = start + (full-start) * (elapsed/growTime)^DomainGrowEase
        ///   1.0 = 匀速；2.0 = 二次缓入（默认，起步慢、后段猛）；3.0 = 更极端。
        /// </summary>
        public double DomainGrowEase = 2.0;

        /// <summary>死亡球粒子每多久画一次（秒）。原版 Orb 是 0.06，太小会掉帧。</summary>
        public double DomainOrbTick = 0.08;

        /// <summary>领域展开瞬间的红色屏幕染色 + 闪光（原版 Fx.customMask / multiFlashBangS）。</summary>
        public bool DomainScreenMask = true;

        // ---------------------------------------------------------------- 斩击特效配色
        //
        // 原版 queenStrike() 会在斩击位置生成 fxQueenRapierCut 粒子（女王细剑的青色斩痕）。
        // 那个粒子的 r/g/b 原版不设，保持白色 → 显示图集本身的颜色。
        // 这里 Hook dc.Fx.allocMultiBatch，把**女王细剑这一刀**生成的那颗粒子染成黑红。

        /// <summary>斩击特效（fxQueenRapierCut）颜色，0xRRGGBB。默认黑红。</summary>
        public string SlashFxColor = "E01414";

        /// <summary>是否把斩击特效染成 SlashFxColor（关掉 = 保持原版青色斩痕）。</summary>
        public bool TintSlashFx = true;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码解析（与 DamageAuraBoost 的 AuraKeys 同一套做法）。</summary>
    public static class SukunaKeys
    {
        /// <summary>模组配置实例（落盘到 coremod/config/Sukuna.json）。</summary>
        public static Config<SukunaConfig> Config { get; } = new Config<SukunaConfig>("Sukuna");

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

        /// <summary>
        /// 把配置里的 "RRGGBB" / "0xRRGGBB" / "#RRGGBB" 解析成 0xRRGGBB。
        /// 解析不出来时返回 fallback。
        /// </summary>
        public static int ParseColor(string s, int fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            string v = s.Trim().TrimStart('#');
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) v = v.Substring(2);
            if (v.Length == 0 || v.Length > 8) return fallback;
            if (!int.TryParse(v, System.Globalization.NumberStyles.HexNumber,
                              System.Globalization.CultureInfo.InvariantCulture, out int c))
            {
                return fallback;
            }
            return c & 0xFFFFFF;
        }
    }
}
