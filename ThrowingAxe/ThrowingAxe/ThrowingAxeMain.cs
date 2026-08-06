using dc;
using dc.en;
using dc.tool;
using dc.tool.hero;
using dc.tool.weap;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;

namespace ThrowingAxe
{
    /// <summary>
    /// 投掷斧增强 — 每次攻击发射 10 把飞斧（扇形散射），攻速极快。
    /// 参考 Weaponbow（武器使用后多次发射）与 SpeedBladeP（_attackSpeed 攻速）。
    /// </summary>
    public class ThrowingAxeMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private const int AXE_COUNT = 15;           // 每次攻击发射的飞斧总数
        private const double ANGLE_STEP = 0.12;     // 扇形角度间隔（弧度）
        private const double ATTACK_SPEED = 100.0;  // 攻速倍率（很快很快）

        public ThrowingAxeMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUseHook;
            System.Console.WriteLine("[ThrowingAxe] 投掷斧增强已加载 — 每次发射15把飞斧，攻速极快");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero?.weaponsManager == null) return;

            Weapon? w = hero.weaponsManager.lastWeaponUsed;
            if (w is not ThrowingAxeWeapon axe) return;

            // 攻速极快：prepare() 每次攻击会覆盖 _attackSpeed，所以每帧重新设置
            axe._attackSpeed = ATTACK_SPEED;

            // 弹药每帧回满，防止攻速极快时瞬间打空
            try
            {
                if (axe.item != null)
                {
                    axe.item.ammo = axe.item.getMaxAmmo();
                }
            }
            catch
            {
                // 弹药回满失败不影响攻速
            }
        }

        /// <summary>
        /// 武器使用时触发：原版发射 1 把，这里再补 14 把，合计 15 把扇形飞斧。
        /// </summary>
        private void OnWeaponUseHook(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            orig(self, w, slot);

            if (w is not ThrowingAxeWeapon axe) return;
            Hero? hero = axe.owner;
            if (hero == null) return;

            try
            {
                double baseAngle = axe.itemInf.props.ang ?? 0.0;
                for (int i = 1; i < AXE_COUNT; i++)
                {
                    double angle = baseAngle + (i - AXE_COUNT / 2.0) * ANGLE_STEP;
                    new ThrowingAxeEntity(hero, axe, angle).init();
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ThrowingAxe] 发射飞斧失败: {ex.Message}");
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUseHook;
            System.Console.WriteLine("[ThrowingAxe] 游戏退出，模组已卸载");
        }
    }
}
