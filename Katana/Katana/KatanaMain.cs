using dc;
using dc.cine;
using dc.en;
using dc.en.active;
using dc.en.bu;
using dc.en.inter;
using dc.en.mob;
using dc.en.mob.boss;
using dc.en.mob.boss.giant;
using dc.en.pet;
using dc.h2d;
using dc.h2d.col;
using dc.h3d.impl;
using dc.h3d.mat;
using dc.h3d.pass;
using dc.haxe.io;
using dc.hl;
using dc.hl.types;
using dc.hxbit.enumSer;
using dc.hxd;
using dc.hxd.fs;
using dc.hxd.res;
using dc.hxd.snd;
using dc.hxsl;
using dc.level;
using dc.light;
using dc.pow;
using dc.pr;
using dc.shader;
using dc.tool;
using dc.tool.atk;
using dc.tool.hero;
using dc.tool.hero.activeSkills;
using dc.tool.mod.script;
using dc.tool.weap;
using dc.ui;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using HaxeProxy.Runtime.Internals.Cache;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Katana
{
    public class KatanaMain : ModBase, IOnHeroUpdate, IOnGameExit
    {
        public KatanaMain(ModInfo info) : base(info) { }

        // ---------- 平砍无敌帧相关 ----------
        private double _invincibleTimer = 0.0;
        // 无敌帧持续时间：覆盖 Katana 平砍动画（参考 onExecute 中普通攻击的 bump/slash 帧）
        private const double INVINCIBLE_DURATION = 0.5;

        // ---------- 残影参数（无敌帧期间附带残影效果） ----------
        private double _trailAccumulator = 0.0;
        private const double TRAIL_INTERVAL = 0.07;
        private const double TRAIL_ALPHA = 0.5;
        private const double TRAIL_DURATION = 0.3;

        public override void Initialize()
        {
            base.Initialize();

            // 钩子：检测 Katana 武器使用
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUseHook;
            // 钩子：阻止无敌帧期间的伤害
            Hook_Entity.applyAttackResult += Hook_Entity_applyAttackResult;
            Hook_Hero.applyAttackResult += Hook_Hero_applyAttackResult;

            global::System.Console.WriteLine("Katana Mod");
        }

        /// <summary>
        /// 武器使用时触发。检测是否为 Katana 平砍（非蓄力冲刺），
        /// 若是则激活短暂无敌帧。
        /// </summary>
        private void OnWeaponUseHook(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            orig(self, w, slot);

            if (self.hero == null) return;

            // 通过类型判断是否为 Katana 武器
            bool isKatana = w is dc.tool.weap.Katana;
            // 备选：通过物品 ID 判断
            if (!isKatana && w?.item?._itemData?.id != null)
            {
                isKatana = w.item._itemData.id.ToString() == "Katana";
            }

            if (!isKatana) return;

            var katana = w as dc.tool.weap.Katana;
            // 仅平砍（非蓄力冲刺）时添加无敌帧
            // Katana 的 nextIsChargeAtk 为 true 时表示蓄力冲刺攻击
            if (katana != null && katana.nextIsChargeAtk)
                return;

            Hero hero = self.hero;

            // 激活无敌帧
            _invincibleTimer = INVINCIBLE_DURATION;
            _trailAccumulator = 0.0;

            double ignore = 0;
            var ignoreRef = new Ref<double>(ref ignore);
            // affectS id 48 = 无敌
            hero.setAffectS(48, INVINCIBLE_DURATION, ignoreRef, null);
        }

        /// <summary>
        /// 实体受到攻击结果时触发。若玩家处于无敌帧中，阻止伤害应用。
        /// </summary>
        private void Hook_Entity_applyAttackResult(Hook_Entity.orig_applyAttackResult orig, Entity self, AttackData attack)
        {
            // 判断受击者是否为玩家英雄
            Hero? targetHero = self as Hero;
            if (targetHero == null && attack?.lastHitTarget is Hero hitHero)
                targetHero = hitHero;

            if (targetHero != null && _invincibleTimer > 0)
            {
                // 无敌帧中，不应用伤害
                return;
            }

            orig(self, attack);
        }

        /// <summary>
        /// 英雄受到攻击结果时触发。若处于无敌帧中，阻止伤害应用。
        /// </summary>
        private void Hook_Hero_applyAttackResult(Hook_Hero.orig_applyAttackResult orig, Hero self, AttackData attack)
        {
            if (self != null && _invincibleTimer > 0)
                return;
            orig(self, attack);
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 无敌帧倒计时
            if (_invincibleTimer > 0)
            {
                _invincibleTimer -= dt;
                if (_invincibleTimer < 0) _invincibleTimer = 0;

                // 无敌期间产生残影特效
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero != null && hero.life > 0)
                {
                    _trailAccumulator += dt;
                    while (_trailAccumulator >= TRAIL_INTERVAL)
                    {
                        _trailAccumulator -= TRAIL_INTERVAL;
                        CreateTrail(hero);
                    }
                }
            }
            else
            {
                _trailAccumulator = 0.0;
            }
        }

        /// <summary>
        /// 创建残影特效（无敌帧期间视觉反馈）
        /// </summary>
        private void CreateTrail(Hero hero)
        {
            if (hero == null) return;
            try
            {
                bool dummyBool = false;
                double dummyDouble = 0.0;
                var refBool1 = new Ref<bool>(ref dummyBool);
                var refBool2 = new Ref<bool>(ref dummyBool);
                var refDouble = new Ref<double>(ref dummyDouble);

                // 白色残影
                int trailColor = unchecked((int)0xFFFFFFFF);

                var trail = OnionSkin.Class.fromEntity(
                    hero,
                    null,
                    trailColor,
                    Ref<double>.In(TRAIL_ALPHA),
                    Ref<double>.In(TRAIL_DURATION),
                    refBool1,
                    refBool2,
                    refDouble
                );

                if (trail != null)
                {
                    double offsetX = -hero.dir * 10.0;
                    trail.offset(offsetX, 0.0);
                    trail.scaleX *= 1.0;
                    trail.scaleY *= 1.0;
                    trail.ds = 0.0;
                    trail.frict = 0.87;
                }
            }
            catch
            {
                // 静默处理残影创建异常
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUseHook;
            Hook_Entity.applyAttackResult -= Hook_Entity_applyAttackResult;
            Hook_Hero.applyAttackResult -= Hook_Hero_applyAttackResult;
            global::System.Console.WriteLine("游戏退出，Katana Mod 资源清理");
        }
    }
}
