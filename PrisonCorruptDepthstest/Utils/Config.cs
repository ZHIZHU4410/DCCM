namespace PrisonCorruptDepthstest.Utils;

public class MainLevelConfig
{
    public string LevelId { get; set; } = "PrisonCorruptDepths";
    public string DisplayName { get; set; } = "深层腐化牢房";
    public string Biome { get; set; } = "PrisonCorruptDepthsBiome";
    public bool Enabled { get; set; } = true;
}

public class DeathArenaConfig
{
    public string LevelId { get; set; } = "DeathArena";
    public string DisplayName { get; set; } = "死亡竞技场";
    public string Biome { get; set; } = "PrisonCourtyard2";
    public string NextLevel { get; set; } = "T_Bridge";
}

public class TransitionLevelConfig
{
    public string LevelId { get; set; } = "T_PrisonCorruptDepths";
    public string DisplayName { get; set; } = "通往深层腐化牢房";
    public string Biome { get; set; } = "PrisonCorrupt";
    public string NextLevel { get; set; } = "PrisonCorruptDepths";
}

public class PrisonCorruptModConfig
{
    public string LevelId { get; set; } = "PrisonCorrupt";
    public bool AddBranchDoor { get; set; } = true;
}
