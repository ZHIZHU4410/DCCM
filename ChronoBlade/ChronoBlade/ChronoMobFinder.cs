using System;
using System.Collections.Generic;
using dc.en;
using dc.hl.types;
using Hashlink.Proxy.Objects;

namespace ChronoBlade
{
    /// <summary>
    /// 目标搜索工具：在英雄周围找出最近的存活怪物（给剑雨 / 一周飞镖选目标）。
    /// 走 _level.entitiesByClass[MobClid]（dc.en.Mob 的 CLID），与原版 Starfury 的做法一致。
    /// </summary>
    public static class ChronoMobFinder
    {
        /// <summary>dc.en.Mob 的 CLID。</summary>
        private const int MobClid = 32068;

        /// <summary>取范围内最近的 max 个目标，返回像素坐标（剑雨用）。</summary>
        public static List<(double px, double py)> Pick(Hero hero, double radiusTiles, int max)
        {
            var result = new List<(double, double)>();
            if (hero == null || hero.destroyed || hero._level == null) return result;

            try
            {
                var mobs = hero._level.entitiesByClass?.get(MobClid) as ArrayObj;
                if (mobs == null || mobs.length == 0) return result;

                double hx = hero.cx + hero.xr;
                double hy = hero.cy + hero.yr - hero.hei / 48.0;
                var team = hero._team;

                var cands = new List<(double d, Mob m)>();
                for (int i = 0; i < mobs.length; i++)
                {
                    if (!(mobs.getDyn(i) is Mob m)) continue;
                    if (m == null || m.destroyed || m.life <= 0) continue;
                    if (m._team == null || (team != null && m._team == team)) continue;

                    bool ok;
                    try { ok = m._targetable && m.canBeDetected() && m.canBeHit(); }
                    catch { continue; }
                    if (!ok) continue;

                    double dx = hx - (m.cx + m.xr);
                    double dy = hy - (m.cy + m.yr - m.hei / 48.0);
                    double dist = System.Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radiusTiles) continue;
                    cands.Add((dist, m));
                }

                if (cands.Count == 0) return result;
                cands.Sort((a, b) => a.d.CompareTo(b.d));

                int take = System.Math.Min(max, cands.Count);
                for (int i = 0; i < take; i++)
                {
                    var m = cands[i].m;
                    result.Add(((m.cx + m.xr) * 24.0, (m.cy + m.yr) * 24.0));
                }
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ChronoBlade] 目标搜索失败: {ex.Message}");
            }
            return result;
        }

        /// <summary>范围内最近的单个怪物（找不到返回 null，用于转身朝向）。</summary>
        public static Mob? Nearest(Hero hero, double radiusTiles)
        {
            if (hero == null || hero.destroyed || hero._level == null) return null;
            try
            {
                var mobs = hero._level.entitiesByClass?.get(MobClid) as ArrayObj;
                if (mobs == null || mobs.length == 0) return null;

                double hx = hero.cx + hero.xr;
                double hy = hero.cy + hero.yr - hero.hei / 48.0;
                var team = hero._team;
                Mob? best = null;
                double bestD = double.MaxValue;

                for (int i = 0; i < mobs.length; i++)
                {
                    if (!(mobs.getDyn(i) is Mob m)) continue;
                    if (m == null || m.destroyed || m.life <= 0) continue;
                    if (m._team == null || (team != null && m._team == team)) continue;

                    bool ok;
                    try { ok = m._targetable && m.canBeDetected() && m.canBeHit(); }
                    catch { continue; }
                    if (!ok) continue;

                    double dx = hx - (m.cx + m.xr);
                    double dy = hy - (m.cy + m.yr - m.hei / 48.0);
                    double dist = System.Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radiusTiles || dist >= bestD) continue;
                    bestD = dist;
                    best = m;
                }
                return best;
            }
            catch
            {
                return null;
            }
        }
    }
}
