#nullable disable
using System;
using System.Collections.Generic;

namespace GojoLimitless
{
    // =====================================================================================
    //  配置
    //
    //  落地文件：coremod/config/GojoLimitless.json（由 ModCore.Storage.Config<T> 管理）
    //
    //  · 菜单里改值 → 立刻写盘
    //  · 手改文件   → F10 菜单里按 Esc，或换个关（Hook_Hero.init）会 Reload 一次
    //  · 缺字段     → 反序列化后由 TryPatchDefaults 补默认值并写回
    //  · ConfigVersion 变了 → 同上，自动迁移
    //
    //  每一项都能在游戏里通过 F10 菜单调，不需要改文件重开游戏。
    // =====================================================================================

    /// <summary>所有招式共有的可调项（基类，DomainCfg 在它基础上加字段）。</summary>
    public class AbilityCfg
    {
        /// <summary>这一招开不开。</summary>
        public bool Enabled = true;

        /// <summary>
        /// 触发键（**键名**，不是原版动作名）。写法见 <see cref="GojoKeys"/>。
        ///
        /// 推荐用死亡细胞里默认空着的键：<c>J K L U I O H</c> 或 <c>F1</c>~<c>F12</c>。
        /// 留空 = 不绑键（该招只能靠连招或被动触发）。
        /// </summary>
        public string Key = "";

        /// <summary>冷却（秒）。</summary>
        public double Cooldown = 3.5;

        /// <summary>
        /// 单次伤害的**基数**。
        ///
        /// ⚠️ 默认会吃**升级卷轴**加成（见 <see cref="UseHeroScaling"/>），
        /// 所以这里的数字是"基础值"，实际伤害会随三个属性等级之和放大。
        /// </summary>
        public double Damage = 300.0;

        /// <summary>
        /// 伤害是否吃**升级卷轴**加成（默认 true）。
        ///
        /// 走原版 <c>AttackData.useHeroScaling = true</c> →
        /// <c>_Const.scaleHeroValueToTier(dmg, 英雄层级)</c>，和原版武器同一套公式
        /// （分叉点见 <c>_AttackUtils.cs:129</c>）。
        /// 关掉则退回固定伤害（<c>scaleMobValueToTier</c>，按怪物层级）。
        /// </summary>
        public bool UseHeroScaling = true;

        /// <summary>作用半径（格）。</summary>
        public double Radius = 10.0;

        /// <summary>位移力度（「苍」的吸力 / 「赤」的推力 / 领域的拉扯）。</summary>
        public double Power = 0.6;

        /// <summary>持续时间（秒）。瞬发招式无意义，领域展开用它。</summary>
        public double Duration = 0.0;
    }

    /// <summary>无下限（被动）。</summary>
    public class InfinityCfg
    {
        public bool Enabled = true;

        /// <summary>屏障半径（格）。</summary>
        public double Radius = 6.0;

        /// <summary>咒力储量上限（再叠加 <see cref="ReservePerLevel"/> × 三个属性等级之和）。</summary>
        public double Reserve = 420.0;
        public double ReservePerLevel = 12.0;

        /// <summary>储量每秒回充。</summary>
        public double RegenPerSecond = 140.0;

        /// <summary>单次吸收最多吃掉储量的百分比（防止"一发吸干"）。</summary>
        public double MaxDrainPerHit = 0.35;

        /// <summary>被打穿后的充能停顿（秒），期间回充降到 <see cref="BreakRegenFactor"/>。</summary>
        public double BreakPenalty = 0.60;
        public double BreakRegenFactor = 0.35;

        /// <summary>迟滞：屏障内敌人贴脸时的移速倍率（0.25 = 只剩 25%）。</summary>
        public double SlowMinMultiplier = 0.25;

        /// <summary>迟滞衰减尾巴（秒）：敌人离开屏障后多久恢复正常速度。</summary>
        public double SlowLinger = 0.30;

        /// <summary>是否湮灭飞进屏障的敌方子弹。</summary>
        public bool EraseBullets = true;

        /// <summary>是否显示青色呼吸光圈。</summary>
        public bool AuraFx = true;

        /// <summary>光圈呼吸幅度（0 = 不呼吸；1 = 半径与透明度 ±20%）。</summary>
        public double AuraBreath = 1.0;

        /// <summary>咒力低于这个比例时光圈转红（0~1）。</summary>
        public double AuraLowReserveRatio = 0.30;
    }

    /// <summary>领域展开（无量空处）。</summary>
    public class DomainCfg : AbilityCfg
    {
        /// <summary>领域内的移速倍率（比无下限本身更狠）。</summary>
        public double SlowMultiplier = 0.20;

        /// <summary>
        /// **内圈半径（格）** —— 只有进了这个圈才会吃到持续伤害。
        ///
        /// 领域是"内外两圈"结构（<c>Radius</c> = 外圈半径）：
        /// <code>
        ///         ┌──────────── 外圈：Radius（默认 22 格）────────────┐
        ///         │   只吸附 + 定身 + 减速，**不掉血**                 │
        ///         │        ┌────── 内圈：InnerRadius（默认 7 格）──┐   │
        ///         │        │   吸附 + 定身 + 减速 + **持续伤害**  │   │
        ///         │        └──────────────────────────────────────┘   │
        ///         └──────────────────────────────────────────────────┘
        /// </code>
        /// 也就是先用外圈把敌人**拽进来**，进了内圈才开始削血 ——
        /// 对应原作"无量空处"把人吸进领域中心再灌爆信息的设定。
        /// </summary>
        public double InnerRadius = 7.0;

        /// <summary>外圈把敌人往里拽时的**额外力度倍率**（比内圈更狠，不然拽不动）。</summary>
        public double OuterPullMultiplier = 1.8;

        /// <summary>伤害跳间隔（秒）。</summary>
        public double TickInterval = 0.5;

        /// <summary>是否给英雄短暂无敌（原版 <c>affect 5</c>，和 <c>Hero.toggleFullInvincibility</c> 同一个 id）。</summary>
        public bool Invincible = true;

        /// <summary>
        /// 是否启用领域的**氛围层**：全屏紫色 + 四周晕影 + 虚空环 + 旋转裂纹 + 扫光 + 开场闪光，
        /// 领域持续期间一直存在，结束立刻恢复环境。
        ///
        /// 觉得晃眼 / 掉帧可以关掉，玩法（定身、伤害、无敌）不受影响。
        /// </summary>
        public bool DomainFx = true;
    }

    /// <summary>HUD / 菜单 / 总开关。</summary>
    public class UiCfg
    {
        /// <summary>总开关：关掉后所有招式与被动都停（菜单仍然能开，否则没法开回来）。</summary>
        public bool ModEnabled = true;

        /// <summary>显示内置 HUD。</summary>
        public bool HudEnabled = true;

        // 默认位置避开原版左上角那一片（血量/细胞/技能栏 + 小地图），
        // 原版那些 UI 大概占到 y ≈ 260，所以默认放到它下面。
        public double HudX = 12.0;
        public double HudY = 276.0;

        /// <summary>HUD 单行基准高度（像素）。最终行距 = 它 × 文字缩放 × <see cref="HudRowSpacing"/>。</summary>
        public double HudLineHeight = 16.0;

        /// <summary>
        /// 行距的额外倍率（1.0 = 行距刚好等于"行高 × 缩放"）。
        /// 调大就是"把每行之间的距离拉开"。
        /// </summary>
        public double HudRowSpacing = 1.35;

        /// <summary>HUD 文字缩放（1.0 = 原尺寸）。</summary>
        public double HudScale = 1.0;

        /// <summary>在 HUD 下面显示最近几条日志（调试/自检用）。默认关 —— 会挡住 HUD 正文。</summary>
        public bool HudShowLog = false;

        /// <summary>HUD 上显示几行日志。</summary>
        public int HudLogLines = 4;

        /// <summary>
        /// 打开/关闭本模组覆盖层菜单的键。默认 <c>F10</c>（原版没用到这个键）。
        /// </summary>
        public string MenuKey = "F10";

        /// <summary>是否写详细日志。</summary>
        public bool VerboseLog = true;

        /// <summary>
        /// 强制开启（忽略一切门槛）的键。默认 <c>O</c>。
        /// </summary>
        public string ForceEnableKey = "O";

        /// <summary>强制开启时长（秒）。</summary>
        public double ForceEnableSeconds = 180.0;
    }

    /// <summary>
    /// 出厂默认值。集中放这里，**迁移(v7→v8)和"恢复默认值"都用同一份**，
    /// 免得两处写两遍又写不一致。
    ///
    /// 数值取向（"最合适的配置"）：
    ///   · 冷却：苍/赤 短（3s，连招手感），茈 中（14s，大招），领域 长（40s，终极技）
    ///   · 伤害：苍低(150)/赤中(220)/茈高(1800 贯穿)/领域每跳 400
    ///   · 咒力：储量 420、每级 +12、回充 140/s —— 被打穿需要 ~1.5s 才回满，
    ///     所以"扛小怪、被 Boss 大伤害打穿"这个设计才成立
    ///     （回充给到 100000 那种数值会让屏障永远打不穿、直接失去意义）
    /// </summary>
    public static class GojoDefaults
    {
        public const string KeyBlue = "J";
        public const string KeyRed = "I";
        public const string KeyPurple = "K";
        public const string KeyDomain = "U";
        public const string KeyForce = "O";
        public const string KeyMenu = "F10";

        public const double BlueCooldown = 3.0, BlueDamage = 150.0, BlueRadius = 12.0, BluePower = 1.10;
        public const double RedCooldown = 3.0, RedDamage = 220.0, RedRadius = 10.0, RedPower = 2.20;
        public const double PurpleCooldown = 14.0, PurpleDamage = 1800.0, PurpleRadius = 26.0, PurplePower = 1.10;
        public const double DomainCooldown = 40.0, DomainDamage = 400.0, DomainRadius = 22.0, DomainPower = 0.30, DomainDuration = 5.0;

        public const double Reserve = 420.0, ReservePerLevel = 12.0, RegenPerSecond = 140.0;
        public const double MaxDrainPerHit = 0.35, BreakPenalty = 0.60, BreakRegenFactor = 0.35;
        public const double ComboWindow = 2.5, BossPullPushFactor = 0.40;
    }

    /// <summary>整个模组的配置根。</summary>
    public class Configs
    {
        /// <summary>
        /// 配置版本。**加/删/改字段时把它 +1** ——
        /// 加载时会检测到版本变化，把缺失字段补默认值再写回文件。
        ///
        /// 历史：
        ///   1 —— 初版（扁平的 RequireLimitlessItem / BlueDamage / … 字段）
        ///   2 —— 嵌套成 Infinity / Blue / Red / Purple / Domain / Ui；
        ///        苍赤重新定位（苍变控制、赤变击飞）、加了 BossPullPushFactor
        ///   3 —— 默认不再需要道具；HUD 默认下移避开原版 HUD
        ///   4 —— **彻底删除自建道具「Limitless」**（连带 res.pak / cdb 数据补丁 /
        ///        RequireLimitlessItem / AutoGrantItem 一起删掉）：招式纯靠按键触发
        ///   5 —— **按键改用原版按键绑定**：Key 字段从"键名"改成"原版动作名"，
        ///        模组不再自己捕获键位，直接跟随原版「选项 → 控制」的改键
        ///   6 —— HUD 去掉父容器（改回逐行绝对定位）、行距支持倍率
        ///        （<c>HudRowSpacing</c>，默认 1.35 = 把每行距离拉开一点）
        ///   7 —— **按键回到"硬绑"**：Key 又变回键名（J/K/L/U/I/O/H…）。
        ///        v5 的"读原版动作绑定"实机下按键检测不动，放弃；迁移时会读原版
        ///        dc_options.json 把当时的绑定换算回来，尽量保住玩家已经习惯的键。
        ///   8 —— 玩法数值校准到推荐基线（咒力 420/回充 140、苍 3s/赤 3s/茈 14s、
        ///        伤害 150/220/1800、领域每跳 400）；键位与 HUD 设置不动。
        /// </summary>
        public int ConfigVersion = 8;

        public InfinityCfg Infinity = new InfinityCfg();

        // 默认键全部挑死亡细胞里原本空着的：J / K / L / U / I / O / H
        //
        // 苍：低伤害、强吸附（Power 大、Radius 大、Cooldown 短）
        public AbilityCfg Blue = new AbilityCfg
        {
            Key = GojoDefaults.KeyBlue,
            Cooldown = GojoDefaults.BlueCooldown, Damage = GojoDefaults.BlueDamage,
            Radius = GojoDefaults.BlueRadius, Power = GojoDefaults.BluePower,
        };

        // 赤：伤害略高、强击飞
        public AbilityCfg Red = new AbilityCfg
        {
            Key = GojoDefaults.KeyRed,
            Cooldown = GojoDefaults.RedCooldown, Damage = GojoDefaults.RedDamage,
            Radius = GojoDefaults.RedRadius, Power = GojoDefaults.RedPower,
        };

        // 茈：大招，长冷却高伤害，击退中等
        public AbilityCfg Purple = new AbilityCfg
        {
            Key = GojoDefaults.KeyPurple,
            Cooldown = GojoDefaults.PurpleCooldown, Damage = GojoDefaults.PurpleDamage,
            Radius = GojoDefaults.PurpleRadius, Power = GojoDefaults.PurplePower,
        };

        // 领域展开：定身 + 持续伤害
        public DomainCfg Domain = new DomainCfg
        {
            Key = GojoDefaults.KeyDomain,
            Cooldown = GojoDefaults.DomainCooldown, Damage = GojoDefaults.DomainDamage,
            Radius = GojoDefaults.DomainRadius, Power = GojoDefaults.DomainPower,
            Duration = GojoDefaults.DomainDuration,
        };

        /// <summary>
        /// Boss / 精英 受到的牵引、击退 额外乘这个系数。
        /// 原版 <c>getBumpResistanceFactor()</c> 之外再压一道，避免把 Boss 当小怪推。
        /// </summary>
        public double BossPullPushFactor = 0.40;

        /// <summary>连招窗口：苍之后多少秒内按赤会接出茈。</summary>
        public double ComboWindow = 2.5;

        public UiCfg Ui = new UiCfg();
    }

    /// <summary>配置门面：全局唯一一份，玩法代码统一从这里读。</summary>
    public static class Cfg
    {
        private static ModCore.Storage.Config<Configs> _store;

        private static readonly Configs Fallback = new Configs();

        /// <summary>
        /// ModCore 管理的持久化配置。
        ///
        /// ⚠️ <c>Config&lt;T&gt;</c> **没有 Reload()** —— 它的 <c>Value</c> 是首次访问时读盘。
        /// 所以"重读"= 丢掉旧实例、下次访问 <c>Value</c> 时重新构造一个。
        /// </summary>
        public static ModCore.Storage.Config<Configs> Store
        {
            get
            {
                if (_store == null) _store = new ModCore.Storage.Config<Configs>("GojoLimitless");
                return _store;
            }
        }

        /// <summary>当前生效的配置对象（永远非 null）。</summary>
        public static Configs V
        {
            get
            {
                try
                {
                    Configs v = Store.Value;
                    if (v != null) return v;
                }
                catch { }
                return Fallback;
            }
        }

        /// <summary>本次加载是否补过默认值 / 迁移过版本（用于日志提示）。</summary>
        public static bool PatchedOnLoad { get; private set; }

        /// <summary>上一次加载看到的版本号（用于告诉玩家"从 vX 升上来的"）。</summary>
        public static int LoadedVersion { get; private set; }

        /// <summary>
        /// 从磁盘重读。
        ///
        /// ⚠️ **不能 new 一个新的 <c>Config&lt;T&gt;</c>**：它的构造函数会
        /// <c>EventSystem.AddReceiver(this)</c>，而没有任何地方会 RemoveReceiver ——
        /// 每 new 一次就泄漏一个接收者，而每个泄漏的实例都会在游戏每次自动存档时
        /// 被 <c>IOnSaveConfig.OnSaveConfig()</c> 广播到，各自把同一个 json 重写一遍。
        ///
        /// 正确做法是**原地重读**：把 <c>Value</c> 置 null，下次访问时它自己会重新 Load()
        /// （<c>get_Value()</c> 只在 backing field 为 null 时才读盘）。
        /// 同一个实例、不新增接收者、照样重读文件。
        /// </summary>
        public static void Reload()
        {
            PatchedOnLoad = false;
            try
            {
                ModCore.Storage.Config<Configs> store = _store
                    ?? (_store = new ModCore.Storage.Config<Configs>("GojoLimitless"));

                store.Value = null;      // 触发下一次 get_Value() 重新读盘
                Configs v = store.Value; // 立刻读，好让下面的迁移跑在最新数据上
                LoadedVersion = v != null ? v.ConfigVersion : 0;
            }
            catch (Exception ex)
            {
                Log.Warn("配置重读失败，沿用内存里的值: " + ex.Message);
                return;
            }

            TryPatchDefaults();
        }

        /// <summary>写回磁盘。</summary>
        public static void Save()
        {
            try { Store.Save(); }
            catch (Exception ex) { Log.Warn("配置保存失败: " + ex.Message); }
        }

        /// <summary>恢复出厂设置。</summary>
        public static void ResetToDefault()
        {
            try
            {
                Configs src = new Configs();
                Configs dst = V;
                dst.ConfigVersion = src.ConfigVersion;
                dst.BossPullPushFactor = src.BossPullPushFactor;
                dst.ComboWindow = src.ComboWindow;
                dst.Infinity = src.Infinity;
                dst.Blue = src.Blue;
                dst.Red = src.Red;
                dst.Purple = src.Purple;
                dst.Domain = src.Domain;
                dst.Ui = src.Ui;
                Save();
                Log.Info("配置已恢复默认值");
            }
            catch (Exception ex)
            {
                Log.Exception(ex, "重置配置失败");
            }
        }

        /// <summary>
        /// 补齐缺失字段 / 迁移版本 / 夹住越界值。
        ///
        /// ModCore 是"用文件内容覆盖内存对象"（Newtonsoft，<c>MissingMemberHandling.Ignore</c>），
        /// 所以**新增字段在旧文件里不存在时会保留 <see cref="Configs"/> 里的字段初始化值**
        /// —— 这就是"缺字段补默认"的实现基础。
        /// 这里额外处理：子对象为 null、版本号不一致、手改文件写出的越界值。
        /// </summary>
        private static void TryPatchDefaults()
        {
            try
            {
                Configs v = Store.Value;
                if (v == null) return;

                Configs def = new Configs();
                bool patched = false;

                if (v.Infinity == null) { v.Infinity = def.Infinity; patched = true; }
                if (v.Ui == null) { v.Ui = def.Ui; patched = true; }
                if (v.Blue == null) { v.Blue = def.Blue; patched = true; }
                if (v.Red == null) { v.Red = def.Red; patched = true; }
                if (v.Purple == null) { v.Purple = def.Purple; patched = true; }
                if (v.Domain == null) { v.Domain = def.Domain; patched = true; }

                int from = v.ConfigVersion;
                if (from != def.ConfigVersion)
                {
                    v.ConfigVersion = def.ConfigVersion;
                    patched = true;

                    if (from < 2)
                    {
                        Log.Info("配置版本 " + from + " → " + def.ConfigVersion
                                 + "：v1 的扁平字段已被嵌套结构取代，请重新在设置页里调");
                    }
                    else
                    {
                        Log.Info("配置版本 " + from + " → " + def.ConfigVersion
                                 + "：新字段已补默认值"
                                 + (from < 4 ? "（自建道具「Limitless」已删除，现在纯按键触发）" : "")
                                 + (from < 5 ? "（按键已改为跟随原版「选项 → 控制」的改键）" : ""));
                    }
                }

                // v5/v6 → v7：Key 从"原版动作名"搬回"键名"。
                // 不清一次的话旧值（cam_left 之类）解析不成键，等于全部失效。
                if (from < 7) MigrateKeys(v, ref patched);

                // v7 → v8：把玩法数值拉回设计基线（见 GojoDefaults 的说明）。
                // 只动"影响手感/平衡"的数值，**不碰键位和 HUD 设置**，那些保留玩家自己的。
                if (from < 8)
                {
                    ResetGameplayToDefaults(v);
                    patched = true;
                    Log.Info("配置数值已校准到推荐基线（咒力 420/回充 140、苍3s/赤3s/茈14s、"
                             + "伤害 150/220/1800、领域每跳 400）；键位与 HUD 设置保持不变");
                }

                if (Clamp(v)) patched = true;

                PatchedOnLoad = patched;
                if (patched) Save();
            }
            catch (Exception ex)
            {
                Log.Warn("配置补默认值失败（沿用当前值）: " + ex.Message);
            }
        }

        /// <summary>
        /// 把玩法数值拉回设计基线。**不动键位、不动 HUD / 菜单设置**。
        /// </summary>
        private static void ResetGameplayToDefaults(Configs v)
        {
            if (v.Infinity != null)
            {
                v.Infinity.Reserve = GojoDefaults.Reserve;
                v.Infinity.ReservePerLevel = GojoDefaults.ReservePerLevel;
                v.Infinity.RegenPerSecond = GojoDefaults.RegenPerSecond;
                v.Infinity.MaxDrainPerHit = GojoDefaults.MaxDrainPerHit;
                v.Infinity.BreakPenalty = GojoDefaults.BreakPenalty;
                v.Infinity.BreakRegenFactor = GojoDefaults.BreakRegenFactor;
            }

            if (v.Blue != null)
            {
                v.Blue.Cooldown = GojoDefaults.BlueCooldown;
                v.Blue.Damage = GojoDefaults.BlueDamage;
                v.Blue.Radius = GojoDefaults.BlueRadius;
                v.Blue.Power = GojoDefaults.BluePower;
            }
            if (v.Red != null)
            {
                v.Red.Cooldown = GojoDefaults.RedCooldown;
                v.Red.Damage = GojoDefaults.RedDamage;
                v.Red.Radius = GojoDefaults.RedRadius;
                v.Red.Power = GojoDefaults.RedPower;
            }
            if (v.Purple != null)
            {
                v.Purple.Cooldown = GojoDefaults.PurpleCooldown;
                v.Purple.Damage = GojoDefaults.PurpleDamage;
                v.Purple.Radius = GojoDefaults.PurpleRadius;
                v.Purple.Power = GojoDefaults.PurplePower;
            }
            if (v.Domain != null)
            {
                v.Domain.Cooldown = GojoDefaults.DomainCooldown;
                v.Domain.Damage = GojoDefaults.DomainDamage;
                v.Domain.Radius = GojoDefaults.DomainRadius;
                v.Domain.Power = GojoDefaults.DomainPower;
                v.Domain.Duration = GojoDefaults.DomainDuration;
            }

            v.ComboWindow = GojoDefaults.ComboWindow;
            v.BossPullPushFactor = GojoDefaults.BossPullPushFactor;
        }

        /// <summary>
        /// v5/v6 的"原版动作名" → v7 的"键名"迁移。
        ///
        /// v5 存的是 <c>"cam_left"</c> 这种动作名。要还原成"当时那个动作实际绑的键"，
        /// 最准的办法是**直接读游戏自己写的 <c>save/dc_options.json</c>** ——
        /// 那里面有 <c>keyboard_normal.&lt;动作&gt;</c> 的原始键码数组。
        ///
        /// 读不到就退回下面这张写死的默认对应表（等于 v5 的默认绑定）。
        /// </summary>
        private static readonly Dictionary<string, string> LegacyActionMap =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["cam_left"] = "J",
                ["cam_right"] = "L",
                ["cam_down"] = "K",
                ["cam_up"] = "I",
                ["map_focus"] = "H",
                ["heal"] = "U",
                ["activate"] = "O",
                ["map"] = "F10",
                ["menu"] = "F11",
                ["map_zoom_in"] = "P",
                ["map_zoom_out"] = "O",
            };

        /// <summary>动作名 → dc_options.json 里的字段名（小写）。</summary>
        private static readonly Dictionary<string, string> ActionToJsonField =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["jump"] = "jump", ["dodge"] = "dodge",
                ["main_weapon"] = "main_weapon", ["side_weapon"] = "side_weapon",
                ["right_skill"] = "right_skill", ["left_skill"] = "left_skill",
                ["activate"] = "activate", ["heal"] = "heal",
                ["menu"] = "menu", ["map"] = "map",
                ["up"] = "up", ["left"] = "left", ["down"] = "down", ["right"] = "right",
                ["validate"] = "validate", ["cancel"] = "cancel",
                ["map_zoom_in"] = "map_zoom_in", ["map_zoom_out"] = "map_zoom_out",
                ["map_focus"] = "map_focus",
                ["cam_up"] = "cam_up", ["cam_left"] = "cam_left",
                ["cam_down"] = "cam_down", ["cam_right"] = "cam_right",
            };

        /// <summary>从 dc_options.json 读出来的 动作名 → 键码 快照（只读一次）。</summary>
        private static Dictionary<string, int> _legacyKeyboard;

        /// <summary>把 Configs 里所有 Key 字段从"动作名"搬回"键名"。</summary>
        private static void MigrateKeys(Configs v, ref bool patched)
        {
            Configs def = new Configs();
            EnsureLegacyKeyboardLoaded();

            v.Blue.Key = MigrateOne(v.Blue.Key, def.Blue.Key, "Blue", ref patched);
            v.Red.Key = MigrateOne(v.Red.Key, def.Red.Key, "Red", ref patched);
            v.Purple.Key = MigrateOne(v.Purple.Key, def.Purple.Key, "Purple", ref patched);
            v.Domain.Key = MigrateOne(v.Domain.Key, def.Domain.Key, "Domain", ref patched);
            v.Ui.ForceEnableKey = MigrateOne(v.Ui.ForceEnableKey, def.Ui.ForceEnableKey, "Ui.ForceEnableKey", ref patched);
            v.Ui.MenuKey = MigrateOne(v.Ui.MenuKey, def.Ui.MenuKey, "Ui.MenuKey", ref patched);
        }

        /// <summary>
        /// 迁一个字段。
        ///   · 空 → 尊重"故意不绑"
        ///   · 已经是合法键名 → 不动
        ///   · 是动作名 → 先查 dc_options.json 当时绑的键，查不到用默认对应表
        /// </summary>
        private static string MigrateOne(string old, string fallback, string field, ref bool patched)
        {
            if (string.IsNullOrWhiteSpace(old))
            {
                if (old == null) patched = true;
                return "";
            }

            string trimmed = old.Trim();

            // 已经是键名？（注意：单个字母既能当键名、也可能是旧动作名的一部分，
            // 所以先看它是不是能解析成"键" —— 单字母/F 键/命名键都算）
            if (LooksLikeKeyName(trimmed)) return trimmed;

            // 是 v5 的动作名 → 换算成当时那个动作实际绑的键
            string key = null;
            if (_legacyKeyboard != null
                && ActionToJsonField.TryGetValue(trimmed, out string jsonField)
                && _legacyKeyboard.TryGetValue(jsonField, out int vk))
            {
                key = GojoKeys.Name(vk);
            }

            if (key == null && LegacyActionMap.TryGetValue(trimmed, out string mapped)) key = mapped;

            if (key != null)
            {
                patched = true;
                Log.Info($"配置 {field}: 旧的动作名 \"{trimmed}\" 已换算成按键 \"{key}\"");
                return key;
            }

            Log.Warn($"配置 {field} 的值 \"{old}\" 既不是键名也不是已知动作名，已改为默认 {fallback}");
            patched = true;
            return fallback;
        }

        /// <summary>判断一个字符串是不是"键名"（单字母 / 数字 / F1-F12 / 命名键 / 0xNN）。</summary>
        private static bool LooksLikeKeyName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            if (s.Length == 1 && char.IsLetterOrDigit(s[0])) return true;

            // GojoKeys.Parse 认不出来时会返回 0，用它当"不是键名"的判据
            return GojoKeys.Parse(s, 0) != 0;
        }

        /// <summary>
        /// 读一次游戏自己的 <c>save/dc_options.json</c>，取出 keyboard_normal 的键码。
        /// 失败就保持 null（迁移会退回默认对应表）。
        /// </summary>
        private static void EnsureLegacyKeyboardLoaded()
        {
            if (_legacyKeyboard != null) return;

            try
            {
                string path = System.IO.Path.Combine(
                    Environment.CurrentDirectory, "save", "dc_options.json");

                if (!System.IO.File.Exists(path))
                {
                    Log.Warn("找不到 save/dc_options.json，旧动作名将按内置对应表换算: " + path);
                    _legacyKeyboard = new Dictionary<string, int>();
                    return;
                }

                string json = System.IO.File.ReadAllText(path);
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);
                var kb = root["keyboard_normal"] as Newtonsoft.Json.Linq.JObject;

                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                if (kb != null)
                {
                    foreach (var prop in kb.Properties())
                    {
                        int first = -1;
                        if (prop.Value is Newtonsoft.Json.Linq.JArray arr)
                        {
                            for (int i = 0; i < arr.Count; i++)
                            {
                                var tok = arr[i];
                                if (tok == null || tok.Type == Newtonsoft.Json.Linq.JTokenType.Null) continue;
                                try
                                {
                                    int v = tok.ToObject<int>();
                                    if (v >= 0 && v < 256) { first = v; break; }
                                }
                                catch { }
                            }
                        }
                        if (first >= 0) map[prop.Name] = first;
                    }
                }

                _legacyKeyboard = map;
                Log.Info($"已读取原版按键表（save/dc_options.json，{map.Count} 个动作）用于迁移旧动作名");
            }
            catch (Exception ex)
            {
                Log.Warn("读取 save/dc_options.json 失败，旧动作名将按内置对应表换算: " + ex.Message);
                _legacyKeyboard = new Dictionary<string, int>();
            }
        }

        /// <summary>单次夹取的报告器（避免每个字段都写一遍 changed 赋值）。</summary>
        private sealed class ClampReport
        {
            public bool Changed;

            /// <summary>夹一个数；被夹过（或原本是 NaN/Inf）就置 Changed 并打日志。</summary>
            public void Field(string name, double value, Action<double> store,
                              double lo, double hi, double fallback)
            {
                double fixedVal = ClampD(value, lo, hi, fallback);
                if (fixedVal.Equals(value)) return;   // 位相等 = 没动
                store(fixedVal);
                Changed = true;
                Log.Warn($"配置 {name}={value} 越界，已夹到 {fixedVal}（合法区间 {lo}~{hi}）");
            }

            /// <summary>
            /// 夹一个"按键名"。空串 = 不绑（合法），认不出来的落回默认并打日志。
            /// </summary>
            public void Action(string name, string value, Action<string> store, string fallback)
            {
                if (value == null) { store(fallback); Changed = true; return; }
                if (value.Length == 0) return;                       // 故意不绑
                if (GojoKeys.Parse(value, 0) != 0) return;            // 能解析成键就不动

                store(fallback);
                Changed = true;
                Log.Warn($"配置 {name}=\"{value}\" 不是有效的按键名，已改为默认 \"{fallback}\"" 
                         + "（可用值见 GojoKeys.HelpText）");
            }
        }

        /// <summary>
        /// 把明显不合理的值夹回可用区间。返回 true = 确实改过任何一项。
        ///
        /// ⚠️ 别只快照两个字段就返回 —— 原来只比 BossPullPushFactor / ComboWindow，
        /// 于是 Infinity.Radius 之类被夹过之后虽然内存里修好了，却不会写回文件。
        /// </summary>
        public static bool Clamp(Configs v)
        {
            if (v == null) return false;
            var r = new ClampReport();

            r.Field("BossPullPushFactor", v.BossPullPushFactor, x => v.BossPullPushFactor = x, 0.0, 1.0, 0.40);
            r.Field("ComboWindow", v.ComboWindow, x => v.ComboWindow = x, 0.0, 10.0, 2.5);

            if (v.Infinity != null)
            {
                InfinityCfg i = v.Infinity;
                r.Field("Infinity.Radius", i.Radius, x => i.Radius = x, 0.5, 40.0, 6.0);
                r.Field("Infinity.Reserve", i.Reserve, x => i.Reserve = x, 1.0, 1000000.0, 420.0);
                r.Field("Infinity.ReservePerLevel", i.ReservePerLevel, x => i.ReservePerLevel = x, 0.0, 100000.0, 12.0);
                r.Field("Infinity.RegenPerSecond", i.RegenPerSecond, x => i.RegenPerSecond = x, 0.0, 1000000.0, 140.0);
                r.Field("Infinity.MaxDrainPerHit", i.MaxDrainPerHit, x => i.MaxDrainPerHit = x, 0.05, 1.0, 0.35);
                r.Field("Infinity.BreakPenalty", i.BreakPenalty, x => i.BreakPenalty = x, 0.0, 10.0, 0.60);
                r.Field("Infinity.BreakRegenFactor", i.BreakRegenFactor, x => i.BreakRegenFactor = x, 0.0, 1.0, 0.35);
                r.Field("Infinity.SlowMinMultiplier", i.SlowMinMultiplier, x => i.SlowMinMultiplier = x, 0.02, 1.0, 0.25);
                r.Field("Infinity.SlowLinger", i.SlowLinger, x => i.SlowLinger = x, 0.0, 5.0, 0.30);
                r.Field("Infinity.AuraBreath", i.AuraBreath, x => i.AuraBreath = x, 0.0, 3.0, 1.0);
                r.Field("Infinity.AuraLowReserveRatio", i.AuraLowReserveRatio, x => i.AuraLowReserveRatio = x, 0.0, 1.0, 0.30);
            }

            ClampAbility(v.Blue, r, "Blue", 0.05, 300.0, 1.0, 60.0, 0.0, 100000.0);
            ClampAbility(v.Red, r, "Red", 0.05, 300.0, 1.0, 60.0, 0.0, 100000.0);
            ClampAbility(v.Purple, r, "Purple", 0.05, 300.0, 2.0, 120.0, 0.0, 1000000.0);
            ClampAbility(v.Domain, r, "Domain", 0.10, 300.0, 2.0, 80.0, 0.5, 100000.0);

            if (v.Domain != null)
            {
                r.Field("Domain.SlowMultiplier", v.Domain.SlowMultiplier, x => v.Domain.SlowMultiplier = x, 0.02, 1.0, 0.20);
                r.Field("Domain.TickInterval", v.Domain.TickInterval, x => v.Domain.TickInterval = x, 0.05, 5.0, 0.5);
            }

            if (v.Ui != null)
            {
                r.Field("Ui.HudLineHeight", v.Ui.HudLineHeight, x => v.Ui.HudLineHeight = x, 8.0, 40.0, 16.0);
                r.Field("Ui.HudRowSpacing", v.Ui.HudRowSpacing, x => v.Ui.HudRowSpacing = x, 0.5, 3.0, 1.35);
                r.Field("Ui.HudX", v.Ui.HudX, x => v.Ui.HudX = x, -2000.0, 4000.0, 12.0);
                r.Field("Ui.HudY", v.Ui.HudY, x => v.Ui.HudY = x, -2000.0, 4000.0, 276.0);
                r.Field("Ui.HudScale", v.Ui.HudScale, x => v.Ui.HudScale = x, 0.4, 3.0, 1.0);
                r.Field("Ui.ForceEnableSeconds", v.Ui.ForceEnableSeconds, x => v.Ui.ForceEnableSeconds = x, 1.0, 3600.0, 180.0);

                if (v.Ui.HudLogLines < 0) { v.Ui.HudLogLines = 0; r.Changed = true; }
                if (v.Ui.HudLogLines > 12) { v.Ui.HudLogLines = 12; r.Changed = true; }

                Configs defUi = new Configs();
                r.Action("Ui.MenuKey", v.Ui.MenuKey, x => v.Ui.MenuKey = x, defUi.Ui.MenuKey);
                r.Action("Ui.ForceEnableKey", v.Ui.ForceEnableKey, x => v.Ui.ForceEnableKey = x, defUi.Ui.ForceEnableKey);
            }

            return r.Changed;
        }

        private static void ClampAbility(AbilityCfg a, ClampReport r, string name,
                                         double cdMin, double cdMax,
                                         double radMin, double radMax,
                                         double dmgMin, double dmgMax)
        {
            if (a == null) return;
            r.Field(name + ".Cooldown", a.Cooldown, x => a.Cooldown = x, cdMin, cdMax, 3.0);
            r.Field(name + ".Damage", a.Damage, x => a.Damage = x, dmgMin, dmgMax, 200.0);
            r.Field(name + ".Radius", a.Radius, x => a.Radius = x, radMin, radMax, 10.0);
            r.Field(name + ".Power", a.Power, x => a.Power = x, 0.0, 8.0, 0.6);
            r.Field(name + ".Duration", a.Duration, x => a.Duration = x, 0.0, 60.0, 0.0);

            // Key 存的是原版动作名；null 补空串，认不出来的落回该招默认动作
            r.Action(name + ".Key", a.Key, x => a.Key = x, DefaultKeyFor(name));
        }

        /// <summary>按招式名取出厂默认键（<see cref="ClampAbility"/> 夹取 Key 时用）。</summary>
        private static string DefaultKeyFor(string name)
        {
            if (name == "Blue") return "J";
            if (name == "Red") return "K";
            if (name == "Purple") return "L";
            if (name == "Domain") return "U";
            return "";
        }

        private static double ClampD(double v, double lo, double hi, double fallback)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return fallback;
            if (v < lo) return lo;
            if (v > hi) return hi;
            return v;
        }
    }
}
