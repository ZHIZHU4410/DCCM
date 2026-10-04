#nullable disable
using System;
using dc.en;

using SysMath = System.Math;

namespace GojoLimitless.Abilities
{
    /// <summary>所有"招式"的统一接口（HUD / 菜单 / 调度都只看这一层）。</summary>
    public interface IGojoAbility
    {
        string Id { get; }
        string DisplayName { get; }

        /// <summary>配置里是否启用。</summary>
        bool Enabled { get; }

        /// <summary>冷却是否结束。</summary>
        bool Ready { get; }

        /// <summary>剩余冷却（秒）。</summary>
        double CooldownLeft { get; }

        /// <summary>剩余冷却占完整冷却的比例（0 = 就绪，1 = 刚放完）。HUD 画条用。</summary>
        double CooldownRatio { get; }

        /// <summary>
        /// 绑定到的**按键虚拟键码**（0 = 没绑 / 被动）。
        /// 每帧现算（一次 Trim + 查表），所以菜单里改完键当帧就生效；
        /// 顺带把键加进 <see cref="Input"/> 的跟踪集。
        /// </summary>
        int TriggerKey { get; }

        void Reset();
    }

    /// <summary>
    /// 招式基类：负责
    ///   * 从配置读 <c>Enabled / Key / Cooldown</c>（每招在 <see cref="CfgOf"/> 里指到自己的那一段）
    ///   * 把配置里的 <c>Key</c>（**键名**，如 <c>"J"</c>）经 <see cref="GojoKeys"/> 解析成键码
    ///   * 冷却计时（<see cref="Ready"/> / <see cref="TriggerCooldown"/>）
    ///   * 触发判定（<see cref="TryActivate"/> 自己直接读键 + 自己记边沿，见那里的注释）
    ///   * 总开关门槛（<see cref="Gated"/>，由 <see cref="GojoHub.UpdateGate"/> 每帧刷新）
    ///
    /// 派生类只需要实现 <see cref="CfgOf"/> / <see cref="Activate"/> / <see cref="OnUpdate"/>。
    /// 所有回调都包了 try/catch —— 玩法逻辑里一行异常绝不能把游戏打崩。
    /// </summary>
    public abstract class GojoAbilityBase : IGojoAbility
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }

        /// <summary>这一招的配置段。</summary>
        protected abstract AbilityCfg CfgOf(Configs c);

        public bool Enabled
        {
            get
            {
                try
                {
                    AbilityCfg a = CfgOf(Cfg.V);
                    return a == null || a.Enabled;
                }
                catch { return false; }
            }
        }

        public bool Ready => _cooldown <= 0.0;
        public double CooldownLeft => _cooldown > 0.0 ? _cooldown : 0.0;

        public double CooldownRatio
        {
            get
            {
                double total = CooldownSeconds;
                if (total <= 0.0) return _cooldown > 0.0 ? 1.0 : 0.0;
                double r = _cooldown / total;
                if (r < 0.0) return 0.0;
                if (r > 1.0) return 1.0;
                return r;
            }
        }

        /// <summary>
        /// 配置里写的按键名（原样返回，给菜单显示用）。
        /// </summary>
        public virtual string ActionName
        {
            get
            {
                try
                {
                    AbilityCfg a = CfgOf(Cfg.V);
                    return a == null ? "" : (a.Key ?? "");
                }
                catch { return ""; }
            }
        }

        /// <summary>
        /// 解析配置里的按键名 → 虚拟键码（0 = 没绑）。
        /// 每帧现算（很便宜：一次 Trim + 字典查表），所以菜单里改完键当帧就生效。
        /// </summary>
        public virtual int TriggerKey
        {
            get
            {
                try { return GojoKeys.Bind(ActionName, 0); }
                catch { return 0; }
            }
        }

        protected double _cooldown;
        protected double _lastFireAt = -999.0;
        protected double _now;

        /// <summary>
        /// 上一帧这个键是不是按着 —— 用来算"刚按下"的那一帧。
        /// 照搬 <c>ZoomVisionMain.cs</c> 的 <c>_lastTDown</c>，不依赖任何中间输入层。
        /// </summary>
        private bool _keyWasDown;

        /// <summary>本招式的当前冷却（秒）。</summary>
        protected virtual double CooldownSeconds
        {
            get
            {
                try
                {
                    AbilityCfg a = CfgOf(Cfg.V);
                    return a == null ? 0.0 : SysMath.Max(0.0, a.Cooldown);
                }
                catch { return 0.0; }
            }
        }

        /// <summary>总开关门槛是否满足（由 <see cref="GojoHub.UpdateGateAlways"/> 每帧刷新）。</summary>
        protected static bool Gated => GojoHub.AbilitiesActive;

        /// <summary>真正放招。返回 true 表示确实打出去了（会进冷却）。</summary>
        protected abstract bool Activate(Hero hero, double dt);

        /// <summary>每帧维护（与冷却无关的持续效果）。</summary>
        protected virtual void OnUpdate(Hero hero, double dt) { }

        public virtual void Reset()
        {
            _cooldown = 0.0;
            _lastFireAt = -999.0;
        }

        /// <summary>把这一招标记为"已使用"（连招判定用）。</summary>
        protected void MarkUsed() => _lastFireAt = _now;

        /// <summary>距离上一次成功放招过去了多少秒。</summary>
        public double SinceLastUse => _now - _lastFireAt;

        /// <summary>每帧由 <see cref="GojoHub.Tick"/> 调用。</summary>
        public void Update(Hero hero, double dt)
        {
            if (_cooldown > 0.0) _cooldown -= dt;
            _now += dt;

            if (hero == null || hero.destroyed || hero.life <= 0) return;

            try { OnUpdate(hero, dt); }
            catch (Exception ex) { Log.Exception(ex, DisplayName + " OnUpdate 异常"); }
        }

        /// <summary>
        /// 尝试触发（由 <see cref="GojoHub.Tick"/> 统一派发）。
        ///
        /// ============================ 按键判定（照搬 ZoomVision 的写法） ============================
        /// 仓库里按键确实能用的模组用的是**最朴素**的写法（<c>ZoomVisionMain.cs:96-122</c>）：
        ///
        /// <code>
        /// bool tDown = IsKeyDown(VK_T);          // 直接 GetAsyncKeyState
        /// if (tDown &amp;&amp; !_lastTDown) { ...触发... }  // 自己一个 bool 做边沿
        /// _lastTDown = tDown;
        /// </code>
        ///
        /// 本模组原来绕了一层 <see cref="Input"/>（HashSet 跟踪集 + JustPressed + 世代去重），
        /// 实机诊断证明**按下会在那一层里被丢掉**（原始状态读到了"按下"，
        /// 但 <c>Input.Pressed</c> 始终是 false）。所以这里改成**每招自己直接读键 + 自己记边沿**，
        /// 不再经过任何中间层。
        ///
        /// ⚠️ 每帧都要更新 <c>_keyWasDown</c>（包括不满足门槛 / 冷却中时），
        ///    否则"按住不放"会在门槛恢复的那一帧被误判成"刚按下"。
        /// </summary>
        public void TryActivate(Hero hero, double dt)
        {
            // ---- 1) 读键并算边沿（每帧都做，不受门槛影响）----
            bool down = false;
            int vk = 0;
            try
            {
                vk = TriggerKey;
                down = vk > 0 && Input.IsDownNow(vk);
            }
            catch { down = false; }

            bool edge = down && !_keyWasDown;
            _keyWasDown = down;

            // ⚠️ 按下就要有日志 —— 这是验收标准，也是排查的第一步。
            //    下面任何一条早退都可能在中间拦住，所以先在这里报一次"键确实按到了"，
            //    并带上所有门槛的原始值，一眼就能看出是被哪一条挡的。
            if (edge)
            {
                Log.Info($"[触发] {DisplayName} 键={GojoKeys.Name(vk)}({vk}) "
                         + $"hero={(hero != null ? "有" : "null")} "
                         + $"destroyed={(hero != null && hero.destroyed ? 1 : 0)} "
                         + $"life={(hero != null ? hero.life : -1)} "
                         + $"Gated={(Gated ? 1 : 0)} Enabled={(Enabled ? 1 : 0)} Ready={(Ready ? 1 : 0)} "
                         + $"冷却={CooldownLeft:0.##}");
            }

            if (hero == null || hero.destroyed || hero.life <= 0) return;
            if (!edge) return;
            if (!Gated || !Enabled || !Ready) return;

            // ---- 2) 真正放招 ----
            bool fired;
            try { fired = Activate(hero, dt); }
            catch (Exception ex)
            {
                Log.Exception(ex, DisplayName + " 释放失败");
                fired = false;
            }

            if (fired)
            {
                TriggerCooldown();
                MarkUsed();
                Log.Info($"{DisplayName} 释放（键 {GojoKeys.Name(vk)}，冷却 {CooldownSeconds:0.##}s）");
            }
            else
            {
                Log.Info($"{DisplayName} 触发被拒（Activate 返回 false，按键与门槛都正常）");
            }
        }

        /// <summary>把招式推进冷却（连招 / 特殊触发路径也走这里）。</summary>
        protected void TriggerCooldown() => _cooldown = CooldownSeconds;
    }
}
