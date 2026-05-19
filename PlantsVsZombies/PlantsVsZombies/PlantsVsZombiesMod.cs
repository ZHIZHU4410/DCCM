using System.Runtime.InteropServices;
using dc.en;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Storage;
using Serilog;

namespace EnemiesVsEnemies.PlantsVsZombies
{
    public class PlantsVsZombiesMod : ModBase, IOnHeroUpdate
    {
        public static ILogger Logger = null!;
        public static Config<Configuration.PlantModConfig> config = new("PlantsVsZombiesConfig");

        private const int VK_T = 0x54;
        private bool _isTKeyPressed = false;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public PlantsVsZombiesMod(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            // 使用主 mod 的 logger 以便输出到同一日志体系
            Logger = EnemiesVsEnemies.EnemiesVsZombiesMod.GetLogger;

            // 载入配置（若无则会创建默认值）
            config.Load();

            InitializeManagers();
            LogInfo("PlantsVsZombiesMod 初始化（骨架）");
        }

        private void InitializeManagers()
        {
            Core.PlantSpawner.Instance = new Core.PlantSpawner();
        }

        public static Core.PlantSpawner GetPlantSpawner() => Core.PlantSpawner.Instance!;
        public static void LogInfo(string msg) => Logger.LogInformation($"[PVZ] {msg}");

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Core.PlantSpawner.Instance?.Update();

            var hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero._level == null) return;

            bool isTPressedNow = GetAsyncKeyState(VK_T) < 0;
            if (isTPressedNow && !_isTKeyPressed)
            {
                StartBattle(hero);
            }
            _isTKeyPressed = isTPressedNow;
        }

        private void StartBattle(Hero hero)
        {
            LogInfo("按下 T：战斗开始！");
            var lvl = hero._level;
            for (int i = -1; i <= 1; i++)
            {
                Core.PlantSpawner.Instance?.SpawnPlant(lvl, hero.cx + i * 2, hero.cy, "Peashooter");
            }
            lvl.fx.shakeS(0.3, 0.06, 0.06);
        }
    }
}
