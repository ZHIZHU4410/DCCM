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
    public class PermaEffectsModMain : ModBase, IOnHeroUpdate, IOnGameExit
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
        private string? _currentMapId = null;

        // 按键控制
        private bool _effectsEnabled = true;
        private bool _isTKeyPressed = false;
        private const int VK_T = 0x54;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public PermaEffectsModMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();

            Hook_HUD.initHero += OnHUDInit;
            Hook_Hero.onLand += OnHeroLand;

            global::System.Console.WriteLine("永久残影+天使头环模组已加载");
            global::System.Console.WriteLine("按 T 键开关残影与头环效果");
        }

        private void OnHUDInit(Hook_HUD.orig_initHero orig, HUD self)
        {
            orig(self);
            _trailAccumulator = 0.0;
            if (_effectsEnabled)
            {
                var hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero != null && hero._level != null)
                {
                    _haloApplied = false;
                    TryApplyHalo(hero);
                }
            }
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            // T 键切换
            bool isTPressedNow = GetAsyncKeyState(VK_T) < 0;
            if (isTPressedNow && !_isTKeyPressed)
            {
                _effectsEnabled = !_effectsEnabled;
                global::System.Console.WriteLine($"残影+天使头环 效果已{(_effectsEnabled ? "开启" : "关闭")}");
                if (_effectsEnabled)
                {
                    _haloApplied = false;
                }
            }
            _isTKeyPressed = isTPressedNow;

            if (!_effectsEnabled)
            {
                _trailAccumulator = 0.0;
                return;
            }

            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero._level == null)
            {
                _trailAccumulator = 0.0;
                return;
            }

            // 英雄对象变化（复活、变身等）
            if (_currentHero != hero)
            {
                _currentHero = hero;
                _haloApplied = false;
            }

            // 关卡变化检测：重置标志，让后续每帧尝试重新施加
            string currentMapId = hero._level.map.id.ToString();
            if (_currentMapId != currentMapId)
            {
                _currentMapId = currentMapId;
                _haloApplied = false;
                global::System.Console.WriteLine($"检测到关卡变化: {currentMapId}，将重新尝试施加头环");
            }

            // ★ 关键修改：每帧都尝试施加头环（内部已做 _haloApplied 检查，成功一次后不再重复）
            TryApplyHalo(hero);

            // 残影生成
            if (hero.life > 0)
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
            orig(self, height);
        }

        private void TryApplyHalo(Hero hero)
        {
            if (!_effectsEnabled || hero == null || _haloApplied) return;

            try
            {
                // 放宽条件：只要英雄未死亡且有关卡就尝试（生命值 > 0 即可）
                // 如果生命值 <= 0，说明死亡中，等待下一帧复活后再尝试
                if (hero.life <= 0 || hero._level == null)
                    return;

                double dummy = 0;
                var ignoreRef = new Ref<double>(ref dummy);
                hero.setAffectS(HaloEffectId, HaloDuration, ignoreRef, null);
                _haloApplied = true;
                global::System.Console.WriteLine($"天使头环已施加于关卡 {hero._level.map.id}");
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

        void IOnGameExit.OnGameExit()
        {
            Hook_HUD.initHero -= OnHUDInit;
            Hook_Hero.onLand -= OnHeroLand;
            global::System.Console.WriteLine("游戏退出，PermaEffectsMod 资源清理");
        }
    }
}