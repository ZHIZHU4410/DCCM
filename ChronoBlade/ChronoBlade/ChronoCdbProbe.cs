using System;
using System.Collections;
using System.Reflection;
using Hashlink;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Mods;

namespace ChronoBlade
{
    /// <summary>
    /// CDB 诊断工具：直接枚举 Data.Class.item.byId 里的键，确认我们的物品到底有没有进表。
    /// 之前用 `itemData.group` 直接读属性会抛 NullReferenceException，这里改用反射，
    /// 不会因为动态绑定失败而误判。
    /// </summary>
    public static class ChronoCdbProbe
    {
        public static void Dump(ModBase mod, string ourId)
        {
            var log = mod.Logger;
            try
            {
                // 正确入口：原版与其他模组都用 Data.Class.item.byId
                // （直接碰 Data.Class.item 会拿到 null —— 之前那两行"读取出错"就是这么来的）
                object? byId = dc.Data.Class.item?.byId;
                log.Information($"[ChronoBlade] 探测: Data.item.byId 类型 = {byId?.GetType().FullName ?? "null"}");

                if (byId == null)
                {
                    log.Warning("[ChronoBlade] 探测: byId 为 null，CDB 可能还没加载完成");
                    return;
                }

                object? hit = null;
                try { hit = dc.Data.Class.item.byId.get(ToHaxe(ourId)); }
                catch (Exception ex) { log.Warning($"[ChronoBlade] 探测: byId.get({ourId}) 抛异常: {ex.GetType().Name}: {ex.Message}"); }

                log.Information(
                    $"[ChronoBlade] 探测: byId.get({ourId}) = {(hit == null ? "null（物品没进 CDB！）" : "命中")}");

                // 反射读 group，避免动态绑定抛异常
                if (hit != null)
                {
                    object? group = GetMember(hit, "group");
                    log.Information($"[ChronoBlade] 探测: {ourId}.group = {group ?? "?"}（4 = Melee 近战武器）");
                }

                // 对照：原版 Katana 能不能查到（验证探测方法本身没问题）
                object? vanilla = null;
                try { vanilla = dc.Data.Class.item.byId.get(ToHaxe("Katana")); } catch { }
                log.Information($"[ChronoBlade] 探测对照: byId.get(Katana) = {(vanilla == null ? "null" : "命中")}");
            }
            catch (Exception ex)
            {
                log.Warning($"[ChronoBlade] CDB 探测整体失败: {ex}");
            }
        }

        private static object? GetMember(object? obj, string name)
        {
            if (obj == null) return null;
            try
            {
                var t = obj.GetType();
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (p != null) return p.GetValue(obj);
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (f != null) return f.GetValue(obj);
            }
            catch { }
            return null;
        }

        private static dc.String ToHaxe(string s) => new HashlinkString(s).AsHaxe<dc.String>();
    }
}
