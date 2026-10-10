#nullable disable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Sukuna
{
    /// <summary>模组里可以被单独开关的功能模块（对应宿傩的三招）。</summary>
    public enum SukunaFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>「解」——通常斩击，命中后追加多段随机斩。</summary>
        Dismantle,

        /// <summary>「捌」——对强者追加贯穿全场的斩线。</summary>
        Cleave,

        /// <summary>「伏魔御厨子」——领域展开。</summary>
        Domain,

        /// <summary>无视墙体 —— 挥砍判定也穿墙（斩击本体本来就不看遮挡）。</summary>
        WallIgnore,

        /// <summary>只保留斩击伤害 —— 吞掉挥砍那一次普通命中。</summary>
        SlashOnly,

        /// <summary>随机斩击角度 —— 每一刀都在范围内随机取角度，而不是用连击表里写死的角度。</summary>
        RandomAngle,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名"集中在一张表里
    /// （与 DamageAuraBoost 的 AuraFeatures 同一套结构）。
    ///
    /// 用法（调用点一律走这里）：
    ///
    ///     if (!SukunaFeatures.IsOn(SukunaFeature.Dismantle)) return;
    ///
    /// 总开关语义：EnableMod = false 时 IsOn() 对除 Mod 自己以外的一切返回 false。
    /// </summary>
    public static class SukunaFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly SukunaFeature[] All =
        {
            SukunaFeature.Mod,
            SukunaFeature.Dismantle,
            SukunaFeature.Cleave,
            SukunaFeature.Domain,
            SukunaFeature.WallIgnore,
            SukunaFeature.SlashOnly,
            SukunaFeature.RandomAngle,
        };

        /// <summary>菜单里那一行的标题。</summary>
        public static string Label(SukunaFeature f) => f switch
        {
            SukunaFeature.Mod => "总开关",
            SukunaFeature.Dismantle => "解（通常斩击 · 追斩）",
            SukunaFeature.Cleave => "捌（贯穿斩线）",
            SukunaFeature.Domain => "伏魔御厨子（领域展开）",
            SukunaFeature.WallIgnore => "无视墙体",
            SukunaFeature.SlashOnly => "只保留斩击伤害",
            SukunaFeature.RandomAngle => "随机斩击角度",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(SukunaFeature f) => f switch
        {
            SukunaFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            SukunaFeature.Dismantle => "女王细剑命中后，在目标周围随机位置追加多段斩击",
            SukunaFeature.Cleave => "精英 / 大型 / 高血量目标额外吃一条贯穿全场的斩线",
            SukunaFeature.Domain => "按释放键在自身周围展开领域，范围内敌人被持续斩击",
            SukunaFeature.WallIgnore => "挥砍判定也穿墙（身前 WallIgnoreRange 格内不看遮挡）",
            SukunaFeature.SlashOnly => "删掉挥砍那次普通命中，只留 queenStrike 的斩击伤害",
            SukunaFeature.RandomAngle => "每一刀的斩击角度都在范围内随机（不再是连击表写死的斜下/斜上/水平）",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(SukunaFeature f)
        {
            try
            {
                var c = SukunaKeys.Config.Value;
                return f switch
                {
                    SukunaFeature.Mod => c.EnableMod,
                    SukunaFeature.Dismantle => c.EnableDismantle,
                    SukunaFeature.Cleave => c.EnableCleave,
                    SukunaFeature.Domain => c.EnableDomain,
                    SukunaFeature.WallIgnore => c.EnableWallIgnore,
                    SukunaFeature.SlashOnly => c.EnableSlashOnly,
                    SukunaFeature.RandomAngle => c.EnableRandomAngle,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：写回配置并落盘。</summary>
        public static void Set(SukunaFeature f, bool on)
        {
            try
            {
                var c = SukunaKeys.Config.Value;
                switch (f)
                {
                    case SukunaFeature.Mod: c.EnableMod = on; break;
                    case SukunaFeature.Dismantle: c.EnableDismantle = on; break;
                    case SukunaFeature.Cleave: c.EnableCleave = on; break;
                    case SukunaFeature.Domain: c.EnableDomain = on; break;
                    case SukunaFeature.WallIgnore: c.EnableWallIgnore = on; break;
                    case SukunaFeature.SlashOnly: c.EnableSlashOnly = on; break;
                    case SukunaFeature.RandomAngle: c.EnableRandomAngle = on; break;
                }
                SukunaKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(SukunaFeature f)
        {
            try
            {
                var c = SukunaKeys.Config.Value;
                return f switch
                {
                    SukunaFeature.Mod => c.KeyToggleMod,
                    SukunaFeature.Dismantle => c.KeyToggleDismantle,
                    SukunaFeature.Cleave => c.KeyToggleCleave,
                    SukunaFeature.Domain => c.KeyToggleDomain,
                    SukunaFeature.WallIgnore => c.KeyToggleWallIgnore,
                    SukunaFeature.SlashOnly => c.KeyToggleSlashOnly,
                    SukunaFeature.RandomAngle => c.KeyToggleRandomAngle,
                    _ => "",
                };
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。统一入口 —— 别直接读配置字段，否则总开关就形同虚设。
        /// 总开关特例：SukunaFeature.Mod 自己不受总开关影响。
        /// </summary>
        public static bool IsOn(SukunaFeature f)
        {
            bool own = RawGet(f);
            if (f == SukunaFeature.Mod) return own;

            try { if (!SukunaKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        /// <summary>每个开关热键上一次的按下状态（边沿检测，按住不会连发）。</summary>
        private static readonly Dictionary<SukunaFeature, bool> _wasDown = new();

        /// <summary>「伏魔御厨子」释放键上一次的按下状态。</summary>
        private static bool _castWasDown;

        /// <summary>取配置（异常时返回 null，调用点自行兜底）。</summary>
        private static SukunaConfig Cfg()
        {
            try { return SukunaKeys.Config.Value; } catch { return null; }
        }

        /// <summary>
        /// 每帧调用（放在 IOnHeroUpdate 里）：
        ///   · 各功能开关的切换热键（默认不绑键）
        ///   · 「伏魔御厨子」释放键（默认 V）→ 回调 onCastDomain
        /// 全部做边沿检测 —— 只在"刚按下"那一帧动作。
        /// </summary>
        public static void PollHotkeys(Action<string> log, Action onCastDomain)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = SukunaKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在"刚按下"那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[Sukuna] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }

            // ---- 领域释放键（一次性动作）----
            try
            {
                string cast = Cfg()?.KeyCastDomain ?? "";
                int vk = SukunaKeys.Resolve(cast, 0);
                if (vk <= 0)
                {
                    _castWasDown = false;
                    return;
                }

                bool down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                bool was = _castWasDown;
                _castWasDown = down;
                if (down && !was) onCastDomain?.Invoke();
            }
            catch { }
        }

        /// <summary>换关 / 卸载时清掉按键状态，避免"上一关按着、下一关一进来就触发"。</summary>
        public static void ResetHotkeyState()
        {
            _wasDown.Clear();
            _castWasDown = false;
        }
    }
}
