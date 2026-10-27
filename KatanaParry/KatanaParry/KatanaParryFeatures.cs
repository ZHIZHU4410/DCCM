using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace KatanaParry
{
    /// <summary>模组里可以被单独开关的功能模块。</summary>
    public enum ParryFeature
    {
        /// <summary>总开关。关掉 = 其余全部停用。</summary>
        Mod = 0,

        /// <summary>前两刀盾反。</summary>
        Parry,

        /// <summary>反击：击退 + 眩晕攻击来源。</summary>
        Stun,

        /// <summary>盾反反馈（光效 / "Parade !" / 音效）。</summary>
        Fx,

        /// <summary>反击伤害（近战来源吃一次盾反伤害）。</summary>
        Counter,

        /// <summary>远程弹幕打回去。</summary>
        Reflect,

        /// <summary>酒桶 / 炸弹打回去。</summary>
        Bounce,
    }

    /// <summary>
    /// 功能开关总控（与 DamageAuraBoost / AdeleScytheOverhaul / TombstonePassive 同一套做法）。
    /// 调用点统一用 <c>ParryFeatures.IsOn(ParryFeature.Xxx)</c>，别直接读 `ParryConfig.EnableXxx`。
    /// </summary>
    public static class ParryFeatures
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        /// <summary>菜单 / 日志里显示的顺序（总开关排最前）。</summary>
        public static readonly ParryFeature[] All =
        {
            ParryFeature.Mod,
            ParryFeature.Parry,
            ParryFeature.Stun,
            ParryFeature.Counter,
            ParryFeature.Reflect,
            ParryFeature.Bounce,
            ParryFeature.Fx,
        };

        public static string Label(ParryFeature f) => f switch
        {
            ParryFeature.Mod => "总开关",
            ParryFeature.Parry => "前两刀盾反",
            ParryFeature.Stun => "反击：击退+眩晕",
            ParryFeature.Counter => "反击伤害",
            ParryFeature.Reflect => "弹幕打回去",
            ParryFeature.Bounce => "酒桶/炸弹打回去",
            ParryFeature.Fx => "盾反特效与文字",
            _ => f.ToString(),
        };

        public static string Hint(ParryFeature f) => f switch
        {
            ParryFeature.Mod => "关掉后其余功能全部停用（本项仍可切换）",
            ParryFeature.Parry => "武士刀第 1、2 段挥砍期间被打 = 盾反（不掉血，窗口见滑条）",
            ParryFeature.Stun => "把近战来源击退并眩晕（弹丸走「弹幕打回去」）",
            ParryFeature.Counter => "盾反成功时对近战来源造成一次伤害（一级 15，暴击 30，随卷轴成长）",
            ParryFeature.Reflect => "远程弹丸盾反时掉头射回来源（伤害 = 盾反伤害 × 75%）",
            ParryFeature.Bounce => "窗口内附近的敌方炸弹/酒桶被打回去（原版 Grenade.counter / 酒桶英雄化）",
            ParryFeature.Fx => "盾反光效 + 「Parade !」文字 + 格挡音效",
            _ => "",
        };

        public static bool RawGet(ParryFeature f)
        {
            try
            {
                var c = ParryKeys.Config.Value;
                return f switch
                {
                    ParryFeature.Mod => c.EnableMod,
                    ParryFeature.Parry => c.EnableParry,
                    ParryFeature.Stun => c.EnableStun,
                    ParryFeature.Counter => c.EnableCounter,
                    ParryFeature.Reflect => c.EnableReflect,
                    ParryFeature.Bounce => c.EnableBounce,
                    ParryFeature.Fx => c.EnableFx,
                    _ => true,
                };
            }
            catch { return true; }
        }

        public static void Set(ParryFeature f, bool on)
        {
            try
            {
                var c = ParryKeys.Config.Value;
                switch (f)
                {
                    case ParryFeature.Mod: c.EnableMod = on; break;
                    case ParryFeature.Parry: c.EnableParry = on; break;
                    case ParryFeature.Stun: c.EnableStun = on; break;
                    case ParryFeature.Counter: c.EnableCounter = on; break;
                    case ParryFeature.Reflect: c.EnableReflect = on; break;
                    case ParryFeature.Bounce: c.EnableBounce = on; break;
                    case ParryFeature.Fx: c.EnableFx = on; break;
                }
                ParryKeys.Config.Save();
            }
            catch { }
        }

        public static string KeyName(ParryFeature f)
        {
            try
            {
                var c = ParryKeys.Config.Value;
                return f switch
                {
                    ParryFeature.Mod => c.KeyToggleMod,
                    ParryFeature.Parry => c.KeyToggleParry,
                    ParryFeature.Stun => c.KeyToggleStun,
                    ParryFeature.Counter => c.KeyToggleCounter,
                    ParryFeature.Reflect => c.KeyToggleReflect,
                    ParryFeature.Bounce => c.KeyToggleBounce,
                    ParryFeature.Fx => c.KeyToggleFx,
                    _ => "",
                } ?? "";
            }
            catch { return ""; }
        }

        /// <summary>功能是否生效（总开关对除 Mod 自己以外的一切生效）。</summary>
        public static bool IsOn(ParryFeature f)
        {
            bool own = RawGet(f);
            if (f == ParryFeature.Mod) return own;

            try { if (!ParryKeys.Config.Value.EnableMod) return false; } catch { }

            return own;
        }

        // ---------------------------------------------------------------- 热键轮询

        private static readonly Dictionary<ParryFeature, bool> _wasDown = new();

        public static void PollHotkeys(Action<string> log)
        {
            foreach (var f in All)
            {
                bool down = false;
                try
                {
                    string name = KeyName(f);
                    if (string.IsNullOrWhiteSpace(name)) continue;      // 默认没绑键 → 跳过
                    int vk = ParryKeys.Resolve(name, 0);
                    if (vk <= 0) continue;

                    down = (GetAsyncKeyState(vk) & 0x8000) != 0;
                }
                catch { continue; }

                bool was = _wasDown.TryGetValue(f, out var w) && w;
                _wasDown[f] = down;
                if (!down || was) continue;

                bool now = !RawGet(f);
                Set(f, now);
                try { log?.Invoke($"[KatanaParry] {Label(f)} → {(now ? "开" : "关")}（热键 {KeyName(f)}）"); } catch { }
            }
        }

        public static void ResetHotkeyState() => _wasDown.Clear();
    }
}
