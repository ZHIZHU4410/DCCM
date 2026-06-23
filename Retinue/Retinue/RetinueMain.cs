using dc;
using dc.en;
using dc.h2d;
using dc.hxd.fs;
using dc.libs.heaps.slib;
using dc.pr;
using dc.tool;
using HaxeProxy.Runtime;
using HaxeProxy.Runtime.Internals;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Utilities;
using System;
using System.Collections.Generic;

namespace Retinue
{
    /// <summary>
    /// 永久随从：背后不攻击的 FlyingSword 随从。
    /// 位置 &amp; 移动完全参考原版 FlyingSword.onMoveTargetReached + MvFly。
    /// </summary>
    public class RetinueMain : ModBase, IOnGameExit, IOnHeroUpdate, IOnGameEndInit
    {
        // ================================================================
        // ★ 配置 — 照抄 FlyingSword.unserializeInit / onMoveTargetReached
        // ================================================================

        /// <summary>图集（照抄 FlyingSword.initGfx）</summary>
        private const string ATLAS_PATH = "atlas/RetinueFollower.atlas";

        /// <summary>动画名 — initSprite(lib, "idle", ...)</summary>
        private const string ANIM = "idle";

        /// <summary>最大帧数</summary>
        private const int MAX_FRAMES = 80;

        /// <summary>帧间隔（秒）</summary>
        private const double INTERVAL = 0.2;

        /// <summary>缩放</summary>
        private const double SCALE = 0.1;

        /// <summary>随从颜色（ARGB，越小越暗，0xFF808080 = 50%灰）</summary>
        private const int TINT = unchecked((int)0xFF808080);
        private const double OFFSET_X = 50.0;
        private const double OFFSET_Y = -70.0;

        // ── 来自 FlyingSword.onMoveTargetReached ──
        private const double MOVE_SPEED = 0.65;          // move.speed = 0.65
        private const double BACK_FORTH_OFFSET = 12.0;   // ±12 像素交替偏移
        private const double VERTICAL_RANDOM_MIN = 0.75; // offsetY * (0.75 + random*0.5)

        // ================================================================
        // 内部
        // ================================================================

        private SpriteLib? _lib;
        private List<int> _frames = new();        // 帧序号列表
        private int _idx;
        private double _timer;
        private string? _levelId;
        private bool _probed;

        // ── 平滑移动（模拟 MvFly） ──
        private double _curX, _curY;              // 当前像素坐标（相对于 hero 的世界偏移）
        private double _tgtX, _tgtY;              // 目标像素坐标
        private bool _backOrForth;
        private bool _firstSpawn = true;           // 首次生成时快照位置
        private int _lastHeroDir = 1;              // 上次 hero 朝向，用于检测转身
        private readonly Random _rng = new();

        public RetinueMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            System.Console.WriteLine("[Retinue] 永久 FlyingSword 随从已加载");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? h = ModCore.Modules.Game.Instance.HeroInstance;
            if (h?._level == null) return;

            string? id = h._level.map.id?.ToString();
            if (_levelId != id)
            {
                _levelId = id; _lib = null; _frames.Clear(); _probed = false;
                _timer = 0; _idx = 0; _curX = _curY = _tgtX = _tgtY = 0; _backOrForth = false; _firstSpawn = true; _lastHeroDir = 1;
            }

            // —— 图集（照抄 FlyingSword） ——
            if (_lib == null)
            {
                try { _lib = Assets.Class.lib.get(ATLAS_PATH.AsHaxeString()); }
                catch { return; }
                if (_lib == null) return;
                System.Console.WriteLine("[Retinue] ✓ atlas 已加载");
            }

            // —— 帧探测 ——
            if (!_probed)
            {
                _probed = true;
                for (int i = 0; i < MAX_FRAMES; i++)
                {
                    int fi = i;
                    if (TileExists(ANIM, ref fi)) _frames.Add(fi);
                    else if (_frames.Count > 0 && i - _frames.Count >= 3) break;
                }
                if (_frames.Count > 0)
                    System.Console.WriteLine($"[Retinue] ✓ {ANIM} 帧数={_frames.Count}");
                else
                    System.Console.WriteLine($"[Retinue] ✗ 未发现 {ANIM} 帧");
            }
            if (_frames.Count == 0) return;

            // —— 移动（照抄 FlyingSword.onMoveTargetReached） ——
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

            // —— 动画 & 渲染 ——
            _timer += dt;
            if (_timer >= INTERVAL)
            {
                _timer -= INTERVAL;
                _idx = (_idx + 1) % _frames.Count;
            }
            SpawnAt(h);
        }

        void IOnGameExit.OnGameExit() { _lib = null; _levelId = null; }

        void IOnGameEndInit.OnGameEndInit()
        {
            string res = Info.ModRoot!.GetFilePath("res.pak");
            FsPak.Instance.FileSystem.loadPak(res.AsHaxeString());
        }

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

        #region Tile & 渲染

        private bool TileExists(string anim, ref int frame)
        {
            try { return _lib!.getTile(anim.AsHaxeString(), Ref<int>.From(ref frame), Ref<double>.Null, Ref<double>.Null, null) != null; }
            catch { return false; }
        }

        /// <summary>
        /// 在平滑后的世界坐标处生成 OnionSkin。
        /// 参考 FlyingSword.overrideEquipedWeapon 的 OnionSkin 用法。
        /// </summary>
        private void SpawnAt(Hero h)
        {
            try
            {
                int fi = _frames[_idx];
                var t = _lib!.getTile(ANIM.AsHaxeString(), Ref<int>.From(ref fi), Ref<double>.Null, Ref<double>.Null, null)?.clone();
                if (t == null) return;

                // 计算相对于 hero 世界坐标的 delta
                double heroWorldX = (h.cx + h.xr) * 24.0;
                double heroWorldY = (h.cy + h.yr) * 24.0 - h.hei * 0.5;
                double px = _curX - heroWorldX;
                double py = _curY - heroWorldY;

                var s = OnionSkin.Class.fromEntity(h, t, TINT,
                    Ref<double>.In(1.0), Ref<double>.In(INTERVAL + 0.05),
                    Ref<bool>.Null, Ref<bool>.Null, Ref<double>.Null);
                if (s == null) return;

                s.offset(px, py);
                s.dx = 0; s.ds = 0; s.frict = 1;
                s.scaleX *= SCALE; s.scaleY *= SCALE;
            }
            catch { }
        }

        #endregion
    }
}
