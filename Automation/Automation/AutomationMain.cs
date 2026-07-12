using dc;
using dc.en;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Storage;
using ModCore.Utilities;
using System;
using System.Runtime.InteropServices;

using SysMath = System.Math;

namespace Automation
{
    /// <summary>
    /// TODO: 填写模组说明
    /// </summary>
    public class AutomationMain : ModBase, IOnGameExit, IOnHeroUpdate, IModMenu
    {
        // ===== 按键 =====
        private bool _isZKeyDown = false;
        private bool _isXKeyDown = false;
        private bool _isCKeyDown = false;
        private const int VK_Z = 0x5A;
        private const int VK_X = 0x58;
        private const int VK_C = 0x43;

        // ===== 数值 =====
        private const int CELL_AMOUNT = 100;
        private const int GOLD_AMOUNT = 10000;
        private const int CURSE_AMOUNT = 114514;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public static Config<Configs> config { get; } = new Config<Configs>("Automation");

        public AutomationMain(ModInfo info) : base(info) { }

        // -- IModMenu --
        public string GetName() => "Automation";

        public void BuildMenu(dc.ui.Options options)
        {
            ((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
                StringUtils.AsHaxeString("AUTOMATION SETTINGS"));
            ((dc.ui.OptionsBase)options).createScroller(0.0);

            bool enabled = config.Value.enabled;
            ((dc.ui.OptionsBase)options).addToggleWidget(
                StringUtils.AsHaxeString("Activate mod"),
                StringUtils.AsHaxeString("Z=cells  X=gold  C=curse"),
                (HlFunc<bool>)delegate { config.Value.enabled = !config.Value.enabled; return config.Value.enabled; },
                new Ref<bool>(ref enabled),
                ((dc.ui.OptionsBase)options).scrollerFlow);

            ((dc.ui.OptionsBase)options).updateScroller();
        }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();
            // Hook_Hero.onMobDeath += OnHeroKillMob;   // 按需启用
            System.Console.WriteLine("[Automation] 模组已加载 (Z=加细胞 X=加金币 C=加诅咒)");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (!config.Value.enabled) return;
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null) return;

            // ── Z 键：自动加 cell ──
            bool isZPressed = GetAsyncKeyState(VK_Z) < 0;
            if (isZPressed && !_isZKeyDown)
            {
                try
                {
                    bool noStats = false;
                    hero.addCells(CELL_AMOUNT, new Ref<bool>(ref noStats));
                    System.Console.WriteLine($"[Automation] ✓ +{CELL_AMOUNT} 细胞 (当前: {hero.cells})");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[Automation] ✗ 加细胞失败: {ex.Message}");
                }
            }
            _isZKeyDown = isZPressed;

            // ── X 键：自动加金币 ──
            bool isXPressed = GetAsyncKeyState(VK_X) < 0;
            if (isXPressed && !_isXKeyDown)
            {
                try
                {
                    bool noStats = false;
                    hero.addMoney(GOLD_AMOUNT, new Ref<bool>(ref noStats));
                    System.Console.WriteLine($"[Automation] ✓ +{GOLD_AMOUNT} 金币");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[Automation] ✗ 加金币失败: {ex.Message}");
                }
            }
            _isXKeyDown = isXPressed;

            // ── C 键：自动加诅咒 ──
            bool isCPressed = GetAsyncKeyState(VK_C) < 0;
            if (isCPressed && !_isCKeyDown)
            {
                try
                {
                    bool hidePopup = false;
                    bool useAltSound = false;
                    hero.curse(CURSE_AMOUNT, null, new Ref<bool>(ref hidePopup), new Ref<bool>(ref useAltSound));
                    System.Console.WriteLine($"[Automation] ✓ +{CURSE_AMOUNT} 诅咒 (当前: {hero.curseCounter})");
                }
                catch (Exception ex)
                {
                    System.Console.WriteLine($"[Automation] ✗ 加诅咒失败: {ex.Message}");
                }
            }
            _isCKeyDown = isCPressed;
        }

        void IOnGameExit.OnGameExit()
        {
            // Hook_Hero.onMobDeath -= OnHeroKillMob;   // 按需取消
            System.Console.WriteLine("[Automation] 游戏退出，模组已卸载");
        }

        #endregion
    }

    /// <summary>
    /// Persistent config for the Automation mod.
    /// </summary>
    public class Configs
    {
        public bool enabled = true;
    }
}
