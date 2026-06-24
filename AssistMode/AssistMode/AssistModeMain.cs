using dc;
using dc.en;
using dc.h2d;
using dc.hxd;
using dc.ui;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;

using SysMath = System.Math;

namespace AssistMode
{
    /// <summary>
    /// 反向辅助模式 — 滑条 20%→20% / 100%→300%（线性映射）
    /// 仿 hutao 模式：Hook_HUD.initHero 确保 UI 跨场景持久。
    /// </summary>
    public class AssistModeMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private const double SLIDER_MIN = 0.2;
        private const double SLIDER_MAX = 1.0;
        private const double TARGET_MIN = 0.2;
        private const double TARGET_MAX = 3.0;
        private const double MAP_SCALE = 3.5;

        private double _rawED = 1.0, _rawEH = 1.0, _rawTD = 1.0;
        private double _prevED = 1.0, _prevEH = 1.0, _prevTD = 1.0;
        private bool _init = false;

        // 帧跳过
        private int _tick = 0;
        private const int TICK_MAX = 120;

        // 浮动文字（仿 hutao 的 _boostDisplayText）
        private dc.ui.Text? _uiText = null;
        private bool _disposed = false;

        public AssistModeMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            _disposed = false;
            // 关键：追 hook HUD 初始化，死亡/换关时自动重建文字
            Hook_HUD.initHero += OnHUDInit;
            System.Console.WriteLine("[AssistMode] 20%~300% 已加载");
        }

        // ── HUD 初始化 hook：每次死亡/换关后重建文字 ──
        private void OnHUDInit(Hook_HUD.orig_initHero orig, HUD self)
        {
            orig(self);
            // 已卸载时跳过，防止与其他 mod 冲突时重复创建 UI
            if (!_disposed) CreateUIText();
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (_disposed) return;
            _tick++;
            if (_tick < TICK_MAX) return;
            _tick = 0;

            try { DoMapping(); } catch { }
        }

        void IOnGameExit.OnGameExit()
        {
            // ★ 取消 HUD hook 订阅，防止与其他 mod（hutao 等）冲突
            Hook_HUD.initHero -= OnHUDInit;

            try
            {
                // 还原 assistMode 原始值
                var m = Main.Class.ME;
                if (m?.options?.assistMode != null)
                {
                    var a = m.options.assistMode;
                    a.enemyDamage = _rawED;
                    a.enemyHealth = _rawEH;
                    a.trapDamage = _rawTD;
                }
            }
            catch { }

            // 移除 UI 文字
            try
            {
                if (_uiText != null)
                {
                    _uiText.remove();
                    _uiText = null;
                }
            }
            catch { }

            _init = false;
            _disposed = true;
            System.Console.WriteLine("[AssistMode] 已卸载");
        }

        #region 核心

        private void DoMapping()
        {
            var m = Main.Class.ME;
            if (m == null) return;
            var o = m.options;
            if (o == null) return;
            var a = o.assistMode;
            if (a == null || !a.enabled) return;

            if (!_init)
            {
                _rawED = a.enemyDamage; _rawEH = a.enemyHealth; _rawTD = a.trapDamage;
                _prevED = Map(_rawED); _prevEH = Map(_rawEH); _prevTD = Map(_rawTD);
                _init = true;
                RefreshText();
                return;
            }

            bool chg = One(a.enemyDamage, ref _rawED, ref _prevED, v => a.enemyDamage = v);
            chg |= One(a.enemyHealth, ref _rawEH, ref _prevEH, v => a.enemyHealth = v);
            chg |= One(a.trapDamage, ref _rawTD, ref _prevTD, v => a.trapDamage = v);
            if (chg) RefreshText();
        }

        private bool One(double cur, ref double raw, ref double prev, Action<double> set)
        {
            if (SysMath.Abs(cur - prev) < 0.001) return false;
            bool c = false;
            if (cur >= SLIDER_MIN - 0.01 && cur <= SLIDER_MAX + 0.01) { raw = cur; c = true; }
            double v = Map(raw);
            set(v); prev = v;
            return c;
        }

        private static double Map(double s)
        {
            double x = SysMath.Max(SLIDER_MIN, SysMath.Min(SLIDER_MAX, s));
            return TARGET_MIN + (x - SLIDER_MIN) * MAP_SCALE;
        }

        #endregion

        #region 屏幕 UI（仿 hutao CreateBoostDisplayText + UpdateBoostDisplay）

        private void CreateUIText()
        {
            try
            {
                // 先移除旧文字
                if (_uiText != null)
                {
                    _uiText.remove();
                    _uiText = null;
                }

                var root = Main.Class.ME?.root;
                if (root == null) return;

                var init = "敌人伤害:100%  敌人生命:100%  陷阱伤害:100%".AsHaxeString();
                _uiText = Assets.Class.makeText(init, null, true, null);
                _uiText.set_textColor(dc.ui.Text.Class.COLORS.get("ST".AsHaxeString()));
                _uiText.set_textAlign(new Align.Left());
                _uiText.scaleX = 2.0f;
                _uiText.scaleY = 2.0f;
                _uiText.x = 20;
                _uiText.y = 40;

                root.addChild(_uiText);

                // 立即刷新文字内容
                RefreshText();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[AssistMode] 创建 UI 失败: {ex.Message}");
            }
        }

        private void RefreshText()
        {
            try
            {
                if (_uiText == null) return;

                string s = $"敌人伤害:{_prevED * 100:F0}%  敌人生命:{_prevEH * 100:F0}%  陷阱伤害:{_prevTD * 100:F0}%";
                _uiText.set_text(s.AsHaxeString());
                _uiText.visible = true;
            }
            catch { }
        }

        private static int ScreenWidth()
        {
            try
            {
                var w = dc.hxd.Window.Class.getInstance();
                if (w != null) return w.get_width();
            }
            catch { }
            return 1920;
        }

        #endregion
    }
}
