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
    public class KatanaMain : ModBase, IOnHeroUpdate, IOnGameExit, IOnGameEndInit
    {
        public KatanaMain(ModInfo info) : base(info) { }

        // ---------- 无敌帧相关 ----------
        private double _invincibleTimer = 0.0;
        // 无敌帧持续时间：覆盖 Katana 攻击动画
        private const double INVINCIBLE_DURATION = 0.5;

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
        /// 武器使用时触发。检测是否为 Katana 攻击（平砍+蓄力冲刺均触发），
        /// 激活短暂无敌帧。
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

            Hero hero = self.hero;

            // 平砍 + 蓄力冲刺均激活无敌帧
            _invincibleTimer = INVINCIBLE_DURATION;

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

        // ---------- 资源加载 ----------
        void IOnGameEndInit.OnGameEndInit()
        {
            string res = Info.ModRoot!.GetFilePath("res.pak");
            FsPak.Instance.FileSystem.loadPak(res.AsHaxeString());
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // 无敌帧倒计时
            if (_invincibleTimer > 0)
            {
                _invincibleTimer -= dt;
                if (_invincibleTimer < 0) _invincibleTimer = 0;

                // 免疫眩晕：清除 stun affect（ID 8）
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero != null && hero.life > 0)
                {
                    hero.removeAllAffects(8);
                }
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
