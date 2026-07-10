using dc;
using dc.en;
using dc.h2d;
using dc.hxd.fs;
using dc.ui;
using dc.libs.heaps.slib;
using dc.tool;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Storage;
using ModCore.Utilities;
using System;

namespace Retinue
{
    /// <summary>
    /// 永久随从：使用 HSprite + AnimManager 管理图集与动画（参考 dc.Fx.playWeaponFx）。
    /// 位置 &amp; 移动完全参考原版 FlyingSword.onMoveTargetReached + MvFly。
    /// </summary>
    public class RetinueMain : ModBase, IOnGameExit, IOnHeroUpdate, IOnGameEndInit, IModMenu
    {
        /// <summary>Persistent mod config — toggle state survives game restarts.</summary>
        public static Config<Configs> config { get; } = new Config<Configs>("Retinue");

        // ================================================================
        // ★ 配置 — 照抄 FlyingSword.unserializeInit / onMoveTargetReached
        // ================================================================

        /// <summary>图集路径</summary>
        private const string ATLAS_PATH = "atlas/RetinueFollower.atlas";

        /// <summary>动画名 — 对应 HSprite groupName</summary>
        private const string ANIM = "idle";

        /// <summary>缩放</summary>
        private const double SCALE = 0.3;

        private const double OFFSET_X = 50.0;
        private const double OFFSET_Y = -70.0;

        // ── 来自 FlyingSword.onMoveTargetReached ──
        private const double MOVE_SPEED = 0.65;          // move.speed = 0.65
        private const double BACK_FORTH_OFFSET = 1.0;   //  像素交替偏移
        private const double VERTICAL_RANDOM_MIN = 0.75; // offsetY * (0.75 + random*0.5)

        // ================================================================
        // 内部
        // ================================================================

        private SpriteLib? _lib;
        private HSprite? _hsprite;          // ★ 使用 HSprite 管理图集 & 动画（参考 playWeaponFx）
        private string? _levelId;

        // ── 平滑移动（模拟 MvFly） ──
        private double _curX, _curY;        // 当前像素坐标（世界空间）
        private double _tgtX, _tgtY;        // 目标像素坐标（世界空间）
        private bool _backOrForth;
        private bool _firstSpawn = true;     // 首次生成时快照位置
        private int _lastHeroDir = 1;        // 上次 hero 朝向，用于检测转身
        private readonly Random _rng = new();
        private bool _disposed;

        public RetinueMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            _disposed = false;
            // 与 AssistMode UI 同步生命周期：死亡/换关/复活时重新快照位置
            Hook_HUD.initHero += OnRetinueHUDInit;
            System.Console.WriteLine("[Retinue] 永久随从已加载 (playWeaponFx HSprite 模式)");
        }

        // ── IModMenu ──
        public string GetName() => "Retinue";

        public void BuildMenu(dc.ui.Options options)
        {
            ((dc.ui.Text)((dc.ui.OptionsBase)options).title).set_text(
                StringUtils.AsHaxeString("Retinue Settings".ToUpper()));
            ((dc.ui.OptionsBase)options).createScroller(0.0);

            bool enabled = config.Value.enabled;
            ((dc.ui.OptionsBase)options).addToggleWidget(
                StringUtils.AsHaxeString("Activate mod"),
                StringUtils.AsHaxeString("Toggle permanent follower"),
                (HlFunc<bool>)delegate { config.Value.enabled = !config.Value.enabled; return config.Value.enabled; },
                new Ref<bool>(ref enabled),
                ((dc.ui.OptionsBase)options).scrollerFlow);

            ((dc.ui.OptionsBase)options).updateScroller();
        }

        // ── HUD 初始化 hook：与 AssistMode UI 同步，死亡/复活时重置随从位置 ──
        private void OnRetinueHUDInit(Hook_HUD.orig_initHero orig, HUD self)
        {
            orig(self);
            if (_disposed) return;
            // 强制下次 Update 快照到 hero 位置，避免从旧坐标 lerp
            _firstSpawn = true;
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (_disposed) return;
            if (!config.Value.enabled) return;
            Hero? h = ModCore.Modules.Game.Instance.HeroInstance;
            if (h?._level == null) return;

            // ── 换关检测：销毁旧 HSprite，重新加载图集 ──
            string? id = h._level.map.id?.ToString();
            if (_levelId != id)
            {
                DestroyHSprite();
                _levelId = id; _lib = null;
                _curX = _curY = _tgtX = _tgtY = 0; _backOrForth = false; _firstSpawn = true; _lastHeroDir = 1;
            }

            // ── 加载图集（参考 playWeaponFx: Assets.Class.fxWeapon） ──
            if (_lib == null)
            {
                try { _lib = Assets.Class.lib.get(ATLAS_PATH.AsHaxeString()); }
                catch { return; }
                if (_lib == null) return;
                System.Console.WriteLine("[Retinue] ✓ atlas 已加载");
            }

            // ── 初始化 HSprite（参考 playWeaponFx: new HSprite(fxWeapon, id, ref f, null)） ──
            if (_hsprite == null && _lib != null && h.spr != null)
            {
                InitHSprite(h);
            }

            // ── 移动（照抄 FlyingSword.onMoveTargetReached） ──
            if (_firstSpawn)
            {
                // 首次直接快照到 hero 位置，避免从 (0,0) lerp 过来
                double hx = (h.cx + h.xr) * 24.0;
                double hy = (h.cy + h.yr) * 24.0 - h.hei * 0.5;
                _curX = _tgtX = hx - h.dir * OFFSET_X;
                _curY = _tgtY = hy + OFFSET_Y;
                _firstSpawn = false;
            }
            UpdateMoveTarget(h);
            SmoothMove(dt);

            // ── 更新 HSprite 相对 hero 的位置 ──
            if (_hsprite != null)
            {
                double heroWorldX = (h.cx + h.xr) * 24.0;
                double heroWorldY = (h.cy + h.yr) * 24.0 - h.hei * 0.5;
                // 预补偿父级 h.spr.scaleX 翻转：子节点本地坐标会被父级缩放影响，
                // 所以乘以 h.dir 确保世界空间偏移方向正确
                _hsprite.x = (_curX - heroWorldX) * h.dir;
                _hsprite.y = _curY - heroWorldY;
                _hsprite.posChanged = true;
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_HUD.initHero -= OnRetinueHUDInit;
            DestroyHSprite();
            _disposed = true;
            _lib = null;
            _levelId = null;
            _firstSpawn = true;
            System.Console.WriteLine("[Retinue] 已卸载");
        }

        void IOnGameEndInit.OnGameEndInit()
        {
            try
            {
                string res = Info.ModRoot!.GetFilePath("res.pak");
                FsPak.Instance.FileSystem.loadPak(res.AsHaxeString());
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Retinue] res.pak 加载失败: {ex.Message}");
            }
        }

        #region HSprite 管理（参考 dc.Fx.playWeaponFx）

        /// <summary>
        /// 初始化 HSprite，完全参考 playWeaponFx 的模式：
        /// <code>
        ///   SpriteLib fxWeapon = Assets.Class.fxWeapon;
        ///   int f = 0;
        ///   HSprite hsprite = new HSprite(fxWeapon, id, ref f, null);
        ///   // set pivot → addChild → play anim → (optional) killAfterPlay / GradientHiLo
        /// </code>
        /// </summary>
        private void InitHSprite(Hero h)
        {
            if (_lib == null || h.spr == null) return;

            try
            {
                // ── 参考: new HSprite(fxWeapon, id, ref f, null) ──
                int startFrame = 0;
                _hsprite = new HSprite(_lib, ANIM.AsHaxeString(), Ref<int>.From(ref startFrame), null);
                if (_hsprite == null) return;

                // ── 参考: pivot.centerFactorX/Y = 0.5 ──
                SpritePivot pivot = _hsprite.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                // ── 缩放 ──
                _hsprite.scaleX = SCALE;
                _hsprite.scaleY = SCALE;

                // ── 参考: spr.addChild(hsprite) ──
                h.spr.addChild(_hsprite);

                // ── 参考: hsprite.get_anim().play(id, num3, null)
                //         不调用 killAfterPlay()，因为随从需要循环播放 ──
                int? loopCount = 99999;
                bool? queueAnim = null;
                _hsprite.get_anim().play(ANIM.AsHaxeString(), loopCount, queueAnim);

                System.Console.WriteLine("[Retinue] ✓ HSprite 已创建并播放动画 (playWeaponFx 模式)");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[Retinue] ✗ HSprite 初始化失败: {ex.Message}");
                _hsprite = null;
            }
        }

        /// <summary>
        /// 销毁 HSprite（换关/退出时调用）。
        /// </summary>
        private void DestroyHSprite()
        {
            if (_hsprite != null)
            {
                try { _hsprite.remove(); }
                catch { }
                _hsprite = null;
            }
        }

        #endregion

        #region 移动逻辑（照抄 FlyingSword.onMoveTargetReached）

        /// <summary>
        /// 到达目标后选取新目标。
        /// 完全参考 FlyingSword.onMoveTargetReached 的位置计算。
        /// </summary>
        private void UpdateMoveTarget(Hero h)
        {
            double dx = _tgtX - _curX;
            double dy = _tgtY - _curY;
            double dist = System.Math.Sqrt(dx * dx + dy * dy);

            // hero 转身 → 立即重算目标，不等到达
            if (h.dir != _lastHeroDir)
            {
                _lastHeroDir = h.dir;
                dist = 0; // 强制进入目标选取
            }

            // 未到达 → 保持当前目标（除非 hero 移动了）
            if (dist > 2.0) return;

            // —— 到达 → 选取新目标（照抄原版） ——

            // X: hero 世界坐标 - dir*offsetX
            double heroWorldX = (h.cx + h.xr) * 24.0;
            _tgtX = heroWorldX - h.dir * OFFSET_X;

            // 交替 ±12px（原版 backOrForth toggle）
            _backOrForth = !_backOrForth;
            _tgtX += h.dir * (_backOrForth ? BACK_FORTH_OFFSET : -BACK_FORTH_OFFSET);

            // Y: hero 世界坐标 - hei*0.5 + offsetY*(0.75+random*0.5)
            double heroWorldY = (h.cy + h.yr) * 24.0 - h.hei * 0.5;
            double randFactor = VERTICAL_RANDOM_MIN + _rng.NextDouble() * 0.5;
            _tgtY = heroWorldY + OFFSET_Y * randFactor;
        }

        /// <summary>
        /// 平滑插值（模拟 MvFly.speed = 0.65）
        /// speed 是 Heaps 时间单位（秒级），用 lerp 因子模拟
        /// </summary>
        private void SmoothMove(double dt)
        {
            double lerp = 1.0 - System.Math.Exp(-MOVE_SPEED * 10.0 * dt);
            _curX += (_tgtX - _curX) * lerp;
            _curY += (_tgtY - _curY) * lerp;
        }

        #endregion
    }

    /// <summary>
    /// Persistent config for the Retinue mod.
    /// </summary>
    public class Configs
    {
        public bool enabled = true;
    }
}
