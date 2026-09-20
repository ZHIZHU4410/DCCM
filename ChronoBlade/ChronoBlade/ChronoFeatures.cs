using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ChronoBlade
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum ChronoFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用（含 P / X 面板）。</summary>
        Mod = 0,

        BladeCombo,
        Shuriken,
        SwordRain,
        Engrave,
        Bullets,
        HudIcon,
        Panels,
        Aura,
        Voice,
        DeathFx,
        PickupSfx,
    }

    /// <summary>
    /// 功能开关总控：把"开关值 / 热键名 / 菜单显示名"三件事集中在一张表里。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 用法（调用点）：
    ///
    ///     if (!ChronoFeatures.IsOn(ChronoFeature.Shuriken)) return;
    ///
    /// **不要**直接去读 `ChronoConfig.EnableXxx` —— 那样会绕过总开关。
    /// 只有这里和 BuildMenu 允许碰那些字段。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 总开关语义（按需求定的）：
    ///   · `EnableMod = false` → `IsOn()` 对**除 Mod 自己以外**的一切返回 false，
    ///     所以连 P / X 面板都打不开；
    ///   · 但 Mod 自己始终可切换（热键 + 菜单复选框都照常），否则就没法从游戏里开回来。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 热键：每个功能一个 `KeyToggleXxx` 配置，**默认全部留空（不绑键）**。
    /// 空字符串 = 不监听，所以默认情况下不会和游戏本体 / 其它模组撞键。
    /// </summary>
    public static class ChronoFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly ChronoFeature[] All =
        {
            ChronoFeature.Mod,
            ChronoFeature.BladeCombo,
            ChronoFeature.Shuriken,
            ChronoFeature.SwordRain,
            ChronoFeature.Engrave,
            ChronoFeature.Bullets,
            ChronoFeature.HudIcon,
            ChronoFeature.Panels,
            ChronoFeature.Aura,
            ChronoFeature.Voice,
            ChronoFeature.DeathFx,
            ChronoFeature.PickupSfx,
        };

        /// <summary>菜单里那一行的标题（中文，和刻刻帝背景那两项一致）。</summary>
        public static string Label(ChronoFeature f) => f switch
        {
            ChronoFeature.Mod => "总开关",
            ChronoFeature.BladeCombo => "时之刃连击效果",
            ChronoFeature.Shuriken => "第2段 一周飞镖",
            ChronoFeature.SwordRain => "第3段 时钟剑雨",
            ChronoFeature.Engrave => "罗马数字刻印",
            ChronoFeature.Bullets => "十二之弹效果",
            ChronoFeature.HudIcon => "HUD 弹药图标",
            ChronoFeature.Panels => "选择面板（P / X）",
            ChronoFeature.Aura => "刻刻帝身后时钟",
            ChronoFeature.Voice => "狂三语音",
            ChronoFeature.DeathFx => "怪物死亡特效",
            ChronoFeature.PickupSfx => "拾取音效",
            _ => f.ToString(),
        };

        /// <summary>菜单里那一行的副标题。</summary>
        public static string Hint(ChronoFeature f) => f switch
        {
            ChronoFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            ChronoFeature.BladeCombo => "关掉则第 2 / 3 段恢复普通斩击",
            ChronoFeature.Shuriken => "第 2 段那 12 枚飞镖",
            ChronoFeature.SwordRain => "第 3 段背景时钟 + 落剑",
            ChronoFeature.Engrave => "命中时在敌人身上刻罗马数字",
            ChronoFeature.Bullets => "关掉后枪照常开火，但没有时间系效果",
            ChronoFeature.HudIcon => "主手图标跟着当前装填的弹药变",
            ChronoFeature.Panels => "关掉则 P / X 面板都打不开",
            ChronoFeature.Aura => "手持刻刻帝时身后的时钟",
            ChronoFeature.Voice => "休闲 / 连杀 / Boss / 换关 四个时刻",
            ChronoFeature.DeathFx => "怪物死亡时的 TIMEJIBAI 特效",
            ChronoFeature.PickupSfx => "捡到 Zaphkiel 时的音效",
            _ => "",
        };

        /// <summary>菜单里复选框右边那个控件要读的当前值。</summary>
        public static bool RawGet(ChronoFeature f)
        {
            try
            {
                var c = ChronoKeys.Config.Value;
                return f switch
                {
                    ChronoFeature.Mod => c.EnableMod,
                    ChronoFeature.BladeCombo => c.EnableBladeCombo,
                    ChronoFeature.Shuriken => c.EnableShuriken,
                    ChronoFeature.SwordRain => c.EnableSwordRain,
                    ChronoFeature.Engrave => c.EnableEngrave,
                    ChronoFeature.Bullets => c.EnableBullets,
                    ChronoFeature.HudIcon => c.EnableHudIcon,
                    ChronoFeature.Panels => c.EnablePanels,
                    ChronoFeature.Aura => c.EnableZaphkielAura,
                    ChronoFeature.Voice => c.EnableVoice,
                    ChronoFeature.DeathFx => c.EnableDeathEffect,
                    ChronoFeature.PickupSfx => c.EnablePickupSfx,
                    _ => true,
                };
            }
            catch { return true; }
        }

        /// <summary>菜单复选框的回调：取反并写回配置。</summary>
        public static void Set(ChronoFeature f, bool on)
        {
            try
            {
                var c = ChronoKeys.Config.Value;
                switch (f)
                {
                    case ChronoFeature.Mod: c.EnableMod = on; break;
                    case ChronoFeature.BladeCombo: c.EnableBladeCombo = on; break;
                    case ChronoFeature.Shuriken: c.EnableShuriken = on; break;
                    case ChronoFeature.SwordRain: c.EnableSwordRain = on; break;
                    case ChronoFeature.Engrave: c.EnableEngrave = on; break;
                    case ChronoFeature.Bullets: c.EnableBullets = on; break;
                    case ChronoFeature.HudIcon: c.EnableHudIcon = on; break;
                    case ChronoFeature.Panels: c.EnablePanels = on; break;
                    case ChronoFeature.Aura: c.EnableZaphkielAura = on; break;
                    case ChronoFeature.Voice: c.EnableVoice = on; break;
                    case ChronoFeature.DeathFx: c.EnableDeathEffect = on; break;
                    case ChronoFeature.PickupSfx: c.EnablePickupSfx = on; break;
                }
                ChronoKeys.Config.Save();
            }
            catch { }
        }

        /// <summary>这个功能绑的热键名（可能是空串 = 不绑）。</summary>
        public static string KeyName(ChronoFeature f)
        {
            try
            {
                var c = ChronoKeys.Config.Value;
                return f switch
                {
                    ChronoFeature.Mod => c.KeyToggleMod,
                    ChronoFeature.BladeCombo => c.KeyToggleBladeCombo,
                    ChronoFeature.Shuriken => c.KeyToggleShuriken,
                    ChronoFeature.SwordRain => c.KeyToggleSwordRain,
                    ChronoFeature.Engrave => c.KeyToggleEngrave,
                    ChronoFeature.Bullets => c.KeyToggleBullets,
                    ChronoFeature.HudIcon => c.KeyToggleHudIcon,
                    ChronoFeature.Panels => c.KeyTogglePanels,
                    ChronoFeature.Aura => c.KeyToggleAura,
                    ChronoFeature.Voice => c.KeyToggleVoice,
                    ChronoFeature.DeathFx => c.KeyToggleDeathFx,
                    ChronoFeature.PickupSfx => c.KeyTogglePickupSfx,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>
        /// 功能是否生效。**统一入口** —— 调用点一律用它，别直接读配置字段，
        /// 否则总开关就形同虚设。
        ///
        /// 总开关特例：`ChronoFeature.Mod` 自己不受总开关影响（否则关掉就再也开不回来了）。
        /// </summary>
        public static bool IsOn(ChronoFeature f)
        {
            bool own = RawGet(f);
            if (f == ChronoFeature.Mod) return own;

            // 总开关关掉 → 其余一切停用
            try { if (!ChronoKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        /// <summary>每个热键上一次的按下状态（做边沿检测，按住不会连发）。</summary>
        private static readonly Dictionary<ChronoFeature, bool> _wasDown = new();

        /// <summary>
        /// 每帧调用（放在 IOnHeroUpdate 里）：检查所有已绑定的热键，按下就切换对应功能。
        ///
        /// ⚠️ 面板打开时游戏**真暂停**、IOnHeroUpdate 不会被调用 ——
        ///    也就是说暂停中切不了开关（总开关也一样）。可接受，但要知道。
        /// </summary>
        public static void PollHotkeys(Action<string> log)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = ChronoKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;                              // 只在"刚按下"那一帧动作

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[ChronoBlade] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        /// <summary>换关 / 卸载时清掉按键状态，避免"上一关按着、下一关一进来就触发"。</summary>
        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
