namespace PrisonCorruptDepthstest.Utils;

public static class GameConstants
{
    public static class Levels
    {
        public const string PrisonCorrupt = "PrisonCorrupt";
        public const string PrisonCorruptDepths = "PrisonCorruptDepths";
        public const string PrisonCorruptDepthsBiome = "PrisonCorruptDepthsBiome";
        public const string T_PrisonCorruptDepths = "T_PrisonCorruptDepths";
        public const string DeathArena = "DeathArena";
        public const string T_Bridge = "T_Bridge";
    }

    public static class Atlas
    {
        public const string CustomAtlas = "jidufuh";
    }

    // Mob balance targets: (quantityFactor, minDifficulty) indexed by mob order in CDB
    public static readonly (double qty, int minDiff)[] MobBalanceTargets =
    {
        (1.5, 0),   // Zombie
        (1.8, 0),   // Runner
        (0.6, 0),   // Shielder
        (3.0, 0),   // BatDasher
        (0.8, 0),   // Grenader
        (0.3, 3),   // ClusterGrenader
        (0.5, 2),   // Ninja
        (0.3, 4),   // AggressiveZombie
        (2.0, 1),   // BatKamikaze
    };

    // Fog effect settings
    public const double FogFactor = 1.2;

    // fogColor: (R:0.02, G:0.02, B:0.02, A:3.0)
    public const float FogColorR = 0.02f;
    public const float FogColorG = 0.02f;
    public const float FogColorB = 0.02f;
    public const float FogColorA = 3.0f;

    // Loot balance settings
    public const int BaseLootLevel = 3;
    public const int MinGold = 3000;
    public const double MobDensity = 1.1;
    public const double EliteWanderChance = 0.2;
    public const double EliteRoomChance = 0.8;
    public const double CellBonus = 0.2;
    public const double TripleUps = 2;
    public const double DoubleUps = 2;
    public const double QuarterUpsBC3 = 2;
    public const double QuarterUpsBC4 = 1;
}
