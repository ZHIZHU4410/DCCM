using System;
using System.Collections.Generic;
using dc;
using dc.en;
using dc.tool;
using dc.tool.weap;
using Hashlink;
using Hashlink.Proxy.Objects;
using HaxeProxy.Runtime;
using ModCore.Storage;

namespace ChronoBlade
{
    /// <summary>
    /// Zaphkiel（十二之弹枪）—— 以原版 Pistol 为基类，十二之弹在手枪里轮换。
    ///
    ///   · 开火：完全走原版 Pistol 的射击逻辑（不碰原版状态机）。
    ///   · **开火不会换弹**：打的一直是当前装填的那一发；换弹改成按 X 打开选择弹药面板
    ///     （见 ChronoPanels.cs 的 ChronoAmmoPanel；打开期间游戏是真暂停的）。
    ///   · 每发子弹在英雄位置"蹦出"对应的罗马数字（一之弹 → I，十二之弹 → XII）。
    ///   · 自身向的子弹（一/三/五/六/九/十一之弹）**开火立刻生效，不需要命中**；
    ///     其余的命中目标才生效（见 ChronoBullets）。
    ///   · 主手拿着它时，英雄**身后**会循环播放 TIMEBEIJING 背景（见 ChronoFx.UpdateAura）。
    ///
    /// 与 ChronoBlade 一样的原则：**绝不去改写原版的连击/蓄力状态机**，
    /// 只在 onExecute 外面套一层记录"这一枪是哪一发"。
    /// </summary>
    public class TimeBullet : Pistol, IHxbitSerializable<object>
    {
        /// <summary>
        /// 游戏内显示名（写进 CDB 的 item.name 字段）。
        /// 玩家在卡片 / 掉落物上看到的就是这个名字。
        /// </summary>
        public const string DisplayName = "Zaphkiel";

        /// <summary>
        /// CDB 里的 **item id**（武器工厂映射、召唤掉落都用它）。
        /// 故意保持 "TimeBullet" 不变：改成别的 id 会让旧存档里已经拿到的那把枪
        /// 变成"未知物品"，读档时可能直接报错。要连 id 一起改的话，
        /// 这里、ChronoWeaponFactory.Register 和 patch_chronoblade_cdb.py 的
        /// PISTOL_ID 三处必须同时改，并且旧存档里的那把枪要先丢掉。
        /// </summary>
        public static string name = "TimeBullet";

        /// <summary>当前弹号（0 基：0 = 一之弹 Aleph … 11 = 十二之弹 Yud·Bet）。</summary>
        private int _bulletIndex;

        /// <summary>防止同一次开火重复弹数字。</summary>
        private int _fireLogCount;

        public TimeBullet(Hero hero, InventItem item) : base(hero, item)
        {
            Log($"{DisplayName} 就绪：共 {ChronoBullets.All.Length} 发，当前第 {_bulletIndex + 1} 发 " +
                $"{ChronoBullets.Get(_bulletIndex).Name}");
        }

        /// <summary>当前弹号（给 ChronoBullets / 主模块读取）。</summary>
        public int BulletIndex => _bulletIndex;

        /// <summary>当前弹的 id（如 "Aleph"），供命中时分类。</summary>
        public string BulletId => ChronoBullets.Get(_bulletIndex).Id;

        /// <summary>
        /// 这把刻刻帝是不是带着"子弹效果翻倍"的传奇词条（ChronoBulletDouble）。
        ///
        /// 词条本身由 CDB 决定：item.legendAffixes 里列了它，物品生成时就会 Roll 上去；
        /// 这里只是查物品身上有没有 —— 走 InventItem.hasAffix()，不要去比对 _itemData，
        /// 因为 _itemData.legendAffixes 是"可 Roll 的池子"，不是"已经 Roll 到的词条"。
        /// </summary>
        public bool IsLegendaryDouble
        {
            get
            {
                try
                {
                    object? raw = wInfos?.item;
                    if (raw is InventItem item) return item.hasAffix(ToHaxe(ChronoBullets.LegendAffixId));
                    return false;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>手动换弹（选择 UI 的 ← → 用）。</summary>
        public void CycleBullet(int delta = 1)
        {
            int n = ChronoBullets.All.Length;
            SetBullet(((_bulletIndex + delta) % n + n) % n);
        }

        /// <summary>直接装填第 index 发（选择 UI 确认时调用）。</summary>
        public void SetBullet(int index)
        {
            int n = ChronoBullets.All.Length;
            if (n <= 0) return;
            _bulletIndex = ((index % n) + n) % n;
            Log($"装填 → 第 {_bulletIndex + 1} 发 {ChronoBullets.Get(_bulletIndex).Name}");
        }

        /// <summary>开火时调用（由主模块的 tool.Weapon.onExecute 钩子触发）。</summary>
        public void OnFired()
        {
            try
            {
                Hero? hero = owner;
                if (hero == null || hero.destroyed) return;

                var def = ChronoBullets.Get(_bulletIndex);
                bool boost = IsLegendaryDouble;

                // 自身向的子弹：开火立刻生效，不需要命中（见 ChronoBullets.OnFire）
                // boost = 带着传奇词条 → 效果翻倍
                ChronoBullets.OnFire(_bulletIndex, hero, boost);

                // 蹦出当前弹的罗马数字
                ChronoFx.PopBulletNumeral(hero, _bulletIndex);

                if (_fireLogCount < 12)
                {
                    _fireLogCount++;
                    Log($"开火：第 {_bulletIndex + 1} 发 {def.Name}" +
                        $"（罗马数字 {ChronoFx.Roman(_bulletIndex + 1)}，" +
                        (def.SelfCast ? "开火即生效" : "需命中") +
                        (boost ? "，传奇·效果翻倍" : "") + "；按选择键换弹）");
                }

                // ⚠️ 这里以前会自动 _bulletIndex + 1，现已按需求去掉：
                //    开火只发射当前装填的这一发，换弹只在选择弹药面板里做（ChronoAmmoPanel）。
                //    不要在这里加回自增。
            }
            catch (Exception ex)
            {
                Log($"开火处理失败: {ex.Message}");
            }
        }

        private static void Log(string msg)
        {
            string line = $"[ChronoBlade] {msg}";
            System.Console.WriteLine(line);
            try { _logger?.Information(line); } catch { }
        }

        private static dc.String ToHaxe(string s) => new HashlinkString(s).AsHaxe<dc.String>();

        private static Serilog.ILogger? _logger;
        public static void AttachLogger(Serilog.ILogger logger) => _logger = logger;

        // ---------------------------------------------------------------- 存档
        object IHxbitSerializable<object>.GetData()
        {
            return new Dictionary<string, object> { { "bullet", _bulletIndex } };
        }

        void IHxbitSerializable<object>.SetData(object data)
        {
            try
            {
                if (data is Dictionary<string, object> d && d.TryGetValue("bullet", out var v))
                {
                    _bulletIndex = Convert.ToInt32(v);
                }
            }
            catch { }
        }
    }
}
