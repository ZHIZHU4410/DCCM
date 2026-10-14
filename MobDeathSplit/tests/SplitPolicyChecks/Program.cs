using MobDeathSplit;
using System.Text.Json;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++;
}

// 两条真实分裂树：普通怪到第二代停止，自爆蝙蝠独立在第一代停止。
var settings = new SplitSettings { ChildHealthPercent = 50, MaxGenerations = 2, KamikazeMaxGenerations = 1 };
int CountTree(bool bat)
{
    var queue = new Queue<int>(); queue.Enqueue(0);
    int born = 0;
    while (queue.TryDequeue(out int generation))
    {
        if (!SplitPolicy.CanSplit(generation, bat, settings)) continue;
        born += 2;
        queue.Enqueue(generation + 1); queue.Enqueue(generation + 1);
    }
    return born;
}
Check(CountTree(false) == 6, "上限2应生成2+4个后代并停止");
Check(CountTree(true) == 2, "自爆蝙蝠应使用独立上限1");
settings.MaxGenerations = 1; settings.KamikazeMaxGenerations = 3;
Check(CountTree(false) == 2 && CountTree(true) == 14, "蝙蝠上限不受普通怪上限约束");
settings.MaxGenerations = 0;
Check(SplitPolicy.CanSplit(200, false, settings), "0表示无限");
settings.KamikazeMaxGenerations = 0; settings.MaxGenerations = 1;
Check(SplitPolicy.CanSplit(200, true, settings), "蝙蝠可以独立无限");

// 回归：即使两种上限均设为无限，Boss 关卡也不允许生成任何后代。
settings.MaxGenerations = 0; settings.KamikazeMaxGenerations = 0;
Check(!SplitPolicy.CanSplit(0, false, settings, bossPresent: true), "Boss召唤普通怪不能分裂");
Check(!SplitPolicy.CanSplit(0, true, settings, bossPresent: true), "Boss召唤自爆蝙蝠也不能分裂");
Check(!SplitPolicy.CanSplit(10, false, settings, bossPresent: true), "已有后代在Boss关卡停止分裂");
Check(SplitPolicy.CanSplit(0, false, settings, bossPresent: false), "离开Boss关卡后恢复普通分裂");

// 任务在死亡时允许，但执行前进入 Boss 关卡时必须被拒绝。
bool queuedBeforeBoss = SplitPolicy.CanSplit(0, false, settings, bossPresent: false);
Check(queuedBeforeBoss && !SplitPolicy.CanSplit(0, false, settings, bossPresent: true),
    "Boss出现后不能执行先前已排队的分裂");

var save = new SplitSave();
save.Descendants["PrisonStart:123:456"] = new SplitLineage { Generation = 2, OriginalMaxLife = 120 };
var reloaded = JsonSerializer.Deserialize<SplitSave>(JsonSerializer.Serialize(save))!;
var lineage = reloaded.Descendants["PrisonStart:123:456"];
Check(lineage.Generation == 2 && lineage.OriginalMaxLife == 120, "谱系数据序列化往返保留代数和母体血量");
settings.MaxGenerations = 2;
Check(!SplitPolicy.CanSplit(lineage.Generation, false, settings), "读回代数不会绕过上限");
for (int generation = 1; generation <= 5; generation++)
    Check(SplitPolicy.ChildHealth(lineage.OriginalMaxLife, 50) == 60, "每代50%均为原始120血量的60");
Check(SplitPolicy.ChildHealth(lineage.OriginalMaxLife, 200) == 240, "修改倍率使用同一最初母体基准");
Check(SplitPolicy.ChildHealth(1, 1) == 1, "低血量仍至少1HP");
Check(SplitPolicy.ChildHealth(int.MaxValue, 500) == int.MaxValue, "血量不溢出");
Check(SplitPolicy.ChildHealth(120, double.NaN) == 120, "非法倍率回退为100%");
Check(SplitPolicy.ChildHealth(120, -1) == 1, "倍率按菜单最小值钳制");
Check(SplitPolicy.NormalizeGenerations(-1) == 0 && SplitPolicy.NormalizeGenerations(100) == 20, "代数输入钳制");
Console.WriteLine($"PASS: {checks} policy and persistence-model checks. No game was launched.");
