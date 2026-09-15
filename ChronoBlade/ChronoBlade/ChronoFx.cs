using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.h2d;
using dc.libs.heaps.slib;
using dc.pr;
using dc.tool;
using HaxeProxy.Runtime;
using Hashlink;
using Hashlink.Proxy.Objects;
using ModCore.Utilities;

using SysMath = System.Math;
using SysConsole = System.Console;

namespace ChronoBlade
{
    /// <summary>
    /// 时之刃的特效层：罗马数字刻印、一周飞镖、背景时钟 + 剑雨。
    ///
    /// 全部是"纯表现"：用 dc.libs.heaps.slib.HSprite 挂在关卡 scroller 上、每帧手动推进
    /// （不新增 Entity，避免哈希链接对象构造的不确定性）；伤害与判定由武器技能数据负责。
    ///
    /// 贴图来源（全部复用游戏自带资源，不新增美术文件）：
    ///   atlas/fxTimeKeeper.atlas
    ///     fxKingsBladeCast   —— TimeKeeper 放"levelUpRadius"时的金色法阵（原作里时钟特效的主体）
    ///     fxThrowShuriken    —— TimeKeeper 丢飞镖的镖身
    ///     kingsBladeFxDash   —— 王者之剑的冲刺剑影（当作砸下来的剑）
    /// </summary>
    public static class ChronoFx
    {
        private sealed class SpriteFx
        {
            public HSprite Sprite = null!;
            public double Life;
            public double MaxLife;
            public bool Rising;
            public double RiseSpeed;
            public bool Falling;
            public double FallSpeed;
            public bool FadeIn;
            public double Spin;
            public bool ScaleIn;
            public double ScaleFrom;
            public double ScaleTo;
        }

        private static readonly List<SpriteFx> _fx = new();

        private static readonly string[] RomanTable =
        {
            "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"
        };

        private static SpriteLib? _fxTimeKeeper;
        private static bool _atlasLoadFailed;

        // ---------------------------------------------------------------- 常量
        private const string GROUP_CLOCK = "fxKingsBladeCast";
        private const string GROUP_SHURIKEN = "fxThrowShuriken";
        private const string GROUP_SWORD = "kingsBladeFxDash";

        private static readonly string[] ATLAS_PATHS =
        {
            "atlas/fxTimeKeeper.atlas",
            "fxTimeKeeper.atlas",
            "atlas/fxTimeKeeper",
        };

        public static string Roman(int n)
        {
            if (n <= 0) return RomanTable[0];
            if (n <= RomanTable.Length) return RomanTable[n - 1];
            return n.ToString();
        }

        // ================================================================ 每帧推进
        public static void Update(double dt)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                try
                {
                    if (f.Sprite == null || f.Sprite.destroyed)
                    {
                        _fx.RemoveAt(i);
                        continue;
                    }

                    f.Life -= dt;
                    if (f.Life <= 0)
                    {
                        Detach(f.Sprite);
                        _fx.RemoveAt(i);
                        continue;
                    }

                    double t = 1.0 - f.Life / SysMath.Max(0.0001, f.MaxLife);   // 0 -> 1

                    if (f.FadeIn)
                    {
                        f.Sprite.alpha = t < 0.15
                            ? t / 0.15
                            : (t > 0.8 ? SysMath.Max(0.0, (1.0 - t) / 0.2) : 1.0);
                    }
                    else
                    {
                        f.Sprite.alpha = SysMath.Max(0.0, SysMath.Min(1.0, f.Life / SysMath.Max(0.0001, f.MaxLife)));
                    }

                    if (f.ScaleIn)
                    {
                        double s = f.ScaleFrom + (f.ScaleTo - f.ScaleFrom) * t;
                        f.Sprite.scaleX = s;
                        f.Sprite.scaleY = s;
                    }

                    if (f.Rising)
                    {
                        f.Sprite.y -= f.RiseSpeed * dt;
                    }

                    if (f.Falling)
                    {
                        f.Sprite.y += f.FallSpeed * dt;
                    }

                    if (f.Spin != 0)
                    {
                        f.Sprite.rotation += f.Spin * dt;
                    }

                    f.Sprite.posChanged = true;
                }
                catch
                {
                    _fx.RemoveAt(i);
                }
            }

            // 自己推帧的循环动画（记忆碎片等）：播满时长后移除
            for (int i = _loops.Count - 1; i >= 0; i--)
            {
                var lp = _loops[i];
                try
                {
                    if (lp.Sprite == null || lp.Sprite.destroyed)
                    {
                        _loops.RemoveAt(i);
                        continue;
                    }

                    lp.Life -= dt;
                    if (lp.Life <= 0)
                    {
                        Detach(lp.Sprite);
                        _loops.RemoveAt(i);
                        continue;
                    }

                    lp.T += dt;
                    int frame = (int)(lp.T * lp.Fps) % lp.Frames;
                    if (frame < 0) frame = 0;
                    try { lp.Sprite.setFrame(frame); } catch { }
                    lp.Sprite.posChanged = true;
                }
                catch
                {
                    _loops.RemoveAt(i);
                }
            }
        }

        public static void Clear()
        {
            foreach (var f in _fx)
            {
                try { Detach(f.Sprite); } catch { }
            }
            _fx.Clear();
            foreach (var lp in _loops)
            {
                try { Detach(lp.Sprite); } catch { }
            }
            _loops.Clear();
            RemoveAura();
        }

        private static void Detach(HSprite? s)
        {
            try
            {
                if (s != null && !s.destroyed && s.parent != null)
                {
                    s.parent.removeChild(s);
                }
            }
            catch { }
        }

        // ================================================================ 弹效果辅助
        /// <summary>五之弹 Hei：在指定位置留下一枚"未来残影"（半透明 TIMEZHANJI 动画）。</summary>
        public static void PlayShadowAfterimage(Mob? mob, double pixelX, double pixelY)
        {
            var level = mob?._level ?? _hero?._level;
            if (level == null) return;

            try
            {
                var lib = Assets.Class.lib.get(ToHaxe(CastAtlasPath));   // 复用技能图集
                if (lib == null) return;

                int startFrame = 0;
                var h = new HSprite(lib, ToHaxe(CastGroup), Ref<int>.From(ref startFrame), null);
                if (h == null) return;

                var pivot = h.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                h.scaleX = 0.7;
                h.scaleY = 0.7;
                h.alpha = 0.45;                       // 半透明 = "残影"
                level.scroller.addChildAt(h, Const.Class.DP_ROOM_MAIN);
                h.x = pixelX;
                h.y = pixelY;
                h.posChanged = true;

                _fx.Add(new SpriteFx
                {
                    Sprite = h,
                    Life = 0.9,
                    MaxLife = 0.9,
                    FadeIn = false,
                });
            }
            catch (Exception ex)
            {
                LogThrottled($"残影创建失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 十之弹 Yud：在目标头顶播放"记忆碎片"动画。
        /// 原来这里是一次性播放（killAfterPlay），动画本身不到 3 秒就播完了，
        /// 所以现在改成**循环播放满 seconds 秒**，正好覆盖"N 秒后处决"的整段时间。
        ///
        /// seconds 由调用方给：普通品质 3 秒，带上传奇词条 ChronoBulletDouble 时 1.5 秒
        /// （动画和处决延时必须用同一个值，否则会"动画播完了人还活着"或者反过来）。
        /// </summary>
        public static void PlayMemoryAt(Mob? mob, double pixelX, double pixelY, double seconds)
        {
            var level = mob?._level ?? _hero?._level;
            if (level == null) return;
            if (seconds <= 0) seconds = MemorySeconds;
            PlayLoopingAt(level, CastAtlasPath, CastGroup, pixelX, pixelY - 40.0, 0.6,
                          seconds, MemoryFps);
        }

        /// <summary>记忆动画默认总时长（普通品质；传奇见 ChronoBullets.ApplyYud）。</summary>
        public const double MemorySeconds = 3.0;

        /// <summary>
        /// 记忆动画的播放帧率。TIMEZHANJI 共 46 帧，30fps 下一遍 ≈1.53 秒，
        /// 3 秒里正好完整播两遍（这就是"多播放几次"）。
        /// 想让它播更多遍就把这个值调大，想变慢就调小。
        /// </summary>
        private const double MemoryFps = 30.0;

        /// <summary>一段自己推帧的循环动画。</summary>
        private sealed class LoopFx
        {
            public HSprite Sprite = null!;
            public double Life;
            public double T;
            public int Frames = 1;
            public double Fps = 15.0;
        }

        private static readonly List<LoopFx> _loops = new();

        /// <summary>
        /// 在指定位置循环播放一段图集动画（自己推帧，不依赖 AnimManager），
        /// 播满 totalSeconds 秒后自动移除。
        /// </summary>
        private static void PlayLoopingAt(Level level, string atlasPath, string group,
                                          double pixelX, double pixelY, double scale,
                                          double totalSeconds, double fps)
        {
            if (level == null) return;

            try
            {
                SpriteLib? lib;
                try { lib = Assets.Class.lib.get(ToHaxe(atlasPath)); }
                catch (Exception ex)
                {
                    LogThrottled($"循环特效图集加载异常 {atlasPath}: {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (lib == null)
                {
                    LogThrottled($"循环特效图集取不到: {atlasPath}");
                    return;
                }

                int startFrame = 0;
                HSprite h;
                try { h = new HSprite(lib, ToHaxe(group), Ref<int>.From(ref startFrame), null); }
                catch (Exception ex)
                {
                    LogThrottled($"循环特效创建失败 {atlasPath}('{group}') → {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (h == null) return;

                // 先钉帧再暂停动画：只 setFrame 会被 AnimManager 覆盖掉
                try { h.setFrame(0); } catch { }
                try { h.get_anim().pauseCurrentAnim(); } catch { }

                var pivot = h.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                h.scaleX = scale;
                h.scaleY = scale;
                h.alpha = 1.0;

                level.scroller.addChildAt(h, Const.Class.DP_ROOM_MAIN);
                h.x = pixelX;
                h.y = pixelY;
                h.posChanged = true;

                int frames = 1;
                try { frames = h.totalFrames(); } catch { }
                if (frames <= 0) frames = 1;

                _loops.Add(new LoopFx
                {
                    Sprite = h,
                    Life = totalSeconds,
                    T = 0,
                    Frames = frames,
                    Fps = fps,
                });

                Log($"记忆动画已开始循环播放：{atlasPath} 帧数={frames} {fps:0}fps 共 {totalSeconds:0.#} 秒");
            }
            catch (Exception ex)
            {
                Log($"[循环特效] 播放失败 {atlasPath}: {ex}");
            }
        }

        private static Hero? _hero;

        /// <summary>缓存英雄引用（残影等辅助用）。</summary>
        public static void SetHero(Hero? hero) => _hero = hero;

        // ================================================================ 通用：一次性动画特效
        /// <summary>
        /// 在指定像素坐标播放"一遍就销毁"的图集动画。
        /// 死亡特效（TIMEJIBAI）与技能释放特效（TIMEZHANJI）共用这段逻辑。
        /// </summary>
        private static void PlayOneShotAt(Level level, string atlasPath, string group,
                                          double pixelX, double pixelY, double scale)
        {
            if (level == null) return;

            try
            {
                SpriteLib? lib;
                try
                {
                    lib = Assets.Class.lib.get(ToHaxe(atlasPath));
                }
                catch (Exception ex)
                {
                    LogThrottled($"特效图集加载异常 {atlasPath}: {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (lib == null)
                {
                    LogThrottled($"特效图集取不到: {atlasPath}");
                    return;
                }

                int startFrame = 0;
                HSprite h;
                try
                {
                    h = new HSprite(lib, ToHaxe(group), Ref<int>.From(ref startFrame), null);
                }
                catch (Exception ex)
                {
                    LogThrottled($"特效创建失败 {atlasPath}('{group}') → {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (h == null) return;

                var pivot = h.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                h.scaleX = scale;
                h.scaleY = scale;

                // 照 MCDcrit：不指定层会被背景盖住，必须挂到世界层的指定深度层
                level.scroller.addChildAt(h, Const.Class.DP_ROOM_MAIN);

                h.x = pixelX;
                h.y = pixelY;
                h.posChanged = true;

                // 播一遍（不循环），播完自动销毁
                var anim = h.get_anim();
                anim.play(ToHaxe(group), 0, null);       // flags 0 = 不循环
                anim.killAfterPlay();
            }
            catch (Exception ex)
            {
                Log($"[特效] 播放失败 {atlasPath}: {ex}");
            }
        }

        // ================================================================ 技能释放特效（TIMEZHANJI）
        /// <summary>
        /// 技能释放特效图集：Assets/atlas/TIMEZHANJI.atlas
        /// （libGDX 文本格式，46 帧 idle_0000~idle_0045，单帧 298×298）。
        /// 释放技能时在英雄位置播放。
        /// </summary>
        private const string CastAtlasPath = "atlas/TIMEZHANJI.atlas";
        private const string CastGroup = "idle";
        private const double CastScale = 1.0;

        /// <summary>在英雄位置播放技能释放特效。</summary>
        public static void PlayCastEffect(Hero? hero)
        {
            if (hero == null || hero.destroyed || hero._level == null) return;

            try
            {
                double px = (hero.cx + hero.xr) * 24.0;
                double py = (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;

                PlayOneShotAt(hero._level, CastAtlasPath, CastGroup, px, py, CastScale);

                if (_castLogCount < 6)
                {
                    _castLogCount++;
                    Log($"技能释放特效已播放: 位置=({px:F0},{py:F0})");
                }
            }
            catch (Exception ex)
            {
                Log($"[技能特效] 播放失败: {ex}");
            }
        }

        private static int _castLogCount;

        // ================================================================ 怪物死亡特效（TIMEJIBAI）
        /// <summary>
        /// 死亡特效图集：Assets/atlas/TIMEJIBAI.atlas（libGDX 文本格式，22 帧 idle_0000~idle_0021）。
        /// 怪物死亡时在它的位置播放一次。
        /// </summary>
        private const string DeathAtlasPath = "atlas/TIMEJIBAI.atlas";
        private const string DeathGroup = "idle";
        private const int DeathFrameCount = 22;

        /// <summary>死亡特效缩放：原图 148×148，1.0 就是原大小。</summary>
        private const double DeathScale = 1.0;

        private static SpriteLib? GetDeathLib()
        {
            try
            {
                var lib = Assets.Class.lib.get(ToHaxe(DeathAtlasPath));
                if (lib != null) return lib;
                LogThrottled($"死亡特效图集取不到: {DeathAtlasPath}");
            }
            catch (Exception ex)
            {
                LogThrottled($"死亡特效图集加载异常: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        /// <summary>怪物死亡时，在它所在位置播放 TIMEJIBAI 动画（播完自动销毁）。</summary>
        public static void PlayDeathEffect(Mob mob)
        {
            if (mob == null || mob._level == null) return;
            PlayDeathEffectAt(mob, (mob.cx + mob.xr) * 24.0, (mob.cy + mob.yr) * 24.0 - mob.hei * 0.5);
        }

        /// <summary>
        /// 指定像素坐标播放死亡特效。
        /// 坐标要在调用原版 onDie **之前**取好 —— 原版可能已经清掉 sprite / 改动实体状态，
        /// 之后再读位置可能不准。
        /// </summary>
        public static void PlayDeathEffectAt(Mob? mob, double pixelX, double pixelY)
        {
            if (mob == null) return;
            var level = mob._level;
            if (level == null) return;

            PlayOneShotAt(level, DeathAtlasPath, DeathGroup, pixelX, pixelY, DeathScale);

            if (_deathLogCount < 5)
            {
                _deathLogCount++;
                Log($"死亡特效已播放: 位置=({pixelX:F0},{pixelY:F0}) 帧数≈{DeathFrameCount}");
            }
        }

        private static int _deathLogCount;

        // ================================================================ 第 1a：罗马数字刻印
        /// <summary>
        /// 自建图集：Assets/atlas/TIMEKASAN.atlas（libGDX 文本格式，图页同名 TIMEKASAN.png）。
        /// 帧名 idle_0000…idle_0011 对应罗马数字 I…XII（共 12 帧，已足够覆盖十二之弹）。
        /// 加载方式参考 Retinue 模组：Assets.Class.lib.get("atlas/xxx.atlas")。
        ///
        /// public：选择弹药面板（ChronoAmmoPanel）要在 UI 格子里复用同一套图集与帧映射。
        /// </summary>
        public const string NumeralAtlasPath = "atlas/TIMEKASAN.atlas";

        /// <summary>
        /// 图集里的组名。你的帧名是 idle_0000…idle_0011，heaps 在解析 libGDX 文本图集时
        /// 会把它们**合并成组 `idle` + 帧索引 0~11**（日志里的
        /// "Unknown frame: idle_0007(0)" 就是因为我直接拿整名当组名了）。
        /// </summary>
        public const string NumeralGroup = "idle";

        /// <summary>
        /// 罗马数字 → 帧索引（I→0 … XII→11，正好等于"第几发 - 1"）。
        /// 斩击刻印、开火蹦字、选择弹药面板三处共用这一张表，改图集只需改这里。
        /// </summary>
        public static int FrameIndexForRoman(string roman)
        {
            switch (roman)
            {
                case "I": return 0;
                case "II": return 1;
                case "III": return 2;
                case "IV": return 3;
                case "V": return 4;
                case "VI": return 5;
                case "VII": return 6;
                case "VIII": return 7;
                case "IX": return 8;
                case "X": return 9;
                case "XI": return 10;
                case "XII": return 11;
                default: return 11;
            }
        }

        /// <summary>第 index 发子弹（0 基）对应的图集帧号。</summary>
        public static int FrameIndexForBullet(int bulletIndex) => FrameIndexForRoman(Roman(bulletIndex + 1));

        /// <summary>
        /// 取图集：照 MCDcrit 的做法 —— **每次现取**，由 AssetsLibManager 内部缓存/自动重载。
        /// 不要自己长期缓存 SpriteLib：换关后旧实例可能已被销毁，贴图就显示不出来。
        /// </summary>
        public static SpriteLib? GetNumeralLib()
        {
            try
            {
                var lib = Assets.Class.lib.get(ToHaxe(NumeralAtlasPath));
                if (lib != null) return lib;
                LogThrottled($"罗马数字图集取不到: {NumeralAtlasPath}");
            }
            catch (Exception ex)
            {
                LogThrottled($"罗马数字图集加载异常: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        /// <summary>兼容旧调用：现在不再缓存，直接现取一次探测即可。</summary>
        public static bool TryPreloadNumeralAtlas() => GetNumeralLib() != null;

        /// <summary>
        /// 技能图集（TIMEZHANJI）的 SpriteLib —— 给"选择武器面板里刻刻帝的动态图标"用。
        ///
        /// 和 <see cref="GetNumeralLib"/> 一样：**每次现取**，不长期缓存
        /// （换关后旧的 SpriteLib 实例可能已被销毁，缓存的贴图就画不出来了）。
        /// </summary>
        public static SpriteLib? GetCastLib()
        {
            try
            {
                var lib = Assets.Class.lib.get(ToHaxe(CastAtlasPath));
                if (lib != null) return lib;
                LogThrottled($"技能图集取不到: {CastAtlasPath}");
            }
            catch (Exception ex)
            {
                LogThrottled($"技能图集加载异常: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        /// <summary>技能图集里 idle 帧的分组名（和释放特效用的是同一组）。</summary>
        public static string CastGroupName => CastGroup;

        private static int _atlasLogCount;
        private static void LogThrottled(string msg)
        {
            if (_atlasLogCount >= 5) return;
            _atlasLogCount++;
            Log(msg + (_atlasLogCount == 5 ? "（后续不再重复打印）" : ""));
        }

        /// <summary>在被斩击的敌人身上刻下罗马数字（怪物还活着时用）。</summary>
        public static void ShowNumeral(Hero hero, Mob mob, string numeral)
        {
            if (mob == null || mob._level == null) return;
            double px = (mob.cx + mob.xr) * 24.0;
            double py = (mob.cy + mob.yr) * 24.0 - mob.hei * 0.5;
            ShowNumeralAt(hero, px, py, numeral, mob);
        }

        /// <summary>
        /// 在指定像素坐标刻下罗马数字（停留 2.5 秒后淡出）。
        ///
        /// ⚠️ 位置一律用**世界坐标**，不依赖怪物是否还活着。
        ///   斩杀的那一击之后怪物已经死亡、spr 也没了，如果还从怪物身上取坐标就刻不出来。
        ///   level 从怪物身上取一次（调用方保证调用时怪物还在场景里）。
        /// </summary>
        public static void ShowNumeralAt(Hero? hero, double pixelX, double pixelY, string numeral, Mob? mob)
        {
            ShowNumeralScaled(hero, pixelX, pixelY, numeral, mob, NumeralScale, 16.0, NumeralLifeS);
        }

        /// <summary>
        /// 开火时在英雄位置"蹦出"当前子弹对应的罗马数字（比刻印更大、更醒目、上升更快）。
        /// 十二之弹对应 XII = 图集第 11 帧（TIMEKASAN 共 12 帧，刚好一一对应）。
        /// </summary>
        public static void PopBulletNumeral(Hero? hero, int bulletIndex)
        {
            if (hero == null || hero.destroyed || hero._level == null) return;

            int n = bulletIndex + 1;
            if (n < 1) n = 1;
            if (n > 12) n = 12;

            double px = (hero.cx + hero.xr) * 24.0;
            double py = (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5 - 20.0;

            ShowNumeralScaled(hero, px, py, Roman(n), null, NumeralPopScale, 55.0, NumeralPopLifeS);
        }

        /// <summary>通用：在指定世界坐标显示一个罗马数字（可自定义缩放/上升速度/停留时长）。</summary>
        public static void ShowNumeralScaled(Hero? hero, double pixelX, double pixelY, string numeral,
                                             Mob? mob, double scale, double riseSpeed, double lifeSeconds)
        {
            var level = mob?._level ?? hero?._level;
            if (level == null) return;

            try
            {
                var lib = GetNumeralLib();
                if (lib == null) return;

                int frameIndex = FrameIndexForRoman(numeral);
                int startFrame = frameIndex;
                HSprite h;
                try
                {
                    // 组名 idle + 帧索引（不是 idle_0007 这种整名）
                    h = new HSprite(lib, ToHaxe(NumeralGroup), Ref<int>.From(ref startFrame), null);
                }
                catch (Exception ex)
                {
                    LogThrottled($"刻印失败: new HSprite('{NumeralGroup}', {frameIndex}) → " +
                                 $"{ex.GetType().Name}: {ex.Message}");
                    return;
                }

                if (h == null) return;

                // 图集帧数保护：如果图集帧数少于 12，第 9~12 个（IX/X/XI/XII）会退回最后一帧。
                // 当前 TIMEKASAN 已经是 12 帧（idle_0000…idle_0011），这条只是防御。
                int frames = 1;
                try { frames = h.totalFrames(); } catch { }
                int shownIndex = frameIndex;
                if (frames > 0 && frameIndex >= frames) shownIndex = frames - 1;

                // 显式钉住帧，并**暂停动画**：
                // 只 setFrame 不够 —— sprite 的 AnimManager 仍会继续推进帧，
                // 于是我要的那一格被覆盖掉，看起来就像"数字没出来"。
                try { h.setFrame(shownIndex); } catch { }
                try { h.get_anim().pauseCurrentAnim(); } catch { }

                // 居中锚点（照 MCDcrit / playWeaponFx 的 pivot 设置）
                var pivot = h.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                h.scaleX = scale;
                h.scaleY = scale;
                h.alpha = 1.0;

                // 照 MCDcrit：挂到关卡世界层的主深度层（不指定层会被背景盖住，看不见）
                level.scroller.addChildAt(h, Const.Class.DP_ROOM_MAIN);

                // 直接用调用方给的世界坐标（头顶略上方）
                h.x = pixelX;
                h.y = pixelY - 30.0;
                h.posChanged = true;

                _fx.Add(new SpriteFx
                {
                    Sprite = h,
                    Life = lifeSeconds,
                    MaxLife = lifeSeconds,
                    Rising = true,
                    RiseSpeed = riseSpeed,
                });
            }
            catch (Exception ex)
            {
                Log($"[刻印] 显示失败: {ex}");
            }
        }

        /// <summary>
        /// 原图每帧 480×420。刻度：0.25 ≈ 120px；再缩小 30% → 0.175 ≈ 84px。
        /// </summary>
        private const double NumeralScale = 0.175;

        /// <summary>停留时长：在原来 1.5s 基础上多留 1 秒。</summary>
        private const double NumeralLifeS = 2.5;

        /// <summary>开火弹出用：比刻印更大、上升更快、停留 1.8 秒。</summary>
        private const double NumeralPopScale = 0.26;
        private const double NumeralPopLifeS = 1.8;

        // ================================================================ Zaphkiel：身后的时间背景
        /// <summary>
        /// 手持 Zaphkiel（十二之弹枪）时，在英雄身后循环播放的背景图集。
        /// 帧名 idle_0000…idle_0045（46 帧，每帧原尺寸 498×498）。
        ///
        /// 说明：用户给的文件名是 TIMEBEIJING.atlas（时间背景）。
        /// 想改成别的图集，只改这一行常量即可。
        /// </summary>
        private const string AuraAtlasPath = "atlas/TIMEBEIJING.atlas";
        private const string AuraGroup = "idle";

        /// <summary>
        /// 缩放：**1.0 = 完全按原图尺寸绘制，不做任何缩放**（每帧 498×498 像素）。
        /// 如果以后觉得太大，把这里调小即可（例如 0.22 ≈ 110px ≈ 4.5 格）。
        /// </summary>
        private const double AuraScale = 1.0;

        /// <summary>播放帧率：46 帧 / 15fps ≈ 3.07 秒一轮。</summary>
        private const double AuraFps = 15.0;

        /// <summary>
        /// 相对**英雄头部**再上下偏移的像素（正数 = 往下）。SpawnAura 里 pivot 是居中 (0.5,0.5)，
        /// 所以 sprite 的 x/y 就是"时钟中心"的位置，这里的偏移直接作用在中心点上。
        /// </summary>
        private const double AuraOffsetY = 0.0;

        /// <summary>
        /// 不透明度的**默认值**（配置读不到时用，0.9 = 90%）。
        /// 实际值走 `ChronoConfig.ZaphkielAuraAlpha` —— 每帧都会同步到 sprite 上，
        /// 所以改配置后不用重开关卡。
        /// </summary>
        private const double AuraAlphaDefault = 0.9;

        /// <summary>是否显示这个背景（配置 `EnableZaphkielAura`）。</summary>
        private static bool AuraEnabled
        {
            get { try { return ChronoKeys.Config.Value.EnableZaphkielAura; } catch { return true; } }
        }

        /// <summary>
        /// 不透明度（配置 `ZaphkielAuraAlpha`，0…1，夹住）。
        /// 那一项在游戏的「选项 → 模组 → ChronoBlade」里是个滑条（见 BuildMenu）。
        /// </summary>
        private static double AuraAlphaValue
        {
            get
            {
                double v;
                try { v = ChronoKeys.Config.Value.ZaphkielAuraAlpha; } catch { v = AuraAlphaDefault; }
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                return v;
            }
        }

        private static HSprite? _aura;
        private static double _auraTime;
        private static int _auraFrameCount = -1;
        private static dc.pr.Level? _auraLevel;

        /// <summary>
        /// 每帧调用。holding = 英雄**主手槽**里拿着 Zaphkiel。
        /// 满足条件就在英雄身后的图层循环播放 TIMEBEIJING；不满足立刻移除。
        ///
        /// 配置：`EnableZaphkielAura`（开关）、`ZaphkielAuraAlpha`（不透明度）。
        /// 不透明度每帧同步，改完立刻生效；开关关掉会直接把已有 sprite 移除。
        /// </summary>
        public static void UpdateAura(double dt, Hero? hero, bool holding)
        {
            try
            {
                if (!holding || !AuraEnabled || hero == null || hero.destroyed || hero._level == null)
                {
                    RemoveAura();
                    return;
                }

                var level = hero._level;

                // 换图 / sprite 已被销毁 → 重建（旧图集的实例会随关卡一起没掉）
                if (_aura == null || _aura.destroyed || !ReferenceEquals(level, _auraLevel))
                {
                    RemoveAura();
                    _aura = SpawnAura(level);
                    if (_aura == null) return;
                    _auraLevel = level;
                    _auraTime = 0;
                }

                // 时钟的**中心**对准英雄的**头部**。
                //
                // 坐标约定（和本模组其它地方一致）：`(cy + yr) * 24` 是英雄的**底边**（脚），
                // 实体框往上一个 `hei` 就是**头顶**，所以：
                //     身体中心 = (cy + yr) * 24 - hei * 0.5   ← 原来用的（时钟套在身体中间）
                //     头部     = (cy + yr) * 24 - hei         ← 现在用的（往上抬半个身高）
                // sprite 的 pivot 是居中 (0.5, 0.5)（见 SpawnAura），所以赋给 x/y 的就是时钟中心。
                // 还想微调就改 AuraOffsetY（正数往下）。
                _aura.x = (hero.cx + hero.xr) * 24.0;
                _aura.y = (hero.cy + hero.yr) * 24.0 - hero.hei + AuraOffsetY;

                // 不透明度每帧同步（配置改了立刻看得出效果）
                try { _aura.alpha = AuraAlphaValue; } catch { }

                // 手动推帧：sprite 的 AnimManager 已被暂停，帧号完全由我们控制
                _auraTime += dt;
                if (_auraFrameCount <= 0)
                {
                    try { _auraFrameCount = _aura.totalFrames(); } catch { _auraFrameCount = 1; }
                    if (_auraFrameCount <= 0) _auraFrameCount = 1;
                }

                int frame = (int)(_auraTime * AuraFps) % _auraFrameCount;
                if (frame < 0) frame = 0;
                try { _aura.setFrame(frame); } catch { }

                _aura.posChanged = true;
            }
            catch (Exception ex)
            {
                LogThrottled($"身后背景更新失败: {ex.GetType().Name}: {ex.Message}");
                RemoveAura();
            }
        }

        private static HSprite? SpawnAura(dc.pr.Level level)
        {
            try
            {
                var lib = Assets.Class.lib.get(ToHaxe(AuraAtlasPath));
                if (lib == null)
                {
                    LogThrottled($"身后背景图集取不到: {AuraAtlasPath}");
                    return null;
                }

                int f0 = 0;
                var h = new HSprite(lib, ToHaxe(AuraGroup), Ref<int>.From(ref f0), null);
                if (h == null) return null;

                // 先钉帧再暂停动画（只 setFrame 会被 AnimManager 覆盖掉）
                try { h.setFrame(0); } catch { }
                try { h.get_anim().pauseCurrentAnim(); } catch { }

                var pivot = h.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;

                h.scaleX = AuraScale;
                h.scaleY = AuraScale;
                h.alpha = AuraAlphaValue;

                // ★ 关键在图层：DP_ROOM_MAIN_BACK 比实体用的 DP_ROOM_MAIN **低一层**，
                //   也就是"英雄/怪物身后、房间背景之前"——原版宠物 Owl 也挂在这一层。
                //   挂在 DP_ROOM_MAIN 会和英雄同层，谁在前取决于添加顺序，不可靠。
                level.scroller.addChildAt(h, Const.Class.DP_ROOM_MAIN_BACK);
                h.posChanged = true;

                // 建立成功不打日志（以前每个关卡都会刷一行 "身后背景已建立…"）；
                // 取不到图集 / 创建失败时仍会报（见上面的 LogThrottled）。

                return h;
            }
            catch (Exception ex)
            {
                LogThrottled($"身后背景创建失败: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void RemoveAura()
        {
            try { if (_aura != null) Detach(_aura); } catch { }
            _aura = null;
            _auraLevel = null;
            _auraFrameCount = -1;
        }

        /// <summary>武器卸下 / 离开关卡时由外部调用，确保背景不会留在场上。</summary>
        public static void HideAura() => RemoveAura();

        private static void Log(string msg)
        {
            string line = $"[ChronoBlade] {msg}";
            System.Console.WriteLine(line);
            try { _logger?.Information(line); } catch { }
        }

        private static Serilog.ILogger? _logger;

        /// <summary>由主模块注入，让特效日志也进 logs\log_latest.log。</summary>
        public static void AttachLogger(Serilog.ILogger logger) => _logger = logger;

        // ================================================================ 第 2a：一周飞镖
        /// <summary>
        /// 参考 TimeKeeper 的 levelUpRadius：以英雄为中心向一整圈发射飞镖。
        /// 视觉用 fxTimeKeeper 的飞镖贴图组，伤害交给武器技能数据（count / radius）。
        /// </summary>
        public static void CastShurikenCircle(Hero hero, int count, double radiusTiles)
        {
            try
            {
                if (count < 3) count = 3;
                if (count > 36) count = 36;

                double cx = (hero.cx + hero.xr) * 24.0;
                double cy = (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;

                ChargeArea(hero, radiusTiles);

                double ringR = radiusTiles * 12.0;
                for (int i = 0; i < count; i++)
                {
                    double ang = i * (SysMath.PI * 2.0) / count;
                    var sp = SpawnFxSprite(hero, GROUP_SHURIKEN);
                    if (sp == null) break;

                    sp.x = cx + SysMath.Cos(ang) * ringR;
                    sp.y = cy + SysMath.Sin(ang) * ringR;
                    sp.rotation = ang;
                    sp.posChanged = true;

                    _fx.Add(new SpriteFx
                    {
                        Sprite = sp,
                        Life = 0.5,
                        MaxLife = 0.5,
                        FadeIn = true,
                        Spin = 14.0,
                        ScaleIn = true,
                        ScaleFrom = 1.6,
                        ScaleTo = 0.9,
                    });
                }
            }
            catch (Exception ex)
            {
                SysConsole.WriteLine($"[ChronoBlade] 一周飞镖失败: {ex.Message}");
            }
        }

        private static void ChargeArea(Hero hero, double radiusTiles)
        {
            try
            {
                var fx = hero._level?.fx;
                if (fx == null) return;
                var area = new Area(radiusTiles * 24.0, null);
                area.setRelativePos(hero, 0.0, 0.0);
                fx.chargeArea(area, 0.35, 1370595);
            }
            catch { }
        }

        // ================================================================ 第 3a：背景时钟 + 剑雨
        /// <summary>
        /// 参考 TimeKeeper 的 swordRain：先竖起背景时钟（fxKingsBladeCast 放大成钟面），
        /// 再把数把剑从天上砸向附近怪物。
        /// </summary>
        public static void CastSwordRain(Hero hero, int swordCount, double radiusTiles,
                                        IList<(double px, double py)> targets)
        {
            try
            {
                double cx = (hero.cx + hero.xr) * 24.0;
                double cy = (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;

                // ---- 背景时钟 ----
                var clock = SpawnFxSprite(hero, GROUP_CLOCK);
                if (clock != null)
                {
                    clock.x = cx;
                    clock.y = cy - 30.0;
                    clock.alpha = 0.9;
                    clock.posChanged = true;

                    _fx.Add(new SpriteFx
                    {
                        Sprite = clock,
                        Life = 1.8,
                        MaxLife = 1.8,
                        FadeIn = true,
                        Spin = 0.55,
                        ScaleIn = true,
                        ScaleFrom = 1.0,
                        ScaleTo = 4.4,
                    });
                }

                // ---- 地面法阵（时钟投在地上的环）----
                for (int i = 0; i < 6; i++)
                {
                    double ang = i * SysMath.PI * 2.0 / 6.0;
                    var ring = SpawnFxSprite(hero, GROUP_CLOCK);
                    if (ring == null) break;
                    ring.x = cx + SysMath.Cos(ang) * radiusTiles * 9.0;
                    ring.y = cy + SysMath.Sin(ang) * radiusTiles * 9.0;
                    ring.posChanged = true;

                    _fx.Add(new SpriteFx
                    {
                        Sprite = ring,
                        Life = 0.8,
                        MaxLife = 0.8,
                        FadeIn = true,
                        Spin = 2.2,
                        ScaleIn = true,
                        ScaleFrom = 0.6,
                        ScaleTo = 1.6,
                    });
                }

                // ---- 剑雨 ----
                int n = SysMath.Min(swordCount, targets.Count);
                for (int i = 0; i < n; i++)
                {
                    var (px, py) = targets[i];
                    double delay = i * 0.07;
                    double life = SysMath.Max(0.45, 1.2 - delay);

                    var sword = SpawnFxSprite(hero, GROUP_SWORD);
                    if (sword == null) break;

                    sword.x = px;
                    sword.y = py - 340.0 - delay * 140.0;
                    sword.rotation = SysMath.PI;
                    sword.posChanged = true;

                    _fx.Add(new SpriteFx
                    {
                        Sprite = sword,
                        Life = life,
                        MaxLife = life,
                        FadeIn = true,
                        Falling = true,
                        FallSpeed = 640.0,
                        ScaleIn = true,
                        ScaleFrom = 2.2,
                        ScaleTo = 1.4,
                    });
                }
            }
            catch (Exception ex)
            {
                SysConsole.WriteLine($"[ChronoBlade] 剑雨失败: {ex.Message}");
            }
        }

        // ================================================================ 工具
        private static HSprite? NewSprite(SpriteLib lib, string group)
        {
            try
            {
                int frame = 0;
                return new HSprite(lib, ToHaxe(group), new Ref<int>(ref frame), null);
            }
            catch (Exception ex)
            {
                SysConsole.WriteLine($"[ChronoBlade] 创建贴图 [{group}] 失败: {ex.Message}");
                return null;
            }
        }

        private static HSprite? SpawnFxSprite(Hero hero, string group)
        {
            var lib = GetFxTimeKeeper();
            if (lib == null) return null;

            var sp = NewSprite(lib, group);
            if (sp == null) return null;
            Attach(hero, sp);

            try { sp.get_anim()?.play(ToHaxe(group), null, null); } catch { }

            try
            {
                var pivot = sp.pivot;
                pivot.centerFactorX = 0.5;
                pivot.centerFactorY = 0.5;
                pivot.usingFactor = true;
                pivot.isUndefined = false;
            }
            catch { }

            try { sp.blendMode = new dc.h2d.BlendMode.Add(); } catch { }

            return sp;
        }

        private static SpriteLib? GetFxTimeKeeper()
        {
            if (_fxTimeKeeper != null) return _fxTimeKeeper;
            if (_atlasLoadFailed) return null;

            try
            {
                var manager = Assets.Class.lib;
                if (manager == null) return null;

                foreach (var path in ATLAS_PATHS)
                {
                    try
                    {
                        var lib = manager.get(ToHaxe(path));
                        if (lib != null)
                        {
                            _fxTimeKeeper = lib;
                            SysConsole.WriteLine($"[ChronoBlade] 已加载图集: {path}");
                            return lib;
                        }
                    }
                    catch { }
                }

                _atlasLoadFailed = true;
                SysConsole.WriteLine("[ChronoBlade] 无法加载 atlas/fxTimeKeeper.atlas，特效将缺席");
            }
            catch (Exception ex)
            {
                _atlasLoadFailed = true;
                SysConsole.WriteLine($"[ChronoBlade] 取图集失败: {ex.Message}");
            }
            return null;
        }

        private static void Attach(Hero hero, HSprite sp)
        {
            try
            {
                var scroller = hero._level?.scroller;
                if (scroller == null) return;
                scroller.addChildAt(sp, Const.Class.DP_ROOM_FRONT);
            }
            catch
            {
                try { hero._level?.scroller?.addChild(sp); } catch { }
            }
        }

        private static dc.String ToHaxe(string s) => new HashlinkString(s).AsHaxe<dc.String>();
    }
}
