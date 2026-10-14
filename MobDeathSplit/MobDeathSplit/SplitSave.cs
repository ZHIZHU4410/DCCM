namespace MobDeathSplit;

/// <summary>按存档记录后代代数及最初母体的血量；死亡后移除其记录。</summary>
public sealed class SplitSave
{
    public Dictionary<string, SplitLineage> Descendants { get; set; } = new();
}

public sealed class SplitLineage
{
    public int Generation { get; set; }
    public int OriginalMaxLife { get; set; }
}
