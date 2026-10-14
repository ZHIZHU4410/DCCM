namespace MobDeathSplit;

/// <summary>不依赖游戏对象的规则计算，供菜单、运行逻辑和独立自检共用。</summary>
internal static class SplitPolicy
{
    public const int MaxMenuGenerations = 20;
    public const double MinHealthPercent = 1, MaxHealthPercent = 500;

    public static double NormalizeHealthPercent(double value) =>
        double.IsFinite(value) ? System.Math.Clamp(value, MinHealthPercent, MaxHealthPercent) : 100;

    public static int NormalizeGenerations(int value) =>
        System.Math.Clamp(value, 0, MaxMenuGenerations);

    public static bool CanSplit(int generation, bool kamikaze, SplitSettings settings, bool bossPresent = false)
    {
        // Boss 关卡保护优先于普通怪与自爆蝙蝠的代数设置，包括 0=无限。
        if (bossPresent)
            return false;

        int limit = NormalizeGenerations(kamikaze
            ? settings.KamikazeMaxGenerations : settings.MaxGenerations);
        return limit == 0 || generation < limit;
    }

    public static int ChildHealth(int originalMaxLife, double percent)
    {
        double health = System.Math.Max(1, originalMaxLife) * NormalizeHealthPercent(percent) / 100;
        return (int)System.Math.Clamp(System.Math.Round(health, System.MidpointRounding.AwayFromZero),
            1, int.MaxValue);
    }
}
