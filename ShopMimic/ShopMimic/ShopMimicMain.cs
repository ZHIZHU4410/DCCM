using dc;
using dc.en;
using dc.en.mob;
using dc.level;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace ShopMimic
{
    /// <summary>
    /// 每只怪物死亡时在其位置生成一个 ShopMimic（拟态魔）
    /// </summary>
    public class ShopMimicMain : ModBase, IOnGameExit
    {
        public ShopMimicMain(ModInfo info) : base(info) { }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onMobDeath += OnHeroKillMob;
            System.Console.WriteLine("[ShopMimic] 每只怪物死亡时生成拟态魔 - 模组已加载");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            System.Console.WriteLine("[ShopMimic] 游戏退出，模组已卸载");
        }

        #endregion

        #region 怪物死亡生成拟态魔

        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob m)
        {
            orig(self, m);

            // 拟态魔死亡不再生成拟态魔，避免无限套娃
            if (m is dc.en.mob.ShopMimic) return;

            try
            {
                var level = m._level;
                if (level == null) return;

                var mimic = new dc.en.mob.ShopMimic(
                    level,
                    m.cx,
                    m.cy,
                    level.map.mobDmgTier,
                    level.map.mobLifeTier,
                    new MerchantType.Talismans(),
                    new BonusAttackType.All(),
                    null
                );
                mimic.init();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ShopMimic] 生成拟态魔失败: {ex.Message}");
            }
        }

        #endregion
    }
}
