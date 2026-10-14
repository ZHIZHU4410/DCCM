using dc;
using dc.en;
using dc.hl.types;
using dc.hxd.fs;
using dc.libs.heaps.slib;
using dc.tool.atk;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Modules;
using ModCore.Storage;
using ModCore.Utilities;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using SysMath = System.Math;

namespace HeliHero
{
    /// <summary>
    /// 一组按键绑定：一个动作最多三个键（写法照抄 PlayableMOB\KeyBind.cs）。
    /// 值就是 Windows 虚拟键码（VK），null 表示该位没绑。
    /// </summary>
    public class KeyBind
    {
        public int? primary;
        public int? secondary;
        public int? third;
    }

    /// <summary>直升机主角的持久化配置（存成 coremod/config/HeliHero.json）。</summary>
    public class Configs
    {
        /// <summary>总开关：关掉立刻还原原版角色。</summary>
        public bool enabled = true;

        /// <summary>碰到敌人就斩杀。</summary>
        public bool killOnContact = true;

        /// <summary>飞行速度（格/秒）。1 格 = 24 像素。</summary>
        public double flySpeed = 14.0;

        /// <summary>直升机模型缩放（1.0 = 原始 240x69 像素；已按需求放大一倍）。</summary>
        public double spriteScale = 1.0;

        /// <summary>斩杀 / 碰撞半径（格；已按需求放大一倍）。</summary>
        public double killRadius = 6.0;

        /// <summary>无敌（affect 5，和 PlayableMOB.disableHero 同一招）。</summary>
        public bool invincible = true;

        /// <summary>
        /// 飞行中也去按交互键（实验，默认关）。
        /// 接管时 manualLock 会把原版交互一起锁死，硬拆锁跑 activateFocus 不一定能走通换关流程，
        /// 所以常规做法是 F7 切成原版角色去交互，交互完再 F7 切回来。
        /// </summary>
        public bool interactWhileFlying = false;

        /// <summary>配置版本号，用于把旧 json 一次性迁到新默认值（勿手改）。</summary>
        public int configVersion;

        /// <summary>
        /// 飞行方向绑定（写法照抄 PlayableMOB 的死神：一个动作最多三个键，值 = VK 码）。
        /// 默认键盘 WASD + 方向键；手改 coremod/config/HeliHero.json 即可换成别的键。
        /// </summary>
        public Dictionary<string, KeyBind> bindings = DefaultBindings();

        /// <summary>默认绑定：left/right/up/down → WASD + 方向键。</summary>
        public static Dictionary<string, KeyBind> DefaultBindings()
        {
            return new Dictionary<string, KeyBind>
            {
                { "left",  new KeyBind { primary = 0x41, secondary = 0x25 } },  // A / ←
                { "right", new KeyBind { primary = 0x44, secondary = 0x27 } },  // D / →
                { "up",    new KeyBind { primary = 0x57, secondary = 0x26 } },  // W / ↑
                { "down",  new KeyBind { primary = 0x53, secondary = 0x28 } },  // S / ↓
                { "interact", new KeyBind { primary = 0x45, secondary = 0x0D, third = 0x46 } },  // E / 回车 / F
                { "toggle",   new KeyBind { primary = 0x76 } },                                 // F7：飞机 <-> 原版角色
            };
        }
    }

    /// <summary>
    /// 把主角换成直升机：
    ///   1) 原版角色（含头、武器特效）整体隐藏，改挂一张裸 HSprite（图集 atlas/heli.atlas）；
    ///   2) 自由飞行 —— WASD / 方向键，直接写 cx/xr/cy/yr 绕过碰撞求解，配合
    ///      hasGravity=false + CollisionMode.IgnoreWalls 实现"想怎么飞就怎么飞、无视墙体"；
    ///   3) 碰到敌人即斩杀 —— 遍历 level.entities，只保留基础合法性校验
    ///      （存活/可标/敌对/可探测/可命中），自己算格距离，然后走原版 AttackUtils 伤害管线，
    ///      所以击杀计数、细胞、掉落、死亡流程全部是原版的。
    ///
    /// 移动写法参考 PlayableMOB 的 HeroDeath（每帧直接写 dir / 位移）与 DashOverhaul 的 MoveBy；
    /// 按键读取照抄 PlayableMOB 的 KeyBind + held —— 问游戏自己的按键表 dc.hxd.Key.isDown。
    /// </summary>
    public class HeliHeroMain : ModBase, IOnAfterLoadingAssets, IOnGameExit, IOnHeroUpdate, IModMenu
    {
        public static Config<Configs> config { get; } = new Config<Configs>("HeliHero");

        // ============================ 资源常量 ============================

        /// <summary>图集在 res.pak 里的路径（csproj 的 PackAssets 打包而来）。</summary>
        private const string AtlasPath = "atlas/heli.atlas";

        /// <summary>动画组名：idle_0000 .. idle_0023（24 帧旋翼）。</summary>
        private const string AnimGroup = "idle";

        /// <summary>帧尺寸（像素）。</summary>
        private const double FrameWid = 240.0;
        private const double FrameHei = 69.0;

        /// <summary>机身重心在帧内的位置（像素，由 build_heli_atlas.py 实测）。</summary>
        private const double BodyCenterX = 113.6;
        private const double BodyCenterY = 42.2;

        // ============================ 数值常量 ============================

        private const double InfDmg = 1000000000.0;   // "无限大"伤害
        private const double MaxTilt = 0.12;          // 飞行倾角（弧度）
        private const double TiltLerp = 12.0;         // 倾角渐变速率

        /// <summary>原版"无敌"是 affect 5（翻滚无敌帧 / KingScepter 都用它）。</summary>
        private const int InvincibleAffectId = 5;

        /// <summary>Controller 里"交互 / 使用"动作的索引（Hero.cs 读 framePresses[6]）。</summary>
        private const int InteractActionIndex = 6;

        // ============================ 按键 ============================

        /// <summary>Win32 兜底读键（依赖窗口焦点，只当游戏按键表读不到时用）。</summary>
        [DllImport("user32.dll", ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vKey);

        // ============================ 运行时状态 ============================

        private SpriteLib? _lib;
        private HSprite? _heli;
        private string? _levelId;
        private bool _applied;            // 是否已接管过 hero
        private double _tilt;             // 当前倾角
        private int _limitCd;             // 日志限流

        /// <summary>配置里读不到某个动作时用的内置默认绑定（防止旧 json 缺键导致飞不动）。</summary>
        private static readonly Dictionary<string, KeyBind> FallbackBindings = Configs.DefaultBindings();

        private Hero? _savedHero;         // 备份原版状态所属的 hero 实例
        private bool _saved;
        private CollisionMode? _prevCollision;
        private bool _prevGravity = true;
        private bool _prevVisible = true;
        private bool _prevHead = true;
        private bool _prevManualLock;
        private double _handoffTime;     // >0 = 刚触发换关，暂时把位移交还原版（秒）
        private bool _physicsDisabled;      // 是否已关掉原版物理
        private bool _interactLatch;        // 交互键上帧状态（边沿触发）
        private bool _heroMode;             // F7 切回原版角色：此模式下本模组完全不干预
        private bool _toggleLatch;          // F7 上帧状态（边沿触发）

        public HeliHeroMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            MigrateConfig();
            System.Console.WriteLine("[HeliHero] 直升机主角已加载（WASD/方向键飞行 · 碰敌即斩 · 无视墙体 · 无敌 · 可交互）");
        }

        /// <summary>
        /// 一次性把旧配置迁到新默认值：老 json 里是 spriteScale=0.5 / killRadius=3.0，
        /// 需求是"都放大一倍"，所以版本号 &lt; 1 时把它们翻倍并写回，之后不再动。
        /// 全新配置走 C# 默认值（已经是 1.0 / 6.0），这里只补版本号。
        /// </summary>
        private void MigrateConfig()
        {
            try
            {
                if (config.Value.configVersion >= 1) return;
                if (config.Value.spriteScale == 0.5) config.Value.spriteScale = 1.0;
                if (config.Value.killRadius == 3.0) config.Value.killRadius = 6.0;
                config.Value.configVersion = 1;
                config.Save();
                Logger.Information("[HeliHero] 配置已迁移：模型 / 碰撞范围放大一倍");
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[HeliHero] 配置迁移失败");
            }
        }

        // ============================ 资源加载 ============================

        void IOnAfterLoadingAssets.OnAfterLoadingAssets()
        {
            try
            {
                string res = Info.ModRoot!.GetFilePath("res.pak");
                FsPak.Instance.FileSystem.loadPak(res.AsHaxeString());
                Logger.Information($"[HeliHero] res.pak 已加载: {res}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[HeliHero] res.pak 加载失败");
            }
        }

        // ============================ 主循环 ============================

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = Game.Instance.HeroInstance;

            // 没英雄 / 已死亡：还原本体（死亡动画要能看见）
            if (hero == null || hero.destroyed || hero.life <= 0 || hero._level == null)
            {
                if (_applied) Restore(hero);
                return;
            }

            if (!config.Value.enabled)
            {
                if (_applied) Restore(hero);
                return;
            }

            // ---- F7：飞机 <-> 原版角色 ----
            // 接管期间 manualLock 会锁住原版交互（开门 / 下一关 / 商店 / 剧情），
            // 切成原版角色就是 100% 原版行为，交互完再 F7 切回来继续飞。
            if (TogglePressed())
            {
                _heroMode = !_heroMode;
                if (_heroMode)
                {
                    Restore(hero, keepInvincible: true);   // 交还 visible / 物理 / 头部 / manualLock，并销毁直升机精灵
                    Logger.Information("[HeliHero] F7 → 原版角色（可正常交互、进下一关）");
                }
                else
                {
                    Logger.Information("[HeliHero] F7 → 直升机");
                }
            }
            // 原版角色模式：可见 / 物理 / 交互全部交还原版，
            // 只保留本模组的两个承诺——无敌、碰到敌人就斩杀（两者都不碰 hero 的位移与物理）。
            if (_heroMode)
            {
                try
                {
                    MaintainInvincible(hero);
                    if (config.Value.killOnContact) KillNearby(hero);
                }
                catch (Exception ex)
                {
                    LogLimited("[HeliHero] 原版模式主线异常: " + ex.Message);
                }
                return;
            }

            try
            {
                Apply(hero, dt);
                _limitCd = 0;
            }
            catch (Exception ex)
            {
                if (_limitCd-- <= 0)
                {
                    _limitCd = 300;
                    Logger.Error(ex, "[HeliHero] 主循环异常");
                }
            }
        }

        private void Apply(Hero hero, double dt)
        {
            dc.pr.Level? lvl = hero._level;
            dc.level.LevelMap? map = lvl?.map;
            if (lvl == null || map == null) return;

            SaveOriginalState(hero);

            // ---- 1) 隐藏原版角色（同 PlayableMOB.disableHero） ----
            ((Entity)hero).visible = false;
            try { hero.heroHead!.heroHasHead = false; } catch { }
            try { hero.controller!.manualLock = true; } catch { }

            // ---- 2) 彻底关掉原版物理：碰撞求解 / 重力 / 体重 ----
            // Entity.disableAllPhysics 内部就是 CollisionMode.None + hasGravity=false + baseWeight=0
            // （和 PlayableMOB.disableHero 同款）；这样"卡墙久了被拉回地面"那套原版保护就不会再触发。
            DisablePhysics(hero);

            // ---- 2b) 无敌（affect 5；丢了就补回来） ----
            MaintainInvincible(hero);

            // ---- 2c) 过场 / 换关交还期：位移交还原版，本模组只跟精灵 ----
            // （原版"走进出口门"的过场是靠写 hero.dx 让物理积分移动 hero 的，
            //   这段时间清零速度 / 按 WASD 写坐标都会把过场卡住。）
            if (HandsOff(lvl, dt))
            {
                _applied = true;
                EnsureSprite(lvl);
                UpdateSprite(hero, dt, 0.0, 0.0);
                return;
            }

            // ---- 3) 方向输入（读绑定的按键，八方向归一化） ----
            // 按键表照抄 PlayableMOB：bindings["left"/"right"/"up"/"down"]，每个动作最多三个键。
            var binds = config.Value.bindings;
            double vx = 0.0, vy = 0.0;
            if (Held(binds, "right")) vx += 1.0;
            if (Held(binds, "left")) vx -= 1.0;
            if (Held(binds, "down")) vy += 1.0;
            if (Held(binds, "up")) vy -= 1.0;   // cy 向下增长

            // ---- 4) 清掉原版速度，位移由本模组直接写格坐标（绕过碰撞求解 = 无视墙体） ----
            hero.dx = 0.0; hero.dy = 0.0;
            hero.bdx = 0.0; hero.bdy = 0.0;

            double step = (dt > 0.0 && dt < 0.25) ? dt : (1.0 / 60.0);
            double speed = SysMath.Clamp(config.Value.flySpeed, 0.0, 200.0);
            if (vx != 0.0 || vy != 0.0)
            {
                double len = SysMath.Sqrt(vx * vx + vy * vy);
                MoveBy(hero, vx / len * speed * step, vy / len * speed * step, map.wid, map.hei);
                if (vx != 0.0) hero.dir = (vx > 0.0) ? 1 : -1;
            }

            _applied = true;

            // ---- 5) 直升机精灵跟随 ----
            EnsureSprite(lvl);
            UpdateSprite(hero, dt, vx, vy);

            // ---- 6) 碰到敌人就斩杀 ----
            if (config.Value.killOnContact) KillNearby(hero);

            // ---- 7) 交互（开门 / 下一关 / 商店 …） ----
            // 默认关：接管时 manualLock 锁住了原版交互，硬拆锁跑 activateFocus 不总能走通换关流程；
            // 常规做法是 F7 切成原版角色去交互。想要飞行中也能触发就打开 config.interactWhileFlying。
            if (config.Value.interactWhileFlying) HandleInteraction(hero, binds);
        }

        // ============================ 精灵 ============================

        /// <summary>换关（或精灵失效）时重建直升机精灵，挂到关卡世界层。</summary>
        private void EnsureSprite(dc.pr.Level lvl)
        {
            string id = "";
            try { id = lvl.map?.id?.ToString() ?? ""; } catch { }

            if (_heli != null && !_heli.destroyed && _levelId == id) return;

            DestroySprite();
            _levelId = id;

            try
            {
                var scroller = lvl.scroller;
                if (scroller == null) return;

                SpriteLib lib = Assets.Class.lib.get(AtlasPath.AsHaxeString());
                if (lib == null)
                {
                    LogLimited("[HeliHero] 图集读取失败: " + AtlasPath);
                    return;
                }
                _lib = lib;

                int f0 = 0;
                HSprite sp = new HSprite(lib, AnimGroup.AsHaxeString(), Ref<int>.From(ref f0), null);
                if (sp == null) return;

                // 居中锚点：帧盒中心对上 sprite 的 (x, y)
                SpritePivot pivot = sp.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                sp.posChanged = true;
                scroller.addChildAt(sp, Const.Class.DP_ROOM_MAIN);

                // 连续循环播放；帧推进由 HSprite.sync -> AnimManager._update(TMOD) 负责
                try { sp.get_anim().play(AnimGroup.AsHaxeString(), 99999, null); }
                catch (Exception ex) { Logger.Warning(ex, "[HeliHero] 旋翼动画启动失败"); }

                _heli = sp;
                Logger.Information($"[HeliHero] 直升机已挂到关卡 {id}（{AnimGroup}，{sp.totalFrames()} 帧）");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[HeliHero] 创建直升机精灵失败");
                _heli = null;
            }
        }

        private void UpdateSprite(Hero hero, double dt, double vx, double vy)
        {
            HSprite? sp = _heli;
            if (sp == null || sp.destroyed) return;

            double scale = SysMath.Clamp(config.Value.spriteScale, 0.05, 4.0);
            double flip = (hero.dir < 0) ? -1.0 : 1.0;

            // 让"机身重心"落在 hero 的格坐标上：帧盒中心与机身重心之差，按缩放/朝向换算
            double offX = (FrameWid * 0.5 - BodyCenterX) * scale * flip;
            double offY = (FrameHei * 0.5 - BodyCenterY) * scale;

            // 前进方向轻微压头（弧度），带一点渐变免得抖
            double targetTilt = (vx > 0.0) ? MaxTilt : ((vx < 0.0) ? -MaxTilt : 0.0);
            double step = (dt > 0.0 && dt < 0.25) ? dt : (1.0 / 60.0);
            _tilt += (targetTilt - _tilt) * SysMath.Clamp(TiltLerp * step, 0.0, 1.0);

            sp.scaleX = scale * flip;
            sp.scaleY = scale;
            sp.rotation = _tilt;
            sp.x = hero.get_targetSprPosX() + offX;
            sp.y = hero.get_targetSprPosY() + offY;
            sp.posChanged = true;
        }

        private void DestroySprite()
        {
            HSprite? sp = _heli;
            _heli = null;
            _levelId = null;
            if (sp == null) return;
            try { if (!sp.destroyed) sp.remove(); } catch { }
        }

        // ============================ 斩杀 ============================

        /// <summary>
        /// 半径内的敌人全部斩杀。完全不问中间有没有墙、也不做视线判定
        /// —— 这就是"无视墙体"的部分；命中仍然走原版 AttackUtils 管线。
        /// </summary>
        private void KillNearby(Hero hero)
        {
            ArrayObj? entities = hero._level?.entities;
            if (entities == null) return;

            double hx = hero.cx + hero.xr;
            double hy = hero.cy + hero.yr;
            double r = SysMath.Clamp(config.Value.killRadius, 0.5, 40.0);
            double r2 = r * r;

            for (int i = 0; i < entities.length; i++)
            {
                object item;
                try { item = entities.getDyn(i); }
                catch { break; }   // 数组被替换等极端情况：安全退出

                if (item is not Mob mob) continue;

                double dx = (mob.cx + mob.xr) - hx;
                double dy = (mob.cy + mob.yr - mob.hei / 48.0) - hy;   // hei 是像素，/48 = 半身高（格）
                if (dx * dx + dy * dy > r2) continue;
                if (!IsValidTarget(hero, mob)) continue;

                try
                {
                    AttackData atk = AttackUtils.Class.createFromHero.Invoke(hero, (dynamic)InfDmg, null);
                    atk.useHeroScaling = false;
                    atk.sourceTier = 1;
                    AttackUtils.Class.hit.Invoke(atk, mob);
                }
                catch (Exception ex)
                {
                    LogLimited("[HeliHero] 斩杀异常: " + ex.Message);
                }
            }
        }

        /// <summary>合法敌人过滤（与 ClusterBombClearMap / IndulgenceWallPierce 一致）。</summary>
        private static bool IsValidTarget(Entity owner, Mob mob)
        {
            if (mob == null || mob.destroyed || mob.life <= 0) return false;
            if (!mob._targetable) return false;
            if (!owner.isOpponent(mob)) return false;
            if (!mob.canBeDetected()) return false;
            if (!mob.canBeHitBy(owner)) return false;
            return true;
        }

        // ============================ 无敌 / 物理 / 交互 ============================

        /// <summary>
        /// 关掉原版物理（同 PlayableMOB.disableHero / Entity.disableAllPhysics）：
        /// collisionMode = None + hasGravity = false + baseWeight = 0。
        /// 这三样正是"卡进墙里久了被强制拉回地面"那套原版保护与碰撞求解的入口；
        /// 关掉之后 hero 的位置只由本模组直接写格坐标，墙里想怎么动就怎么动。
        /// </summary>
        private void DisablePhysics(Hero hero)
        {
            try
            {
                Entity e = hero;
                if (_physicsDisabled && !e.hasGravity && e.collisionMode is CollisionMode.None) return;

                bool touchChecks = e.hasEntityTouchChecks;
                e.disableAllPhysics(new Ref<bool>(ref touchChecks));
                _physicsDisabled = true;
            }
            catch (Exception ex)
            {
                LogLimited("[HeliHero] 关闭原版物理失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 无敌：原版"不可被命中"就是 affects[5] 非空（Entity.canBeHit 只查这一个索引）。
        /// 和 PlayableMOB.disableHero 一样 setAffectS(5, 99999 秒)；丢了才补，避免每帧堆实例。
        /// </summary>
        private void MaintainInvincible(Hero hero)
        {
            if (!config.Value.invincible) return;
            try
            {
                ArrayObj aff = hero.affects;
                if (aff != null && aff.length > InvincibleAffectId)
                {
                    if (aff.getDyn(InvincibleAffectId) is ArrayObj list && list.length > 0) return;
                }
                hero.setAffectS(InvincibleAffectId, 99999.0, Ref<double>.Null, null);
            }
            catch { }
        }

        /// <summary>
        /// 交互（开门 / 下一关 / 商店 / 宝箱 …）。
        /// 接管时把 controller.manualLock 打开了，而原版 Hero 的聚焦(setFocus)与激活(activateFocus)
        /// 都被 controlsLocked() 拦着（Hero.cs 里 activateFocus 外面套着 !manualLock），
        /// 所以按下交互键的这一帧临时解锁 -> 跑一遍原版 updateInteraction() -> activateFocus() -> 再锁回去。
        /// 触发信号优先用游戏自己的 framePresses[6]（= 交互动作这一帧被按下），
        /// 改键 / 手柄都能认；配置里的 interact 键只作兜底。
        /// </summary>
        private void HandleInteraction(Hero hero, Dictionary<string, KeyBind>? binds)
        {
            try
            {
                dc.tool.ControllerAccess? access = hero.controller;
                dc.tool.Controller? parent = access?.parent;
                if (access == null || parent == null) return;

                bool pressed = false;
                ArrayBytes_Int? fp = parent.framePresses;
                if (fp != null && fp.length > InteractActionIndex)
                {
                    try
                    {
                        object? v = fp.getDyn(InteractActionIndex);
                        if (v is int iv && iv == 1) pressed = true;
                    }
                    catch { }
                }
                if (!pressed) pressed = Held(binds, "interact");

                bool edge = pressed && !_interactLatch;
                _interactLatch = pressed;
                if (!edge) return;

                bool saved = access.manualLock;
                try
                {
                    access.manualLock = false;
                    hero.updateInteraction();          // 原版聚焦：3 格内 / |Δ| < 5 格的可交互物
                    Entity? target = hero.focused;
                    if (target == null) return;

                    bool needLong = false;
                    try { needLong = target.needsLongPress; } catch { }
                    bool isExit = false;
                    try { isExit = (bool)Std.Class.@is.Invoke((object)target, (object)dc.en.inter.Exit.Class); } catch { }

                    hero.activateFocus(needLong);      // 原版激活：门 / 商店 / 传送 / 下一关
                    if (isExit)
                    {
                        // 换关会放一段"走进出口门"的原版过场，那段要自己写 hero.dx 让物理积分移动 hero，
                        // 接下来几秒不再抢位移、也不清零速度，等换关完成再接管。
                        _handoffTime = 2.0;
                    }
                }
                finally
                {
                    try { access.manualLock = saved; } catch { }
                }
            }
            catch (Exception ex)
            {
                LogLimited("[HeliHero] 交互失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 是否把位移交还给原版：过场 / 模态弹窗期间，以及刚点完"下一关"门的几秒。
        /// 交还期间仍然保持隐藏 + 无物理 + 无敌，只是不按 WASD 写坐标、也不清零 dx/dy，
        /// 这样原版过场能正常驱动 hero，换关流程不会卡住。
        /// </summary>
        private bool HandsOff(dc.pr.Level lvl, double dt)
        {
            double step = (dt > 0.0 && dt < 0.25) ? dt : (1.0 / 60.0);
            if (_handoffTime > 0.0)
            {
                _handoffTime -= step;
                if (_handoffTime > 0.0) return true;
                _handoffTime = 0.0;
            }
            try { if (lvl.game != null && lvl.game.hasCinematic()) return true; } catch { }
            return false;
        }

        // ============================ 位移 / 输入 ============================

        /// <summary>直接按格位移（绕过碰撞求解 = 无视墙体），并保持 xr/yr 落在 [0,1)、不飞出地图。</summary>
        private static void MoveBy(Hero hero, double ddx, double ddy, int wid, int hei)
        {
            double nx = hero.cx + hero.xr + ddx;
            int ncx = (int)SysMath.Floor(nx);
            if (ncx < 0) { ncx = 0; nx = 0.0; }
            else if (wid > 1 && ncx > wid - 1) { ncx = wid - 1; nx = ncx; }
            hero.cx = ncx;
            hero.xr = nx - ncx;

            double ny = hero.cy + hero.yr + ddy;
            int ncy = (int)SysMath.Floor(ny);
            if (ncy < 0) { ncy = 0; ny = 0.0; }
            else if (hei > 1 && ncy > hei - 1) { ncy = hei - 1; ny = ncy; }
            hero.cy = ncy;
            hero.yr = ny - ncy;
        }

        /// <summary>某个动作此时是否按住（三个绑定键任意一个按下即算）。写法照抄 PlayableMOB.Utils.held。</summary>
        private static bool Held(Dictionary<string, KeyBind>? binds, string action)
        {
            KeyBind? kb = null;
            if (binds != null) binds.TryGetValue(action, out kb);
            if (kb == null) FallbackBindings.TryGetValue(action, out kb);
            if (kb == null) return false;
            return IsDown(kb.primary) || IsDown(kb.secondary) || IsDown(kb.third);
        }

        /// <summary>F7 这一帧是否刚按下（边沿触发，按住不会连切）。</summary>
        private bool TogglePressed()
        {
            bool down = Held(config.Value.bindings, "toggle");
            bool edge = down && !_toggleLatch;
            _toggleLatch = down;
            return edge;
        }

        /// <summary>读一个键的按住状态：优先问游戏，Win32 兜底。</summary>
        private static bool IsDown(int? code)
        {
            if (!code.HasValue) return false;
            int vk = code.Value;
            if (vk <= 0) return false;
            return IsDownViaGame(vk) || IsDownViaWin32(vk);
        }

        /// <summary>
        /// 优先读游戏自己的按键状态表 dc.hxd.Key.isDown —— 原版 Beheaded 判断方向键用的就是它，
        /// 是"游戏认为这个键按着没有"的权威答案，且不依赖窗口焦点。
        /// （实机经验：GetAsyncKeyState 在游戏主循环里常被判成"一直松开"，所以必须有这条路。）
        /// </summary>
        private static bool IsDownViaGame(int vk)
        {
            try
            {
                dc.hxd._Key? k = dc.hxd.Key.Class;
                if (k == null) return false;
                HlFunc<bool, int>? f = k.isDown;
                return f != null && f.Invoke(vk);
            }
            catch { return false; }
        }

        /// <summary>Win32 兜底读键（主菜单 / 按键表还没初始化时才用得上）。</summary>
        private static bool IsDownViaWin32(int vk)
        {
            try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
            catch { return false; }
        }

        // ============================ 还原 / 卸载 ============================

        private void SaveOriginalState(Hero hero)
        {
            if (_saved && ReferenceEquals(_savedHero, hero)) return;

            _saved = true;
            _savedHero = hero;
            try { _prevCollision = hero.collisionMode; } catch { _prevCollision = null; }
            try { _prevGravity = hero.hasGravity; } catch { _prevGravity = true; }
            try { _prevVisible = ((Entity)hero).visible; } catch { _prevVisible = true; }
            try { _prevHead = hero.heroHead!.heroHasHead; } catch { _prevHead = true; }
            try { _prevManualLock = hero.controller!.manualLock; } catch { _prevManualLock = false; }
            // 换了新的 hero 实例（重生 / 换关重建）时，物理与交还状态都重新开始
            _physicsDisabled = false;
            _handoffTime = 0.0;
        }

        /// <summary>还原原版角色（关模组 / 退出游戏 / 主角死亡时调用）。</summary>
        private void Restore(Hero? hero, bool keepInvincible = false)
        {
            try
            {
                if (hero != null && !hero.destroyed)
                {
                    ((Entity)hero).visible = _prevVisible;
                    try { hero.heroHead!.heroHasHead = _prevHead; } catch { }
                    try { hero.controller!.manualLock = _prevManualLock; } catch { }
                    // 物理：DisablePhysics 改过 collisionMode / hasGravity / baseWeight，一并还原
                    if (_physicsDisabled)
                    {
                        try
                        {
                            bool touchChecks = ((Entity)hero).hasEntityTouchChecks;
                            ((Entity)hero).enableAllPhysics(new Ref<bool>(ref touchChecks));
                        }
                        catch { }
                        _physicsDisabled = false;
                    }
                    hero.hasGravity = _prevGravity;
                    hero.collisionMode = _prevCollision ?? new CollisionMode.Normal();
                    // F7 切原版角色时（keepInvincible）保留 affect 5：交互期间照样"打不中"
                    if (!(keepInvincible && config.Value.invincible))
                    {
                        try { ((Entity)hero).removeAllAffects(InvincibleAffectId); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[HeliHero] 还原角色失败");
            }

            DestroySprite();
            _applied = false;
            _tilt = 0.0;
            _handoffTime = 0.0;
            _interactLatch = false;
            _saved = false;
            _savedHero = null;
        }

        void IOnGameExit.OnGameExit()
        {
            DestroySprite();
            _applied = false;
            _physicsDisabled = false;
            _handoffTime = 0.0;
            _interactLatch = false;
            _heroMode = false;
            _toggleLatch = false;
            _saved = false;
            _savedHero = null;
            System.Console.WriteLine("[HeliHero] 游戏退出，模组已卸载");
        }

        private void LogLimited(string msg)
        {
            if (_limitCd-- > 0) return;
            _limitCd = 600;
            Logger.Warning(msg);
        }

        // ============================ 选项菜单（IModMenu） ============================

        public string GetName() => "HeliHero";

        public string? GetSubText() => "直升机主角";

        public void BuildMenu(dc.ui.Options options)
        {
            try
            {
                var ob = (dc.ui.OptionsBase)options;

                ob.title.set_text(StringUtils.AsHaxeString("HELIHERO 设置"));
                ob.createScroller(0.0);

                bool enabled = config.Value.enabled;
                ob.addToggleWidget(
                    StringUtils.AsHaxeString("启用直升机"),
                    StringUtils.AsHaxeString("把主角换成直升机，自由飞行且无视墙体"),
                    (HlFunc<bool>)delegate
                    {
                        config.Value.enabled = !config.Value.enabled;
                        config.Save();
                        return config.Value.enabled;
                    },
                    new Ref<bool>(ref enabled),
                    ob.scrollerFlow);

                bool kill = config.Value.killOnContact;
                ob.addToggleWidget(
                    StringUtils.AsHaxeString("碰到敌人就斩杀"),
                    StringUtils.AsHaxeString("机身范围内的敌人直接秒杀（穿墙判定）"),
                    (HlFunc<bool>)delegate
                    {
                        config.Value.killOnContact = !config.Value.killOnContact;
                        config.Save();
                        return config.Value.killOnContact;
                    },
                    new Ref<bool>(ref kill),
                    ob.scrollerFlow);

                bool inv = config.Value.invincible;
                ob.addToggleWidget(
                    StringUtils.AsHaxeString("无敌"),
                    StringUtils.AsHaxeString("affect 5：撞不伤、打不死、免掉落伤害"),
                    (HlFunc<bool>)delegate
                    {
                        config.Value.invincible = !config.Value.invincible;
                        config.Save();
                        return config.Value.invincible;
                    },
                    new Ref<bool>(ref inv),
                    ob.scrollerFlow);

                bool flyInteract = config.Value.interactWhileFlying;
                ob.addToggleWidget(
                    StringUtils.AsHaxeString("飞行中也能交互（实验）"),
                    StringUtils.AsHaxeString("默认关：按 F7 切回原版角色去交互更稳"),
                    (HlFunc<bool>)delegate
                    {
                        config.Value.interactWhileFlying = !config.Value.interactWhileFlying;
                        config.Save();
                        return config.Value.interactWhileFlying;
                    },
                    new Ref<bool>(ref flyInteract),
                    ob.scrollerFlow);

                ob.addSliderWidget(
                    StringUtils.AsHaxeString("飞行速度"),
                    (HlAction<double>)delegate (double v)
                    {
                        config.Value.flySpeed = v;
                        config.Save();
                    },
                    config.Value.flySpeed,
                    Ref<double>.In(0.5),
                    ob.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(2.0),
                    Ref<double>.In(60.0),
                    null,
                    Ref<int>.In(0));

                ob.addSliderWidget(
                    StringUtils.AsHaxeString("直升机大小"),
                    (HlAction<double>)delegate (double v)
                    {
                        config.Value.spriteScale = v;
                        config.Save();
                    },
                    config.Value.spriteScale,
                    Ref<double>.In(0.05),
                    ob.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.15),
                    Ref<double>.In(3.00),
                    null,
                    Ref<int>.In(0));

                ob.addSliderWidget(
                    StringUtils.AsHaxeString("斩杀半径"),
                    (HlAction<double>)delegate (double v)
                    {
                        config.Value.killRadius = v;
                        config.Save();
                    },
                    config.Value.killRadius,
                    Ref<double>.In(0.1),
                    ob.scrollerFlow,
                    Ref<bool>.In(false),
                    Ref<bool>.In(true),
                    Ref<double>.In(0.5),
                    Ref<double>.In(15.0),
                    null,
                    Ref<int>.In(0));

                ob.updateScroller();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "[HeliHero] 建立选项菜单失败");
            }
        }
    }
}