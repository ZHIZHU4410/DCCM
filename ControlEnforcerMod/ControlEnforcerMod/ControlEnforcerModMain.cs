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

namespace ControlEnforcerMod
{
    public class ControlEnforcerMod : ModBase, IOnHeroUpdate
    {
        private const double ShieldHealth = 100.0;
        private const string ShieldBreakSoundPath = "sfx/enm/enm_enforcer_shield_break.wav";
        private const int StunAffectId = 8;

        private bool _isReplaced;
        private double _shieldHP = ShieldHealth;
        private bool _shieldActive = true;

        public ControlEnforcerMod(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onLand += OnHeroLand;
            Hook_Hero.onDamage += OnHeroDamage;
            global::System.Console.WriteLine("[ControlEnforcer] Loaded");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            var hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero._level == null) return;

            if (!_isReplaced) ReplaceWithEnforcer(hero);

            // 检查眩晕破盾
            if (_shieldActive && hero.hasAffect(23))
            {
                BreakShield(hero, null);
            }
        }

        private void OnHeroLand(Hook_Hero.orig_onLand orig, Hero self, double height)
        {
            orig(self, height);
            if (!_isReplaced) ReplaceWithEnforcer(self);
        }

        private void ReplaceWithEnforcer(Hero hero)
        {
            try
            {
                // 加载执法者图集
                var lib = Assets.Class.lib.get("atlas/Enforcer.atlas");
                if (lib == null)
                {
                    global::System.Console.WriteLine("[ControlEnforcer] Atlas not found");
                    return;
                }

                // 替换精灵
                hero.initSprite(lib, null, 0.5f, 0.5f, null, true, null, null);

                _shieldActive = true;
                _shieldHP = ShieldHealth;
                _isReplaced = true;
            }
            catch (Exception e)
            {
                global::System.Console.WriteLine($"[ControlEnforcer] Replace error: {e.Message}");
            }
        }

        private void OnHeroDamage(Hook_Hero.orig_onDamage orig, Hero self, AttackData a)
        {
            if (!_isReplaced || !_shieldActive)
            {
                orig(self, a);
                return;
            }

            if (a.hitResult is HitResult.Block)
            {
                _shieldHP -= a.finalMissedDmg;
                if (_shieldHP <= 0.0)
                {
                    BreakShield(self, a);
                }
                else
                {
                    // 播放格挡火花
                    // self._level.fx.hitLines(
                    //     ((double)self.cx + self.xr) * 24.0,
                    //     ((double)self.cy + self.yr) * 24.0 - self.hei * 0.5,
                    //     self.dir, 16777215, null, null, null);
                }
                return; // 吸收伤害
            }
            orig(self, a);
        }

        private void BreakShield(Hero hero, AttackData a)
        {
            _shieldActive = false;
            _shieldHP = 0;

            // 音效
            // var loader = hxd.Res.get_loader();
            // var snd = loader.loadCache(new dc.String(ShieldBreakSoundPath), Sound.Class);
            // hero._level.lAudio.playEventOn(snd, hero, null, null, null);

            // 屏幕特效
            hero._level.fx.customMask(2142719, 0.1, 0.04, 0.1, 0.15, null);
            hero._level.fx.wood(
                ((double)hero.cx + hero.xr) * 24.0 + 20.0,
                ((double)hero.cy + hero.yr) * 24.0 - hero.hei * 0.5 - 20.0,
                25, hero.dir);

            // 反冲攻击者
            if (a != null && a.source != null)
            {
                if (a.hasTag(18))
                    a.source.bump((double)(-(double)a.source.dir) * 0.1, -0.3, null);
                else if (a.carrier == null && !a.hasTag(14) && !a.hasTag(29))
                    a.source.bump((double)(-(double)a.source.dir) * 0.1, -0.3, null);
            }

            // 短暂眩晕
            double dummy = 0;
            hero.setAffectS(StunAffectId, 0.5, new Ref<double>(ref dummy), null);

            // 播放破盾动画
            hero.spr.get_anim().play("hitSHIELD", null, null);

            // 额外逻辑匹配Enforcer
            // hero.interruptSkills(); // 方法不存在
            // hero.removeAllAffects(96);
            // hero.lockAiS(0.5);
            // hero.setGlowColor(16347714, 14756869, null, null);
            // hero.spr.get_anim().stopWithStateAnims();
        }
    }
}