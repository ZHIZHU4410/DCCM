namespace MobDeathSplit;

/// <summary>选项菜单设置；默认保持旧版的满血、无限递归分裂行为。</summary>
public sealed class SplitSettings
{
    public double ChildHealthPercent { get; set; } = 100;
    public int MaxGenerations { get; set; } = 0;
    public int KamikazeMaxGenerations { get; set; } = 0;
}
