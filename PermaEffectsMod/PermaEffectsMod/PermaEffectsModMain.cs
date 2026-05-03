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

namespace PermaEffectsMod
{
    public class PermaEffectsModMain : ModBase, IOnHeroUpdate
    {
        // ---------- 颜色转换辅助 ----------
        private static int ColorFromHex(string hex)
        {
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length == 6) hex = "FF" + hex;
            return Convert.ToInt32(hex, 16);
        }

        // ---------- 残影参数 ----------
        private const double TrailInterval = 0.15;   //残影生成的间隔时间
        private const double TrailDuration = 1.2;  //存在的时间（秒）
        private const double TrailAlpha = 1;    //残影的不透明度
        private const double TrailScale = 2.5;    //缩放倍数
        private static readonly int TrailColor = ColorFromHex("#ff0000"); 
        private const double TrailOffset = 10.0;    //水平方向的偏移量
        private const double TrailFrict = 0.87;   //物理摩擦力

        // ---------- 天使头环参数 ----------
        private const double HaloDuration = 99999.0; // 超长持续时间，视为永久
        private const int HaloEffectId = 79; // 天使头环

        // ---------- 内部状态 ----------
        private double _trailAccumulator = 0.0;
        private Hero _lastHero = null;

        public PermaEffectsModMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            System.Console.WriteLine("SimpleMod 初始化完成");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero._level == null)
            {
                _trailAccumulator = 0.0;
                _lastHero = null;
                return;
            }
            if (_lastHero != hero)
            {
                _lastHero = hero;
                double dummy = 0;
                var ignoreRef = new Ref<double>(ref dummy);
                hero.setAffectS(HaloEffectId, HaloDuration, ignoreRef, null);
            }
            _trailAccumulator += dt;
            while (_trailAccumulator >= TrailInterval)
            {
                _trailAccumulator -= TrailInterval;
                CreateTrail(hero);
            }
        }

        private void CreateTrail(Hero hero)
        {
            if (hero == null) return;

            var trail = OnionSkin.Class.fromEntity(
                hero,
                null,                           // 使用英雄当前动画
                TrailColor,
                Ref<double>.In(TrailAlpha),
                Ref<double>.In(TrailDuration),
                Ref<bool>.Null,
                Ref<bool>.Null,
                Ref<double>.Null
            );

            // 朝英雄反方向偏移，制造拖尾
            double offsetX = -hero.dir * TrailOffset;
            trail.offset(offsetX, 0.0);

            // 放大尺寸
            trail.scaleX *= TrailScale;
            trail.scaleY *= TrailScale;

            // 物理阻力，使其运动更自然
            trail.ds = 0.0;
            trail.frict = TrailFrict;
        }
    }
}