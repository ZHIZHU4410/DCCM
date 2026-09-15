using System;
using System.Collections.Generic;
using ModCore.Storage;

namespace ChronoBlade
{
    /// <summary>
    /// 时之刃的持久化配置（存成 coremod/mods/ChronoBlade/ChronoBlade.json，可手改）。
    ///
    /// 所有热键都写成"按键名"字符串，不再硬编码 Q/E：
    ///   · 单个字母：A ~ Z
    ///   · 数字：0 ~ 9
    ///   · 功能键：F1 ~ F12
    ///   · 特殊键名：Space / Tab / Enter / Shift / Ctrl / Alt /
    ///              LeftBracket / RightBracket / Semicolon / Quote / Comma / Period /
    ///              Slash / Backslash / Minus / Equals / Up / Down / Left / Right
    ///   （也支持直接写十六进制虚拟键码，例如 "0x51"）
    /// </summary>
    public class ChronoConfig
    {
        /// <summary>
        /// 打开"选择武器"面板的按键。**只列本模组新增的两把武器**（时之刃 / Zaphkiel）。
        ///
        /// ⚠ 这里原来是"召唤时之刃 = \、召唤 Zaphkiel = P"两个直接召唤热键。
        ///   那两个**已经删掉**，`P` 改绑成这个面板 —— 删直召和加面板必须在同一次改动里完成，
        ///   否则中间会出现"没有任何途径拿到武器"的状态。想调整键位就在 选项 → 模组 里改，
        ///   或者直接改 coremod/config/ChronoBlade.json。
        ///
        /// 面板内的操作（和原版训练场选武器完全一致）：← → 选择、Enter 召唤、
        /// 品质/无色/等级用面板底部的原版控件调、Esc 返回。打开期间游戏是**真暂停**的。
        /// </summary>
        public string KeyWeaponPanel = "P";

        /// <summary>
        /// 打开"选择弹药"UI 的按键。**换弹机制已取消** —— 开火永远只打当前装填的这一发，
        /// 想换弹只在这里选（UI 内：← → 选择、Enter 确认、Esc 取消）。打开期间游戏也是真暂停。
        /// </summary>
        public string KeySelectBullet = "X";

        /// <summary>刻印渲染自测的按键。</summary>
        public string KeyTestNumeral = "RightBracket";

        /// <summary>怪物死亡特效是否启用。</summary>
        public bool EnableDeathEffect = true;

        // ---------------------------------------------------------------- 狂三语音
        //
        // Assets/sfx/kurumi01~08.WAV（打包后在 pak 里的路径是 sfx/kurumiNN.WAV）。
        // 四个时刻各自独立掷骰，全部走 ChronoVoice 那条**独占的最高优先级声道**，
        // 所以音量与游戏内的"音效音量"滑块无关（见 ChronoVoice.cs 的说明）。

        /// <summary>语音总开关。</summary>
        public bool EnableVoice = true;

        /// <summary>
        /// 语音音量。**不跟随游戏的音效音量滑块**（专用声道 volume 固定 1.0，
        /// 游戏改的是它自己那几个 sfxChanGroup）—— 想调只能用这个值。
        /// </summary>
        public double VoiceVolume = 1.0;

        /// <summary>休闲时刻（附近没敌人、安静一段时间）每段安静期掷一次骰的概率。</summary>
        public double VoiceChanceIdle = 0.35;

        /// <summary>连杀时刻（短时间内连续击杀）的触发概率。</summary>
        public double VoiceChanceKillStreak = 0.70;

        /// <summary>打败 Boss 的触发概率（默认必出）。</summary>
        public double VoiceChanceBoss = 1.0;

        /// <summary>进入下一关的触发概率。</summary>
        public double VoiceChanceLevel = 0.85;

        // ---------------------------------------------------------------- 刻刻帝的身后时钟背景
        //
        // 手持 Zaphkiel 时在英雄身后循环播放 TIMEBEIJING 的那个大时钟
        // （ChronoFx.UpdateAura / SpawnAura）。

        /// <summary>是否显示这个背景。关掉就完全不创建、不更新。</summary>
        public bool EnableZaphkielAura = true;

        /// <summary>
        /// 背景的不透明度，单位是**百分比**（0 = 看不见，100 = 完全不透明）。
        ///
        /// ⚠️ 这里故意用 `int` 而不是 `double` —— 游戏的"选项 → 模组"菜单是 ModCore
        ///    按配置字段**逐个反射**生成的，而它只认 `bool` / `int` / `string`
        ///    （ModCore.dll 里 `Boolean`、`Int32` 都出现了，`Double` **一次都没出现**，
        ///    也就是它根本不引用 System.Double → 遇到 double 字段会**静默跳过**）。
        ///    用 int 才能出现在游戏菜单里；`bool` 同理（复选框）。
        /// </summary>
        public int ZaphkielAuraAlphaPercent = 90;
    }

    /// <summary>配置读取 + 按键名 → 虚拟键码的解析。</summary>
    public static class ChronoKeys
    {
        public static Config<ChronoConfig> Config { get; } = new Config<ChronoConfig>("ChronoBlade");

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

            // 十六进制写法
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(s.Substring(2), System.Globalization.NumberStyles.HexNumber,
                                System.Globalization.CultureInfo.InvariantCulture, out int hex))
            {
                return hex;
            }

            // 特殊键名
            if (Special.TryGetValue(s, out int vk)) return vk;

            // 单个字母
            if (s.Length == 1)
            {
                char c = char.ToUpperInvariant(s[0]);
                if (c >= 'A' && c <= 'Z') return c;                 // 0x41 ~ 0x5A
                if (c >= '0' && c <= '9') return c;                 // 0x30 ~ 0x39
            }

            // F1 ~ F12
            if ((s.StartsWith("F", StringComparison.OrdinalIgnoreCase) || s.StartsWith("f", StringComparison.OrdinalIgnoreCase))
                && int.TryParse(s.Substring(1), out int fn) && fn >= 1 && fn <= 12)
            {
                return 0x70 + (fn - 1);                             // VK_F1 = 0x70
            }

            // 十进制虚拟键码
            if (int.TryParse(s, out int dec) && dec > 0 && dec < 256) return dec;

            return fallback;
        }
    }
}
