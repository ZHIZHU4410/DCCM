namespace PrisonCourtyardtest.Utils;

public static class GameConstants
{
    public static class Levels
    {
        public const string PrisonCourtyard = "PrisonCourtyard";
        public const string PrisonCourtyardTest = "PrisonCourtyardTest";
        public const string PrisonCourtyardTestBiome = "PrisonCourtyardTestBiome";
        public const string T_Roof = "T_Roof";
        public const string PrisonDepths = "PrisonDepths";
    }

    // Loot balance settings (PrisonCourtyard-level scaling)
    public const int BaseLootLevel = 2;
    public const int MinGold = 1500;
    public const double MobDensity = 0.9;
    public const double EliteWanderChance = 0.15;
    public const double EliteRoomChance = 0.5;
    public const double CellBonus = 0.1;
    public const double TripleUps = 1;
    public const double DoubleUps = 1;
    public const double QuarterUpsBC3 = 1;
    public const double QuarterUpsBC4 = 0;
}
