using dc;
using dc.en;
using dc.tool.atk;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System.Runtime.InteropServices;

namespace Enforcer
{
    /// <summary>
    /// Enforcer 执法者模组
    /// - 盾牌格挡 / 持盾移速~50% / 攻击盾反硬直
    /// - T=盾牌 J=攻击 K=盾反
    /// 外观替换不可行：initSprite/spr.lib 在运行时操作精灵必崩溃
    /// </summary>
    public class EnforcerMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private const double ShieldMax = 120, RegenDelay = 3, RegenRate = 30;
        private const int StunId = 8;
        private const double Range = 1.5, AtkPow = 80, BashPow = 40;
        private const double AtkCd = 0.4, BashCd = 0.6;
        private const double SpdNorm = 0.23, SpdShield = 0.11, LockDur = 0.15;
        private const int VK_T = 0x54, VK_J = 0x4A, VK_K = 0x4B;

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int v);

        private bool _ready, _shield = true, _broken;
        private double _hp = ShieldMax, _bt, _acd, _bcd, _lcd;
        private bool _tp, _to, _jp, _jo, _kp, _ko;

        public EnforcerMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onDamage += OnDmg;
            System.Console.WriteLine("[Enforcer] OK! T=盾牌 J=攻击 K=盾反");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            var h = ModCore.Modules.Game.Instance.HeroInstance;
            if (h == null || h._level == null) return;
            if (!_ready) { _ready = true; System.Console.WriteLine("[Enforcer] 就绪"); }

            if (_acd > 0) _acd -= dt; if (_bcd > 0) _bcd -= dt; if (_lcd > 0) _lcd -= dt;

            _tp = GetAsyncKeyState(VK_T) < 0; _jp = GetAsyncKeyState(VK_J) < 0; _kp = GetAsyncKeyState(VK_K) < 0;

            if (_tp && !_to) { if (_broken) System.Console.WriteLine("[Enforcer] 破盾中!"); else { _shield = !_shield; System.Console.WriteLine($"[Enforcer] 盾:{(_shield?"ON":"OFF")}"); } }
            if (_jp && !_jo && _acd <= 0 && _lcd <= 0) { Attack(h); _acd = AtkCd; _lcd = LockDur; }
            if (_kp && !_ko && _bcd <= 0 && _lcd <= 0) { if (_shield) { Bash(h); _lcd = LockDur * 1.5; } else System.Console.WriteLine("[Enforcer] 需持盾!"); _bcd = BashCd; }
            _to = _tp; _jo = _jp; _ko = _kp;

            Move(h);
            if (_broken) { _bt -= dt; if (_bt <= 0) { _broken = false; _hp = ShieldMax; _shield = true; System.Console.WriteLine("[Enforcer] 盾牌恢复!"); } }
            else if (_shield && _hp < ShieldMax) _hp = System.Math.Min(ShieldMax, _hp + RegenRate * dt);
        }

        void IOnGameExit.OnGameExit() { Hook_Hero.onDamage -= OnDmg; System.Console.WriteLine("[Enforcer] 卸载"); }

        void Move(Hero h)
        {
            h.runSpd = _lcd > 0 ? 0 : (_shield && !_broken) ? SpdShield : SpdNorm;
            if (_shield && _lcd <= 0)
            {
                if (System.Math.Abs(h.dx) > SpdShield * 2) h.dx = h.dir * SpdShield;
                if (System.Math.Abs(h.bdx) > SpdShield) h.bdx = h.dir * SpdShield * 0.5;
            }
        }

        void OnDmg(Hook_Hero.orig_onDamage o, Hero s, AttackData a)
        {
            if (!_shield || _broken) { o(s, a); return; }
            if (!Front(s, a)) { o(s, a); return; }
            _hp -= a.finalMissedDmg;
            if (_hp <= 0) { Break(s, a); o(s, a); }
        }

        void Attack(Hero h)
        {
            if (h._level == null || h._team == null) return;
            double hx = (h.cx + h.xr) * 24, hy = (h.cy + h.yr) * 24 - h.hei * 0.5;
            double p = _shield ? AtkPow * 1.3 : AtkPow;
            var it = h._team.opponentsIterator.reset(h._team);
            while (it.hasNext()) { var e = it.next(); if (HitCheck(h, e, hx, hy)) { e.life = System.Math.Max(0, e.life - (int)p); e.bump(h.dir * 0.3, -0.1, null); } }
        }

        void Bash(Hero h)
        {
            if (h._level == null || h._team == null) return;
            double hx = (h.cx + h.xr) * 24, hy = (h.cy + h.yr) * 24 - h.hei * 0.5;
            h._level.viewport.shakeS(0, 0.2, 0.4);
            h._level.fx.kickShockWave(hx + h.dir * 30, hy, 80, 16776960);
            var it = h._team.opponentsIterator.reset(h._team);
            while (it.hasNext())
            {
                var e = it.next();
                if (HitCheck(h, e, hx, hy))
                {
                    e.life = System.Math.Max(0, e.life - (int)BashPow);
                    e.bump(h.dir * 0.5, -0.2, null);
                    double d = 0; e.setAffectS(StunId, 0.3, new Ref<double>(ref d), null);
                }
            }
        }

        bool HitCheck(Entity src, Entity tgt, double hx, double hy)
        {
            if (tgt == null || tgt.destroyed || tgt.life <= 0) return false;
            double ex = (tgt.cx + tgt.xr) * 24, ey = (tgt.cy + tgt.yr) * 24 - tgt.hei * 0.5;
            double d = System.Math.Sqrt((ex - hx) * (ex - hx) + (ey - hy) * (ey - hy));
            if (d > Range * 24) return false;
            bool f = (src.dir == 1 && ex >= hx) || (src.dir == -1 && ex <= hx);
            return f || d < 24;
        }

        static bool Front(Hero s, AttackData a)
        {
            if (a.source == null) return true;
            double sx = (s.cx + s.xr) * 24, ax = (a.source.cx + a.source.xr) * 24;
            return s.dir == 1 ? ax >= sx : ax <= sx;
        }

        void Break(Hero h, AttackData a)
        {
            _shield = false; _hp = 0; _broken = true; _bt = RegenDelay;
            h._level.fx.customMask(2142719, 0.1, 0.04, 0.1, 0.15, null);
            h._level.fx.wood((h.cx + h.xr) * 24 + 20, (h.cy + h.yr) * 24 - h.hei * 0.5 - 20, 25, h.dir);
            h._level.viewport.shakeS(0, 0.3, 0.5);
            if (a?.source != null && (a.hasTag(18) || (a.carrier == null && !a.hasTag(14) && !a.hasTag(29))))
                a.source.bump(-a.source.dir * 0.1, -0.3, null);
            double d = 0; h.setAffectS(StunId, 0.5, new Ref<double>(ref d), null);
            Console.WriteLine("[Enforcer] 盾碎!3秒恢复");
        }
    }
}
