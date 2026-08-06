using dc;
using dc.cine;
using dc.en;
using dc.pr;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using HaxeProxy.Runtime.Internals.Cache;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Runtime.InteropServices;

namespace DashShield
{
    /// <summary>
    /// DashShield（突击盾）— 使用它即可直达结局。
    /// 使用突击盾后传送到 QueenArena（女王竞技场），
    /// 并给突击盾 +10000000% 伤害（×100000），由玩家自己手打最终结局。
    /// 不触发任何自动通关/结局动画，完全走游戏原生流程。
    /// </summary>
    public class DashShieldMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private const double TELEPORT_DELAY = 0.3;    // 使用突击盾后的延迟（让攻击先正常结算）
        private const string QUEEN_ARENA_LEVEL_ID = "QueenArena";
        private const long DAMAGE_MULT = 100000;      // 突击盾伤害 ×100000（+10000000%）

        private double _teleportTimer = 0.0;
        private bool _teleporting = false;
        private bool _isPKeyDown = false;
        private const int VK_P = 0x50;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public DashShieldMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUseHook;
            Hook_Entity.applyAttackResult += OnEntityAttackResult;
            System.Console.WriteLine("[DashShield] 模组已加载 — 突击盾传送女王竞技场+伤害×100000 | 按 P 键一键秒杀");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;

            // 到达女王竞技场后解除传送中状态（之后离开可再次传送）
            string? mapId = hero?._level?.map?.id?.ToString();
            if (mapId == QUEEN_ARENA_LEVEL_ID)
            {
                _teleporting = false;
            }

            // 延迟后传送（0.3 秒）
            if (_teleportTimer > 0.0)
            {
                _teleportTimer -= dt;
                if (_teleportTimer <= 0.0)
                {
                    _teleportTimer = 0.0;
                    StartTeleport();
                }
            }

            // P 键一键秒杀
            bool isPPressed = GetAsyncKeyState(VK_P) < 0;
            if (isPPressed && !_isPKeyDown)
            {
                KillAllEnemies();
            }
            _isPKeyDown = isPPressed;
        }

        /// <summary>
        /// 一键秒杀：把当前关卡所有与英雄敌对且可命中的实体直接击杀。
        /// </summary>
        private void KillAllEnemies()
        {
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero._team == null)
                {
                    System.Console.WriteLine("[DashShield] 未找到英雄/队伍，秒杀失败");
                    return;
                }

                int killed = 0;
                var iter = hero._team.opponentsIterator.reset(hero._team);
                while (iter.hasNext())
                {
                    Entity e = iter.next();
                    if (e == null || e.destroyed || e.life <= 0 || !e.canBeHit()) continue;

                    e.life = 0;
                    if (e is dc.en.Mob mob)
                    {
                        try { mob.onDie(); } catch { }
                    }
                    killed++;
                }

                System.Console.WriteLine($"[DashShield] 一键秒杀！已击杀 {killed} 个敌人");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DashShield] 秒杀失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 使用武器时触发：如果是突击盾，0.3 秒后传送至女王竞技场。
        /// </summary>
        private void OnWeaponUseHook(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            orig(self, w, slot);

            if (w is not dc.tool.weap.sh.DashShield) return;

            if (_teleporting)
            {
                System.Console.WriteLine("[DashShield] 传送中，稍后再试");
                return;
            }

            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            string? mapId = hero?._level?.map?.id?.ToString();
            if (mapId == QUEEN_ARENA_LEVEL_ID)
            {
                System.Console.WriteLine("[DashShield] 已在女王竞技场，无需再次传送");
                return;
            }

            _teleportTimer = TELEPORT_DELAY;
            System.Console.WriteLine("[DashShield] 突击盾出击！0.3 秒后传送至女王竞技场");
        }

        /// <summary>
        /// 突击盾的伤害放大：命中的攻击只要来自突击盾，伤害 ×100000。
        /// </summary>
        private void OnEntityAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            try
            {
                if (attack != null &&
                    attack.sourceWeapon is dc.tool.weap.sh.DashShield &&
                    attack.finalDmg > 0)
                {
                    long boosted = (long)attack.finalDmg * DAMAGE_MULT;
                    attack.finalDmg = boosted > int.MaxValue ? int.MaxValue : (int)boosted;
                    attack.rawFinalDmg *= DAMAGE_MULT;
                }
            }
            catch
            {
                // 伤害放大失败不影响游戏
            }

            orig(self, attack);
        }

        private void StartTeleport()
        {
            try
            {
                dc.cine.LevelTransition.Class.@goto(ToHaxeString(QUEEN_ARENA_LEVEL_ID));
                _teleporting = true;
                System.Console.WriteLine("[DashShield] 已传送至女王竞技场，祝手打顺利！");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[DashShield] 传送失败: {ex.Message}");
                _teleportTimer = 0.0;
            }
        }

        private static dc.String ToHaxeString(string s)
        {
            return new HashlinkString(s).AsHaxe<dc.String>();
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUseHook;
            Hook_Entity.applyAttackResult -= OnEntityAttackResult;
            _teleportTimer = 0.0;
            _teleporting = false;
            _isPKeyDown = false;
            System.Console.WriteLine("[DashShield] 游戏退出，模组已卸载");
        }
    }
}
