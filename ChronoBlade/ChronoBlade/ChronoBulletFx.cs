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

        public static readonly BulletDef[] All =
        {
            new() { Id = "Aleph",    Name = "一之弹 Aleph",       Color = 0x8FE3FF, SelfCast = true,
                    Desc = "开火即生效：自身移速 ×5.0（affect 116 +4.0），持续 10 秒",
                    DescLegendary = "开火即生效：自身移速 ×10.0（affect 116 +9.0），持续 10 秒" },
            new() { Id = "Bet",      Name = "二之弹 Bet",         Color = 0x6FA8FF, SelfCast = false,
                    Desc = "命中后：目标移速 ×0.45（减速），持续 10 秒",
                    DescLegendary = "命中后：目标移速 ×0.225（减速翻倍），持续 10 秒" },
            new() { Id = "Gimel",    Name = "三之弹 Gimel",       Color = 0x9BE86B, SelfCast = true,
                    Desc = "开火即生效：回复 30% 生命 + 移速 ×2.0（affect 116 +1.0），持续 10 秒",
                    DescLegendary = "开火即生效：回复 60% 生命 + 移速 ×4.0（affect 116 +3.0），持续 10 秒" },
            new() { Id = "Dalet",    Name = "四之弹 Dalet",       Color = 0xFFD86B, SelfCast = false,
                    Desc = "命中后：把目标拽回 5 秒前的位置与生命",
                    DescLegendary = "命中后：把目标拽回 10 秒前的位置与生命" },
            new() { Id = "Hei",      Name = "五之弹 Hei",         Color = 0xC9B6FF, SelfCast = true,
                    Desc = "开火即生效：获得全图视野（等同探险家符文）" },
            new() { Id = "Vav",      Name = "六之弹 Vav",         Color = 0xB0FFE0, SelfCast = false,
                    Desc = "命中后：把目标拽回 15 秒前的位置与生命" },
            new() { Id = "Zayin",    Name = "七之弹 Zayin",       Color = 0xFFB0F0, SelfCast = false,
                    Desc = "命中后：时停 3 秒（移速归零 + 禁止攻击）",
                    DescLegendary = "命中后：时停 6 秒（移速归零 + 禁止攻击）" },
            new() { Id = "Het",      Name = "八之弹 Het",         Color = 0xFF9E6B, SelfCast = false,
                    Desc = "命中后：在命中点召唤我方怪物，最多 3 只、存活 10 秒",
                    DescLegendary = "命中后：在命中点召唤我方怪物，最多 6 只、存活 20 秒" },
            new() { Id = "Tet",      Name = "九之弹 Tet",         Color = 0xFFF0A0, SelfCast = true,
                    Desc = "开火即生效：随机传送到本关任意位置（以随机怪物为坐标）" },
            new() { Id = "Yud",      Name = "十之弹 Yud",         Color = 0xA0E0FF, SelfCast = false,
                    Desc = "命中后：目标头顶播放记忆动画 3 秒，动画结束立即处决",
                    DescLegendary = "命中后：目标头顶播放记忆动画 1.5 秒，动画结束立即处决" },
            new() { Id = "YudAleph", Name = "十一之弹 Yud-Aleph", Color = 0xFF7BD0, SelfCast = true,
                    Desc = "开火即生效：向前突进 6 格 + 无敌 2 秒，2 秒后拉回原位",
                    DescLegendary = "开火即生效：向前突进 12 格 + 无敌 4 秒，4 秒后拉回原位" },
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
        /// 英雄基础跑速倍率是 1.0，所以 +4.0 = ×5.0，+1.0 = ×2.0。
        /// </summary>
        private const double AlephSpeedAdd = 4.0;
        private const double GimelSpeedAdd = 1.0;

        /// <summary>三之弹回复的生命比例。</summary>
        private const double GimelHealPct = 0.30;

        /// <summary>二之弹：目标移速倍率与持续时间。</summary>
        private const double BetSlowMul = 0.45;
        private const double BetSlowDurS = 10.0;

        /// <summary>七之弹时停持续时间（移速归零 + 锁 AI 不能攻击）。</summary>
        private const double ZayinDurS = 3.0;

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

        /// <summary>位置/状态历史的保留时长（十二之弹要 15 秒，所以留 16 秒）与采样间隔。</summary>
        private const double HistoryKeepS = 16.0;
        private const double HistorySampleS = 0.1;

        /// <summary>十之弹：记忆动画播完后处决的延时。</summary>
        private const double ExecuteDelayS = 3.0;

        /// <summary>十一之弹：英雄前移格数与无敌时长。</summary>
        private const double YudAlephDashTiles = 6.0;
        private const double YudAlephInvulnS = 2.0;

        // ================================================================ 状态
        private static double _now;
        private static double _heroSampleAcc;

        /// <summary>英雄的位置 + 生命历史：(时间, x, y, life)。</summary>
        private static readonly List<(double t, double x, double y, double life)> _heroHist = new();

        /// <summary>每只怪物的位置 + 生命历史。</summary>
        private static readonly Dictionary<int, List<(double t, double x, double y, double life)>> _mobHist = new();

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
                    case "Hei": ApplyHei(hero); break;                  // 五之弹：无"量"可翻倍
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
                    case "Dalet": ApplyDalet(mob, legendaryDouble); break;
                    case "Vav": ApplyVavRewind(mob); break;              // 六之弹：不变
                    case "Zayin": ApplyZayin(mob, legendaryDouble); break;
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
                    _heroHist.Add((_now, hero.cx + hero.xr, hero.cy + hero.yr, hero.life));
                    Trim(_heroHist);
                }
                catch { }

                try
                {
                    var mobs = hero._level.entitiesByClass?.get(32068) as ArrayObj;
                    if (mobs != null)
                    {
                        for (int i = 0; i < mobs.length; i++)
                        {
                            if (mobs.getDyn(i) is not Mob m || m.destroyed || m.life <= 0) continue;
                            int uid = m.__uid;
                            if (!_mobHist.TryGetValue(uid, out var list))
                            {
                                list = new List<(double, double, double, double)>();
                                _mobHist[uid] = list;
                            }
                            list.Add((_now, m.cx + m.xr, m.cy + m.yr, m.life));
                            Trim(list);
                        }
                    }
                }
                catch { }

                PruneMobHistory();
            }

            // 八之弹的召唤物：一律在攻击循环之外生成
            try { DrainAllySpawns(hero); } catch { }
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

        private static void Trim(List<(double t, double x, double y, double life)> list)
        {
            double cut = _now - HistoryKeepS;
            int n = 0;
            while (n < list.Count && list[n].t < cut) n++;
            if (n > 0) list.RemoveRange(0, n);
        }

        /// <summary>取 secondsAgo 秒前的那一份位置 + 生命（找不到就退化成最早的一份）。</summary>
        private static (double x, double y, double life)? Past(
            List<(double t, double x, double y, double life)> list, double secondsAgo)
        {
            if (list.Count == 0) return null;
            double target = _now - secondsAgo;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].t <= target) return (list[i].x, list[i].y, list[i].life);
            }
            return (list[0].x, list[0].y, list[0].life);
        }

        private static void PruneMobHistory()
        {
            if (_mobHist.Count <= 64) return;

            List<int>? dead = null;
            foreach (var kv in _mobHist)
            {
                if (kv.Value.Count == 0)
                {
                    dead ??= new List<int>();
                    dead.Add(kv.Key);
                }
            }
            if (dead == null) return;
            foreach (int k in dead) _mobHist.Remove(k);
        }

        // ================================================================ 自身向（开火即生效）

        /// <summary>
        /// 一之弹：自身移速 ×5.0，维持 10 秒（原版 affect 116）。
        /// 传奇：倍率翻倍 → ×10.0（affect +9.0）。持续时间不变。
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
        /// 三之弹：回复 30% 生命 + 移速 ×2.0。
        /// 传奇：回复 60% 生命 + 移速 ×4.0（affect +3.0）。持续时间不变。
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
                Log($"Gimel 回复生命 +{heal}（{healPct:P0}，当前 {newLife}/{hero.maxLife}）" +
                    (legendaryDouble ? "【传奇·效果翻倍】" : ""));
            }
            catch (Exception ex)
            {
                Log($"Gimel 回血失败: {ex.Message}");
            }

            try
            {
                double v = SpeedAffectFromMultiplier(1.0 + GimelSpeedAdd, legendaryDouble);
                var r = new Ref<double>(ref v);
                hero.setAffectS(116, HeroSpeedDurS, r, null);
                Log($"Gimel 自身移速 ×{1.0 + v:0.0} 已施加（{HeroSpeedDurS:0} 秒）" +
                    (legendaryDouble ? "【传奇·效果翻倍】" : ""));
            }
            catch (Exception ex)
            {
                Log($"Gimel 加速失败: {ex.Message}");
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

        /// <summary>九之弹：随机传送到本关卡的任意位置（用关卡里随机一只怪物的位置）。</summary>
        private static void ApplyTet(Hero hero)
        {
            try
            {
                var mobs = hero._level?.entitiesByClass?.get(32068) as ArrayObj;
                if (mobs == null || mobs.length == 0)
                {
                    Log("Tet：本关没有可作目标的怪物，传送取消");
                    return;
                }

                for (int tries = 0; tries < 8; tries++)
                {
                    int i = _rng.Next(mobs.length);
                    if (mobs.getDyn(i) is not Mob m || m.destroyed || m.life <= 0) continue;
                    hero.setPosPixel((m.cx + m.xr) * 24.0, (m.cy + m.yr) * 24.0 - hero.hei * 0.5);
                    Log($"Tet 随机传送已施加（跳到 ({m.cx},{m.cy})）");
                    return;
                }
                Log("Tet：随机到的目标都无效，传送取消");
            }
            catch (Exception ex)
            {
                Log($"Tet 传送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 十一之弹：英雄前移 6 格 + 无敌 2 秒，2 秒后拉回原位置。
        /// 传奇：前移 12 格 + 无敌 4 秒。
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
                double v = 0;
                var r = new Ref<double>(ref v);
                try { hero.setAffectS(48, invuln, r, null); } catch { }
                Log($"Yud-Aleph 前移 {tiles:0} 格 + 无敌 {invuln:0} 秒" +
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
        /// 四之弹：把目标拽回 5.0 秒前的位置 + 状态（生命）。
        /// 传奇：拽回 10 秒前。
        /// </summary>
        private static void ApplyDalet(Mob? mob, bool legendaryDouble)
        {
            if (mob == null) return;
            double secondsAgo = 5.0 * Boost(legendaryDouble);
            if (!RestoreMobState(mob, secondsAgo, "Dalet")) Log("Dalet 无历史数据，未生效");
        }

        /// <summary>六之弹 Vav：命中目标后，把它拽回 15 秒前的位置 + 状态（生命）。</summary>
        private static void ApplyVavRewind(Mob? mob)
        {
            if (mob == null) return;
            if (!RestoreMobState(mob, 15.0, "Vav")) Log("Vav 无历史数据，未生效");
        }

        private static bool RestoreMobState(Mob mob, double secondsAgo, string tag)
        {
            try
            {
                if (!_mobHist.TryGetValue(mob.__uid, out var list)) return false;
                var p = Past(list, secondsAgo);
                if (p == null) return false;

                mob.setPosPixel(p.Value.x * 24.0, p.Value.y * 24.0 - mob.hei * 0.5);
                try
                {
                    int life = (int)p.Value.life;
                    if (life > 0) mob.life = life;
                }
                catch { }

                Log($"{tag} 时间倒流已施加（回到 {secondsAgo:0.#} 秒前的位置与状态）");
                return true;
            }
            catch (Exception ex)
            {
                Log($"{tag} 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 七之弹：时停 —— 移速归零 3 秒，并且锁住 AI（不能攻击）。
        /// 传奇：时停 6 秒（这里"效果"就是时长，所以时长翻倍）。
        /// </summary>
        private static void ApplyZayin(Mob? mob, bool legendaryDouble)
        {
            if (mob == null) return;

            double dur = ZayinDurS * Boost(legendaryDouble);

            try
            {
                double v = 0;
                var r = new Ref<double>(ref v);
                mob.setAffectS(23, dur, r, null);
            }
            catch { }

            try { mob.lockAiS(dur); } catch (Exception ex) { Log($"Zayin 锁 AI 失败: {ex.Message}"); }

            try
            {
                double old = mob.baseMoveSpeedMul;
                mob.baseMoveSpeedMul = 0.0;
                Later(dur, () =>
                {
                    try
                    {
                        if (!mob.destroyed)
                        {
                            mob.baseMoveSpeedMul = old;
                            mob.unlockAi();
                        }
                    }
                    catch { }
                });
                Log($"Zayin 时停已施加（移速归零 + 禁止攻击 {dur:0.#} 秒）" +
                    (legendaryDouble ? "【传奇·效果翻倍】" : ""));
            }
            catch { }
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
            _mobHist.Clear();
            _timed.Clear();
            _allies.Clear();
            _pendingAllies.Clear();
            _heroSampleAcc = 0;
            _visitedLevels.Clear();
            _lastSeenLevelId = "";
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
