using System.Runtime.InteropServices;
using dc;
using dc.cine;
using dc.libs;
using dc.level;
using dc.pr;
using dc.tool.mod;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using PrisonCourtyardtest.Core.Configuration;
using PrisonCourtyardtest.Utils;

namespace PrisonCourtyardtest.EntryPoint;

public class ModInitializer(ModInfo info) : ModBase(info), IOnGameEndInit, IOnHeroUpdate, IModMenu
{
    public static ModCore.Storage.Config<CoreConfig> Config = new("PrisonCourtyardtestCoreConfig");

    private LevelManager? _levelManager;
    private bool _k1WasDown, _k2WasDown;

    public override void Initialize()
    {
        base.Initialize();
        Config.Value.debugMode = true;
        Config.Save();

        Logger.Information("Commencing initialisation of PrisonCourtyardtest DLC module");

        _ = new RoomGroup(this);
        _ = new DLCLang(this);
        _levelManager = new LevelManager(this);
        _levelManager.RegisterHooks();

        Logger.Information("PrisonCourtyardtest initialisation complete (1=混乱大道)");
    }

    // ── IModMenu ──
    public string GetName() => "Prison Courtyard Test";

    public void BuildMenu(dc.ui.Options options)
    {
        ((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
            StringUtils.AsHaxeString("PRISON COURTYARD TEST SETTINGS"));
        ((dc.ui.OptionsBase)options).createScroller(0.0);

        bool enabled = Config.Value.enabled;
        ((dc.ui.OptionsBase)options).addToggleWidget(
            StringUtils.AsHaxeString("Activate mod"),
            StringUtils.AsHaxeString("Adds Chaos Avenue biome after PrisonCourtyard"),
            (HlFunc<bool>)delegate { Config.Value.enabled = !Config.Value.enabled; return Config.Value.enabled; },
            new Ref<bool>(ref enabled),
            ((dc.ui.OptionsBase)options).scrollerFlow);

        ((dc.ui.OptionsBase)options).updateScroller();
    }

    void IOnGameEndInit.OnGameEndInit()
    {
        try
        {
            var resPath = Info.ModRoot!.GetFilePath("res.pak");
            if (!string.IsNullOrWhiteSpace(resPath))
            {
                FsPak.Instance.FileSystem.loadModPak(resPath.AsHlxStr());
                Logger.Information("ResPak loaded");

                // Merge custom CDB entries (biome + levels) from data.cdb in res.pak
                var json = CDBManager.Class.instance.getAlteredCDB();
                dc.Data.Class.loadJson(json, default);
                Logger.Information("CDB data merged (1=混乱大道)");

                _levelManager?.PatchLevelLogo();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Resource loading failed", ex);
        }
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        if (!Config.Value.enabled) return;

        // Runtime CDB injection
        _levelManager?.TryInject();

        // Key 1: teleport to PrisonCourtyardTest
        if (KeyPressed(VK_1, ref _k1WasDown)) On1Pressed();
        // Key 2: teleport to T_Roof
        if (KeyPressed(VK_2, ref _k2WasDown)) On2Pressed();
    }

    // ═══════════════════════════════════════════
    // Keyboard shortcut
    // ═══════════════════════════════════════════

    private const int VK_1 = 0x31;
    private const int VK_2 = 0x32;

    private void On1Pressed()
    {
        try
        {
            Logger.Information("1 → 混乱大道");
            _levelManager?.PatchLevelLogo();
            LevelTransition.Class.@goto(GameConstants.Levels.PrisonCourtyardTest.AsHlxStr());
        }
        catch (Exception ex) { Logger.Error("1 fail", ex); }
    }

    private void On2Pressed()
    {
        try
        {
            Logger.Information("2 → T_Roof");
            _levelManager?.PatchLevelLogo();
            LevelTransition.Class.@goto("T_Roof".AsHlxStr());
        }
        catch (Exception ex) { Logger.Error("2 fail", ex); }
    }

    // ═══════════════════════════════════════════
    // Win32 Keyboard Helpers
    // ═══════════════════════════════════════════

    private bool KeyPressed(int key, ref bool wasDown)
    {
        bool down = IsKeyDown(key);
        bool r = down && !wasDown;
        wasDown = down;
        return r;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsKeyDown(int vKey) => ((int)GetAsyncKeyState(vKey) & 0x8000) != 0;
}
