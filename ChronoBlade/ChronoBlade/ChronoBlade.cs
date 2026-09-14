using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.tool;
using dc.tool.weap;
using Hashlink;
using HaxeProxy.Runtime;
using ModCore.Storage;

namespace ChronoBlade
{
    /// <summary>
    /// 时之刃 (Chrono Blade) —— 以原版 Katana 为基类。
    ///
    /// 段位分配：
    ///   第 1a (cycle 0)：**居合前冲斩** —— 参考 Katana 项目的向前斩击。
    ///                     做法与 Katana 项目一致：置 nextIsChargeAtk = true 并给满蓄力，
    ///                     原版 Katana.onExecute 就会走"瞬移前冲 + 斩击路径上敌人依次被斩"
    ///                     的居合分支；命中时逐个刻罗马数字 I / II / III。
    ///   第 2a (cycle 1)：普通平砍 + 向一整圈召唤飞镖（参考 TimeKeeper 的 levelUpRadius）
    ///   第 3a (cycle 2)：普通平砍 + 背景时钟与落剑（参考 TimeKeeper 的 swordRain）
    ///   第 4a (cycle 3)：普通平砍（按住攻击键时基类会 set_cycle(3)，必须有这一段）
    ///
    /// ⚠️ 两条铁律（都是踩崩换来的）：
    ///   1) strikeChain 必须凑满 4 段，否则 get_curSkill() 返回 null → Null access .chargeF 崩游戏；
    ///   2) 每段的 hitFrame / charge / dynamicCharge / props 结构沿用原版 Katana 数值，
    ///      差异（距离、伤害、动画）都在 CDB 里显式改，代码只负责"什么时候走哪个分支"。
    /// </summary>
    public class ChronoBlade : Katana, IHxbitSerializable<object>
    {
        public static string name = "ChronoBlade";

        /// <summary>第 1a：居合前冲斩。</summary>
        public const int CycleDash = 0;
        /// <summary>第 2a：平砍 + 一周飞镖。</summary>
        public const int CycleShuriken = 1;
        /// <summary>第 3a：平砍 + 背景时钟与剑雨。</summary>
        public const int CycleSwordRain = 2;
        /// <summary>第 4a：普通平砍。</summary>
        public const int CycleSlash = 3;

        /// <summary>
        /// 连击段号（**自己数**，与下面的原版 `Cycle*` 无关）：
        /// 1 = 第 1 下（居合前冲）、2 = 第 2 下（一周飞镖）、3 = 第 3 下（时钟剑雨）。
        /// 见 <see cref="AddCycleEffect"/> 的注释：原版 `_cycle` 已经被本模组的
        /// "每次都注入居合"搞乱了，不能拿来判断"第几下"。
        /// </summary>
        public const int ComboDash = 1;

        public const int ComboShuriken = 2;

        public const int ComboSwordRain = 3;

        /// <summary>满蓄力帧数（原版判定：katanaChargeF/30/threshold ≥ 1 即满）。</summary>
        public const int FullChargeF = 60;

        private const int ShurikenCount = 12;   // 一周飞镖数量
        private const double ShurikenRadius = 9.0;
        private const int SwordCount = 6;       // 剑雨把数
        private const double SwordRadius = 22.0;

        /// <summary>第 2a 每枚飞镖的基础伤害（走原版 createFromHero，会跟英雄属性缩放）。</summary>
        private const int ShurikenPower = 20;

        /// <summary>第 3a 每把落剑的基础伤害。</summary>
        private const int SwordPower = 55;

        private bool _ready;

        /// <summary>
        /// 本刀已经刻过印的敌人：key = 怪物 uid，value = 刻印时的刀号。
        /// 用"刀号"而不是永久标记 —— 这样**每一刀都能重新刻**，
        /// 只在"同一刀内"对同一只怪物去重。
        /// （旧实现用 List 永久记录，结果一只怪一辈子只刻一次。）
        /// </summary>
        private readonly Dictionary<int, int> _engravedSlashOf = new();
        /// <summary>刀号：每开始一次新的斩击 +1。</summary>
        private int _slashId;
        /// <summary>本次斩击内的刻印序号（每刀从 1 数到 MaxEngravePerSlash）。</summary>
        private int _engraveIndex;
        /// <summary>一次斩击内最多刻到几（1..12）。</summary>
        private const int MaxEngravePerSlash = 12;

        public ChronoBlade(Hero hero, InventItem item) : base(hero, item)
        {
            int skillCount = -1;
            int chainCount = -1;
            try { skillCount = skills?.length ?? -1; } catch { }
            try { chainCount = wInfos?.strikeChain?.length ?? -1; } catch { }

            _ready = skillCount > 0 && chainCount > 0;

            System.Console.WriteLine(
                _ready
                    ? $"[ChronoBlade] ChronoBlade 就绪：skills={skillCount} 段 / strikeChain={chainCount} 段"
                    : $"[ChronoBlade] ⚠ ChronoBlade 初始化异常：skills={skillCount} / strikeChain={chainCount}");
        }

        // ================================================================ 攻击
        /// <summary>
        /// ★ 核心：override 原版 Katana.fixedUpdate（每帧推进居合蓄力的地方）。
        ///
        /// 为什么必须在这里做：
        ///   实测日志证明 —— 武器类确实被实例化了（Weapon.create 命中 ChronoBlade、
        ///   槽0 类型=ChronoBlade 是我方武器=True），但挂在 tool.Weapon.onExecute 和
        ///   tool.$Katana.onExecute 上的 Hook 都从未触发。游戏的攻击执行不走 hashlink 的
        ///   onExecute 分派，Hook 这条路走不通。
        ///   而 fixedUpdate 每帧都被调用（BeheadedWeaponsManager.fixedUpdate → weapon.fixedUpdate），
        ///   原版 Katana 就是在里面攒 katanaChargeF、满了以后调用 onExecute 走居合的。
        ///
        /// 手感目标：**按下攻击键的那一帧就直接前冲斩**，不要蓄力、也不要松手才触发。
        /// 做法：
        ///   1) 用 _wasDown 记录上一帧按键状态，检测"本帧刚按下"；
        ///   2) 刚按下的那一帧：置 nextIsChargeAtk=true + 满蓄力，然后**立刻**调用 onExecute()
        ///      → 原版走"瞬移前冲 + 斩击路径上敌人依次被斩"的居合分支（位移由原版负责）；
        ///   3) hitFromWeapon 会为路径上每个敌人各调用一次 → 逐个刻罗马数字。
        /// </summary>
        /// <summary>
        /// 拦掉原版的第 4 段（居合段）：按住攻击键时基类会把 _cycle 推到 3 并自动再放一次，
        /// 那正是"居合之后又多出动作"的来源。这里只允许 0/1/2 三段。
        /// </summary>
        public override int set_cycle(int v)
        {
            if (_ready && v == CycleSlash)
            {
                v = CycleDash;      // 越界请求(3) 折回第 1a，不会再触发蓄力分支
            }
            return base.set_cycle(v);
        }

        public override void fixedUpdate()
        {
            bool down = false;
            try { down = isWeaponButtonDown(); } catch { }

            // ---------------- 一次按下 = 只放一次居合 ----------------
            //
            // 原版 Katana.fixedUpdate 有两条会在"按住攻击键"期间继续推进连击的分支：
            //   · 前半段：按着键且 cycle≠0 → set_cycle(3) + nextIsChargeAtk=true
            //   · 后半段：蓄力满了 → 自己再调一次 onExecute()
            // 只挡住"我的主动居合"是不够的，原版自己还会补出一次，于是"居合之后多一个动作"。
            //
            // 压制办法：按住期间给一个**覆盖整个按住时长**的操作锁。
            // isWeaponButtonDown() 内部会检查 controlsLocked，锁着时返回 false，
            // 原版两条分支都不会进入；松开瞬间解锁，不影响玩家任何操作。
            bool holdSuppress = _ready && down;
            if (holdSuppress)
            {
                try { owner?.lockControlsS(0.3); } catch { }
            }
            else
            {
                // 松手：解除压制，并复位"本次按压已用掉"的标记，下次按下才能再放居合
                if (_suppressActive)
                {
                    try { owner?.unlockControls(); } catch { }
                }
                _consumed = false;
            }
            _suppressActive = holdSuppress;

            bool shouldDash = _ready && down && !_consumed;

            if (shouldDash)
            {
                // 新的一刀开始：刀号 +1、本次刻印序号清零
                ResetSwing();

                // 喂给引擎：满蓄力 + 居合标记。原版随后的蓄力判定直接通过，
                // 由它调用 onExecute() 走居合分支（瞬移前冲 + 路径群伤）。
                // 不要自己调 onExecute：实测 isCharging() 为假时它返回 false，
                // 会走普通斩击分支（日志里的 onExecute=False 就是这么来的）。
                try
                {
                    katanaChargeF = FullChargeF;
                    nextIsChargeAtk = true;
                    _consumed = true;

                    if (_pressLogCount < 8)
                    {
                        _pressLogCount++;
                        Write($"[ChronoBlade] 居合发动 #{_pressLogCount}: cycle={MaybeCycle()} " +
                              $"range={GetDashRange()} 刀号={_slashId}");
                    }
                }
                catch (Exception ex)
                {
                    Write($"[ChronoBlade] 喂蓄力失败: {ex}");
                }

                // ---- 连击段推进 + 第 2a / 3a 触发 ----
                //
                // 放在这里，而不是 `tool.Weapon.onExecute` 钩子里，原因：
                //   `shouldDash` 是本模组**自己**判定的"新一刀开始"，每次按下保证只进一次
                //   （由 _consumed 把关，松手才复位）—— 前面几轮"以为 onExecute 每刀都会进、
                //   结果 2a/3a 一直不触发"就是这么踩的。
                // 顺序放在喂蓄力之后：效果和这一刀同一帧生效。
                AdvanceCombo();
            }

            try
            {
                base.fixedUpdate();
            }
            catch (Exception ex)
            {
                Write($"[ChronoBlade] base.fixedUpdate 异常: {ex.Message}");
                throw;
            }
        }

        private int _pressLogCount;
        /// <summary>本次按压是否已经发动过居合（松手后才复位）。</summary>
        private bool _consumed;
        /// <summary>当前是否处于"按住压制"状态（松手时需要解锁）。</summary>
        private bool _suppressActive;

        // ---------------------------------------------------------------- 连击段计数
        //
        // "这是第几下"**必须自己数**：原版 `_cycle` 已经和玩家看到的连击脱钩了 ——
        // 本模组对每一次攻击都强制注入满蓄力 + nextIsChargeAtk，原版一律走居合冲刺分支，
        // 而且 `set_cycle(3)` 还被我们折回 0。
        private int _comboStep;
        private long _comboLastMs;
        private int _comboLogCount;

        /// <summary>多久没出刀就从第 1 段重新起手（毫秒）。</summary>
        private const long ComboWindowMs = 1500;

        /// <summary>
        /// 连击段推进：1 → 2 → 3 → 1；超过 <see cref="ComboWindowMs"/> 没出刀就从第 1 段重新起手。
        /// 推完立刻按段触发对应的第 2a / 3a 效果。
        /// </summary>
        private void AdvanceCombo()
        {
            try
            {
                long now = Environment.TickCount64;
                if (now - _comboLastMs > ComboWindowMs) _comboStep = 0;
                _comboLastMs = now;
                _comboStep = _comboStep % 3 + 1;

                if (_comboLogCount < 24 && _comboStep >= ComboShuriken)
                {
                    _comboLogCount++;
                    Write($"[ChronoBlade] 连击第 {_comboStep} 段 → " +
                          (_comboStep == ComboShuriken ? "一周飞镖" : "时钟剑雨") +
                          $"（原版 cycle={MaybeCycle()}）");
                }

                AddCycleEffect(_comboStep);
            }
            catch (Exception ex)
            {
                Write($"[ChronoBlade] 连击推进失败: {ex.Message}");
            }
        }

        private int MaybeCycle()
        {
            try { return get_cycle(); } catch { return -1; }
        }

        private static void Write(string msg)
        {
            System.Console.WriteLine(msg);
            try { _logger?.Information(msg); } catch { }
        }

        private static Serilog.ILogger? _logger;

        /// <summary>由主模块注入，让武器侧日志也进 logs\log_latest.log。</summary>
        public static void AttachLogger(Serilog.ILogger logger) => _logger = logger;

        /// <summary>
        /// 招式总入口。由 Hook_Katana.onExecute / tool.Weapon.onExecute 调用（若可用）。
        /// 走不通时由上面的 fixedUpdate 注入负责居合。
        /// </summary>
        /// <param name="callOriginal">调用原版 Katana.onExecute 的委托。</param>
        public bool RunAttack(Func<bool> callOriginal)
        {
            int cycle = CycleSlash;
            try { cycle = _cycle; } catch { }

            // ---- 第 1a：居合前冲斩 ----
            // 与原版 Katana 的"满蓄力居合"完全同一条路径：
            //   nextIsChargeAtk = true  → onExecute 走冲刺分支（瞬移 + 路径群伤）
            //   katanaChargeF   = 满    → 距离取 range × 1.5、伤害不衰减
            if (_ready && cycle == CycleDash)
            {
                try
                {
                    nextIsChargeAtk = true;
                    katanaChargeF = FullChargeF;
                    Write($"[ChronoBlade] 第 1a 居合：cycle={cycle} 满蓄力={FullChargeF} range={GetDashRange()}");
                }
                catch (Exception ex)
                {
                    Write($"[ChronoBlade] 置居合状态失败: {ex.Message}");
                }
            }

            bool result = callOriginal();

            // 注意：第 2a / 第 3a（一周飞镖 / 时钟剑雨）**不在这里触发**。
            // `RunAttack` 挂在 `Hook_Katana.onExecute` 上，而那个挂点实测整局都不触发，
            // 分派放这儿会变成死代码。它们由 `fixedUpdate` 的 shouldDash 分支
            // （每次按下保证进一次）→ AdvanceCombo() → AddCycleEffect() 触发。
            // 这里只负责第 1a 的居合前冲斩，连击保持干净的平砍手感。
            return result;
        }

        /// <summary>把日志同时写到控制台，方便在 logs\log_latest.log 里排查。</summary>
        private double GetDashRange()
        {
            try { return get_curSkillInf()?.props?.range ?? -1; }
            catch { return -1; }
        }

        /// <summary>
        /// 第 2a / 3a 的"召唤"部分：**表现层（特效）+ 实体层（真投射物）**，
        /// 不改原版连击 / 蓄力状态机。
        ///
        /// ⚠️ 参数是**自己数的连击段号**（<see cref="ComboShuriken"/> = 2 / <see cref="ComboSwordRain"/> = 3），
        ///    **不是原版 `_cycle`**！
        ///
        ///    原因：本模组在 `fixedUpdate` 里对**每一次**攻击都强制注入"满蓄力 + nextIsChargeAtk"，
        ///    让原版一律走居合冲刺分支 —— 于是原版 `_cycle` 已经和玩家看到的"第几下"脱钩了
        ///    （而且 `set_cycle(3)` 还被我们自己折回 0）。
        ///    之前拿 `_cycle` 去分派，实际永远匹配不到 1/2，效果自然"不见了"。
        ///
        ///    调用点在 `ChronoBladeMod.OnAnyWeaponExecute`（`tool.Weapon.onExecute` 钩子），
        ///    那里维护连击段计数器。**不要**改到 `RunAttack` 里 —— 那是挂在
        ///    `Hook_Katana.onExecute` 上的，而那个挂点实测整局都不触发，放那儿又是死代码。
        /// </summary>
        public void AddCycleEffect(int comboStep)
        {
            Hero? hero = owner;
            if (hero == null || hero.destroyed || hero._level == null) return;

            switch (comboStep)
            {
                case ComboShuriken:
                    ResetSwing();
                    FaceTarget(hero, ShurikenRadius + 6.0);
                    // 表现层：金色法阵 + 一圈飞镖贴图
                    ChronoFx.CastShurikenCircle(hero, ShurikenCount, ShurikenRadius);
                    // 实体层：12 枚真正会飞、会打伤害的旋转刃
                    int shurikens = SpawnShurikenEntities(hero, ShurikenCount, ShurikenPower);
                    Write($"[ChronoBlade] 第 2a 一周飞镖已触发：实体 {shurikens}/{ShurikenCount} 枚");
                    break;

                case ComboSwordRain:
                    ResetSwing();
                    CastSwordRain(hero);
                    break;

                default:
                    // 连击第 1 段（居合前冲）与其它：不额外做任何事。
                    // 第 1 段命中时由 Hook_Katana.hitFromWeapon 逐个刻罗马数字。
                    ResetSwing();
                    break;
            }
        }

        private void CastSwordRain(Hero hero)
        {
            var targets = ChronoMobFinder.Pick(hero, SwordRadius, SwordCount);
            if (targets.Count == 0)
            {
                targets.Add(((hero.cx + hero.xr) * 24.0 + hero.dir * 120.0,
                             (hero.cy + hero.yr) * 24.0));
            }

            // 表现层：背景时钟展开 + 砸下来的剑影
            ChronoFx.CastSwordRain(hero, SwordCount, SwordRadius, targets);

            // 实体层：每个目标点上方落一柄真正会砸、会打伤害的剑
            SpawnSwordRainEntities(hero, targets, SwordPower);
        }

        // ================================================================ 实体层
        //
        // ⚠️ 为什么不用原版时之守护者那两个实体：
        //   · `dc.en.bu.TimeKeeperShuriken` 的发光逻辑要 `be.onions`（boss 专属贴图池）、取色要 `be._infos`；
        //   · `dc.en.mob.boss.TimeKeeperSword` 的**构造函数里**就 `be.getOldSkillInfos("swordRain").props.duration`，
        //     update 里还要 `be.brutalityTier` / `be.cy` / `be.get_tmod()`。
        //   两者的构造参数类型都是 `TimeKeeper`，普通关卡里没有 boss 实例 → 传 Hero 必 NPE。
        //
        // 所以改用**玩家可拥有**的同类投射物（构造函数只收泛型 `Entity from`）：
        //   · 飞镖 → `dc.en.bu.Saw`（旋转刃，视觉最接近飞镖）
        //   · 落剑 → `dc.en.bu.Stalactite`（从天而降）

        /// <summary>
        /// 造一个"以英雄为施法者"的 AttackData。
        ///
        /// 走原版统一入口 `AttackUtils.Class.createFromHero()` —— 它会把 `useHeroScaling` 打开，
        /// 也就是**伤害自动跟英雄的属性 / 等级 / 变异走**，不需要自己算缩放，
        /// 也不会漏掉暴击、减抗这些链路。
        ///
        /// 注意：Haxe 的静态成员在 GameProxy 里挂在"类对象"上，要写 `AttackUtils.Class.xxx`。
        /// </summary>
        private static dc.tool.atk.AttackData? MakeHeroAttack(Hero hero, int power)
        {
            try
            {
                object baseDmg = power;             // 原签名是 dynamic
                int? tier = null;                   // null = 用英雄自己的 tier
                return dc.tool.atk.AttackUtils.Class.createFromHero(hero, baseDmg, tier);
            }
            catch (Exception ex)
            {
                Write($"第 2a/3a 的 AttackData 构造失败（这一下只有特效）: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 第 2a 的真飞镖：以英雄为中心，向一整圈甩出 `count` 枚 `Saw`。
        /// 用法照原版陷阱 `dc.en.ltrap.Shooter`：`new Saw(this, atk, ang, 0.5).init();`
        /// </summary>
        private static int SpawnShurikenEntities(Hero hero, int count, int power)
        {
            var atk = MakeHeroAttack(hero, power);
            if (atk == null) return 0;

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    double ang = i * (System.Math.PI * 2.0) / count;
                    var saw = new dc.en.bu.Saw(hero, atk, ang, 0.5);
                    saw.init();                       // 必须调用，否则不进场
                    spawned++;
                }
                catch (Exception ex)
                {
                    Write($"第 {i + 1} 枚飞镖实体生成失败: {ex.Message}");
                    break;
                }
            }
            return spawned;
        }

        /// <summary>
        /// 第 3a 的真落剑：在每个目标点上方生成一柄 `Stalactite`。
        /// 用法照原版 `dc.en.mob.boss.Giant`：
        /// `var st = new Stalactite(this, atk); st.init(); st.initOrigin(x, y);`
        /// </summary>
        private static int SpawnSwordRainEntities(Hero hero, IList<(double px, double py)> targets, int power)
        {
            var atk = MakeHeroAttack(hero, power);
            if (atk == null) return 0;

            int spawned = 0;
            foreach (var (px, py) in targets)
            {
                try
                {
                    var st = new dc.en.bu.Stalactite(hero, atk);
                    st.init();
                    st.initOrigin(px, py);
                    spawned++;
                }
                catch (Exception ex)
                {
                    Write("落剑实体生成失败: " + ex.Message);
                    break;
                }
            }
            return spawned;
        }

        private static void FaceTarget(Hero hero, double radiusTiles)
        {
            try
            {
                var mob = ChronoMobFinder.Nearest(hero, radiusTiles);
                if (mob != null)
                {
                    int dir = (mob.cx + mob.xr) >= (hero.cx + hero.xr) ? 1 : -1;
                    if (hero.dir != dir) hero.dir = dir;
                }
            }
            catch { }
        }

        // ================================================================ 罗马数字刻印
        /// <summary>
        /// 刻印入口（怪物仍在世时用这个，坐标从怪物身上现取）。
        /// </summary>
        public void Engrave(Mob mob)
        {
            if (mob == null) return;
            double px = 0, py = 0;
            try
            {
                px = (mob.cx + mob.xr) * 24.0;
                py = (mob.cy + mob.yr) * 24.0 - mob.hei * 0.5;
            }
            catch { }
            EngraveAt(mob, px, py);
        }

        /// <summary>
        /// 刻印（带位置快照）。
        ///
        /// ⚠️ 不要在这里判断 mob.life / destroyed：
        ///   斩杀的那一击之后怪物已经死了，判定活着就等于"斩杀时永远看不到数字"。
        ///   只要这一击是时之刃打的，就照样在快照坐标处刻印。
        /// 只做"同一刀内同一只怪不重复"的去重（用 __uid）。
        /// </summary>
        public void EngraveAt(Mob? mob, double pixelX, double pixelY)
        {
            if (!_ready) return;
            try
            {
                // 去重键：优先用怪物的 uid；怪物已经销毁时退回用坐标，保证仍能刻
                int key = 0;
                try { key = mob?.__uid ?? 0; } catch { }
                if (key == 0)
                {
                    key = unchecked((int)(pixelX * 31 + pixelY));
                }

                // 同一刀内已经刻过就跳过；下一刀会用新的 _slashId，于是能重新刻
                if (_engravedSlashOf.TryGetValue(key, out int engravedOn) && engravedOn == _slashId)
                {
                    return;
                }
                _engravedSlashOf[key] = _slashId;

                // 本次斩击内的序号（每刀从 1 开始，最多 12）
                _engraveIndex++;
                int n = _engraveIndex > MaxEngravePerSlash ? MaxEngravePerSlash : _engraveIndex;

                ChronoFx.ShowNumeralAt(owner, pixelX, pixelY, ChronoFx.Roman(n), mob);
            }
            catch (Exception ex)
            {
                Write($"[ChronoBlade] 刻印异常: {ex}");
            }
        }

        /// <summary>
        /// 开始新的一刀：刀号 +1（同一只怪物可在下一刀重新被刻），
        /// 同时把**本次斩击的刻印计数清零** —— 这样每一刀的数字都从 I 重新数起。
        /// </summary>
        public void ResetSwing()
        {
            _slashId++;
            _engraveIndex = 0;
        }

        // ================================================================ 存档序列化
        object IHxbitSerializable<object>.GetData()
        {
            return new Dictionary<string, object> { { "engrave", _engraveIndex } };
        }

        void IHxbitSerializable<object>.SetData(object data)
        {
            try
            {
                if (data is Dictionary<string, object> d && d.TryGetValue("engrave", out var v))
                {
                    _engraveIndex = Convert.ToInt32(v);
                }
            }
            catch { }
        }
    }
}
