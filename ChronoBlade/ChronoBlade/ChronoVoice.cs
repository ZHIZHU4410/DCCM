using System;
using System.Collections.Generic;
using dc.en;
using HaxeProxy.Runtime;
using ModCore.Utilities;

namespace ChronoBlade
{
    /// <summary>狂三语音的四个触发时刻。</summary>
    public enum VoiceMoment
    {
        /// <summary>休闲时刻：附近没有敌人、安静了一段时间。</summary>
        Idle = 0,

        /// <summary>连杀时刻：短时间窗口内连续击杀到阈值。</summary>
        KillStreak = 1,

        /// <summary>打败 Boss。</summary>
        BossKilled = 2,

        /// <summary>去下一关（关卡 id 真的变了）。</summary>
        NextLevel = 3,
    }

    /// <summary>
    /// 狂三语音（Assets/sfx/kurumi01~08.WAV → pak 内 sfx/kurumiNN.WAV）。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 一、四个时刻
    ///
    ///   | 时刻 | 判定方式 |
    ///   |---|---|
    ///   | 休闲 | 每帧 Tick：附近 `IdleClearRadius` 格内没有可打的目标，且已经安静 `IdleAfterMs` |
    ///   | 连杀 | `OnEntityDie` 里把击杀时间戳入滑动窗口，`KillStreakWindowMs` 内凑够 `KillStreakCount` |
    ///   | Boss | `OnEntityDie` 里 `self is dc.en.mob.Boss`（原版有 `Boss : Mob` 基类，所有 boss 都继承它）|
    ///   | 下一关 | `ChronoBullets.TrackLevel` 报的关卡 id 变化（**第一次记录不算**，那次只是记录起点）|
    ///
    /// 每个时刻都**独立掷骰**（概率见 ChronoConfig.VoiceChance*），再叠一条全局最小间隔，
    /// 所以不会出现"一次连杀连播三条"。休闲时刻是"一段安静期只掷一次"——
    /// 打完一个房间有 35% 左右会开口，没中就这一段安静期不再追问。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 二、为什么是"独占声道"，而不是直接用游戏的 sfxChanGroup
    ///
    /// 需求是"最高层、不被别的音频压制、强制同一音量播完"。翻引擎源码
    /// （GamePseudocode/dc.hxd.snd/Manager.cs）后确认，能压住一段声音的只有三条路：
    ///
    ///   1. **声道被顶掉**：`Manager.update()` 会把所有 Channel 按
    ///      `sortChannel` 排序（**先比 channelGroup.priority，再比 channel.priority**），
    ///      然后从前往后分配真实的 OpenAL source；一旦 `sources.length` 用完，
    ///      排在后面的 Channel 就被标成 `isVirtual`（= 不出声）。
    ///      → 所以我们用一个 **priority 极高的独立 ChannelGroup**，永远排在最前面，
    ///        不可能被其他音效挤掉。
    ///
    ///   2. **soundGroup 的并发上限**：`Manager.update()` 里还会按
    ///      `soundGroup.maxAudible` 限制同一个 SoundGroup 同时发声的数量。
    ///      → 所以再配一个**自己的 SoundGroup 且 `maxAudible = -1`（不限）**。
    ///
    ///   3. **音量链**：`Channel.updateCurrentVolume()` 算的是
    ///      `channel.volume * (channelGroup.currentVolume * soundGroup.volume)`。
    ///      也就是说**最终音量 = 三者的乘积**。游戏内"音效音量"滑块改的是
    ///      `dc.Audio.sfxChanGroup.volume`，而我们**根本不用它那个组** ——
    ///      自己的组 volume 恒为 1.0，所以音量完全由 ChronoConfig.VoiceVolume 决定，
    ///      也就不会被任何游戏内设置或其它音效的变化带偏。
    ///
    ///   （另外确认过：引擎里**没有** sidechain（"播一个音就把别的音压低"）那种机制。
    ///     唯一会整体压低音效的是 `dc.Audio.update()` 里的 `keyFrameCineMute`
    ///     —— 过场动画时把游戏自己那 8 个 sfx 组全 `set_volume(0)`。
    ///     它动的还是**游戏自己的组**，我们自己的组不在名单里，所以语音在过场里也照常出声。
    ///     `dc.Audio.updatePriorities()` 每帧都会重写它自己那几个 sfx 子组的 priority ——
    ///     这也是不能借用它们的原因：写进去下一帧就被覆盖。）
    ///
    ///   ⚠ Assets/sfx 里的**每一个**音频（包括原来的拾取音 CHUXIAN）都走这条声道，
    ///     统一由 `PlayRaw` 播放，不然就不算"所有音频都在最高层"。
    ///
    /// ────────────────────────────────────────────────────────────────
    /// 三、想给四个时刻各配不同的句子
    ///
    ///  改 `MomentPool` 就行（现在是四个时刻共用全部 8 条）。
    /// </summary>
    public static class ChronoVoice
    {
        // ---------------------------------------------------------------- 常量

        /// <summary>kurumi01 ~ kurumi08。</summary>
        private const int LineCount = 8;

        /// <summary>
        /// 专用 ChannelGroup 的优先级。
        /// 原版那几个 sfx 子组的 priority 是 `ambient.priority + 1 + 小量`（个位数量级），
        /// 给个 5 位数保证永远排在最前面。
        /// </summary>
        private const double GroupPriority = 10000.0;

        /// <summary>组内排序用的次级键（同组内也不容易被挤掉）。</summary>
        private const double ChannelPriority = 10000.0;

        /// <summary>任意两条语音之间至少隔这么久（防止"连杀 + 进关"连着播）。</summary>
        private const long GlobalGapMs = 6000;

        /// <summary>连杀：窗口内击杀数达到这个值就触发。</summary>
        private const int KillStreakCount = 8;

        /// <summary>连杀窗口。</summary>
        private const long KillStreakWindowMs = 10000;

        /// <summary>休闲：多大范围内没有敌人算"安全"（格）。</summary>
        private const double IdleClearRadius = 20.0;

        /// <summary>休闲：安静多久之后才可能开口（毫秒）。</summary>
        private const double IdleAfterMs = 12000.0;

        /// <summary>休闲：多久查一次"附近还有没有敌人"（秒）。别每帧查，那个接口会遍历所有怪。</summary>
        private const double IdleCheckInterval = 1.0;

        /// <summary>各时刻自己的冷却（毫秒），索引 = VoiceMoment。</summary>
        private static readonly long[] MomentCooldownMs = { 45_000, 30_000, 5_000, 5_000 };

        /// <summary>配置读不到时的兜底概率，索引 = VoiceMoment。</summary>
        private static readonly double[] FallbackChance = { 0.35, 0.70, 1.00, 0.85 };

        /// <summary>
        /// 每个时刻可以用哪些句子（索引到 kurumiNN）。
        /// 现在是四个时刻共用全部 8 条 —— 想细分就往对应行里填不同的下标。
        /// </summary>
        private static readonly int[][] MomentPool =
        {
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },   // Idle
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },   // KillStreak
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },   // BossKilled
            new[] { 0, 1, 2, 3, 4, 5, 6, 7 },   // NextLevel
        };

        // ---------------------------------------------------------------- 状态

        private static readonly List<dc.hxd.res.Sound> _lines = new();
        private static dc.hxd.snd.ChannelGroup? _chanGroup;
        private static dc.hxd.snd.SoundGroup? _sndGroup;
        private static readonly Random _rng = new();

        private static readonly long[] _lastMomentMs = new long[4];
        private static long _lastAnyMs;
        private static int _lastLine = -1;

        /// <summary>连杀用的滑动窗口（击杀时间戳）。</summary>
        private static readonly List<long> _killTimes = new();

        /// <summary>休闲判定：这一轮"安静期"是否已经掷过骰。</summary>
        private static bool _idleRolled;

        /// <summary>这份安静是从什么时候开始的（0 = 目前不安静）。</summary>
        private static long _quietSinceMs;

        private static double _checkAcc;
        private static bool _loadTried;
        private static int _playLogCount;

        private static Serilog.ILogger? _logger;

        public static void AttachLogger(Serilog.ILogger logger) => _logger = logger;

        /// <summary>控制台 + 日志文件（coremod/logs/log_latest.log）都写一份，方便排错。</summary>
        private static void Log(string msg)
        {
            string line = "[ChronoVoice] " + msg;
            System.Console.WriteLine(line);
            try { _logger?.Information(line); } catch { }
        }

        // ---------------------------------------------------------------- 配置

        private static bool Enabled
        {
            get { try { return ChronoKeys.Config.Value.EnableVoice; } catch { return true; } }
        }

        private static double Volume
        {
            get
            {
                double v;
                try { v = ChronoKeys.Config.Value.VoiceVolume; } catch { v = 1.0; }
                if (v < 0) v = 0;
                if (v > 2) v = 2;
                return v;
            }
        }

        private static double ChanceFor(VoiceMoment m)
        {
            try
            {
                var c = ChronoKeys.Config.Value;
                double v = m switch
                {
                    VoiceMoment.Idle => c.VoiceChanceIdle,
                    VoiceMoment.KillStreak => c.VoiceChanceKillStreak,
                    VoiceMoment.BossKilled => c.VoiceChanceBoss,
                    VoiceMoment.NextLevel => c.VoiceChanceLevel,
                    _ => FallbackChance[(int)m],
                };
                if (v < 0) v = 0;
                if (v > 1) v = 1;
                return v;
            }
            catch
            {
                return FallbackChance[(int)m];
            }
        }

        // ---------------------------------------------------------------- 加载

        /// <summary>
        /// 加载 kurumi01~08。必须在 res.pak 载入之后调用（OnAfterLoadingAssets）。
        /// 幂等：失败也只试一次（缺文件时不必每帧重试）。
        /// </summary>
        public static void Load()
        {
            if (_loadTried) return;
            _loadTried = true;

            try
            {
                var loader = dc.hxd.Res.Class.get_loader();
                if (loader == null)
                {
                    Log("资源加载器不可用，语音不加载");
                    return;
                }

                for (int i = 0; i < LineCount; i++)
                {
                    var snd = TryLoadOne(loader, i);
                    if (snd != null) _lines.Add(snd);
                }

                Log(_lines.Count > 0
                    ? $"语音已加载 {_lines.Count}/{LineCount} 条（kurumi01~08）"
                    : $"✗ pak 里没找到语音（第一条试的是 {PathOf(0)}）");
            }
            catch (Exception ex)
            {
                Log($"语音加载失败: {ex.Message}");
            }
        }

        /// <summary>pak 内路径（Assets/sfx/kurumiNN.WAV → sfx/kurumiNN.WAV）。</summary>
        private static string PathOf(int i) => $"sfx/kurumi{i + 1:00}.WAV";

        /// <summary>
        /// 载一条。⚠️ 这里必须是强类型的 `dc.hxd.res.Loader` ——
        /// DCCM 对 hashlink 代理用 `dynamic` 派发是不可靠的（踩过坑），别图省事写 `dynamic`。
        /// </summary>
        private static dc.hxd.res.Sound? TryLoadOne(dc.hxd.res.Loader loader, int i)
        {
            // 大小写都试一遍：打包器有可能改大小写（沿用拾取音效那套写法）
            string upper = $"sfx/KURUMI{i + 1:00}.WAV";
            string lower = PathOf(i);
            string[] cands = { lower, lower.ToLowerInvariant(), upper };

            foreach (string p in cands)
            {
                try
                {
                    if (!loader.exists(p.AsHaxeString())) continue;
                    var snd = (dc.hxd.res.Sound)loader.loadCache(p.AsHaxeString(), dc.hxd.res.Sound.Class);
                    if (snd != null) return snd;
                }
                catch { }
            }
            return null;
        }

        // ---------------------------------------------------------------- 独占声道

        /// <summary>
        /// 专用 ChannelGroup：priority 拉满、volume 恒为 1、mute=false。
        /// 不复用游戏那几个组的原因见类头注释（它们每帧会被 dc.Audio 重写）。
        /// </summary>
        private static dc.hxd.snd.ChannelGroup Group()
        {
            if (_chanGroup == null)
            {
                var g = new dc.hxd.snd.ChannelGroup("ChronoVoice".AsHaxeString());
                g.volume = 1.0;
                g.mute = false;
                g.priority = GroupPriority;
                _chanGroup = g;
                Log($"独占声道已建立（group priority={GroupPriority:F0}, volume=1.0，不跟随游戏音效音量）");
            }
            return _chanGroup;
        }

        /// <summary>专用 SoundGroup：maxAudible = -1（不限同时发声数）。</summary>
        private static dc.hxd.snd.SoundGroup SoundGroupFor()
        {
            if (_sndGroup == null)
            {
                var g = new dc.hxd.snd.SoundGroup("ChronoVoice".AsHaxeString());
                g.volume = 1.0;
                g.maxAudible = -1;
                g.mono = false;
                _sndGroup = g;
            }
            return _sndGroup;
        }

        /// <summary>
        /// Assets/sfx 里**所有**音频的统一出口 —— 拾取音效也走这里。
        /// 固定走独占声道 + 固定音量 + 高优先级，保证"最高层、不被压制、同一音量播完"。
        /// </summary>
        public static void PlayRaw(dc.hxd.res.Sound? snd, string what)
        {
            if (snd == null) return;

            try
            {
                var ch = snd.play(false, Volume, Group(), SoundGroupFor());
                if (ch != null)
                {
                    ch.priority = ChannelPriority;
                    ch.mute = false;
                }

                if (_playLogCount < 12)
                {
                    _playLogCount++;
                    Log($"播放 {what}（音量 {Volume:F2}，独占声道 {GroupPriority:F0}）");
                }
            }
            catch (Exception ex)
            {
                Log($"{what} 播放失败: {ex.Message}");
            }
        }

        // ---------------------------------------------------------------- 触发

        /// <summary>某个时刻到了：掷骰 → 抽一句 → 播。</summary>
        public static void Play(VoiceMoment moment, string reason)
        {
            try
            {
                if (!Enabled) return;
                if (_lines.Count == 0) return;   // 没加载到就不吭声（加载失败已经在 Load 里报过）

                long now = Environment.TickCount64;
                int mi = (int)moment;

                if (now - _lastMomentMs[mi] < MomentCooldownMs[mi]) return;
                if (now - _lastAnyMs < GlobalGapMs) return;

                double chance = ChanceFor(moment);
                if (_rng.NextDouble() > chance)
                {
                    // 只在日志里留痕，方便调概率
                    Log($"{reason}：掷骰未中（概率 {chance:P0}）");
                    return;
                }

                int idx = PickLine(moment);
                if (idx < 0) return;

                _lastMomentMs[mi] = now;
                _lastAnyMs = now;
                _lastLine = idx;

                PlayRaw(_lines[idx], $"{reason} → kurumi{idx + 1:00}");
                Log($"{reason}：播放 kurumi{idx + 1:00}");
            }
            catch (Exception ex)
            {
                Log($"触发失败（{reason}）: {ex.Message}");
            }
        }

        /// <summary>抽一句：避免和上一句重复（只有在候选多于 1 条时才有意义）。</summary>
        private static int PickLine(VoiceMoment moment)
        {
            var pool = MomentPool[(int)moment];
            if (pool.Length == 0) return -1;

            // 过滤掉没加载成功的那几条，保证下标有效
            var ok = new List<int>(pool.Length);
            foreach (int k in pool)
            {
                if (k >= 0 && k < _lines.Count) ok.Add(k);
            }
            if (ok.Count == 0) return -1;

            if (ok.Count == 1) return ok[0];

            for (int attempt = 0; attempt < 8; attempt++)
            {
                int pick = ok[_rng.Next(ok.Count)];
                if (pick != _lastLine) return pick;
            }
            return ok[_rng.Next(ok.Count)];
        }

        /// <summary>
        /// 发生了战斗（任何一边挨打 / 我方出刀）—— 重新开始计"安静时间"。
        /// 由 ChronoBladeMod 的 onDamage / onExecute 钩子调用。
        /// </summary>
        public static void NotifyCombat()
        {
            _quietSinceMs = 0;
            _idleRolled = false;
        }

        /// <summary>
        /// 一只怪死了。`isBoss` 由调用方判定（`self is dc.en.mob.Boss`）。
        /// 只会被"敌人"的死亡调用 —— 我方召唤物不算（调用方已按队伍过滤）。
        /// </summary>
        public static void NotifyKill(bool isBoss)
        {
            try
            {
                long now = Environment.TickCount64;

                if (isBoss)
                {
                    _killTimes.Clear();
                    Play(VoiceMoment.BossKilled, "打败 Boss");
                    return;
                }

                _killTimes.Add(now);
                _killTimes.RemoveAll(t => now - t > KillStreakWindowMs);

                if (_killTimes.Count >= KillStreakCount)
                {
                    _killTimes.Clear();
                    Play(VoiceMoment.KillStreak, $"连杀 {KillStreakCount} 只（{KillStreakWindowMs / 1000} 秒内）");
                }
            }
            catch (Exception ex)
            {
                Log($"击杀计数失败: {ex.Message}");
            }
        }

        /// <summary>关卡 id 真的变了（ChronoBullets 报的）。第一次记录起点不会走到这里。</summary>
        public static void OnLevelChanged(string from, string to)
        {
            _killTimes.Clear();
            _quietSinceMs = 0;
            _idleRolled = false;
            Play(VoiceMoment.NextLevel, $"去下一关（{from} → {to}）");
        }

        /// <summary>
        /// 每帧：休闲时刻判定。最多每秒查一次"附近还有没有敌人"。
        /// 面板打开时游戏真暂停 → IOnHeroUpdate 不会被调用，所以暂停中不会开口。
        /// </summary>
        public static void Tick(double dt, Hero? hero)
        {
            try
            {
                if (!Enabled || _lines.Count == 0) return;
                if (hero == null || hero.destroyed || hero._level == null) return;

                _checkAcc += dt;
                if (_checkAcc < IdleCheckInterval) return;
                _checkAcc = 0;

                long now = Environment.TickCount64;

                // 附近还有可打的目标 → 不休闲，重新计时
                bool clear;
                try { clear = ChronoMobFinder.Pick(hero, IdleClearRadius, 1).Count == 0; }
                catch { return; }

                if (!clear)
                {
                    _quietSinceMs = 0;
                    _idleRolled = false;
                    return;
                }

                // 刚开始安静：从这一刻起计时
                if (_quietSinceMs == 0)
                {
                    _quietSinceMs = now;
                    return;
                }

                if (_idleRolled) return;                        // 这一段安静期已经掷过了
                if (now - _quietSinceMs < IdleAfterMs) return;   // 还没安静够久

                _idleRolled = true;                              // 一段安静期只掷一次
                Play(VoiceMoment.Idle, $"休闲时刻（安静 {IdleAfterMs / 1000.0:F0} 秒）");
            }
            catch { }
        }

        /// <summary>退出游戏时清理（并把声音对象放掉）。</summary>
        public static void Shutdown()
        {
            try
            {
                _lines.Clear();
                _killTimes.Clear();
                _chanGroup = null;
                _sndGroup = null;
                _lastAnyMs = 0;
                _quietSinceMs = 0;
                _idleRolled = false;
                _playLogCount = 0;
                for (int i = 0; i < _lastMomentMs.Length; i++) _lastMomentMs[i] = 0;
            }
            catch { }
        }
    }
}
