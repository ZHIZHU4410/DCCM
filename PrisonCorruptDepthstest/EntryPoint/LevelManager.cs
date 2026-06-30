using dc;
using dc.level;
using dc.libs;
using dc.pr;
using HaxeProxy.Runtime;
using Hashlink.Virtuals;
using PrisonCorruptDepthstest.Core.Interfaces;
using PrisonCorruptDepthstest.Levels.MainLevel;
using PrisonCorruptDepthstest.Levels.TransitionLevel;
using PrisonCorruptDepthstest.Levels.PrisonCorruptMod;
using PrisonCorruptDepthstest.Levels.DeathArena;
using PrisonCorruptDepthstest.Levels.MainLevel.Structure;
using PrisonCorruptDepthstest.Levels.TransitionLevel.Structure;
using PrisonCorruptDepthstest.Levels.PrisonCorruptMod.Structure;
using PrisonCorruptDepthstest.Levels.DeathArena.Structure;
using PrisonCorruptDepthstest.Levels.Display;
using PrisonCorruptDepthstest.Utils;
using LevelInfo = Hashlink.Virtuals.virtual_baseLootLevel_biome_bonusTripleScrollAfterBC_cellBonus_dlc_doubleUps_eliteRoomChance_eliteWanderChance_flagsProps_group_icon_id_index_loreDescriptions_mapDepth_minGold_mobDensity_mobs_name_nextLevels_parallax_props_quarterUpsBC3_quarterUpsBC4_specificLoots_specificSubBiome_transitionTo_tripleUps_worldDepth_;

namespace PrisonCorruptDepthstest.EntryPoint;

public class LevelManager
{
    private readonly Serilog.ILogger _logger;

    private readonly MainLevel _mainLevel = new();
    private readonly TransitionLevel _transitionLevel = new();
    private readonly PrisonCorruptModLevel _prisonModLevel = new();
    private readonly DeathArenaLevel _deathArenaLevel = new();

    private string? _savedPrisonCorruptAtlas;
    private bool _injected;
    private bool _fogApplied;
    private bool _mainInjected;
    private bool _cdbReady;

    public bool IsCDBReady => _cdbReady;

    public LevelManager(ModInitializer entry)
    {
        _logger = entry.Logger;
        _logger.Information("Level Manager initialisation commences");
    }

    public void RegisterHooks()
    {
        Hook__LevelStruct.get += Hook__LevelStruct_get;
        dc.pr.Hook_Level.init += Hook_Level_init;
        _logger.Information("Level hooks registered (LevelStruct + Level.init)");
    }

    // ═══════════════════════════════════════════
    // Lifecycle — called from ModInitializer
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
                _cdbReady = SafeLevelExists(GameConstants.Levels.PrisonCorruptDepths);
                _logger.Information("自动注入完成 CDB=" + _cdbReady);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("TryInject failed", ex);
        }
    }

    public void TryApplyFog()
    {
        if (_fogApplied) return;
        ApplyBlackFog();
    }

    // ═══════════════════════════════════════════
    // Hooks
    // ═══════════════════════════════════════════

    private LevelStruct Hook__LevelStruct_get(
        Hook__LevelStruct.orig_get orig,
        User user,
        LevelInfo level,
        Rand rng)
    {
        string id = "";
        try { if (level?.id != null) id = Normalize(level.id.ToString()); } catch { }

        _fogApplied = false;

        // CDB 已定义每个 biome 的 atlas，不再需要运行时切换
        if (SameId(id, GameConstants.Levels.PrisonCorrupt))
        {
            return _prisonModLevel.CreateLevelStruct(user, level, rng);
        }
        if (SameId(id, GameConstants.Levels.PrisonCorruptDepths) || SameId(id, GameConstants.Levels.DeathArena))
        {
            if (SameId(id, GameConstants.Levels.PrisonCorruptDepths))
                return _mainLevel.CreateLevelStruct(user, level, rng);
            else
                return _deathArenaLevel.CreateLevelStruct(user, level, rng);
        }
        if (SameId(id, GameConstants.Levels.T_PrisonCorruptDepths))
            return _transitionLevel.CreateLevelStruct(user, level, rng);

        return orig(user, level, rng);
    }

    // ═══════════════════════════════════════════
    // Hook_Level.init — pre-patch PrisonCorrupt biome atlas
    // for our PrisonCorruptDepthsBiome, then call game's native init.
    // This avoids copying the entire Level.init (like dlc does)
    // while still allowing our biome to use jidufuh atlas + Prison disp.
    // ═══════════════════════════════════════════

    private void Hook_Level_init(dc.pr.Hook_Level.orig_init orig, dc.pr.Level self)
    {
        string biomeId = "";
        try { biomeId = self.map?.biome?.id?.ToString() ?? ""; } catch { }

        bool isOurBiome = SameId(biomeId, GameConstants.Levels.PrisonCorruptDepthsBiome);

        if (isOurBiome && Data.Class.biome?.byId != null)
        {
            // Save original PrisonCorrupt atlas on first use
            if (_savedPrisonCorruptAtlas == null)
            {
                try
                {
                    var pcKey = "PrisonCorrupt".AsHlxStr();
                    if (Data.Class.biome.byId.exists(pcKey))
                    {
                        object bio = Data.Class.biome.byId.get(pcKey);
                        dynamic dyn = bio;
                        _savedPrisonCorruptAtlas = dyn.atlasName?.ToString() ?? "prison_L2";
                    }
                }
                catch { _savedPrisonCorruptAtlas = "prison_L2"; }
                _logger.Information("Saved PrisonCorrupt atlas: " + _savedPrisonCorruptAtlas);
            }

            // Patch: swap PrisonCorrupt biome's atlas to jidufuh
            try
            {
                var pcKey = "PrisonCorrupt".AsHlxStr();
                object bio = Data.Class.biome.byId.get(pcKey);
                var refl = new _Reflect();
                refl.setField(bio, "atlasName".AsHlxStr(), "jidufuh".AsHlxStr());
            }
            catch { }

            // Patch biome ID on the map so game's native disp lookup finds "PrisonCorrupt"
            var origBiomeId = self.map.biome.id;
            try { self.map.biome.id = "PrisonCorrupt".AsHlxStr(); } catch { }

            orig(self); // Game creates Prison disp + loads jidufuh atlas

            // Restore biome ID
            try { self.map.biome.id = origBiomeId; } catch { }

            // Restore PrisonCorrupt biome's original atlas
            try
            {
                var pcKey = "PrisonCorrupt".AsHlxStr();
                object bio = Data.Class.biome.byId.get(pcKey);
                var refl = new _Reflect();
                refl.setField(bio, "atlasName".AsHlxStr(), _savedPrisonCorruptAtlas!.AsHlxStr());
            }
            catch { }
        }
        else
        {
            orig(self); // All other biomes: native init
        }
    }

    // ═══════════════════════════════════════════
    // Injection
    // ═══════════════════════════════════════════

    private void InjectAll()
    {
        try
        {
            var refl = new _Reflect();
            if (Data.Class.level?.byId == null || Data.Class.mob?.byId == null)
            {
                _logger.Information("Inject: CDB 未就绪");
                return;
            }

            InjectMainLevel(refl);
            PatchLevelLogo();
        }
        catch (Exception ex)
        {
            _logger.Error("InjectAll failed", ex);
        }
    }

    private void InjectMainLevel(_Reflect refl)
    {
        if (_mainInjected) return;

        var tKey = "PrisonCourtyard".AsHlxStr();
        if (!Data.Class.level.byId.exists(tKey))
        {
            _logger.Information("Inject: PrisonCourtyard 不存在");
            return;
        }

        // ── PrisonCorruptDepths main level ──
        var mainKey = GameConstants.Levels.PrisonCorruptDepths.AsHlxStr();
        if (!Data.Class.level.byId.exists(mainKey))
        {
            object mainTpl = Data.Class.level.byId.get(tKey);
            refl.setField(mainTpl, "id".AsHlxStr(), GameConstants.Levels.PrisonCorruptDepths.AsHlxStr());
            refl.setField(mainTpl, "group".AsHlxStr(), 0);
            refl.setField(mainTpl, "name".AsHlxStr(), "深层腐化牢房".AsHlxStr());
            refl.setField(mainTpl, "biome".AsHlxStr(), GameConstants.Levels.PrisonCorruptDepthsBiome.AsHlxStr());

            BalanceLevelMobs(refl, mainTpl);
            SetLootBalance(refl, mainTpl);
            FixNextToSingle(refl, mainTpl, GameConstants.Levels.DeathArena);

            Data.Class.level.byId.set(mainKey, mainTpl);
            _logger.Information("Inject: " + GameConstants.Levels.PrisonCorruptDepths + " exit → " + GameConstants.Levels.DeathArena);
        }
        else
        {
            _logger.Information("Inject: " + GameConstants.Levels.PrisonCorruptDepths + " 已存在");
        }

        // ── Transition level ──
        var transKey = GameConstants.Levels.T_PrisonCorruptDepths.AsHlxStr();
        if (!Data.Class.level.byId.exists(transKey))
        {
            var srcKey = "T_OssuaryAfterPrison".AsHlxStr();
            if (Data.Class.level.byId.exists(srcKey))
            {
                object transTpl = Data.Class.level.byId.get(srcKey);
                refl.setField(transTpl, "id".AsHlxStr(), transKey);
                refl.setField(transTpl, "name".AsHlxStr(), "通往深层腐化牢房".AsHlxStr());
                FixNextToSingle(refl, transTpl, GameConstants.Levels.PrisonCorruptDepths);
                Data.Class.level.byId.set(transKey, transTpl);
                _logger.Information("Inject: " + GameConstants.Levels.T_PrisonCorruptDepths + " created");
            }
        }

        _mainInjected = true;
    }

    // ═══════════════════════════════════════════
    // Mob balance
    // ═══════════════════════════════════════════

    private void BalanceLevelMobs(_Reflect refl, object levelObj)
    {
        var targets = GameConstants.MobBalanceTargets;

        try
        {
            dynamic dynLvl = levelObj;
            object rawMobs;
            try { rawMobs = dynLvl.mobs; } catch { return; }
            if (rawMobs == null) return;

            dynamic mobsArr = rawMobs;
            int len;
            try { len = mobsArr.length; } catch { return; }

            object[]? items = null;
            try { items = (object[])mobsArr.array; }
            catch
            {
                try
                {
                    var arrKey = "array".AsHlxStr();
                    items = (object[]?)refl.GetType().GetMethod("getField")?.Invoke(refl, new[] { rawMobs, arrKey });
                }
                catch { }
            }
            if (items == null) return;

            int changed = 0;
            for (int i = 0; i < len && i < items.Length && i < targets.Length; i++)
            {
                try
                {
                    object entry = items[i];
                    if (entry == null) continue;
                    refl.setField(entry, "quantityFactor".AsHlxStr(), targets[i].qty);
                    refl.setField(entry, "minDifficulty".AsHlxStr(), targets[i].minDiff);
                    changed++;
                }
                catch { }
            }
            _logger.Information("Mobs: " + changed + "/" + len + " 条目已平衡");
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // Loot balance
    // ═══════════════════════════════════════════

    private void SetLootBalance(_Reflect refl, object levelObj)
    {
        var fields = new (string name, object value)[] {
            ("baseLootLevel", GameConstants.BaseLootLevel),
            ("minGold", GameConstants.MinGold),
            ("mobDensity", GameConstants.MobDensity),
            ("eliteWanderChance", GameConstants.EliteWanderChance),
            ("eliteRoomChance", GameConstants.EliteRoomChance),
            ("cellBonus", GameConstants.CellBonus),
            ("tripleUps", GameConstants.TripleUps),
            ("doubleUps", GameConstants.DoubleUps),
            ("quarterUpsBC3", GameConstants.QuarterUpsBC3),
            ("quarterUpsBC4", GameConstants.QuarterUpsBC4),
        };

        int set = 0;
        foreach (var f in fields)
        {
            try
            {
                refl.setField(levelObj, f.name.AsHlxStr(), f.value);
                set++;
            }
            catch { }
        }
        _logger.Information("Loot: " + set + "/" + fields.Length + " 字段已设置 (Ossuary标准)");
    }

    // ═══════════════════════════════════════════
    // NextLevels fix
    // ═══════════════════════════════════════════

    private void FixNextToSingle(_Reflect refl, object levelObj, string target)
    {
        try
        {
            dynamic dyn = levelObj;
            object raw;
            try { raw = dyn.nextLevels; } catch { return; }
            if (raw == null) return;
            dynamic arr = raw;
            int len;
            try { len = arr.length; } catch { return; }
            if (len == 0) return;

            object[]? items = null;
            try { items = (object[])arr.array; } catch { return; }
            if (items == null || items.Length == 0) return;

            object entry = items[0];
            refl.setField(entry, "level".AsHlxStr(), target.AsHlxStr());
            while (true) { try { if (arr.length <= 1) break; arr.pop(); } catch { break; } }
            _logger.Information("NextLevels → " + target);
        }
        catch { }
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
            string[] fbIds = { "Ossuary", "PrisonCorrupt", "PrisonStart" };
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
                GameConstants.Levels.PrisonCorruptDepths,
                GameConstants.Levels.T_PrisonCorruptDepths,
                GameConstants.Levels.PrisonCorrupt,
                GameConstants.Levels.DeathArena
            })
                try { logos.textureCoordinateByLevelKind.set.Invoke(t.AsHlxStr(), coord); } catch { }

            _logger.Information("Logo: ok");
        }
        catch { }
    }

    // ═══════════════════════════════════════════
    // Apply black fog (called from TryApplyFog)
    // ═══════════════════════════════════════════

    private void ApplyBlackFog()
    {
        try
        {
            if (!_injected) return;
            var game = dc.pr.Game.Class?.ME;
            var level = game?.curLevel;
            if (level?.scroller == null || level.map == null) return;

            string lid = "";
            try { lid = level.map.id?.ToString() ?? ""; } catch { }
            if (string.IsNullOrEmpty(lid)) return;

            if (!SameId(lid, GameConstants.Levels.PrisonCorruptDepths) &&
                !SameId(lid, GameConstants.Levels.DeathArena)) return;

            _fogApplied = true;
            var s = level.scroller;
            var refl = new _Reflect();

            refl.setField(s, "fogFactor".AsHlxStr(), GameConstants.FogFactor);
            dynamic fc = s.fogColor;
            fc.x = GameConstants.FogColorR;
            fc.y = GameConstants.FogColorG;
            fc.z = GameConstants.FogColorB;
            fc.w = GameConstants.FogColorA;

            _logger.Information("BlackFog: applied to " + lid);
        }
        catch (Exception ex)
        {
            _logger.Error("BlackFog err", ex);
            _fogApplied = true;
        }
    }

    // ═══════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════

    private bool SafeLevelExists(string id)
    {
        try
        {
            return Data.Class.level?.byId != null && Data.Class.level.byId.exists(id.AsHlxStr());
        }
        catch { return false; }
    }

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
