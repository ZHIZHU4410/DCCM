using dc;
using dc.en;
using dc.h2d;
using dc.hxd.fs;
using dc.ui;
using dc.libs.heaps.slib;
using dc.pr;
using dc.tool;
using dc.tool.atk;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SysMath = System.Math;

namespace Waponfxpro
{
    /// <summary>
    /// 武器特效遮罩模组 — glowRef 替换武器 VFX 遮罩。
    ///
    /// 使用 dc.h2d.Bitmap（Heaps 基本纹理四边形）作为 hero.spr 子节点，
    /// Add 混合模式叠加到武器位置。仅攻击时更新位置和显隐。
    /// </summary>
    public class WaponfxproMain : ModBase, IOnGameExit, IOnHeroUpdate, IOnGameEndInit
    {
        private const string MASK_ATLAS_PATH = "atlas/WaponFxGlow.atlas";
        private const string MASK_ANIM = "glowRef";
        private const double MASK_SCALE = 1.5;
        private const double WEAPON_OFFSET_X = 30.0;  // 武器水平偏移（像素）
        private const double WEAPON_OFFSET_Y = -22.0; // 武器垂直偏移（负=上）
        private const double ATTACK_VISIBLE_TIME = 0.15; // 攻击后遮罩保持时间（秒）

        private SpriteLib? _maskLib;
        private Tile? _maskTile;
        private Bitmap? _maskBmp;        // 持久 Bitmap 子节点
        private bool _ready;
        private string? _levelId;
        private bool _disposed;
        private double _attackTimer;      // 攻击后可见倒计时
        private int _logCount;

        public WaponfxproMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            _disposed = false; _ready = false;
            _maskLib = null; _maskTile = null; _maskBmp = null;
            _levelId = null; _attackTimer = 0; _logCount = 0;

            Hook_Hero.applyAttackResult += OnAttack;
            System.Console.WriteLine("[WaponFxPro] glowRef Bitmap 遮罩已加载");
        }

        private void OnAttack(Hook_Hero.orig_applyAttackResult orig, Hero self, AttackData a)
        {
            orig(self, a);
            if (_disposed || !_ready) return;
            _attackTimer = ATTACK_VISIBLE_TIME; // 激活遮罩可见
        }

        void IOnGameEndInit.OnGameEndInit()
        {
            FsPak.Instance.FileSystem.loadPak(Info.ModRoot!.GetFilePath("res.pak").AsHaxeString());
            System.Console.WriteLine("[WaponFxPro] ✓ res.pak");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (_disposed) return;
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero?._level == null) return;

            // 关卡切换
            string? id = hero._level.map.id?.ToString();
            if (_levelId != id)
            {
                _levelId = id;
                DestroyMask();
                _maskLib = null; _maskTile = null; _ready = false;
                System.Console.WriteLine($"[WaponFxPro] 关卡 → {_levelId}");
            }

            // 图集加载（照抄 RetinueMain）
            if (_maskLib == null)
            {
                try { _maskLib = Assets.Class.lib.get(MASK_ATLAS_PATH.AsHaxeString()); }
                catch { _ready = true; return; }
                if (_maskLib == null) { _ready = true; return; }
                System.Console.WriteLine("[WaponFxPro] ✓ 图集");
            }

            // Tile 提取
            if (!_ready && _maskLib != null)
            {
                try
                {
                    int f = 0;
                    var tile = _maskLib.getTile(MASK_ANIM.AsHaxeString(), Ref<int>.From(ref f), Ref<double>.Null, Ref<double>.Null, null);
                    if (tile != null) { _maskTile = tile; _ready = true; }
                    else for (int i = 0; i <= 5; i++) { int fi = i; tile = _maskLib.getTile(MASK_ANIM.AsHaxeString(), Ref<int>.From(ref fi), Ref<double>.Null, Ref<double>.Null, null); if (tile != null) { _maskTile = tile; _ready = true; break; } }
                }
                catch { _ready = true; }
                if (_ready) System.Console.WriteLine("[WaponFxPro] ✓ Tile");
            }

            if (!_ready) return;

            // 创建/更新遮罩 Bitmap
            if (_maskBmp == null && hero.spr != null)
                CreateMaskBitmap(hero);

            // 攻击倒计时
            if (_attackTimer > 0) _attackTimer -= dt;

            // 更新可见性和位置
            if (_maskBmp != null)
            {
                bool visible = _attackTimer > 0;
                try { _maskBmp.GetType().GetProperty("visible")?.SetValue(_maskBmp, visible); } catch { }

                if (visible)
                    UpdateMaskPos(hero);

                if (_logCount == 0 || (_attackTimer > 0 && _logCount % 60 == 0) || (_attackTimer <= 0 && _logCount == 1))
                {
                    _logCount++;
                    if (_logCount <= 2 || _attackTimer > 0)
                        System.Console.WriteLine($"[WaponFxPro] bmp={(object?)_maskBmp != null} visible={visible} timer={_attackTimer:F3}");
                }
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.applyAttackResult -= OnAttack;
            DestroyMask();
            _disposed = true;
            System.Console.WriteLine("[WaponFxPro] 退出");
        }

        // ====================================================================
        // Bitmap 创建/销毁
        // ====================================================================

        private void CreateMaskBitmap(Hero hero)
        {
            if (_maskTile == null || hero.spr == null) return;

            try
            {
                var clone = _maskTile.clone();
                if (clone == null) return;

                // dc.h2d.Bitmap(tile, parent) — 基本纹理四边形
                var bmp = new Bitmap(clone, hero.spr);
                if (bmp == null) return;

                // 缩放
                bmp.scaleX = MASK_SCALE;
                bmp.scaleY = MASK_SCALE;

                // 武器相对 hero.spr 的偏移
                bmp.x = hero.dir * WEAPON_OFFSET_X;
                bmp.y = WEAPON_OFFSET_Y;

                // 初始不可见（等攻击触发）
                try { bmp.GetType().GetProperty("visible")?.SetValue(bmp, false); } catch { }
                // Add blend
                try { bmp.GetType().GetProperty("blendMode")?.SetValue(bmp, 1); } catch { }

                _maskBmp = bmp;
                System.Console.WriteLine($"[WaponFxPro] ✓ Bitmap 创建 x={bmp.x:F0} y={bmp.y:F0} parent=hero.spr");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[WaponFxPro] ✗ Create: {ex.Message}");
            }
        }

        private void UpdateMaskPos(Hero hero)
        {
            if (_maskBmp == null) return;
            try
            {
                _maskBmp.x = hero.dir * WEAPON_OFFSET_X;
                _maskBmp.y = WEAPON_OFFSET_Y;
            }
            catch { }
        }

        private void DestroyMask()
        {
            try { _maskBmp?.remove(); } catch { }
            _maskBmp = null;
        }
    }
}
