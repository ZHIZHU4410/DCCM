using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.hl.types;
using Hashlink;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;

using SysMath = System.Math;

namespace ChronoBlade
{
    /// <summary>
    /// Zaphkiel 十二之弹的效果层。
    ///
    /// 两种触发方式（由 BulletDef.SelfCast 决定，也就是需求里的"部分子弹即使不命中也触发效果"）：
    ///   · SelfCast = true  —— **开火立刻生效**，不需要命中任何东西（一/三/五/六/九/十一之弹）
    ///   · SelfCast = false —— 命中怪物时才生效（二/四/七/八/十/十二之弹）
    /// 开火路径：TimeBullet.OnFired → OnFire()；命中路径：ChronoBladeMod.OnEntityDamage → Apply()。
    ///
    /// 需要"过一段时间还原/结束"的效果统一挂到 _timed 上，由 PostUpdate() 到点执行。
    /// </summary>
    public static class ChronoBullets
    {
        public sealed class BulletDef
        {
            public string Id = "";
            public string Name = "";
            public int Color;

            /// <summary>true = 开火立即生效（自身向）；false = 命中目标才生效（目标向）。</summary>
            public bool SelfCast;

            /// <summary>选择弹药面板里显示的效果说明（普通品质）。</summary>
            public string Desc = "";

            /// <summary>
            /// 带上传奇词条 ChronoBulletDouble 时的效果说明。
            /// 留空 = 这一发不受传奇词条影响（说明同普通）。
            /// </summary>
            public string DescLegendary = "";
        }

        /// <summary>刻刻帝的传奇词条 id（TimeBullet.LegendAffixId，两边必须一致）。</summary>
        public const string LegendAffixId = "ChronoBulletDouble";

        /// <summary>
        /// 十二发子弹的定义。
        ///
        /// ⚠️ **说明文字里绝对不能用 `×`（U+00D7）** —— 游戏字体没有这个字形，
        ///    在面板里会显示成一个方块。所有"几倍"都用中文写（"变成五倍"），
        ///    也不要用 `·`（中点）之类的全角/符号字符，用汉字或 ASCII。
        /// </summary>
        public static readonly BulletDef[] All =
        {
            new() { Id = "Aleph",    Name = "一之弹 Aleph",       Color = 0x8FE3FF, SelfCast = true,
                    Desc = "开火即生效：自身移速变成两倍，持续 10 秒",
                    DescLegendary = "开火即生效：自身移速变成四倍，持续 10 秒" },
            new() { Id = "Bet",      Name = "二之弹 Bet",         Color = 0x6FA8FF, SelfCast = false,
                    Desc = "命中后：目标减速 3.5 秒，并且移速变成 0.45 倍，持续 10 秒后还原",
                    DescLegendary = "命中后：目标减速 3.5 秒，并且移速变成 0.225 倍，持续 10 秒后还原" },
            new() { Id = "Gimel",    Name = "三之弹 Gimel",       Color = 0x9BE86B, SelfCast = true,
                    Desc = "开火即生效：回复 30% 生命",
                    DescLegendary = "开火即生效：回复 60% 生命" },
            new() { Id = "Dalet",    Name = "四之弹 Dalet",       Color = 0xFFD86B, SelfCast = true,
                    Desc = "开火即生效：把自己拽回 5 秒前的位置与生命",
                    DescLegendary = "开火即生效：把自己拽回 10 秒前的位置与生命" },
            new() { Id = "Hei",      Name = "五之弹 Hei",         Color = 0xC9B6FF, SelfCast = true,
                    Desc = "开火即生效：获得全图视野（等同探险家符文）" },
            new() { Id = "Vav",      Name = "六之弹 Vav",         Color = 0xB0FFE0, SelfCast = true,
                    Desc = "开火即生效：把自己拽回 25 秒前的位置与生命" },
            new() { Id = "Zayin",    Name = "七之弹 Zayin",       Color = 0xFFB0F0, SelfCast = false,
                    Desc = "命中后：触发时间扭曲，全关卡敌人与弹幕一起变慢 3 秒",
                    DescLegendary = "命中后：触发时间扭曲，全关卡敌人与弹幕一起变慢 6 秒" },
            new() { Id = "Het",      Name = "八之弹 Het",         Color = 0xFF9E6B, SelfCast = false,
                    Desc = "命中后：在命中点召唤我方近战怪物，跟随英雄打敌人，最多 3 个、10 秒后消失",
                    DescLegendary = "命中后：在命中点召唤我方近战怪物，跟随英雄打敌人，最多 6 个、20 秒后消失" },
            new() { Id = "Tet",      Name = "九之弹 Tet",         Color = 0xFFF0A0, SelfCast = true,
                    Desc = "开火即生效：随机传送到本关任意位置" },
            new() { Id = "Yud",      Name = "十之弹 Yud",         Color = 0xA0E0FF, SelfCast = false,
                    Desc = "命中后：目标头顶播放记忆动画 3 秒，动画结束立即处决",
                    DescLegendary = "命中后：目标头顶播放记忆动画 1.5 秒，动画结束立即处决" },
            new() { Id = "YudAleph", Name = "十一之弹 Yud-Aleph", Color = 0xFF7BD0, SelfCast = true,
                    Desc = "开火即生效：英雄前移 6 格并无敌 2 秒，2 秒后拉回原位",
                    DescLegendary = "开火即生效：英雄前移 12 格并无敌 4 秒，4 秒后拉回原位" },
            new() { Id = "YudBet",   Name = "十二之弹 Yud-Bet",   Color = 0xFF4D6D, SelfCast = true,
                    Desc = "开火即生效：回到上一关" },
        };

        /// <summary>取某一发子弹的效果说明（legendaryDouble = 这把枪带着传奇词条）。</summary>
        public static string DescriptionFor(int index, bool legendaryDouble)
        {
            var def = Get(index);
            if (!legendaryDouble) return def.Desc;
            return string.IsNullOrEmpty(def.DescLegendary) ? def.Desc : def.DescLegendary;
        }

        /// <summary>这一发是否会因为传奇词条而改变效果。</summary>
        public static bool IsBoostedByLegendary(int index)
        {
            var def = Get(index);
            return !string.IsNullOrEmpty(def.DescLegendary);
        }

        public static BulletDef Get(int index)
        {
            if (index < 0) index = 0;
            if (index >= All.Length) index = All.Length - 1;
            return All[index];
        }

        // ================================================================ 可调参数
        /// <summary>一之弹 / 三之弹 的加速持续时间（秒）。</summary>
        private const double HeroSpeedDurS = 10.0;

        /// <summary>
        /// affect 116 = 原版的"移动速度加成"，值是**加上去**的：
        /// 英雄基础跑速倍率是 1.0，所以 +1.0 = ×2.0，+4.0 = ×5.0。
        ///
        /// ⚠️ 一之弹按需求是 **×2.0**（affect 116 值 +1.0）。
        ///    早先这里是 +4.0（×5.0），需求改成 ×2.0 了 —— 别看到"+4.0"就以为是它。
        ///
        /// 现在**只有一之弹**用 affect 116：三之弹的移速加成已按要求删除
        /// （常量 `GimelSpeedAdd` 也一起删了）。
        /// </summary>
        private const double AlephSpeedAdd = 1.0;

        /// <summary>三之弹回复的生命比例（三之弹只有回血，没有任何移速加成）。</summary>
        private const double GimelHealPct = 0.30;

        /// <summary>二之弹：目标移速倍率与持续时间。</summary>
        private const double BetSlowMul = 0.45;
        private const double BetSlowDurS = 10.0;

        /// <summary>
        /// 七之弹：时间扭曲（完全等同原版 TimeDistorsion 技能）的持续时间。
        /// 传奇：时长翻倍（"效果翻倍"对这一个效果就是时长）。
        /// </summary>
        private const double ZayinDistortS = 3.0;

        /// <summary>
        /// 原版 TimeDistorsion 的参数（抄自 GamePseudocode/dc.pow/_TimeDistorsion.cs）：
        /// 以**施法者（这里就是英雄）**为中心，半径 192，颜色 3591558。
        /// </summary>
        private const double DistortRadius = 192.0;
        private const int DistortColor = 3591558;

        /// <summary>原版"时间扭曲"施加 / 结束时要清掉的 affect id。</summary>
        private const int DistortAffectId = 24;

        /// <summary>原版 TimeDistorsion 会一起拖慢的实体类别。</summary>
        private const int ClidMob = 32068;
        private const int ClidBullet = 1428;
        private const int ClidGrenade = 27931;
        private const int ClidInteractive = 47977;

        /// <summary>六之弹：把目标拽回多少秒前（需求 25.0 秒）。</summary>
        private const double VavRewindS = 25.0;

        /// <summary>八之弹召唤物的存活时间与数量上限。</summary>
        private const double AllyLifetimeS = 10.0;
        private const int MaxAliveAllies = 3;
        private const int MaxPendingAllies = 4;

        /// <summary>
        /// 当前生效的召唤参数（普通 / 传奇各一套，由八之弹命中时写入）。
        /// 真正的生成发生在下一帧的 DrainAllySpawns 里，所以要用静态字段带过去。
        /// </summary>
        private static int _curMaxAliveAllies = MaxAliveAllies;
        private static int _curMaxPendingAllies = MaxPendingAllies;
        private static double _curAllyLifetimeS = AllyLifetimeS;

        /// <summary>
        /// 位置/状态历史的保留时长与采样间隔。
        /// 六之弹 Vav 要**拽回 25 秒前**（需求值），所以必须留够 25 秒以上 —— 给 27。
        /// ⚠️ 改 Vav 的秒数时**必须同步改这里**，否则"没有历史数据"会静默不生效。
        /// 代价：10Hz × 27 秒 = 每只怪约 270 个采样点（Trim 会按时长裁掉旧的）。
        /// </summary>
        private const double HistoryKeepS = 27.0;
        private const double HistorySampleS = 0.1;

        /// <summary>十之弹：记忆动画播完后处决的延时。</summary>
        private const double ExecuteDelayS = 3.0;

        /// <summary>十一之弹：英雄前移格数与无敌时长。</summary>
        private const double YudAlephDashTiles = 6.0;
        private const double YudAlephInvulnS = 2.0;

        // ================================================================ 状态
        private static double _now;
        private static double _heroSampleAcc;

        /// <summary>
        /// 英雄的"某一时刻状态"采样。四之弹 / 六之弹就是把自己拽回这里（时间倒流）。
        ///
        /// ★ 想再纳入别的状态就**往这个结构里加字段**，然后在两处各加一行：
        ///   `Update()` 的采样、`ApplyHeroRewind()` 的还原。
        ///   取不到的字段用哨兵值（见下），还原时**跳过**，免得读失败反而把状态清成 0。
        /// </summary>
        private struct HeroSample
        {
            /// <summary>采样时刻（`_now` 时间轴）。</summary>
            public double T;

            /// <summary>格坐标中心（`cx + xr` / `cy + yr`），不是像素。</summary>
            public double X, Y;

            /// <summary>生命。-1 = 没取到。</summary>
            public int Life;

            /// <summary>身上的诅咒层数（`Hero.curseCounter`，也就是新版里的"疫病/诅咒"槽）。-1 = 没取到。</summary>
            public int Curse;

            /// <summary>历史最高诅咒（`Hero.curCurseMaxReached`）。-1 = 没取到。</summary>
            public int CurseMax;
        }

        /// <summary>
        /// 英雄的位置 + 状态历史，每 0.1 秒采样一次。
        /// 四之弹（5 秒）/ 六之弹（25 秒）的"把自己拽回去"就读这里。
        /// </summary>
        private static readonly List<HeroSample> _heroHist = new();

        /// <summary>到点要执行的动作（还原移速、拉回位置、处决、召唤物到期…）。</summary>
        private static readonly List<(double due, Action act)> _timed = new();

        /// <summary>八之弹等待召唤的队列（真正的生成在 Update 里，远离攻击循环）。</summary>
        private static readonly List<(double px, double py)> _pendingAllies = new();

        /// <summary>当前的召唤物（用于"召唤物不再召唤"和数量封顶）。</summary>
        private static readonly List<Mob> _allies = new();

        /// <summary>CDB mob 表里 Melee 分组（第 0~46 行）的怪物 id。</summary>
        private static readonly string[] MeleeMobIds =
        {
            "Zombie", "FlyZombie", "WormZombie", "Worm", "Ninja", "Runner", "LeapingDuelyst",
            "Shield", "SpikedSatyr", "Comboter", "Hooker", "Spinner", "Shocker", "Golem",
            "CastleKnight", "Lancer", "AggressiveZombie", "Bomber", "Earthquaker", "Stomper",
            "KingsFinger", "StompSkeleton", "Rampager", "SewerTtcl", "ThrowableMushroom",
            "Fugitive", "Blobby", "Enforcer", "Duelist", "Samurai", "FatZombie", "Rat",
        };

        private static readonly Random _rng = new Random();

        // ================================================================ 开火 / 命中入口

        /// <summary>
        /// 开火时调用：自身向的子弹立刻生效（不需要命中）。
        /// legendaryDouble = 这把刻刻帝带着传奇词条 ChronoBulletDouble → 效果翻倍。
        /// </summary>
        public static void OnFire(int bulletIndex, Hero? hero, bool legendaryDouble)
        {
            var def = Get(bulletIndex);
            if (!def.SelfCast) return;
            if (hero == null || hero.destroyed) return;

            try
            {
                Impact(hero._level, (hero.cx + hero.xr) * 24.0,
                       (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5, def.Color);
            }
            catch { }

            try
            {
                switch (def.Id)
                {
                    case "Aleph": ApplyAleph(hero, legendaryDouble); break;
                    case "Gimel": ApplyGimel(hero, legendaryDouble); break;
                    case "Dalet": ApplyHeroRewind(hero, DaletRewindS * Boost(legendaryDouble), "Dalet"); break;
                    case "Hei": ApplyHei(hero); break;                  // 五之弹：无"量"可翻倍
                    case "Vav": ApplyHeroRewind(hero, VavRewindS, "Vav"); break;
                    case "Tet": ApplyTet(hero); break;                  // 九之弹：无"量"可翻倍
                    case "YudAleph": ApplyYudAleph(hero, legendaryDouble); break;
                    case "YudBet": ApplyGoPreviousLevel(hero); break;    // 十二之弹：无"量"可翻倍
                }
            }
            catch (Exception ex)
            {
                Log($"「{def.Name}」开火效果异常: {ex.Message}");
            }
        }

        /// <summary>
        /// 命中怪物时调用（坐标是调用前的快照，斩杀的那一击也照样生效）。
        /// legendaryDouble = 这把刻刻帝带着传奇词条 → 效果翻倍。
        /// </summary>
        public static void Apply(int bulletIndex, Mob? mob, double px, double py, Hero? hero,
                                 bool legendaryDouble)
        {
            var def = Get(bulletIndex);

            // 自身向的子弹在开火时已经生效，命中时不要再触发一次
            if (def.SelfCast) return;

            try
            {
                Impact(hero?._level ?? mob?._level, px, py, def.Color);
            }
            catch { }

            try
            {
                switch (def.Id)
                {
                    case "Bet": ApplyBet(mob, legendaryDouble); break;
                    case "Zayin": ApplyZayin(mob, hero, legendaryDouble); break;
                    case "Het": ApplyHet(mob, px, py, legendaryDouble); break;
                    case "Yud": ApplyYud(mob, px, py, legendaryDouble); break;
                }
            }
            catch (Exception ex)
            {
                Log($"「{def.Name}」命中效果异常: {ex.Message}");
            }
        }

        /// <summary>传奇词条统一 ×2（"效果翻倍"）；说明见各 Apply* 里的注释。</summary>
        private static double Boost(bool legendaryDouble) => legendaryDouble ? 2.0 : 1.0;

        /// <summary>
        /// 把"移速 ×N"换算成 affect 116 要传的值。
        /// affect 116 是**加上去**的：基础跑速倍率 1.0，所以 ×N 要传 N-1。
        /// 想把倍率翻倍必须先把 N 翻倍再减 1 —— 直接翻倍加成是错的：
        /// ×2.0（加成 1.0）翻倍加成只有 ×3.0，而需求要的是 ×4.0。
        /// </summary>
        private static double SpeedAffectFromMultiplier(double baseMultiplier, bool legendaryDouble)
            => baseMultiplier * Boost(legendaryDouble) - 1.0;

        private static void Impact(dc.pr.Level? level, double px, double py, int color)
        {
            try { level?.fx?.impact(px, py, 46, color, 0.85); } catch { }
        }

        // ================================================================ 每帧

        public static void Update(double dt, Hero? hero)
        {
            _now += dt;

            // 十一之弹的无敌要每帧补（会被开火/命中清掉），放最前面、别被下面的早退跳过
            try { MaintainInvulnerability(); } catch { }

            if (hero == null || hero.destroyed || hero._level == null)
            {
                _pendingAllies.Clear();
                return;
            }

            // 按固定间隔采样（不是每帧），16 秒历史也不会有多少点
            _heroSampleAcc += dt;
            if (_heroSampleAcc >= HistorySampleS)
            {
                _heroSampleAcc = 0;
                try
                {
                    var s = new HeroSample
                    {
                        T = _now,
                        X = hero.cx + hero.xr,
                        Y = hero.cy + hero.yr,
                        Life = -1,
                        Curse = -1,
                        CurseMax = -1,
                    };

                    try { s.Life = (int)hero.life; } catch { }
                    // 诅咒也一起采样：四/六之弹"回溯"要把身上的诅咒也带回当时的状态，
                    // 不能只把血条拉回去（需求）。
                    try { s.Curse = hero.curseCounter; } catch { }
                    try { s.CurseMax = hero.curCurseMaxReached; } catch { }

                    _heroHist.Add(s);
                    Trim(_heroHist);
                }
                catch { }
            }

            // 八之弹的召唤物：一律在攻击循环之外生成
            try { DrainAllySpawns(hero); } catch { }
        }

        /// <summary>
        /// **真正的无敌 = affect 5**（不是 48！）。
        ///
        /// 依据（都是原版代码）：
        ///   · `dc.Entity.canBeHit()` —— 只要 `affects[5]` 非空就 `return false`，
        ///     而 `canBeHitBy()` / `canReceiveAttack()` 全都转发到 `canBeHit()`，
        ///     攻击管线（`AttackTargetImpl` / `Weapon` / `Mob`）也都查它。**这就是"打不到我"的开关**。
        ///   · 英雄翻滚的无敌帧：`Hero.cs` 里 `setAffectS(5, 0.08)` / `0.23`。
        ///   · 权杖（KingScepter）给玩家的无敌：`hero.setAffectS(5, sec)`；烟幕弹同理。
        ///
        /// ⚠️ 别再拿 **affect 48** 当无敌 —— 那是**隐身**（Invisibility），
        ///    而且 `Hero.onInvisibilityBreakingAction()` 会 `removeAllAffects(48)`，
        ///    而那个方法被 `dc.tool.Weapon`（开火）/ 近战命中 / 翻滚等处调用，
        ///    所以"开火即生效"的无敌用 48 等于刚加上就被这次开火自己清掉（踩过）。
        ///    affect 5 不会被这些动作清掉，所以加一次就够。
        /// </summary>
        private const int InvincibleAffectId = 5;

        /// <summary>
        /// 十一之弹的无敌状态：要无敌到 `_now` 的哪一刻。
        /// 0 = 当前没有在维持无敌。见 `MaintainInvulnerability()`。
        /// </summary>
        private static double _invulnUntil;
        private static Hero? _invulnHero;
        private static bool _invulnLogged;

        /// <summary>无敌还在不在（读 `affects[5]` 的长度，和 `Entity.canBeHit()` 同一处判据）。</summary>
        private static bool HeroHasInvincible(Hero hero)
        {
            try
            {
                var aff = hero.affects;
                if (aff == null || aff.length <= InvincibleAffectId) return false;
                var list = aff.getDyn(InvincibleAffectId) as ArrayObj;
                return list != null && list.length > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 每帧维护十一之弹的无敌。
        ///
        /// affect 5 不会被开火 / 命中清掉，所以正常情况下**只需要加一次**；
        /// 这里每帧只是"看看还在不在，不在了就按剩余时间补一次" ——
        /// 这样既不会每帧堆一个新的 affect 实例（那会把 `affects[5]` 撑成一长串），
        /// 也能兜住某些原版流程（如 PolloPower / 过场）中途 `removeAllAffects(5)`。
        /// </summary>
        private static void MaintainInvulnerability()
        {
            if (_invulnUntil <= 0) return;

            var hero = _invulnHero;
            if (hero == null || hero.destroyed)
            {
                _invulnUntil = 0;
                _invulnHero = null;
                return;
            }

            double remain = _invulnUntil - _now;
            if (remain <= 0)
            {
                _invulnUntil = 0;
                _invulnHero = null;
                Log("Yud-Aleph 无敌已结束");
                return;
            }

            if (HeroHasInvincible(hero)) return;   // 还在，别重复加

            try
            {
                hero.setAffectS(InvincibleAffectId, remain, Ref<double>.Null, null);
                if (!_invulnLogged)
                {
                    _invulnLogged = true;
                    Log($"Yud-Aleph 无敌已生效（affect 5，剩余 {remain:0.##} 秒）");
                }
            }
            catch (Exception ex)
            {
                if (!_invulnLogged)
                {
                    _invulnLogged = true;
                    Log($"Yud-Aleph 无敌施加失败: {ex.Message}");
                }
            }
        }

        /// <summary>每帧处理到点动作。</summary>
        public static void PostUpdate()
        {
            for (int i = _timed.Count - 1; i >= 0; i--)
            {
                if (_now < _timed[i].due) continue;
                var act = _timed[i].act;
                _timed.RemoveAt(i);
                try { act(); } catch (Exception ex) { Log($"延时动作失败: {ex.Message}"); }
            }
        }

        private static void Later(double sec, Action act) => _timed.Add((_now + sec, act));

        private static void Trim(List<HeroSample> list)
        {
            double cut = _now - HistoryKeepS;
            int n = 0;
            while (n < list.Count && list[n].T < cut) n++;
            if (n > 0) list.RemoveRange(0, n);
        }

        /// <summary>取 secondsAgo 秒前的那一份状态（找不到就退化成最早的一份）。</summary>
        private static HeroSample? Past(List<HeroSample> list, double secondsAgo)
        {
            if (list.Count == 0) return null;
            double target = _now - secondsAgo;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].T <= target) return list[i];
            }
            return list[0];
        }

        // ================================================================ 自身向（开火即生效）

        /// <summary>
        /// 一之弹：自身移速 ×2.0，维持 10 秒（原版 affect 116，值 +1.0）。
        /// 传奇：倍率翻倍 → ×4.0（affect +3.0）。持续时间不变。
        /// </summary>
        private static void ApplyAleph(Hero hero, bool legendaryDouble)
        {
            double v = SpeedAffectFromMultiplier(1.0 + AlephSpeedAdd, legendaryDouble);
            var r = new Ref<double>(ref v);
            hero.setAffectS(116, HeroSpeedDurS, r, null);
            Log($"Aleph 自身移速 ×{1.0 + v:0.0} 已施加（{HeroSpeedDurS:0} 秒）" +
                (legendaryDouble ? "【传奇·效果翻倍】" : ""));
        }

        /// <summary>
        /// 三之弹：**只回血**，回复 30% 生命。
        /// 传奇：回复 60% 生命。
        ///
        /// ⚠️ 这里以前还附带"移速 ×2.0（affect 116）维持 10 秒"，**已经按要求删掉**
        ///    —— 三之弹不再有任何移速加成（连带传奇那份也没有）。
        ///    常量 `GimelSpeedAdd` 也一起删了，别再往这儿加回来。
        /// </summary>
        private static void ApplyGimel(Hero hero, bool legendaryDouble)
        {
            double healPct = GimelHealPct * Boost(legendaryDouble);
            try
            {
                int heal = (int)SysMath.Ceiling(hero.maxLife * healPct);
                if (heal < 1) heal = 1;
                int newLife = hero.life + heal;
                if (newLife > hero.maxLife) newLife = hero.maxLife;
                hero.life = newLife;
                // 这里以前每开一枪就打一行日志（"Gimel 回复生命 +60（30%，当前 198/198）"），
                // 按要求删掉 —— 实战里太吵。要排查回血就直接看血条。
            }
            catch (Exception ex)
            {
                Log($"Gimel 回血失败: {ex.Message}");
            }
        }

        /// <summary>五之弹：获得全图视野（等于触发探险家符文）。</summary>
        private static void ApplyHei(Hero hero)
        {
            try
            {
                dc.ui.HUD.Class.ME.minimap.revealAll();
                Log("Hei 全图视野已开启（minimap.revealAll）");
            }
            catch (Exception ex)
            {
                Log($"Hei 全图视野失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 十二之弹 Yud·Bet：开火即生效 —— 英雄回到上一关。
        ///
        /// 关卡 id 的取法参考 ModEntry.cs（那个模组是能用的）：
        /// `me._level.map.id.ToString()` —— **`dc.level.LevelMap` 上直接就有 `id`**
        /// （LevelMap.cs 第 40 行 `public extern String id`）。
        ///
        /// 之前两次都没成：
        ///   1. 用 `dynamic d = entry; d.id` —— DCCM 的 hashlink 代理上动态绑定不可靠，
        ///      每次都抛异常 → 归一化成空串 → 只打一行日志就 return；
        ///   2. 改成读 `map.infos.id`（CDB 关卡行）—— 那是"关卡定义"，不一定等于 `map.id`。
        /// 现在主路径用**强类型** `map.id`，另外自己做关卡历史，不再依赖 serverStats 的语义。
        /// </summary>
        private static void ApplyGoPreviousLevel(Hero hero)
        {
            try
            {
                string current = CurrentLevelId(hero);

                // 主路径：模组自己记录的历史（最稳，只依赖 hero._level.map.id）
                string id = PreviousLevelFromTracker();
                string source = "自记录";

                // 兜底：游戏的 serverStats.history
                if (id.Length == 0)
                {
                    id = PreviousLevelFromServerStats(current);
                    source = "serverStats.history";
                }

                if (id.Length == 0)
                {
                    Log($"Yud-Bet：找不到上一关（当前关 {current}，" +
                        $"自记录 {_visitedLevels.Count} 关）—— 换过关之后就能用");
                    return;
                }

                Log($"Yud-Bet 回到上一关：{id}（来源={source}，当前关 {current}）");
                dc.cine.LevelTransition.Class.@goto(ToHaxe(id));
            }
            catch (Exception ex)
            {
                Log($"Yud-Bet 切换关卡失败: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 关卡历史（十二之弹用）

        /// <summary>模组自己记录的关卡历史（旧 → 新，最新在末尾）。</summary>
        private static readonly List<string> _visitedLevels = new();

        /// <summary>上一帧看到的关卡 id。</summary>
        private static string _lastSeenLevelId = "";

        /// <summary>
        /// 关卡**真的变了**时触发：(旧 id, 新 id)。
        ///
        /// ⚠️ 只在"已经记录过至少一关"之后才触发 —— 第一次看到关卡只是记录起点，
        ///    那不是"去下一关"，不该当成事件（狂三语音就是靠这个区分的第一关）。
        ///    用 Yud-Bet 回到上一关也会触发（那确实也是一次关卡切换）。
        /// </summary>
        public static event Action<string, string>? LevelChanged;

        /// <summary>
        /// 每帧调用：发现关卡变了就记一笔。
        ///
        /// 为什么不直接读 `game.serverStats.history`：它的语义不稳 ——
        /// 是否包含当前关、同一关会不会重复入栈、读档后还在不在，都不确定；
        /// 实测十二之弹就是卡在那条路上回不去的。
        /// 自己记只依赖 `hero._level.map.id`（ModEntry.cs 用的就是这个），稳得多。
        /// </summary>
        public static void TrackLevel(Hero? hero)
        {
            try
            {
                string id = CurrentLevelId(hero);
                if (id.Length == 0) return;
                if (string.Equals(id, _lastSeenLevelId, StringComparison.Ordinal)) return;

                if (_lastSeenLevelId.Length > 0)
                {
                    _visitedLevels.Add(_lastSeenLevelId);
                    if (_visitedLevels.Count > 32) _visitedLevels.RemoveAt(0);
                    Log($"关卡变化：{_lastSeenLevelId} → {id}（已记录 {_visitedLevels.Count} 关）");
                    try { LevelChanged?.Invoke(_lastSeenLevelId, id); } catch { }
                }
                else
                {
                    Log($"关卡记录起点：{id}");
                }
                _lastSeenLevelId = id;
            }
            catch { }
        }

        private static string PreviousLevelFromTracker()
        {
            for (int i = _visitedLevels.Count - 1; i >= 0; i--)
            {
                string cand = _visitedLevels[i];
                if (cand.Length == 0) continue;
                if (string.Equals(cand, _lastSeenLevelId, StringComparison.OrdinalIgnoreCase)) continue;
                return cand;
            }
            return "";
        }

        /// <summary>兜底：从游戏的 serverStats.history 里往前找一条与当前关不同的记录。</summary>
        private static string PreviousLevelFromServerStats(string current)
        {
            try
            {
                var stats = dc.pr.Game.Class.ME?.serverStats;
                var hist = stats?.history as ArrayObj;
                if (hist == null || hist.length < 2) return "";

                for (int i = hist.length - 2; i >= 0; i--)
                {
                    string cand = LevelIdOf(hist.getDyn(i));
                    if (cand.Length == 0) continue;
                    if (current.Length > 0 && string.Equals(cand, current, StringComparison.OrdinalIgnoreCase))
                        continue;
                    return cand;
                }
            }
            catch { }
            return "";
        }

        /// <summary>
        /// 当前关卡的 id。
        /// 主路径 `map.id`（强类型，参考 ModEntry.cs）；`map.infos.id` 只作兜底。
        /// </summary>
        private static string CurrentLevelId(Hero? hero)
        {
            try
            {
                var map = hero?._level?.map;
                if (map == null) return "";

                string viaId = Normalize(map.id?.ToString() ?? "");
                if (viaId.Length > 0) return viaId;

                return Normalize(map.infos?.id?.ToString() ?? "");
            }
            catch { return ""; }
        }

        /// <summary>从 history 的一条记录里取关卡 id（history 里存的是 LevelMap）。</summary>
        private static string LevelIdOf(object? entry)
        {
            if (entry == null) return "";

            try
            {
                if (entry is dc.level.LevelMap map)
                {
                    string viaId = Normalize(map.id?.ToString() ?? "");
                    if (viaId.Length > 0) return viaId;
                    string viaInfos = Normalize(map.infos?.id?.ToString() ?? "");
                    if (viaInfos.Length > 0) return viaInfos;
                }
            }
            catch { }

            // 兜底：反射读 id（照 ChronoCdbProbe 的做法，避免 dynamic 在代理上不可靠）
            string reflected = PropText(entry, "id");
            if (reflected.Length > 0) return reflected;

            return "";
        }

        /// <summary>反射读一个公开属性/字段并归一化成字符串。</summary>
        private static string PropText(object? obj, string name)
        {
            if (obj == null) return "";
            try
            {
                var t = obj.GetType();
                const System.Reflection.BindingFlags Flags =
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.IgnoreCase;

                var p = t.GetProperty(name, Flags);
                if (p != null) return Normalize(Convert.ToString(p.GetValue(obj)) ?? "");

                var f = t.GetField(name, Flags);
                if (f != null) return Normalize(Convert.ToString(f.GetValue(obj)) ?? "");
            }
            catch { }
            return "";
        }

        /// <summary>
        /// 九之弹：随机传送到本关**任意位置**。
        ///
        /// 做法：随机取一列，用英雄**当前所在的行**为起点往下找第一块实地
        /// （`map.getGroundY(cx, hero.cy)`），把英雄放到那块地的上面。
        ///
        /// ⚠️ 起点为什么要用"英雄当前行"而不是地图顶端：
        ///    `getGroundY` 是从给定行往下扫第一块 **solid** 格子。从顶端(0)开始扫，
        ///    扫到的是**房间天花板/顶层岩壁**那一块 —— 英雄会被放到天花板上面（地图外）。
        ///    从英雄当前行扫，拿到的一定是同层的地面，安全得多。
        ///
        /// 拿不到落点时退回"随机一只怪的位置"（早先的实现，仍然可用）。
        /// </summary>
        private static void ApplyTet(Hero hero)
        {
            try
            {
                var map = hero._level?.map;
                if (map != null && map.wid > 4 && map.hei > 4)
                {
                    for (int tries = 0; tries < 24; tries++)
                    {
                        int cx = 1 + _rng.Next(map.wid - 2);
                        int ground;
                        try { ground = map.getGroundY(cx, hero.cy); } catch { continue; }

                        // 越界 = 这一列往下没有地（坑/竖井），换一列
                        if (ground <= 0 || ground >= map.hei - 1) continue;

                        double px = (cx + 0.5) * 24.0;
                        double py = ground * 24.0 - hero.hei * 0.5;
                        hero.setPosPixel(px, py);
                        Log($"Tet 随机传送已施加（落点 列 {cx}，地面行 {ground}）");
                        return;
                    }
                    Log("Tet：24 列都没找到合法落点，改用怪物坐标兜底");
                }

                ApplyTetViaMob(hero);
            }
            catch (Exception ex)
            {
                Log($"Tet 传送失败: {ex.Message}");
            }
        }

        /// <summary>九之弹的兜底：跳到随机一只怪物的坐标（早先的实现）。</summary>
        private static void ApplyTetViaMob(Hero hero)
        {
            try
            {
                var mobs = hero._level?.entitiesByClass?.get(ClidMob) as ArrayObj;
                if (mobs == null || mobs.length == 0)
                {
                    Log("Tet：既没有合法落点也没有怪物，传送取消");
                    return;
                }

                for (int tries = 0; tries < 8; tries++)
                {
                    int i = _rng.Next(mobs.length);
                    if (mobs.getDyn(i) is not Mob m || m.destroyed || m.life <= 0) continue;
                    hero.setPosPixel((m.cx + m.xr) * 24.0, (m.cy + m.yr) * 24.0 - hero.hei * 0.5);
                    Log($"Tet 随机传送已施加（兜底：跳到 ({m.cx},{m.cy})）");
                    return;
                }
                Log("Tet：随机到的目标都无效，传送取消");
            }
            catch (Exception ex)
            {
                Log($"Tet 兜底传送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 十一之弹：英雄前移 6 格 + 无敌 2 秒，2 秒后拉回原位置。
        /// 传奇：前移 12 格 + 无敌 4 秒。
        ///
        /// ⚠️ **无敌是 affect 5**，不是 48（48 是隐身，而且开火会被
        ///    `onInvisibilityBreakingAction()` 当场清掉）。详见 `InvincibleAffectId` 的注释。
        ///
        /// 这里只登记"要无敌到什么时候"并立刻加一次；续期 / 补加在
        /// `MaintainInvulnerability()`（每帧检查 `affects[5]` 还在不在）。
        /// </summary>
        private static void ApplyYudAleph(Hero hero, bool legendaryDouble)
        {
            double tiles = YudAlephDashTiles * Boost(legendaryDouble);
            double invuln = YudAlephInvulnS * Boost(legendaryDouble);

            double ox = (hero.cx + hero.xr) * 24.0;
            double oy = (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;
            int dir = hero.dir == 0 ? 1 : hero.dir;

            try
            {
                hero.setPosPixel(ox + dir * tiles * 24.0, oy);

                // 无敌：先加一次，之后由每帧维护兜底
                _invulnHero = hero;
                _invulnUntil = _now + invuln;
                _invulnLogged = false;

                try { hero.setAffectS(InvincibleAffectId, invuln, Ref<double>.Null, null); }
                catch (Exception ex) { Log($"Yud-Aleph 无敌施加失败: {ex.Message}"); }

                Log($"Yud-Aleph 前移 {tiles:0} 格 + 无敌 {invuln:0.#} 秒（affect 5）" +
                    (legendaryDouble ? "【传奇·效果翻倍】" : ""));
            }
            catch (Exception ex)
            {
                Log($"Yud-Aleph 前移失败: {ex.Message}");
                return;
            }

            Later(invuln, () =>
            {
                try
                {
                    if (!hero.destroyed)
                    {
                        hero.setPosPixel(ox, oy);
                        Log("Yud-Aleph 已拉回原位");
                    }
                }
                catch { }
            });
        }

        // ================================================================ 目标向（命中才生效）

        /// <summary>
        /// 二之弹：目标减速 3.5 秒 + 移速 ×0.45（维持 10 秒后还原）。
        /// 传奇：减速**幅度**翻倍 → 移速 ×0.225。
        /// （这是负面效果，翻倍的是"慢了多少"；直接把 0.45 乘 2 会变成 ×0.90 = 减速减弱，那是错的。）
        /// </summary>
        private static void ApplyBet(Mob? mob, bool legendaryDouble)
        {
            if (mob == null) return;

            double slowMul = legendaryDouble ? BetSlowMul / Boost(legendaryDouble) : BetSlowMul;

            try
            {
                double v = 0;
                var r = new Ref<double>(ref v);
                mob.setAffectS(23, 3.5, r, null);
            }
            catch { }

            try
            {
                double old = mob.baseMoveSpeedMul;
                mob.baseMoveSpeedMul = old * slowMul;
                Later(BetSlowDurS, () =>
                {
                    try { if (!mob.destroyed) mob.baseMoveSpeedMul = old; } catch { }
                });
                Log($"Bet 目标减速 3.5 秒 + 移速 ×{slowMul:0.000}（{BetSlowDurS:0} 秒后还原）" +
                    (legendaryDouble ? "【传奇·效果翻倍】" : ""));
            }
            catch { }
        }

        /// <summary>
        /// 四之弹：把自己拽回 5.0 秒前的位置 + 状态（生命）。
        /// 传奇：拽回 10 秒前。
        /// </summary>
        private const double DaletRewindS = 5.0;

        /// <summary>
        /// 四之弹 / 六之弹：把**英雄自己**拽回 N 秒前的位置 + **状态**。
        ///
        /// ⚠️ 这两发是**开火即生效、对着自己**的（需求）。
        ///    早先这里是"命中后把**目标**拽回去"，方向完全反了：
        ///    它们的"时间倒流"是对自己用的回溯，不是对敌人的惩罚。
        ///
        /// 数据来自 `_heroHist`（Update 里每 0.1 秒采样一次，见 `HeroSample`），
        /// 保留时长由 `HistoryKeepS` 决定 —— 六之弹要 25 秒，所以那里至少要是 25。
        ///
        /// ★ "状态"不只是血条：**身上的诅咒也要一起回到当时**（需求）。
        ///   所以这里还原的是 `HeroSample` 里的**每一个**字段；
        ///   以后想再纳入别的状态，就往那个结构里加字段、并在这里补一行。
        ///   取不到的字段（哨兵 -1）**跳过**，免得读失败反而把状态清成 0。
        /// </summary>
        private static void ApplyHeroRewind(Hero hero, double secondsAgo, string tag)
        {
            try
            {
                var past = Past(_heroHist, secondsAgo);
                if (past == null)
                {
                    Log($"{tag} 还没有自己的历史数据（刚开始采样），本次不生效");
                    return;
                }
                var s = past.Value;

                // ---- 位置 ----
                // 采样存的是 (cx + xr, cy + yr) 的格坐标，换算成像素中心的公式
                // 必须和采样处（ChronoFx/Impact 那套）一致：y 要减半个身高。
                double px = s.X * 24.0;
                double py = s.Y * 24.0 - hero.hei * 0.5;
                hero.setPosPixel(px, py);

                // ---- 生命 ----
                string lifeMsg = "生命未变";
                if (s.Life > 0)
                {
                    try
                    {
                        int before = hero.life;
                        int life = s.Life;
                        if (life > hero.maxLife) life = hero.maxLife;
                        hero.life = life;
                        lifeMsg = $"生命 {before} → {life}";
                    }
                    catch (Exception ex) { lifeMsg = $"生命还原失败({ex.Message})"; }
                }

                // ---- 诅咒（需求点：不能只把血条拉回去）----
                //
                // ⚠️⚠️ **必须走原版的诅咒接口，不能直接写 `curseCounter` 字段**。
                //    身上那个诅咒图标是 `Hero.curseLabel`（一个 LightTip），
                //    它**只在 `Hero.curse()` / `Hero.reduceCurse()` / `endCurse()` 里被重建**：
                //       · curse(count, reason, ...)  —— 加，并重建 label（`Hero.cs:26504`）
                //       · reduceCurse(n)             —— 减，并重建 / 清掉 label（`Hero.cs:26438`）
                //    直接 `hero.curseCounter = n` 只改了数值：**屏幕上那个诅咒数不会变**，
                //    表现就是"回溯了但诅咒没回溯"。（第一版就是这么写的。）
                //    原版所有调用方传的 reason 都是 null，这里照抄。
                string curseMsg = "诅咒未变";
                if (s.Curse >= 0)
                {
                    try
                    {
                        int before = hero.curseCounter;
                        if (s.Curse < before)
                        {
                            hero.reduceCurse(before - s.Curse);
                        }
                        else if (s.Curse > before)
                        {
                            hero.curse(s.Curse - before, null, Ref<bool>.Null, Ref<bool>.Null);
                        }
                        curseMsg = $"诅咒 {before} → {hero.curseCounter}";
                    }
                    catch (Exception ex) { curseMsg = $"诅咒还原失败({ex.Message})"; }
                }
                if (s.CurseMax >= 0)
                {
                    try { hero.curCurseMaxReached = s.CurseMax; } catch { }
                }

                Log($"{tag} 时间倒流已施加（自己回到 {secondsAgo:0.#} 秒前：{lifeMsg}，{curseMsg}）");
            }
            catch (Exception ex)
            {
                Log($"{tag} 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 七之弹：**完整触发原版 TimeDistorsion（时间扭曲）的效果**。
        ///
        /// 原版实现（GamePseudocode/dc.pow/_TimeDistorsion.cs）就三件事，这里逐条照抄：
        ///   1. `fx.timeDistorsionStart(英雄x, 英雄y, 192, 3591558)` —— 环形光环，**以施法者为中心**；
        ///      结束时 `timeDistorsionEnd(...)`；期间 `Audio.fadeTimeDistortEffect(1.0)` → 结束回 0.0
        ///      （那层"时间扭曲"的画面滤镜 + 音频处理就在这个混音参数上）。
        ///   2. 对**全关卡**的 Mob(32068) / Bullet(1428) / Grenade(27931) / Interactive(47977)
        ///      施加 `affect 24`，时长 = 本次扭曲的秒数。Bullet 只处理"parent 是 Mob"的（原版就是这样）。
        ///   3. 结束时对同样的类别 `removeAllAffects(24)`。
        ///
        /// 和原版唯一的有意差别：**跳过英雄自己队伍的 Mob**。
        /// 原版是玩家自己放的技能，没有"自家召唤物"这回事；而本模组八之弹会召我方怪，
        /// 照抄会把自家召唤物一起拖慢 —— 那是纯负面，不是需求。
        ///
        /// 传奇：时长翻倍（这个效果"翻倍"就是翻时长）。
        /// </summary>
        private static void ApplyZayin(Mob? mob, Hero? hero, bool legendaryDouble)
        {
            var level = hero?._level ?? mob?._level;
            if (level == null) return;

            double dur = ZayinDistortS * Boost(legendaryDouble);

            // 原版是在**施法者（英雄）**位置放光环，不是命中点
            double hx = 0, hy = 0;
            try
            {
                if (hero != null)
                {
                    hx = (hero.cx + hero.xr) * 24.0;
                    hy = (hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;
                }
            }
            catch { }

            try { level.fx?.timeDistorsionStart(hx, hy, DistortRadius, DistortColor); }
            catch (Exception ex) { Log($"Zayin 光环播放失败: {ex.Message}"); }

            try { dc.Audio.Class.ME.fadeTimeDistortEffect(1.0, Ref<double>.Null); }
            catch (Exception ex) { Log($"Zayin 时间扭曲混音失败: {ex.Message}"); }

            // 同一次扭曲的"代号"：万一在持续时间里又打了一发七之弹，
            // 前一发的收尾不能把后一发的时间扭曲一起清掉（否则后一发会提前失效）。
            int gen = ++_distortGen;

            int n = ApplyDistortAffect(level, dur, hero);
            Log($"Zayin 时间扭曲已触发（全关卡 {dur:0.#} 秒，拖慢 {n} 个目标）" +
                (legendaryDouble ? "【传奇·效果翻倍】" : ""));

            Later(dur, () =>
            {
                if (gen != _distortGen)
                {
                    Log("Zayin 时间扭曲收尾被跳过（期间又触发了一次新的扭曲）");
                    return;
                }
                try { level.fx?.timeDistorsionEnd(hx, hy, DistortRadius, DistortColor); } catch { }
                try { dc.Audio.Class.ME.fadeTimeDistortEffect(0.0, Ref<double>.Null); } catch { }
                ClearDistortAffect(level);
                Log("Zayin 时间扭曲已结束（affect 24 已清除）");
            });
        }

        /// <summary>时间扭曲的代号（见 ApplyZayin 里的说明）。</summary>
        private static int _distortGen;

        /// <summary>对全关卡的 Mob / Mob 弹幕 / 手雷 / 可交互物施加 affect 24（时间扭曲）。</summary>
        private static int ApplyDistortAffect(dc.pr.Level level, double dur, Hero? hero)
        {
            int n = 0;

            // 怪物：跳过英雄自己队伍（我方召唤物），其余全拖慢
            n += EachEntity(level, ClidMob, e =>
            {
                if (e is Mob m)
                {
                    try
                    {
                        if (hero != null && m._team != null && m._team == hero._team) return false;
                    }
                    catch { }
                }
                return true;
            }, dur);

            // 怪物发射的弹幕（原版只处理 parent 是 Mob 的）
            n += EachEntity(level, ClidBullet, e =>
            {
                try { return e is Bullet b && b.parent is Mob; }
                catch { return false; }
            }, dur);

            n += EachEntity(level, ClidGrenade, _ => true, dur);
            n += EachEntity(level, ClidInteractive, _ => true, dur);

            return n;
        }

        /// <summary>结束时间扭曲：对同样的类别清掉 affect 24（照抄原版的收尾）。</summary>
        private static void ClearDistortAffect(dc.pr.Level level)
        {
            foreach (int clid in new[] { ClidMob, ClidBullet, ClidGrenade, ClidInteractive })
            {
                try
                {
                    if (level.entitiesByClass?.get(clid) is not ArrayObj list) continue;
                    for (int i = 0; i < list.length; i++)
                    {
                        if (list.getDyn(i) is Entity e)
                        {
                            try { e.removeAllAffects(DistortAffectId); } catch { }
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// 遍历某一类实体，对通过 <paramref name="filter"/> 的那些施加 affect 24。
        /// 返回实际施加成功的数量。
        /// </summary>
        private static int EachEntity(dc.pr.Level level, int clid, Func<Entity, bool> filter, double dur)
        {
            int n = 0;
            try
            {
                if (level.entitiesByClass?.get(clid) is not ArrayObj list) return 0;

                for (int i = 0; i < list.length; i++)
                {
                    if (list.getDyn(i) is not Entity e) continue;
                    try { if (e.destroyed) continue; } catch { continue; }
                    try { if (!filter(e)) continue; } catch { continue; }

                    // 第 3 个参数传 null —— 原版就是 `ref *(double*)null`（affect 24 不吃值）
                    try
                    {
                        e.setAffectS(DistortAffectId, dur, Ref<double>.Null, null);
                        n++;
                    }
                    catch { }
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// 八之弹：在命中点召唤我方 Melee 组怪物（最多 3 个，10 秒后自动消失）。
        /// 传奇：上限 6 个、存活 20 秒。
        ///
        /// 上限存在静态字段里，因为真正的生成发生在下一帧的 DrainAllySpawns 里；
        /// 以"最后一次八之弹命中"的参数为准。
        /// </summary>
        private static void ApplyHet(Mob? mob, double px, double py, bool legendaryDouble)
        {
            try
            {
                if (IsOurSummon(mob))
                {
                    Log("Het 命中自家召唤物：不再召唤（防连锁）");
                    return;
                }

                _curMaxAliveAllies = (int)(MaxAliveAllies * Boost(legendaryDouble));
                _curMaxPendingAllies = (int)(MaxPendingAllies * Boost(legendaryDouble));
                _curAllyLifetimeS = AllyLifetimeS * Boost(legendaryDouble);

                int alive = PruneAllies();
                if (alive >= _curMaxAliveAllies || _pendingAllies.Count >= _curMaxPendingAllies)
                {
                    Log($"Het 召唤已达上限（存活 {alive}/{_curMaxAliveAllies}），本次不召唤");
                    return;
                }

                _pendingAllies.Add((px, py));
                Log($"Het 召唤已排队（下一帧生成，当前存活 {alive}/{_curMaxAliveAllies}，" +
                    $"存活时长 {_curAllyLifetimeS:0} 秒）" + (legendaryDouble ? "【传奇·效果翻倍】" : ""));
            }
            catch (Exception ex)
            {
                Log($"Het 排队失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 十之弹：头顶记忆动画 3 秒，动画结束直接处决。
        /// 传奇：记忆动画 1.5 秒 —— 处决更快 = 效果翻倍，所以这里是**除以** 2。
        /// </summary>
        private static void ApplyYud(Mob? mob, double px, double py, bool legendaryDouble)
        {
            double delay = ExecuteDelayS / Boost(legendaryDouble);   // 传奇：3.0 → 1.5

            // 动画时长必须和处决延时是同一个值，否则会出现"动画播完了人还活着"
            ChronoFx.PlayMemoryAt(mob, px, py, delay);

            if (mob == null) return;
            Later(delay, () =>
            {
                try
                {
                    if (mob.destroyed) return;
                    mob.life = 0;
                    mob.onDie();
                    Log($"Yud 记忆动画结束 → 已处决目标");
                }
                catch (Exception ex)
                {
                    Log($"Yud 处决失败: {ex.Message}");
                }
            });
            Log($"Yud 记忆动画已播放（{delay:0.#} 秒后处决）" +
                (legendaryDouble ? "【传奇·效果翻倍】" : ""));
        }

        // ================================================================ 召唤物

        private static void DrainAllySpawns(Hero hero)
        {
            if (_pendingAllies.Count == 0) return;

            var level = hero._level;
            if (level == null)
            {
                _pendingAllies.Clear();
                return;
            }

            while (_pendingAllies.Count > 0)
            {
                int alive = PruneAllies();
                if (alive >= _curMaxAliveAllies)
                {
                    _pendingAllies.Clear();
                    return;
                }

                double px = _pendingAllies[0].px;
                double py = _pendingAllies[0].py;
                _pendingAllies.RemoveAt(0);

                int cx = (int)(px / 24.0);
                int cy = (int)(py / 24.0);
                string id = MeleeMobIds[_rng.Next(MeleeMobIds.Length)];

                Mob? ally = SpawnMeleeMob(level, cx, cy, id);
                if (ally == null)
                {
                    Log($"Het：{id} 没有对应的构造入口，跳过");
                    continue;
                }

                try { ally.parent = hero; } catch { }
                ally.init();
                try { ally.set_team(level.teamHero); } catch { }

                _allies.Add(ally);
                Log($"Het 已召唤我方 {id}（跟随英雄攻击敌人；{_curAllyLifetimeS:0} 秒后消失；存活 {_allies.Count}/{_curMaxAliveAllies}）");

                var victim = ally;
                Later(_curAllyLifetimeS, () =>
                {
                    try
                    {
                        if (!victim.destroyed)
                        {
                            victim.destroy();
                            Log("Het 召唤物到期已消失");
                        }
                    }
                    catch { }
                });
            }
        }

        /// <summary>
        /// Melee 组怪物的构造入口。构造签名各版本可能不同，所以逐个列出、由编译器验证；
        /// 没列进来的 id 会在日志里说明"没有构造入口"，不会崩。
        /// </summary>
        private static Mob? SpawnMeleeMob(dc.pr.Level level, int cx, int cy, string id)
        {
            try
            {
                switch (id)
                {
                    case "Zombie": return new dc.en.mob.Zombie(level, cx, cy, 1, 1);
                    case "AggressiveZombie": return new dc.en.mob.AggressiveZombie(level, cx, cy, 1, 1);
                    case "Ninja": return new dc.en.mob.Ninja(level, cx, cy, 1, 1);
                    case "Runner": return new dc.en.mob.Runner(level, cx, cy, 1, 1);
                    case "Shield": return new dc.en.mob.Shield(level, cx, cy, 1, 1);
                    case "SpikedSatyr": return new dc.en.mob.SpikedSatyr(level, cx, cy, 1, 1);
                    case "Golem": return new dc.en.mob.Golem(level, cx, cy, 1, 1);
                    case "CastleKnight": return new dc.en.mob.CastleKnight(level, cx, cy, 1, 1);
                    case "Lancer": return new dc.en.mob.Lancer(level, cx, cy, 1, 1);
                    case "Bomber": return new dc.en.mob.Bomber(level, cx, cy, 1, 1);
                    case "Blobby": return new dc.en.mob.Blobby(level, cx, cy, 1, 1);
                    case "FatZombie": return new dc.en.mob.FatZombie(level, cx, cy, 1, 1);
                    case "Rat": return new dc.en.mob.Rat(level, cx, cy, 1, 1);
                    case "Duelist": return new dc.en.mob.Duelist(level, cx, cy, 1, 1);
                    case "Samurai": return new dc.en.mob.Samurai(level, cx, cy, 1, 1);
                    case "Fugitive": return new dc.en.mob.Fugitive(level, cx, cy, 1, 1);
                    case "Enforcer": return new dc.en.mob.Enforcer(level, cx, cy, 1, 1);
                    case "Stomper": return new dc.en.mob.Stomper(level, cx, cy, 1, 1);
                    case "Earthquaker": return new dc.en.mob.Earthquaker(level, cx, cy, 1, 1);
                    case "ThrowableMushroom": return new dc.en.mob.ThrowableMushroom(level, cx, cy, 1, 1);
                    default: return null;
                }
            }
            catch (Exception ex)
            {
                Log($"Het 构造 {id} 失败: {ex.Message}");
                return null;
            }
        }

        private static int PruneAllies()
        {
            for (int i = _allies.Count - 1; i >= 0; i--)
            {
                Mob? c = _allies[i];
                bool dead;
                try { dead = c == null || c.destroyed || c.life <= 0; }
                catch { dead = true; }
                if (dead) _allies.RemoveAt(i);
            }
            return _allies.Count;
        }

        private static bool IsOurSummon(Mob? m)
        {
            if (m == null) return false;
            try
            {
                int uid = m.__uid;
                for (int i = 0; i < _allies.Count; i++)
                {
                    Mob? c = _allies[i];
                    if (c != null && !c.destroyed && c.__uid == uid) return true;
                }
            }
            catch { }
            return false;
        }

        // ================================================================ 杂项

        private static string Normalize(string s)
        {
            s = (s ?? "").Trim();
            int eq = s.IndexOf('=');
            if (eq >= 0) s = s.Substring(eq + 1).Trim();
            return s.Trim('"', '\'', ' ');
        }

        private static dc.String ToHaxe(string s) => new HashlinkString(s).AsHaxe<dc.String>();

        public static void Clear()
        {
            _heroHist.Clear();
            _timed.Clear();
            _invulnUntil = 0;
            _invulnHero = null;
            _allies.Clear();
            _pendingAllies.Clear();
            _heroSampleAcc = 0;
            _visitedLevels.Clear();
            _lastSeenLevelId = "";
            _distortGen++;      // 让所有还没到点的"时间扭曲收尾"失效
        }

        private static void Log(string msg)
        {
            string line = $"[ChronoBlade] {msg}";
            System.Console.WriteLine(line);
            try { _logger?.Information(line); } catch { }
        }

        private static Serilog.ILogger? _logger;
        public static void AttachLogger(Serilog.ILogger logger) => _logger = logger;
    }
}
