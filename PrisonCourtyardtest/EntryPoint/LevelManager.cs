using dc;
using dc.level;
using dc.libs;
using dc.pr;
using HaxeProxy.Runtime;
using Hashlink.Virtuals;
using PrisonCourtyardtest.Core.Interfaces;
using PrisonCourtyardtest.Levels.MainLevel;
using PrisonCourtyardtest.Levels.MainLevel.Structure;
using PrisonCourtyardtest.Levels.T_RoofMod;
using PrisonCourtyardtest.Utils;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace PrisonCourtyardtest.EntryPoint;

public class LevelManager
{
    private readonly Serilog.ILogger _logger;

    private readonly MainLevel _mainLevel = new();
    private readonly T_RoofModLevel _tRoofModLevel = new();
    private readonly ChaosGlitchFx _chaosFx;

    private string? _savedPrisonCourtyardAtlas;
    private bool _injected;

    public LevelManager(ModInitializer entry)
    {
        _logger = entry.Logger;
        _chaosFx = new ChaosGlitchFx(_logger);
        _logger.Information("Level Manager initialisation commences");
    }

    public void UpdateChaosFx(double dt)
    {
        _chaosFx.Update(dt);
    }

    public void RegisterHooks()
    {
        Hook__LevelStruct.get += Hook__LevelStruct_get;
        dc.pr.Hook_Level.init += Hook_Level_init;
        _logger.Information("Level hooks registered (LevelStruct + Level.init)");
    }

    // ═══════════════════════════════════════════
    // Lifecycle — called from ModInitializer OnHeroUpdate
    // ═══════════════════════════════════════════

    public void TryInject()
    {
        if (_injected) return;
        try
        {
            if (Data.Class.level?.byId != null && Data.Class.mob?.byId != null)
            {
                InjectAll();
                _injected = true;
                _logger.Information("Runtime CDB injection complete");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("TryInject failed", ex);
        }
    }

    // ═══════════════════════════════════════════
    // Hook: LevelStruct.get — 只路由自定义关卡，不拦截原版 PrisonCourtyard
    // ═══════════════════════════════════════════

    private LevelStruct Hook__LevelStruct_get(
        Hook__LevelStruct.orig_get orig,
        User user,
        LevelInfo level,
        Rand rng)
    {
        string id = "";
        try { if (level?.id != null) id = Normalize(level.id.ToString()); } catch { }

        if (SameId(id, GameConstants.Levels.T_Roof))
            return _tRoofModLevel.CreateLevelStruct(user, level, rng);

        if (SameId(id, GameConstants.Levels.PrisonCourtyardTest))
            return _mainLevel.CreateLevelStruct(user, level, rng);

        return orig(user, level, rng);
    }

    // ═══════════════════════════════════════════
    // Hook: Level.init — PrisonCourtyardTest 图集替换
    // ═══════════════════════════════════════════

    private void Hook_Level_init(dc.pr.Hook_Level.orig_init orig, dc.pr.Level self)
    {
        string biomeId = "";
        try { biomeId = self.map?.biome?.id?.ToString() ?? ""; } catch { }

        bool isOurLevel = SameId(biomeId, GameConstants.Levels.PrisonCourtyardTestBiome);

        if (isOurLevel && Data.Class.biome?.byId != null)
        {
            // Save original PrisonCourtyard atlas on first use
            if (_savedPrisonCourtyardAtlas == null)
            {
                try
                {
                    var pcKey = "PrisonCourtyard".AsHlxStr();
                    if (Data.Class.biome.byId.exists(pcKey))
                    {
                        object bio = Data.Class.biome.byId.get(pcKey);
                        dynamic dyn = bio;
                        _savedPrisonCourtyardAtlas = dyn.atlasName?.ToString() ?? "prisonCourtyard";
                    }
                }
                catch { _savedPrisonCourtyardAtlas = "prisonCourtyard"; }
                _logger.Information("Saved PrisonCourtyard atlas: " + _savedPrisonCourtyardAtlas);
            }

            // Swap PrisonCourtyard biome's atlas to prisonCourtyardx
            try
            {
                var pcKey = "PrisonCourtyard".AsHlxStr();
                object bio = Data.Class.biome.byId.get(pcKey);
                var refl = new _Reflect();
                refl.setField(bio, "atlasName".AsHlxStr(), "prisonCourtyardx".AsHlxStr());
            }
            catch { }

            // Spoof biome ID so game creates PrisonCourtyard display
            var origBiomeId = self.map.biome.id;
            try { self.map.biome.id = "PrisonCourtyard".AsHlxStr(); } catch { }

            orig(self);

            // Restore biome ID
            try { self.map.biome.id = origBiomeId; } catch { }

            // Restore PrisonCourtyard biome's original atlas
            try
            {
                var pcKey = "PrisonCourtyard".AsHlxStr();
                object bio = Data.Class.biome.byId.get(pcKey);
                var refl = new _Reflect();
                refl.setField(bio, "atlasName".AsHlxStr(), _savedPrisonCourtyardAtlas!.AsHlxStr());
            }
            catch { }
        }
        else
        {
            orig(self); // All other biomes: native init
        }
    }

    // ═══════════════════════════════════════════
    // Runtime CDB injection (clone from existing entries)
    // ═══════════════════════════════════════════

    private void InjectAll()
    {
        try
        {
            InjectTRoofNextLevels();
            FixWorldMapVisibility();
            PatchLevelLogo();
        }
        catch (Exception ex) { _logger.Error("InjectAll failed", ex); }
    }

    // ═══════════════════════════════════════════
    // 确保自定义关卡在世界地图上可见
    // canLevelBeDisplayed 要求: group==0 && (metaFlags & 4) == 0
    // （metaFlags 第 2 位置 1 反而会隐藏该关卡，见 _WorldMap.canLevelBeDisplayed）
    // ═══════════════════════════════════════════

    private void FixWorldMapVisibility()
    {
        try
        {
            if (Data.Class.level?.byId == null || Data.Class.level?.all == null) return;

            foreach (var levelId in new[] {
                GameConstants.Levels.PrisonCourtyardTest
            })
            {
                try
                {
                    var key = levelId.AsHlxStr();
                    if (!Data.Class.level.byId.exists(key)) continue;

                    object lvl = Data.Class.level.byId.get(key);

                    // 确保关卡在 Data.Class.level.all 数组中（WorldMap 从此读取）
                    dynamic all = Data.Class.level.all;
                    bool found = false;
                    int len = 0;
                    try { len = all.length; } catch { }
                    for (int i = 0; i < len; i++)
                    {
                        try
                        {
                            var entry = (Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_)((object[])all.array)[i];
                            string existingId = entry.id?.ToString() ?? "";
                            if (SameId(existingId, levelId)) { found = true; break; }
                        }
                        catch { }
                    }
                    if (!found)
                    {
                        all.push(lvl);
                        _logger.Information("WorldMap: pushed " + levelId + " to level.all");
                    }
                    else
                    {
                        _logger.Information("WorldMap: " + levelId + " already in level.all");
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error("FixWorldMapVisibility failed for " + levelId, ex);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("FixWorldMapVisibility failed", ex);
        }
    }

    // ═══════════════════════════════════════════
    // T_Roof CDB injection — append PrisonCourtyardTest to nextLevels
    // ═══════════════════════════════════════════

    private void InjectTRoofNextLevels()
    {
        try
        {
            var tKey = "T_Roof".AsHlxStr();
            if (Data.Class.level?.byId == null || !Data.Class.level.byId.exists(tKey)) return;

            object tpl = Data.Class.level.byId.get(tKey);
            dynamic dyn = tpl;
            object rawNextLevels;
            try { rawNextLevels = dyn.nextLevels; } catch { return; }
            if (rawNextLevels == null) return;

            dynamic arr = rawNextLevels;
            int len;
            try { len = arr.length; } catch { return; }

            // Check if already added
            for (int i = 0; i < len; i++)
            {
                try
                {
                    var entry = (virtual_gates_level_)((object[])arr.array)[i];
                    string existing = entry.level?.ToString() ?? "";
                    if (SameId(existing, GameConstants.Levels.PrisonCourtyardTest))
                    {
                        _logger.Information("T_Roof nextLevels: already has PrisonCourtyardTest");
                        return;
                    }
                }
                catch { }
            }

            // Create new nextLevel entry and push
            var newEntry = new virtual_gates_level_();
            newEntry.gates = 0;
            newEntry.level = GameConstants.Levels.PrisonCourtyardTest.AsHlxStr();
            arr.push(newEntry);

            _logger.Information("T_Roof nextLevels: added " + GameConstants.Levels.PrisonCourtyardTest);
        }
        catch (Exception ex)
        {
            _logger.Error("InjectTRoofNextLevels failed", ex);
        }
    }

    // ═══════════════════════════════════════════
    // Level Logo patching
    // ═══════════════════════════════════════════

    public void PatchLevelLogo()
    {
        try
        {
            if (Assets.Class?.levelLogos?.textureCoordinateByLevelKind == null ||
                Assets.Class.levelLogos.levelLogoTexture == null) return;

            var logos = Assets.Class.levelLogos;
            string[] fbIds = { "PrisonCourtyard", "PrisonStart", "Ossuary" };
            dc.String? fbKey = null;
            foreach (var f in fbIds)
            {
                fbKey = f.AsHlxStr();
                try { if (logos.textureCoordinateByLevelKind.exists.Invoke(fbKey)) break; } catch { }
                fbKey = null;
            }
            if (fbKey == null) return;

            object coord;
            try { coord = logos.textureCoordinateByLevelKind.get.Invoke(fbKey); } catch { return; }
            if (coord == null) return;

            foreach (var t in new[] {
                GameConstants.Levels.PrisonCourtyardTest
            })
                try { logos.textureCoordinateByLevelKind.set.Invoke(t.AsHlxStr(), coord); } catch { }

            _logger.Information("Logo: registered");
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════

    private static bool SameId(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string s)
    {
        if (s == null) return "";
        s = s.Trim();
        while (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();
        return s;
    }
}
