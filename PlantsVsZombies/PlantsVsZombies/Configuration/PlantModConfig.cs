using System.Collections.Generic;

namespace EnemiesVsEnemies.PlantsVsZombies.Configuration
{
    public class PlantPreset
    {
        public string Id { get; set; } = string.Empty;
        public int Damage { get; set; } = 10;
        public int FireRate { get; set; } = 60;
    }

    public class PlantModConfig
    {
        public Dictionary<string, PlantPreset> Presets { get; set; } = new();
        public PlantModConfig()
        {
            Presets["Peashooter"] = new PlantPreset { Id = "Peashooter", Damage = 10, FireRate = 60 };
            Presets["Sunflower"] = new PlantPreset { Id = "Sunflower", Damage = 0, FireRate = 120 };
            Presets["Wallnut"] = new PlantPreset { Id = "Wallnut", Damage = 0, FireRate = 180 };
        }
    }
}
