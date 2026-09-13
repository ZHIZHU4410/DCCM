using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using dc;
using dc.en;
using dc.tool;
using dc.tool.weap;
using Hashlink;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;

using SysConsole = System.Console;

namespace ChronoBlade
{
    /// <summary>
    /// 运行时诊断：把"英雄当前手里拿的到底是什么武器"写进日志文件。
    ///
    /// 之前所有诊断都走了 System.Console.WriteLine，但 DCCM 的 logs\log_latest.log
    /// 只收 Serilog 的输出，所以攻击/技能那一路的证据一直是空白，白排查了好几轮。
    /// 现在统一用 Logger（既进日志文件也进控制台）。
    /// </summary>
    public static class ChronoDiag
    {
        public static void LogWeapons(ModCore.Mods.ModBase mod)
        {
            try
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero == null || hero.destroyed)
                {
                    mod.Logger.Information("[ChronoBlade] 诊断: 当前没有英雄实例");
                    return;
                }

                var manager = hero.weaponsManager;
                if (manager?.mainWeapons == null)
                {
                    mod.Logger.Information("[ChronoBlade] 诊断: weaponsManager 还没准备好");
                    return;
                }

                mod.Logger.Information($"[ChronoBlade] 诊断: 主手槽位 {manager.mainWeapons.length} 个");

                for (int i = 0; i < manager.mainWeapons.length; i++)
                {
                    var w = manager.mainWeapons.getDyn(i) as Weapon;
                    mod.Logger.Information(
                        $"   槽{i}: {(w == null ? "空" : Describe(w))}");
                }

                var backpack = manager.backpackWeapons;
                if (backpack != null)
                {
                    for (int i = 0; i < backpack.length; i++)
                    {
                        var w = backpack.getDyn(i) as Weapon;
                        if (w != null)
                        {
                            mod.Logger.Information($"   背包{i}: {Describe(w)}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                mod.Logger.Warning($"[ChronoBlade] 诊断失败: {ex.Message}");
            }
        }

        private static string Describe(Weapon w)
        {
            string cls = w.GetType().Name;
            string item = "?";
            string group = "?";
            try { item = w.wInfos?.item?.ToString() ?? "?"; } catch { }
            try { group = Convert.ToString(w.item?._itemData?.group) ?? "?"; } catch { }
            return $"类型={cls} item={item} group={group} 是我方武器={w is ChronoBlade || w is TimeBullet}";
        }

        /// <summary>打印一次"英雄是否正握着时之刃"，用于确认注入居合的前提。</summary>
        public static void LogDashGate(ModCore.Mods.ModBase mod, string phase, bool holding)
        {
            mod.Logger.Information($"[ChronoBlade] 1a 居合注入检查（{phase}）: 手持时之刃={holding}");
        }
    }
}
