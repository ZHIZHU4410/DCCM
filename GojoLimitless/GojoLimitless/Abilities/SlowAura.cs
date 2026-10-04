#nullable disable
using System;
using System.Collections.Generic;
using dc.en;

namespace GojoLimitless.Abilities
{
    /// <summary>
    /// 无下限「迟滞领域」的实际减速实现 —— 挂在原版 <c>Mob.getMoveSpeedMul()</c> 上。
    ///
    /// ============================ 为什么不用 affect ============================
    /// 一开始想用原版 affect 133（就是"冰冻/解冻"那个计数器，<c>Mob.get_slowFactor()</c>
    /// 会读它的层数）。读代码后发现两处都走不通：
    ///
    ///   1) <c>Entity.setAffectS</c> 里 **层数是由 val 参数决定的**：
    ///      <c>num15 = (int)num; while (num2 &lt; num15 - 1) { push 同一条 affect }</c>。
    ///      传 1.0 只会叠 1 层；而 133 还有一条"桶非空就直接 return"的早退，
    ///      所以循环调用也不可能叠上去。
    ///   2) 就算叠上去了也没用：<c>get_slowPerStack() = 1.0 / thawMaxStacks</c>，
    ///      而 <c>thawMaxStacks = 99999</c>（<c>_Mob.__inst_construct__</c>）——
    ///      每层只减 0.001% 移速。affect 133 是"解冻计数"，不是减速。
    ///
    /// ============================ 所以改成 hook ============================
    /// <c>Mob.getMoveSpeedMul()</c> 是移动速度的唯一乘数出口，几乎所有 mob 的
    /// 行走 / 飞行 / 突进（<c>MobWalk</c>、<c>Fly</c>、<c>Demon</c>、<c>Bomber</c> …）
    /// 都乘它。这里对"位于无下限半径内"的敌人整体乘一个倍率，
    /// 越靠近英雄倍率越低 —— 也就是原作"越接近越慢"的数值化。
    ///
    /// ⚠️ 性能红线：这个 hook 会被**每个敌人每一帧**调用多次，
    ///    所以内部只做一次字典查找 + 一次乘法：
    ///    **不读配置、不分配对象、不打日志、不遍历敌人表**。
    ///    标记工作全部由「无下限」每 0.1 秒做一次（见 InfinityAbility.UpdateField）。
    /// </summary>
    public static class SlowAura
    {
        /// <summary>Mob → (倍率, 剩余秒数)。用引用键，一个 mob 只有一条。</summary>
        private static readonly Dictionary<Mob, Entry> Affected = new();

        /// <summary>正在衰减的条目（复用列表，避免每帧分配）。</summary>
        private static readonly List<Mob> Expired = new();

        /// <summary>用 struct 存值，避免 (double,double) 元组装箱。</summary>
        private struct Entry
        {
            public double Mul;
            public double Ttl;
        }

        /// <summary>当前被迟滞的敌人数量（HUD / 日志用）。</summary>
        public static int ActiveCount => Affected.Count;

        /// <summary>某个 mob 当前的迟滞倍率（1.0 = 没被迟滞）。</summary>
        public static double MultiplierFor(Mob mob)
        {
            if (mob != null && Affected.TryGetValue(mob, out Entry e)) return e.Mul;
            return 1.0;
        }

        /// <summary>
        /// 标记一个敌人。由「无下限」每 0.1 秒、领域每 0.12 秒调用一次。
        ///
        /// 同一帧可能被"外圈弱、内圈强"刷新两次，取更狠（更小）的那个倍率。
        /// </summary>
        public static void Mark(Mob mob, double multiplier, double ttlSeconds)
        {
            if (mob == null) return;

            if (double.IsNaN(multiplier) || multiplier >= 1.0)
            {
                // 倍率 >= 1 等于没减速，直接不记
                if (multiplier >= 1.0) return;
                multiplier = 0.02;
            }
            if (multiplier < 0.02) multiplier = 0.02;

            if (ttlSeconds <= 0.0) ttlSeconds = 0.30;

            if (Affected.TryGetValue(mob, out Entry cur) && cur.Ttl > 0.0 && cur.Mul <= multiplier)
            {
                // 已有更狠的，只续时间
                cur.Ttl = ttlSeconds;
                Affected[mob] = cur;
                return;
            }

            Affected[mob] = new Entry { Mul = multiplier, Ttl = ttlSeconds };
        }

        /// <summary>衰减计时（每帧一次，由「无下限」驱动）。</summary>
        public static void Tick(double dt)
        {
            if (Affected.Count == 0) return;
            if (dt <= 0.0) return;

            Expired.Clear();
            foreach (KeyValuePair<Mob, Entry> kv in Affected)
            {
                Entry e = kv.Value;
                e.Ttl -= dt;
                if (e.Ttl <= 0.0) Expired.Add(kv.Key);
                else Affected[kv.Key] = e;
            }

            for (int i = 0; i < Expired.Count; i++)
            {
                Affected.Remove(Expired[i]);
            }
            Expired.Clear();
        }

        /// <summary>
        /// hook 主体：<c>Mob.getMoveSpeedMul()</c> 无参数、返回 double。
        ///
        /// ⚠️ DCCM 要求处理器第一个参数是**与原函数签名完全一致**的委托
        /// （见 ChronoWeaponFactory 头注释），所以这里显式用
        /// <c>Hook_Mob.orig_getMoveSpeedMul</c>。
        /// </summary>
        public static double HookGetMoveSpeedMul(Hook_Mob.orig_getMoveSpeedMul orig, Mob self)
        {
            double baseVal;
            try { baseVal = orig(self); }
            catch { return 1.0; }

            if (self == null) return baseVal;

            // 这个判断必须在字典查找之前 —— 未激活时不产生任何额外开销
            if (!GojoHub.AbilitiesActive) return baseVal;

            if (Affected.TryGetValue(self, out Entry e) && e.Ttl > 0.0)
            {
                return baseVal * e.Mul;
            }

            return baseVal;
        }

        public static void Reset()
        {
            Affected.Clear();
            Expired.Clear();
        }
    }
}
