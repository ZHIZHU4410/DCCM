using dc;
using dc.en;
using dc.en.active;
using dc.en.dookuInteractions;
using dc.en.hero;
using dc.en.inter;
using dc.en.mob;
using dc.hl;
using dc.hl.types;
using dc.level;
using dc.libs.heaps.slib;
using dc.pow;
using dc.pr;
using dc.pr.infection;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero.activeSkills;
using dc.tool.mainSkills;
using dc.tool.mod.script;
using dc.tool.weap;
using dc.ui;
using dc.ui.sel;
using Hashlink;
using Hashlink.Proxy;
using Hashlink.Proxy.Clousre;
using Hashlink.Proxy.DynamicAccess;
using Hashlink.Proxy.Objects;
using Hashlink.Proxy.Values;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AutoParry
{
    /// <summary>
    /// 自动盾反模组：按下 T 键切换自动盾反模式。
    /// 开启后，持盾状态下受到攻击时自动触发完美弹反，
    /// 包含：格挡动画、反击伤害、子弹反射、手雷弹回。
    /// </summary>
    public class AutoParryMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private bool _autoParryEnabled = false;
        private bool _isTKeyPressed = false;
        private const int VK_T = 0x54;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public AutoParryMain(ModInfo info) : base(info) { }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();
            Hook_BaseShield.onOwnerAttackResultReceived += OnShieldAttackResult;
            // 核心：在伤害生效前拦截，防止扣血
            Hook_Entity.applyAttackResult += OnEntityApplyAttackResult;

            System.Console.WriteLine("[AutoParry] 自动盾反模组已加载，按 T 键切换");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            bool isTPressedNow = GetAsyncKeyState(VK_T) < 0;
            if (isTPressedNow && !_isTKeyPressed)
            {
                _autoParryEnabled = !_autoParryEnabled;
                System.Console.WriteLine($"[AutoParry] 自动盾反已{(_autoParryEnabled ? "开启" : "关闭")}");
            }
            _isTKeyPressed = isTPressedNow;
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_BaseShield.onOwnerAttackResultReceived -= OnShieldAttackResult;
            Hook_Entity.applyAttackResult -= OnEntityApplyAttackResult;
            System.Console.WriteLine("[AutoParry] 游戏退出，资源清理完毕");
        }

        #endregion

        #region 钩子实现

        /// <summary>
        /// 实体受击钩子：伤害生效前拦截。
        /// 只在自动盾反开启且玩家持有盾牌时工作，阻挡伤害并交给 OnShieldAttackResult 完成弹反反馈。
        /// </summary>
        private void OnEntityApplyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            // 检查受击者是玩家英雄
            Hero? hero = attack?.lastHitTarget as Hero;
            if (hero == null && self is Hero s)
                hero = s;

            // 条件：盾反开启 + 玩家持盾 + 攻击源存在 + 非陷阱伤害
            bool shouldBlock = _autoParryEnabled
                && hero != null
                && attack != null
                && attack.source != null
                && !attack.hasTag(7)
                && !attack.hasTag(29);

            if (shouldBlock)
            {
                // 不调用 orig() = 伤害被完全阻挡，不扣血
                // OnShieldAttackResult 会在此之后自动被引擎触发，执行弹反动画/反击
                return;
            }

            orig(self, attack);
        }

        /// <summary>
        /// 盾牌受击回调：持盾时自动触发完美弹反。
        /// 包含：格挡动画、反击伤害、子弹反射、手雷弹回。
        /// </summary>
        private void OnShieldAttackResult(Hook_BaseShield.orig_onOwnerAttackResultReceived orig, BaseShield self, AttackData attack)
        {
            bool shouldAutoParry = _autoParryEnabled
                && self?.owner != null
                && attack != null
                && attack.source != null
                && !attack.hasTag(7)
                && !attack.hasTag(29);

            if (!shouldAutoParry)
            {
                orig(self, attack);
                return;
            }

            try
            {
                self.owner.dir = -attack.source.dir;
                self.startParry();
                self.triggerParryFeedbacks();
                self.applyStunAndBumpFromParry(attack);
                self.interrupt();
                self.requireRelease = true;
                self.owner.unlockControls();
                attack.removeTag(7);
                self.owner.recoil(attack.dirSourceToTarget() * 7);
                self.onShieldBlock(attack, true);

                if (attack.carrier != null)
                {
                    if (attack.carrier is Bullet bullet)
                        self.counterBullet(attack, bullet, true);
                    else if (attack.carrier is Grenade grenade)
                        self.counterGrenade(grenade);
                }

                double tempValue = self.item?.getShieldAbsorb() ?? 0;
                var ignoreResist = new Ref<double>(ref tempValue);
                self.owner.setAffectS(98, 0.5, ignoreResist, null);
                self.owner.setAffectS(96, 0.5, ignoreResist, null);
                self.shieldCounterAttack(attack, true);
                attack.hitResult = new HitResult.Block();
                self.owner?.spr?.get_anim()?.playCustomSequence(self.parryAnimId, 0, 4, null);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[AutoParry] 弹反处理异常: {ex.Message}");
                orig(self, attack);
            }
        }

        #endregion
    }
}