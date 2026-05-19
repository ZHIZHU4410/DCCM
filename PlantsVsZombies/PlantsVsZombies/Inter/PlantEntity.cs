using dc.h2d;
using dc.pr;
using ModCore.Modules;
using System;

namespace EnemiesVsEnemies.PlantsVsZombies.Inter
{
    public class PlantEntity : Interactive
    {
        private readonly string plantType;
        private double attackCooldown = 0;

        public PlantEntity(Level lvl, int x, int y, string plantType) : base(lvl, x, y)
        {
            this.plantType = plantType;
        }

        public override void init()
        {
            base.init();
            initGfx();
        }

        public override void initGfx()
        {
            base.initGfx();
            // 使用占位贴图
            base.initSprite(Assets.Class.gameElements, "switchBiomeMobs".ToHaxeString(), null, null, null, null, null, null);
            spr.set_visible(true);
        }

        public override void postUpdate()
        {
            base.postUpdate();
            attackCooldown -= ftime;
            if (attackCooldown <= 0)
            {
                // TODO: 发射子弹或创建一个友军实体来攻击向前的僵尸（这是占位实现）
                attackCooldown = 60; // 每秒一次（基于引擎时间刻度，这只是示例）
            }
        }
    }
}
