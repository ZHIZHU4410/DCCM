#nullable disable

using System;
using dc;
using dc.en;
using dc.hxd;
using dc.hxd.res;
using dc.libs.data;
using ModCore.Utilities;

namespace KatanaParry
{
    /// <summary>
    /// 盾反反馈的复刻 —— 把原版 <c>BaseShield.triggerParryFeedbacks()</c>
    /// （GamePseudocode/dc.tool.weap/BaseShield.cs:3161-3226）里那几层效果
    /// 用同样的 FX 常量重放一遍，这样不看数字也能一眼认出"这是盾反"。
    ///
    /// 原版顺序（对应下面的 ①~⑥）：
    ///   ① <c>hero.popText(Lang.t.get("Parade !"), 65535)</c>   —— 头顶文字
    ///   ② <c>fx.customMask(9353156, 0.2, 0.04, 0.1, 0.15, null)</c> —— 全屏色彩一闪
    ///   ③ <c>fx.frontShieldHit(hero, 6921449)</c>            —— 正面盾击光
    ///   ④ <c>fx.hitLines(x, y, dir, 16777215, ...)</c>       —— 命中放射线
    ///   ⑤ <c>playHitSfx(...)</c>                              —— 格挡音（这里用 weapon_shield_block.wav）
    ///   ⑥ 另有 <c>BaseShield.startParry()</c> 里的 <c>fx.shieldParryStart(hero, 4480637, null)</c>
    ///      —— 盾反起手光环；本模组在**开窗口那一刻**播它（见 KatanaParryMain.OnWeaponPrepare）
    ///
    /// 所有调用都单独 try/catch：少一个特效不应该把攻击结算搞崩。
    /// </summary>
    internal static class ParryFx
    {
        /// <summary>原版 <c>startParry()</c> 用的光环颜色（BaseShield.cs:1068-1070）。</summary>
        public const int ColorParryStart = 4480637;

        /// <summary>原版 <c>triggerParryFeedbacks()</c> 里 frontShieldHit 的颜色（BaseShield.cs:3200）。</summary>
        public const int ColorShieldHit = 6921449;

        /// <summary>原版 customMask 的颜色（BaseShield.cs:3189）。</summary>
        public const int ColorMask = 9353156;

        /// <summary>原版 hitLines 的颜色（BaseShield.cs:3215）。</summary>
        public const int ColorHitLines = 16777215;

        /// <summary>格挡音效（res/sfx/weapon/weapon_shield_block.wav，盾系武器共用的格挡音）。</summary>
        public const string BlockSfxPath = "sfx/weapon/weapon_shield_block.wav";

        /// <summary>开窗口时的"盾反起手"光环（原版 startParry 的第一步）。</summary>
        public static void PlayParryStart(Hero hero)
        {
            if (hero == null || hero.destroyed) return;

            try
            {
                dc.pr.Level level = hero._level;
                dc.Fx fx = level?.fx;
                if (fx != null) fx.shieldParryStart(hero, ColorParryStart, null);
            }
            catch { }
        }

        /// <summary>盾反成功那一刻的完整反馈（对应原版 triggerParryFeedbacks）。</summary>
        public static void PlayParrySuccess(Hero hero)
        {
            if (hero == null || hero.destroyed) return;

            dc.pr.Level level = hero._level;
            if (level == null) return;

            double px = ((double)hero.cx + hero.xr) * 24.0;
            double py = ((double)hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;

            // ① 头顶 "Parade !"（走本地化表；取不到就不弹）
            try
            {
                GetText t = dc.Lang.Class?.t;
                dc.String txt = t?.get(StringUtils.AsHaxeString("Parade !"), null);
                if (txt != null) hero.popText(txt, 65535);
            }
            catch { }

            // ② 全屏色彩一闪
            try { level.fx?.customMask(ColorMask, 0.2, 0.04, 0.1, 0.15, null); } catch { }

            // ③ 正面盾击光
            try { level.fx?.frontShieldHit(hero, ColorShieldHit); } catch { }

            // ④ 命中放射线（sparkLineCount / deflectLineCount 传引用，原版也是 null 引用）
            try
            {
                int spark = 0;
                int deflect = 0;
                level.fx?.hitLines(px, py, hero.dir, ColorHitLines, null,
                                   HaxeProxy.Runtime.Ref<int>.From(ref spark),
                                   HaxeProxy.Runtime.Ref<int>.From(ref deflect));
            }
            catch { }

            // ⑤ 格挡音
            try
            {
                Loader loader = Res.Class?.get_loader();
                dc.String path = StringUtils.AsHaxeString(BlockSfxPath);
                if (loader != null && loader.exists(path))
                {
                    Sound snd = loader.loadCache(path, Sound.Class) as Sound;
                    if (snd != null) level.lAudio?.playEventAt(snd, px, py, null, null, null);
                }
            }
            catch { }
        }
    }
}
