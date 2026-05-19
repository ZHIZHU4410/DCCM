using CoreLibrary.Core.Extensions;
using dc.h2d.Interactive;
using dc.pr;
using EnemiesVsEnemies.PlantsVsZombies.Core;
using ModCore.Modules;

namespace EnemiesVsEnemies.PlantsVsZombies.Inter
{
    public class PlantSelector : dc.h2d.Interactive
    {
        public PlantSelector(Level lvl, int x, int y) : base(lvl, x, y) { }

        public override void init()
        {
            base.init();
        }

        public override void onActivate(Hero by, bool longPress)
        {
            base.onActivate(by, longPress);
            var lvl = ModCore.Modules.Game.Instance.HeroInstance!._level;
            PlantSpawner.Instance?.RequestPlacement(lvl, cx, cy);
            destroy();
        }
    }
}
