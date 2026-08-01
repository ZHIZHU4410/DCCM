namespace PrisonCourtyardtest.Utils;

public class MainLevelConfig
{
    public string LevelId { get; set; } = "PrisonCourtyardTest";
    public string DisplayName { get; set; } = "混乱大道";
    public string Biome { get; set; } = "PrisonCourtyardTestBiome";
    public bool Enabled { get; set; } = true;
}

public class T_RoofModConfig
{
    public string LevelId { get; set; } = "T_Roof";
    public string Biome { get; set; } = "PrisonRoof";
    public bool AddBranchDoor { get; set; } = true;
}
