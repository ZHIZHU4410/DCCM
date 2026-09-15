using System;
using System.Collections.Generic;
using dc.en;
using dc.tool;
using dc.tool.weap;

namespace ChronoBlade
{
    /// <summary>
    /// 自定义武器的创建拦截器。
    ///
    /// ⚠️ 关键点：DCCM 的 Hook 协议要求**第一个参数必须是与原函数签名完全一致的委托类型**，
    /// 不能写成 object / Delegate。签名不匹配会在运行时抛
    /// System.MissingMethodException: Method 'System.Object.Invoke' not found
    /// 并直接把游戏打崩（HashlinkHookManager.CreateAdaptDelegate 处）。
    ///
    /// 原函数原型：Weapon tool.$Weapon.create(Hero hero, InventItem item)
    /// 因此委托必须是 public delegate Weapon orig_create(Hero hero, InventItem item);
    /// </summary>
    public static class ChronoWeaponFactory
    {
        /// <summary>与原函数签名一一对应的委托。</summary>
        public delegate Weapon orig_create(Hero? hero, InventItem? item);

        /// <summary>武器名称 → 构造工厂。</summary>
        public static readonly Dictionary<string, Func<Hero, InventItem, Weapon>> WeaponCreateMap = new();

        /// <summary>Hook 处理器：命中自定义武器就走我们的类，否则回退原版。</summary>
        public static Weapon Hook_create(orig_create orig, Hero? hero, InventItem? item)
        {
            string raw = "";
            try
            {
                raw = item?._itemData?.id?.ToString() ?? "";
            }
            catch { }

            string id = NormalizeId(raw);

            if (id.Length > 0 && WeaponCreateMap.TryGetValue(id, out var creator))
            {
                try
                {
                    Weapon created = creator(hero!, item!);
                    // 命中成功不打日志（每次造武器一行，正常玩是噪音）；
                    // 命中失败 / 没命中仍会报（见下面的分支）。
                    return created;
                }
                catch (Exception ex)
                {
                    Log($"构造 {id} 失败，回退原版: {ex}");
                }
            }
            else if (raw.Length > 0 && _missLog < 12)
            {
                // 这条日志很关键：它说明 create 钩子有正常触发，只是 id 对不上。
                _missLog++;
                Log($"Weapon.create 未命中（原样 id='{raw}' → 归一化 '{id}'）");
            }

            return orig(hero, item);
        }

        private static int _missLog;

        /// <summary>
        /// 归一化物品 id。
        /// hashlink 把 CDB 里的字符串序列化成 "id=TimeBullet" 这种带键名前缀的形式
        /// （ChronoPanelLog.Plain 里也要剥掉这个前缀，就是这个原因），
        /// 直接拿它查表会查不到 —— 那会静默退回原版武器，等于整个特性不生效。
        /// </summary>
        private static string NormalizeId(string? s)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0) return "";

            int eq = s.IndexOf('=');
            if (eq >= 0) s = s.Substring(eq + 1).Trim();

            return s.Trim('"', '\'', ' ');
        }

        private static Serilog.ILogger? _logger;
        public static void AttachLogger(Serilog.ILogger logger) => _logger = logger;

        private static void Log(string msg)
        {
            string line = $"[ChronoBlade] {msg}";
            System.Console.WriteLine(line);
            try { _logger?.Information(line); } catch { }
        }

        /// <summary>注册自定义武器（id → 构造工厂）。</summary>
        public static void Register()
        {
            WeaponCreateMap[ChronoBlade.name] = (hero, item) => new ChronoBlade(hero, item);
            WeaponCreateMap[TimeBullet.name] = (hero, item) => new TimeBullet(hero, item);
        }
    }
}
