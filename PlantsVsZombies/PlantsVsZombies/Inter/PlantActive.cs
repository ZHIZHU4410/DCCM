using dc;
using dc.pr;
using dc.tool;
using dc.tool.hero.activeSkills;
using EnemiesVsEnemies.PlantsVsZombies.Core;

namespace EnemiesVsEnemies.PlantsVsZombies.Inter
{
    public class PlantActive : Active
    {
        public PlantActive(Hero h, int cx, int cy, InventItem i) : base(h, cx, cy, i)
        {
            PlantSpawner.Instance?.RequestPlacement(h._level, cx, cy);
            destroy();
        }

        public override void initGfx()
        {
            base.initGfx();
            base.initSprite(Assets.Class.gameElements, "switchBiomeMobs".ToHaxeString(), null, null, null, null, null, null);
        }
    }
}
