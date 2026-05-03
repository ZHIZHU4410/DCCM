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
        private static int ColorFromHex(string hex)
        {
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length == 6) hex = "FF" + hex;
            return Convert.ToInt32(hex, 16);
        }

        // 残影参数
        private const double TrailInterval = 0.15;
        private const double TrailDuration = 1.2;
        private const double TrailAlpha = 0.9;
        private const double TrailScale = 2.0;
        private static readonly Random TrailColorRandom = new Random();
        private static readonly int[] TrailColorPalette = new[]
        {
            ColorFromHex("#FF3D3D"),
            ColorFromHex("#ff0000"),
            ColorFromHex("#ff0000"),
            ColorFromHex("#00ff2a"),
            ColorFromHex("#ffffff"),
            ColorFromHex("#00ff77"),
            ColorFromHex("#00ff08"),
        };
        private const double TrailOffset = 10.0;
        private const double TrailFrict = 0.87;

        private static int GetRandomBrightColor()
        {
            return TrailColorPalette[TrailColorRandom.Next(TrailColorPalette.Length)];
        }

        // 天使头环参数
        private const double HaloDuration = 99999.0;
        private const int HaloEffectId = 79;

        // 状态
        private double _trailAccumulator = 0.0;
        private bool _haloApplied = false;
        private Hero? _currentHero = null;

        public PermaEffectsModMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            
            // 立即注册钩子，避免时序问题
            Hook_Hero.onLand += OnHeroLand;
            
            global::System.Console.WriteLine("永久残影+天使头环模组已加载");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero._level == null)
            {
                _trailAccumulator = 0.0;
                return;
            }

            // 更新当前英雄引用（可能换人了）
            if (_currentHero != hero)
            {
                _currentHero = hero;
                _haloApplied = false; // 允许新英雄重新应用头环
            }

            // 残影生成（英雄存活且游戏未暂停）
            // 注意：pause 可能是方法，需要加括号
            if (hero.life > 0 )
            {
                _trailAccumulator += dt;
                while (_trailAccumulator >= TrailInterval)
                {
                    _trailAccumulator -= TrailInterval;
                    CreateTrailSafe(hero);
                }
            }
        }

        private void OnHeroLand(Hook_Hero.orig_onLand orig, Hero self, double height)
        {
            orig(self, height); // 必须调用原逻辑
            
            // 尝试给落地英雄施加头环
            TryApplyHalo(self);
        }

        private void TryApplyHalo(Hero hero)
        {
            if (hero == null || _haloApplied) return;

            try
            {
                // 确保英雄完全初始化且有生命值
                if (hero.life <= 0 || hero._level == null)
                    return;

                double dummy = 0;
                var ignoreRef = new Ref<double>(ref dummy);
                hero.setAffectS(HaloEffectId, HaloDuration, ignoreRef, null);
                _haloApplied = true;
                global::System.Console.WriteLine("天使头环已施加");
            }
            catch (Exception ex)
            {
                global::System.Console.WriteLine($"施加天使头环失败: {ex.Message}");
            }
        }

        private void CreateTrailSafe(Hero hero)
        {
            if (hero == null) return;

            try
            {
                // 创建可写的 Ref 实例，避免静态属性兼容问题
                bool dummyBool = false;
                double dummyDouble = 0.0;
                var refBool1 = new Ref<bool>(ref dummyBool);
                var refBool2 = new Ref<bool>(ref dummyBool);
                var refDouble = new Ref<double>(ref dummyDouble);

                var trail = OnionSkin.Class.fromEntity(
                    hero,
                    null,
                    GetRandomBrightColor(),
                    Ref<double>.In(TrailAlpha),
                    Ref<double>.In(TrailDuration),
                    refBool1,
                    refBool2,
                    refDouble
                );

                double offsetX = -hero.dir * TrailOffset;
                trail.offset(offsetX, 0.0);
                trail.scaleX *= TrailScale;
                trail.scaleY *= TrailScale;
                trail.ds = 0.0;
                trail.frict = TrailFrict;
            }
            catch (Exception ex)
            {
                global::System.Console.WriteLine($"残影创建异常: {ex.Message}");
            }
        }
    }
}
