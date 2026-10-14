using dc.en;
using dc.en.mob;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Events.Interfaces.Game.Save;
using ModCore.Mods;
using ModCore.Storage;
using GameModule = ModCore.Modules.Game;

namespace MobDeathSplit;

public sealed partial class MobDeathSplitMain(ModInfo info) : ModBase(info),
    IOnHeroUpdate, IOnBeforeSavingSave, IOnGameExit
{
    private readonly SaveData<SplitSave> _save = new("MobDeathSplit");
    private readonly Queue<MobSnapshot> _pending = new();
    private readonly HashSet<string> _processing = new();
    private bool _creatingChildren;

    public override void Initialize()
    {
        Hook_Mob.onDie += OnMobDie;
        Logger.Information("怪物死亡分裂：选项菜单可调整血量与代数；Boss 所在关卡自动禁用所有怪物分裂。");
    }

    internal static string Key(Mob mob) =>
        $"{mob._level.map.id}:{mob._level.map.seed}:{mob.__uid}";

    private SplitLineage GetLineage(Mob mob)
    {
        _save.Value.Descendants ??= new();
        if (_save.Value.Descendants.TryGetValue(Key(mob), out var lineage) && lineage != null)
            return lineage;
        // 旧版未保存代数，升级时已存在且没有记录的怪物按初代处理。
        return new SplitLineage { Generation = 0, OriginalMaxLife = mob.maxLife };
    }

    private bool CanSplit(Mob mob)
    {
        if (_creatingChildren || mob.onDieDone || mob.destroyed || mob._level == null ||
            mob._level.map == null || mob.type == null || mob is Boss ||
            mob._team != mob._level.teamMob)
            return false;

        // 与原版 Mob.initLife 的 Boss genTags 判断一致；覆盖不直接继承 Boss 的特殊实体。
        if (mob._infos == null || (mob._infos.genTags & 4) != 0 ||
            mob.type.ToString() == "AdeleScytheGhost")
            return false;

        string key = Key(mob);
        // BatKamikaze 与 GoldenBatKamikaze 均继承自该模板，使用独立上限。
        return !_processing.Contains(key) &&
            SplitPolicy.CanSplit(GetLineage(mob).Generation, mob is BatKamikazeTemplate,
                Settings.Value, mob._level.boss != null);
    }

    private void OnMobDie(Hook_Mob.orig_onDie orig, Mob self)
    {
        MobSnapshot? snapshot = null;
        string? key = null;
        try
        {
            if (self._level?.map != null)
                key = Key(self);
            if (CanSplit(self))
            {
                key = Key(self);
                snapshot = new MobSnapshot(self, GetLineage(self));
                _processing.Add(key);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "读取怪物分裂属性失败，继续执行原版死亡逻辑。");
        }

        // 保留原版掉落、击杀计数、剧情和尸体处理，orig 异常不能被吞掉。
        try
        {
            orig(self);
            if (snapshot != null && self.onDieDone)
                _pending.Enqueue(snapshot);
            // 最终死亡（包括已到上限的后代）不再需要保留谱系；临时死亡仍保留。
            if (key != null && self.onDieDone)
                _save.Value.Descendants?.Remove(key);
        }
        finally
        {
            if (snapshot != null && key != null)
                _processing.Remove(key);
        }
    }

    void IOnHeroUpdate.OnHeroUpdate(double dt)
    {
        FlushPending();
    }

    void IOnBeforeSavingSave.OnBeforeSavingSave(IOnBeforeSavingSave.EventData data)
    {
        // 暂停后立即保存时，英雄更新可能尚未执行；在序列化前完成生成，避免读档丢失后代。
        FlushPending();
    }

    private void FlushPending()
    {
        var hero = GameModule.Instance.HeroInstance;
        if (hero == null)
            return;

        // Level.boss 在原版加载 Boss 时设置，包含读档恢复和战斗过场。
        // 整个 Boss 关卡保持屏蔽，避免阶段切换/胜利清场时继续复制 Boss 依赖实体。
        if (hero._level?.boss != null)
        {
            _pending.Clear();
            return;
        }

        // 固定本帧任务数量；生成期间触发的其他死亡留到下一帧。
        int count = _pending.Count;
        for (int n = 0; n < count; n++)
        {
            var snapshot = _pending.Dequeue();
            if (snapshot.Level != hero._level || snapshot.Level.destroyed ||
                !SplitPolicy.CanSplit(snapshot.NextGeneration - 1, snapshot.IsKamikaze,
                    Settings.Value, snapshot.Level.boss != null))
                continue;

            _creatingChildren = true;
            try
            {
                for (int i = 0; i < 2; i++)
                    SpawnChild(snapshot);
            }
            finally
            {
                _creatingChildren = false;
            }
        }
    }

    private void SpawnChild(MobSnapshot snapshot)
    {
        Mob? child = null;
        try
        {
            int lifeTier = snapshot.LifeTier;
            child = Mob.Class.create(snapshot.Kind, snapshot.Level, snapshot.Cx, snapshot.Cy,
                snapshot.Brutality, new Ref<int>(ref lifeTier));
            if (child == null)
            {
                Logger.Warning("原版工厂无法创建怪物 {Kind}。", snapshot.Kind.ToString());
                return;
            }

            // 每代均以最初母体血量计算，不重复乘百分比；设置修改在生成时读取。
            int childHealth = SplitPolicy.ChildHealth(snapshot.OriginalMaxLife, Settings.Value.ChildHealthPercent);
            _save.Value.Descendants ??= new();
            _save.Value.Descendants[Key(child)] = new SplitLineage
            {
                Generation = snapshot.NextGeneration,
                OriginalMaxLife = snapshot.OriginalMaxLife
            };
            // 工厂内部已 init，不重复调用。
            snapshot.Apply(child, childHealth);
            Logger.Debug("分裂后代 {Kind}, uid={Uid}, HP={Hp}, 精英={Elite}",
                child.type.ToString(), child.__uid, child.maxLife, child.elite);
        }
        catch (Exception e)
        {
            // 避免属性只设置了一半的怪物留在场景里；destroy 不触发死亡奖励。
            if (child != null && !child.destroyed)
            {
                _save.Value.Descendants?.Remove(Key(child));
                try { child.destroy(); }
                catch (Exception cleanup) { Logger.Error(cleanup, "清理未完成的分裂怪物失败。"); }
            }
            Logger.Error(e, "生成分裂后代失败：{Kind}", snapshot.Kind.ToString());
        }
    }

    void IOnGameExit.OnGameExit()
    {
        Hook_Mob.onDie -= OnMobDie;
        _pending.Clear();
        _processing.Clear();
    }
}
