using dc;
using dc.en;
using dc.en.inter;
using dc.en.mob;
using dc.en.mob.boss.death;
using dc.level;
using dc.pr;
using HaxeProxy.Runtime;
using Serilog;

namespace PrisonCorruptDepthstest.EntryPoint;

public class EntityManager
{
    public static ILogger GetLogger = null!;
    private readonly List<ShopMimic> _mimics = new();
    private Level? _arenaLevel;
    private bool _mimicsSpawned;

    public EntityManager(ModInitializer entry)
    {
        GetLogger = entry.Logger;
        GetLogger.Information("Entity Manager initialisation commences");

        Hook_Death.onDie += Hook_Death_onDie;
        GetLogger.Information("Entity hooks registered (Death.onDie → 3x ShopMimic)");
    }

    public void Update()
    {
        if (!_mimicsSpawned || _mimics.Count == 0) return;

        // 检查是否有存活的拟态魔
        bool anyAlive = false;
        foreach (var m in _mimics)
        {
            if (m != null && !m.destroyed)
            {
                anyAlive = true;
                break;
            }
        }

        if (anyAlive)
        {
            // 有怪物存活 → 持续关闭力场（对抗 Death.delayer 开门）
            SetForceFields(true);
        }
        else
        {
            // 全部死亡 → 开门
            GetLogger.Information("All mimics dead — opening doors");
            OpenAllDoors();
            _mimics.Clear();
            _mimicsSpawned = false;
        }
    }

    private void Hook_Death_onDie(Hook_Death.orig_onDie orig, Death self)
    {
        orig(self); // 原版死亡逻辑

        if (_mimicsSpawned) return;
        _mimicsSpawned = true;
        _mimics.Clear();

        try
        {
            var level = self._level;
            _arenaLevel = level;
            int cx = self.cx;
            int cy = self.cy;

            GetLogger.Information("Death died at ({0},{1}), closing force field + spawning 3 mimics", cx, cy);

            // 生成 3 个拟态魔（先生成，避免异步问题）
            int[] offsets = { -2, 0, 2 };
            foreach (var off in offsets)
            {
                var mimic = new ShopMimic(
                    level, cx + off, cy,
                    level.map.mobDmgTier,
                    level.map.mobLifeTier,
                    new MerchantType.Talismans(),
                    new BonusAttackType.All(),
                    null
                );
                mimic.init();
                _mimics.Add(mimic);
                GetLogger.Information("ShopMimic spawned at ({0},{1})", cx + off, cy);
            }

            // 关闭力场（Update 中持续重关，对抗 Death.delayer 开门）
        }
        catch (Exception ex)
        {
            GetLogger.Error(ex, "Failed to spawn ShopMimics");
            CloseAllDoors(); // 恢复
            _mimicsSpawned = false;
        }
    }

    private static void CloseAllDoors()
    {
        SetForceFields(true);
    }

    private static void OpenAllDoors()
    {
        SetForceFields(false);
    }

    private static void SetForceFields(bool closed)
    {
        try
        {
            var level = Game.Class.ME?.curLevel;
            if (level?.entitiesByClass == null) return;

            var arr = level.entitiesByClass.get(47407); // ForceField
            if (arr == null) return;

            dynamic dynArr = arr;
            int len = dynArr.length;
            object[]? items = null;
            try { items = (object[])dynArr.array; } catch { }

            if (items == null) return;

            for (int i = 0; i < len && i < items.Length; i++)
            {
                try
                {
                    if (items[i] is ForceField ff)
                        ff.closed = closed;
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            GetLogger.Error(ex, "SetForceFields failed");
        }
    }
}
