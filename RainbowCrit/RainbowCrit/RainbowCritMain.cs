using dc;
using dc.en;
using dc.shader;
using dc.tool.atk;
using dc.ui.popd;
using Hashlink.Virtuals;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Menu;
using ModCore.Mods;
using ModCore.Utilities;
using System.Reflection;

namespace RainbowCrit;

/// <summary>
/// 全局彩色暴击（Rainbow Crit）
/// =================================================================
/// 原版特性（GamePseudocode/dc/Entity.cs: popDamage）：
///   只有穿着 HotlineMiamiChicken / HotlineMiamiHorse / HotlineMiamiOwl
///   三套服装，并且攻击命中时带暴击标记（AttackData.hasTag(2)）时，
///   伤害数字才会走 dc.ui.popd.PopDamageHotline 这条分支 ——
///   用的是 hotline 位图字体（fonts/exported/hotline_20|30|45.fnt）
///   再加上 dc.shader.HotlineText 这个按 time/pos 循环取色的彩虹着色器，
///   于是暴击数字会变成流动的彩色。
///
/// 本模组把这条分支"解锁"成全局的：
///   Hook_Entity.popDamage 里判断（暴击标记 且 伤害来源是玩家），
///   满足就复制原版的 PopDamageHotline.create(...) 调用，
///   不满足则原样交给原版逻辑，任何服装、任何武器都能触发彩色暴击。
///
/// 同时照抄原版那两条 0.3s / 0.4s 的小冷却与 dmgIdx 自增，
/// 避免高频暴击时彩虹字叠爆（原版 PopDamage 上限 64 个）。
/// </summary>
public class RainbowCritMain : ModBase, IOnGameExit, IModMenu
{
    // ================================================================
    // ★ 原版常量（照抄 dc.Entity.popDamage 的反编译结果）
    // ================================================================

    /// <summary>暴击标记。原版 pc/src/dc/Entity.hx 里 Crit = 1 &lt;&lt; 1。</summary>
    private const int CritTag = 2;

    /// <summary>原版用于限流的两个 Cooldown 槽位 key。</summary>
    private const int CooldownKey1 = 356515840;
    private const int CooldownKey2 = 272629760;

    /// <summary>原版两个槽位对应的秒数（0.3s / 0.4s）。</summary>
    private const double CooldownSec1 = 0.3;
    private const double CooldownSec2 = 0.4;

    /// <summary>原版 dmgIdx 的循环上限（0..4）。</summary>
    private const int MaxDmgIdx = 4;

    /// <summary>原版 PopDamage 同屏上限：达到后 PopDamageHotline.create 会返回 null。</summary>
    private const int MaxSimultaneousPopDamage = 64;

    /// <summary>原版字体配置：charset = "numbers"，font = "hotline"。</summary>
    private const string CharsetNumbers = "numbers";
    private const string FontHotline = "hotline";

    // ================================================================
    // 运行时状态
    // ================================================================

    private bool _disposed;
    private bool _hooked;

    /// <summary>「伤害来源属于玩家」的判定：先看是不是主角，再看宠物 / 召唤物身上的 owner 字段。</summary>
    private static readonly string[] OwnerFieldNames = { "owner", "master", "parentHero", "src" };
    private static readonly Dictionary<System.Type, FieldInfo?> OwnerFieldCache = new();

    public RainbowCritMain(ModInfo info) : base(info) { }

    // ================================================================
    // 生命周期
    // ================================================================

    public override void Initialize()
    {
        base.Initialize();
        _disposed = false;

        try
        {
            Hook_Entity.popDamage += OnEntityPopDamage;
            _hooked = true;
        }
        catch (Exception ex)
        {
            Log($"[RainbowCrit] Hook_Entity.popDamage 挂载失败: {ex.Message}");
        }

        Log("[RainbowCrit] 已加载：所有服装都能打出彩色暴击数字（玩家暴击）");
    }

    void IOnGameExit.OnGameExit()
    {
        _disposed = true;
        if (_hooked)
        {
            try { Hook_Entity.popDamage -= OnEntityPopDamage; } catch { }
            _hooked = false;
        }
        Log("[RainbowCrit] 游戏退出，已卸载");
    }

    // ================================================================
    // 核心 Hook
    // ================================================================

    private void OnEntityPopDamage(Hook_Entity.orig_popDamage orig, Entity self, AttackData a)
    {
        try
        {
            if (!_disposed && ShouldRainbow(self, a) && TryCreateRainbowPopDamage(self, a))
            {
                UpdateCooldownsAndIndex(self);
                return; // 原版在这条分支里也只创建一个数字，不调用 orig
            }
        }
        catch (Exception ex)
        {
            Log($"[RainbowCrit] 彩色暴击数字创建失败，回退原版: {ex.Message}");
        }

        // 非暴击 / 非玩家来源 / 生成失败 —— 全部交回原版
        orig(self, a);
    }

    /// <summary>是否应该把这次的伤害数字换成彩色暴击。</summary>
    private static bool ShouldRainbow(Entity self, AttackData a)
    {
        var cfg = RainbowCritKeys.Config.Value;
        if (!cfg.EnableMod) return false;

        // 调试开关：完全走原版路径，方便和开启后的效果做对比
        if (cfg.DisableRainbow) return false;

        if (a == null || self == null) return false;
        if (!a.hasTag(CritTag)) return false;

        // 原版 UI 开关：隐藏 UI 时不弹伤害数字（与 dc.Entity.popDamage 首段一致）
        try
        {
            var console = dc.ui.Console.Class.ME;
            if (console != null && console.flags != null && console.flags.exists("NoPopText".AsHaxeString()))
                return false;
        }
        catch { /* 控制台不可用时忽略 */ }

        if (cfg.PlayerCritsOnly && !IsFromPlayer(self, a)) return false;

        return true;
    }

    /// <summary>
    /// 复刻 dc.ui.popd._PopDamageHotline.create：
    ///   1) 自己判断同屏上限（原版 create 在上限时返回 null，这里显式判断方便回退）
    ///   2) big = true（原版暴击分支传的是 a.hasTag(2)，必为 true）
    ///   3) customFont = { chars: "numbers", font: "hotline" }
    ///
    /// 彩虹着色器 dc.shader.HotlineText 由 Hotline Miami 服装本身那条原版路径
    /// 加在 popDamage.text 上（_PopDamageHotline 构造里），这里不需要额外加。
    /// </summary>
    private static bool TryCreateRainbowPopDamage(Entity self, AttackData a)
    {
        try
        {
            if (dc.ui.PopDamage.Class.popDamageCount >= MaxSimultaneousPopDamage) return false;
        }
        catch { /* 读不到计数器就不拦截 */ }

        bool big = true;
        var font = new virtual_chars_font_
        {
            chars = CharsetNumbers.AsHaxeString(),
            font = FontHotline.AsHaxeString(),
        };

        var pop = PopDamageHotline.Class.create(self, a, self.dmgIdx, Ref<bool>.From(ref big), font);
        if (pop == null) return false;

        // 兜底：个别版本的构造里可能没给主数字挂着色器，这里补一次，保证一定出彩色。
        // Drawable.shaders 是 dc.hxsl.ShaderList 单链表。
        try
        {
            bool hasRainbowShader = false;
            for (var node = pop.text?.shaders; node != null; node = node.next)
            {
                if (node.s is HotlineText) { hasRainbowShader = true; break; }
            }

            if (!hasRainbowShader) pop.text?.addShader(new HotlineText());
        }
        catch { /* 兜底失败也照样显示，不阻塞 */ }

        return true;
    }

    // ================================================================
    // 原版冷却 / dmgIdx 复刻（dc.Entity.popDamage 后半段）
    // ================================================================

    private static void UpdateCooldownsAndIndex(Entity entity)
    {
        if (entity == null) return;

        double baseFps = entity.cd.baseFps;
        SetCooldown(entity, CooldownKey1, baseFps, CooldownSec1);
        SetCooldown(entity, CooldownKey2, baseFps, CooldownSec2);

        entity.dmgIdx++;
        if (entity.dmgIdx > MaxDmgIdx) entity.dmgIdx = 0;
    }

    private static void SetCooldown(Entity entity, int key, double baseFps, double seconds)
    {
        // 原版：floor(seconds * baseFps * 1000) / 1000
        double frames = System.Math.Floor(seconds * baseFps * 1000.0) / 1000.0;

        var inst = entity.cd.fastCheck.get(key);
        if (inst != null)
        {
            inst.frames = frames;
            return;
        }

        var created = new dc.tool._Cooldown.CdInst(key, frames);
        entity.cd.fastCheck.set(key, created);
        entity.cd.cdList.push(created);
    }

    // ================================================================
    // 「这次伤害是不是玩家打的」
    // ================================================================

    private static bool IsFromPlayer(Entity target, AttackData a)
    {
        var hero = ResolveHero(target);
        if (hero == null) return false;

        if (IsHero(a.source, hero)) return true;

        // 子弹 / 手雷 / 宠物 / 炮台：载体或持有者指向主角
        if (IsHero(a.carrier, hero)) return true;
        if (HasOwner(hero, a.source)) return true;
        if (HasOwner(hero, a.carrier)) return true;

        return false;
    }

    private static Hero? ResolveHero(Entity target)
    {
        try
        {
            var level = target._level;
            if (level?.game != null) return level.game.hero;
        }
        catch { }

        try { return ModCore.Modules.Game.Instance?.HeroInstance; } catch { }

        return null;
    }

    private static bool IsHero(Entity? e, Hero hero)
    {
        if (e == null) return false;
        try { return ReferenceEquals(e, hero) || e is Hero; } catch { return false; }
    }

    /// <summary>宠物 / 召唤物身上的 owner 字段指向主角。</summary>
    private static bool HasOwner(Hero hero, Entity? e)
    {
        if (e == null) return false;
        try
        {
            var f = FindOwnerField(e.GetType());
            if (f == null) return false;
            var owner = f.GetValue(e) as Entity;
            return IsHero(owner, hero);
        }
        catch { return false; }
    }

    private static FieldInfo? FindOwnerField(System.Type t)
    {
        if (OwnerFieldCache.TryGetValue(t, out var cached)) return cached;

        FieldInfo? found = null;
        try
        {
            foreach (var name in OwnerFieldNames)
            {
                found = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (found != null) break;
            }
        }
        catch { found = null; }

        OwnerFieldCache[t] = found;
        return found;
    }

    // ================================================================
    // 选项菜单（IModMenu）
    // ================================================================

    public string GetName() => "全局彩色暴击";

    public void BuildMenu(dc.ui.Options options)
    {
        try
        {
            var b = (dc.ui.OptionsBase)options;
            var cfg = RainbowCritKeys.Config;

            ((dc.ui.Text)b.title).set_text(StringUtils.AsHaxeString("RAINBOW CRIT 设置"));
            b.createScroller(0.0);

            bool enabled = cfg.Value.EnableMod;
            b.addToggleWidget(
                StringUtils.AsHaxeString("启用彩色暴击"),
                StringUtils.AsHaxeString("暴击伤害数字使用 Hotline Miami 的彩虹字体，任何服装都能触发"),
                (HlFunc<bool>)delegate
                {
                    cfg.Value.EnableMod = !cfg.Value.EnableMod;
                    cfg.Save();
                    return cfg.Value.EnableMod;
                },
                new Ref<bool>(ref enabled),
                b.scrollerFlow);

            bool playerOnly = cfg.Value.PlayerCritsOnly;
            b.addToggleWidget(
                StringUtils.AsHaxeString("仅玩家暴击"),
                StringUtils.AsHaxeString("开启=只有你打出的暴击变彩色；关闭=怪物暴击你时也是彩色"),
                (HlFunc<bool>)delegate
                {
                    cfg.Value.PlayerCritsOnly = !cfg.Value.PlayerCritsOnly;
                    cfg.Save();
                    return cfg.Value.PlayerCritsOnly;
                },
                new Ref<bool>(ref playerOnly),
                b.scrollerFlow);

            bool disableRainbow = cfg.Value.DisableRainbow;
            b.addToggleWidget(
                StringUtils.AsHaxeString("关闭彩虹字（对比用）"),
                StringUtils.AsHaxeString("开启后回到原版伤害数字：方便对比开关前后的暴击表现"),
                (HlFunc<bool>)delegate
                {
                    cfg.Value.DisableRainbow = !cfg.Value.DisableRainbow;
                    cfg.Save();
                    return cfg.Value.DisableRainbow;
                },
                new Ref<bool>(ref disableRainbow),
                b.scrollerFlow);

            b.updateScroller();
        }
        catch (Exception ex)
        {
            Log($"[RainbowCrit] 建立选项菜单失败: {ex.Message}");
        }
    }

    // ================================================================
    // 日志
    // ================================================================

    private void Log(string msg)
    {
        System.Console.WriteLine(msg);
        try { Logger.Information(msg); } catch { }
    }
}
