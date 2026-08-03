using System.Runtime.InteropServices;
using dc;
using dc.cine;
using dc.level;
using dc.libs;
using dc.pr;
using dc.tool.mod;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using PrisonCorruptDepthstest.Core.Configuration;
using PrisonCorruptDepthstest.Utils;

namespace PrisonCorruptDepthstest.EntryPoint;

public class ModInitializer(ModInfo info) : ModBase(info), IOnGameEndInit, IOnHeroUpdate, IModMenu
{
    public static ModCore.Storage.Config<CoreConfig> Config = new("PrisonCorruptDepthstestCoreConfig");

    // Keyboard key codes: 1=腐化牢房 2=深层牢房 3=Boss
    private const int VK_1 = 0x31;
    private const int VK_2 = 0x32;
    private const int VK_3 = 0x33;

    private LevelManager? _levelManager;
    private EntityManager? _entityManager;
    private bool _k1WasDown, _k2WasDown, _k3WasDown;

    public override void Initialize()
    {
        base.Initialize();
        Config.Value.debugMode = true;
        Config.Save();

        Logger.Information("Commencing initialisation of PrisonCorruptDepthstest DLC module");

        _ = new RoomGroup(this);
        _ = new DLCLang(this);
        _levelManager = new LevelManager(this);
        _entityManager = new EntityManager(this);

        // Register hooks directly
        _levelManager.RegisterHooks();

        Logger.Information("PrisonCorruptDepthstest initialisation complete");
    }

    // ── IModMenu ──
    public string GetName() => "Prison Corrupt Depths";

    public void BuildMenu(dc.ui.Options options)
    {
        ((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
            StringUtils.AsHaxeString("PRISON CORRUPT DEPTHS SETTINGS"));
        ((dc.ui.OptionsBase)options).createScroller(0.0);

        bool enabled = Config.Value.enabled;
        ((dc.ui.OptionsBase)options).addToggleWidget(
            StringUtils.AsHaxeString("Activate mod"),
            StringUtils.AsHaxeString("Adds Prison Corrupt Depths biome and boss arena"),
            (HlFunc<bool>)delegate { Config.Value.enabled = !Config.Value.enabled; return Config.Value.enabled; },
            new Ref<bool>(ref enabled),
            ((dc.ui.OptionsBase)options).scrollerFlow);

        ((dc.ui.OptionsBase)options).updateScroller();
    }

    void IOnGameEndInit.OnGameEndInit()
    {
        try
        {
            Logger.Information("Commencing loading of mod resources");
            var resPath = Info.ModRoot!.GetFilePath("res.pak");
            if (string.IsNullOrWhiteSpace(resPath))
            {
                Logger.Information("Resource path is empty");
                return;
            }
            FsPak.Instance.FileSystem.loadModPak(resPath.AsHlxStr());
            Logger.Information("ResPak loaded: " + resPath);

            var json = CDBManager.Class.instance.getAlteredCDB();
            dc.Data.Class.loadJson(json, default);
            Logger.Information("CDB data loaded");

            Logger.Information("等待 CDB 就绪... (1=腐化牢房 2=深层牢房 3=Boss)");
        }
        catch (Exception ex)
        {
            Logger.Error("An error occurred while loading module resources.", ex);
        }
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        if (!Config.Value.enabled) return;

        // Delegate injection to LevelManager
        _levelManager?.TryInject();

        // Delegate fog application to LevelManager
        _levelManager?.TryApplyFog();

        // Check mimic cleanup
        _entityManager?.Update();

        // Keyboard shortcuts: 1=腐化牢房 2=深层牢房 3=Boss
        if (KeyPressed(VK_1, ref _k1WasDown)) On1Pressed();
        if (KeyPressed(VK_2, ref _k2WasDown)) On2Pressed();
        if (KeyPressed(VK_3, ref _k3WasDown)) On3Pressed();
    }

    // ═══════════════════════════════════════════
    // Keyboard shortcuts
    // ═══════════════════════════════════════════

    private void On1Pressed()
    {
        try
        {
            Logger.Information("1 → 腐化牢房");
            _levelManager?.PatchLevelLogo();
            LevelTransition.Class.@goto(GameConstants.Levels.PrisonCorrupt.AsHlxStr());
        }
        catch (Exception ex) { Logger.Error("1 fail", ex); }
    }

    private void On2Pressed()
    {
        try
        {
            if (!_levelManager!.IsCDBReady)
            {
                Logger.Information("2: CDB 未就绪");
                return;
            }
            Logger.Information("2 → 深层腐化牢房");
            _levelManager.PatchLevelLogo();
            Ref<bool> nd = default;
            var trans = new LevelTransition(GameConstants.Levels.PrisonCorruptDepths.AsHlxStr(), null, null, null, nd);
            if (trans != null) trans.loadNewLevel();
        }
        catch (Exception ex) { Logger.Error("2 fail", ex); }
    }

    private void On3Pressed()
    {
        try
        {
            if (!_levelManager!.IsCDBReady)
            {
                Logger.Information("3: CDB 未就绪");
                return;
            }
            Logger.Information("3 → Boss 房间");
            _levelManager.PatchLevelLogo();
            Ref<bool> nd = default;
            var trans = new LevelTransition(GameConstants.Levels.DeathArena.AsHlxStr(), null, null, null, nd);
            if (trans != null) trans.loadNewLevel();
        }
        catch (Exception ex) { Logger.Error("3 fail", ex); }
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
