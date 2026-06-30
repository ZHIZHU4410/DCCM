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
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using PrisonCorruptDepthstest.Core.Configuration;
using PrisonCorruptDepthstest.Utils;

namespace PrisonCorruptDepthstest.EntryPoint;

public class ModInitializer(ModInfo info) : ModBase(info), IOnGameEndInit, IOnHeroUpdate
{
    public static ModCore.Storage.Config<CoreConfig> Config = new("PrisonCorruptDepthstestCoreConfig");

    // Keyboard key codes
    private const int VK_P = 0x50;
    private const int VK_O = 0x4F;
    private const int VK_B = 0x42;
    private const int VK_T = 0x54;

    private LevelManager? _levelManager;
    private bool _pWasDown, _oWasDown, _bWasDown, _tWasDown;

    public override void Initialize()
    {
        base.Initialize();
        Config.Value.debugMode = true;
        Config.Save();

        Logger.Information("Commencing initialisation of PrisonCorruptDepthstest DLC module");

        _ = new RoomGroup(this);
        _ = new DLCLang(this);
        _levelManager = new LevelManager(this);
        _ = new EntityManager(this);

        // Register hooks directly (same pattern as original TestCorruptPlusLevel)
        _levelManager.RegisterHooks();

        Logger.Information("PrisonCorruptDepthstest initialisation complete");
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

            Logger.Information("等待 CDB 就绪... (O=腐化牢房 P=深层牢房 B=Boss)");
        }
        catch (Exception ex)
        {
            Logger.Error("An error occurred while loading module resources.", ex);
        }
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        // Delegate injection to LevelManager
        _levelManager?.TryInject();

        // Delegate fog application to LevelManager
        _levelManager?.TryApplyFog();

        // Keyboard shortcuts
        if (KeyPressed(VK_P, ref _pWasDown)) OnPPressed();
        if (KeyPressed(VK_O, ref _oWasDown)) OnOPressed();
        if (KeyPressed(VK_B, ref _bWasDown)) OnBPressed();
        if (KeyPressed(VK_T, ref _tWasDown)) OnTPressed();
    }

    // ═══════════════════════════════════════════
    // Keyboard shortcuts
    // ═══════════════════════════════════════════

    private void OnOPressed()
    {
        try
        {
            Logger.Information("O → 腐化牢房");
            _levelManager?.PatchLevelLogo();
            LevelTransition.Class.@goto(GameConstants.Levels.PrisonCorrupt.AsHlxStr());
        }
        catch (Exception ex) { Logger.Error("O fail", ex); }
    }

    private void OnTPressed()
    {
        try
        {
            LevelTransition.Class.@goto("SewerShort".AsHlxStr());
        }
        catch { }
    }

    private void OnPPressed()
    {
        try
        {
            if (!_levelManager!.IsCDBReady)
            {
                Logger.Information("P: CDB 未就绪");
                return;
            }
            Logger.Information("P → 深层腐化牢房");
            _levelManager.PatchLevelLogo();
            Ref<bool> nd = default;
            var trans = new LevelTransition(GameConstants.Levels.PrisonCorruptDepths.AsHlxStr(), null, null, null, nd);
            if (trans != null) trans.loadNewLevel();
        }
        catch (Exception ex) { Logger.Error("P fail", ex); }
    }

    private void OnBPressed()
    {
        try
        {
            if (!_levelManager!.IsCDBReady)
            {
                Logger.Information("B: CDB 未就绪");
                return;
            }
            Logger.Information("B → Boss 房间");
            _levelManager.PatchLevelLogo();
            Ref<bool> nd = default;
            var trans = new LevelTransition(GameConstants.Levels.DeathArena.AsHlxStr(), null, null, null, nd);
            if (trans != null) trans.loadNewLevel();
        }
        catch (Exception ex) { Logger.Error("B fail", ex); }
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
