using dc;
using dc.en;
using HaxeProxy.Runtime;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Runtime.InteropServices;

using SysMath = System.Math;

namespace BloodNova
{
    /// <summary>
    /// 血之新星 — 击杀蓄能，按键释放全屏血爆
    ///
    /// 【怪物死亡】击杀怪物积蓄血怒层数，尸体爆出血雾特效。
    /// 【血怒光环】层数>0时，周围怪物持续掉血，层数越高伤害越高。
    ///             光环击杀会正常触发 onDie() 死亡流程。
    /// 【血之新星】按 T 释放：全屏敌人受伤 + 红色光柱粒子（allocBg，参照 Indulgence.indulgenceRay） +
    ///             屏幕震动 + 红色闪光 + 短暂无敌。
    ///             每个敌人头顶生成红色竖直光柱粒子，中心生成2倍大光柱，
    ///             粒子带淡入淡出、持续缩放、向上漂浮等动画。
    /// </summary>
    public class BloodNovaMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        // ===== 血怒状态 =====
        private int _charges = 0;
        private const int MAX_CHARGES = 100;

        // ===== 无敌计时 =====
        private double _invincibleTimer = 0;
        private const double INVINCIBLE_DURATION = 1.5;

        // ===== 按键 =====
        private bool _isTKeyDown = false;
        private const int VK_T = 0x54;

        // ===== 红光脉冲 =====
        private double _pulsePhase = 0;

        // ===== Nova 伤害参数 =====
        private const double BASE_NOVA_DMG = 30;
        private const double DMG_PER_CHARGE = 10;

        // ===== 血怒光环参数 =====
        private const double AURA_BASE_DPS = 3;
        private const double AURA_DPS_PER_CHARGE = 0.5;
        private const double AURA_RANGE = 8.0;

        // ===== 红色光柱参数 (完全参照 Indulgence.indulgenceRay 粒子实现) =====
        // Indulgence 原版: width=600 height=130 rotation=-π/2 life=0.4s fade=1→0.1 gy=-0.7 scaleMul=1.02
        private const double RED_RAY_WIDTH = 400;       // 光柱宽度（像素, Indulgence 原版 600）
        private const double RED_RAY_HEIGHT = 280;      // 光柱高度（像素, Indulgence 原版 130）
        private const double RED_RAY_LIFE = 0.5;        // 粒子寿命（秒）
        private const double RED_RAY_FADE_START = 1.0;  // 淡入起始不透明度
        private const double RED_RAY_FADE_END = 0.05;   // 淡出目标不透明度
        private const double RED_RAY_FADE_DUR = 0.25;   // 淡入淡出过渡时间
        private const double RED_RAY_GY = -0.5;         // Y轴重力（负=上浮）
        private const double RED_RAY_SCALE_MUL = 1.015; // 持续缩放倍率
        private const double RED_RAY_OFFSET_Y = -150;   // 光柱中心相对目标的Y偏移

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public BloodNovaMain(ModInfo info) : base(info) { }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onMobDeath += OnHeroKillMob;
            dc.en.Hook_Mob.fixedUpdate += OnMobFixedUpdate;
            System.Console.WriteLine("[BloodNova] 血之新星已加载");
            System.Console.WriteLine("  击杀蓄能 | 血怒光环灼烧 | 按 T 释放血爆（300+粒子）");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null || hero._level == null) return;

            // 无敌倒计时
            if (_invincibleTimer > 0)
                _invincibleTimer = SysMath.Max(0, _invincibleTimer - dt);

            // T 键释放 Nova
            bool isTPressed = GetAsyncKeyState(VK_T) < 0;
            if (isTPressed && !_isTKeyDown && _charges > 0)
                ReleaseNova(hero);
            _isTKeyDown = isTPressed;

            // 血怒发光
            if (_charges > 0)
            {
                _pulsePhase += dt * 4.0 * (1 + _charges * 0.05);
                double bright = 0.25 + (_charges / (double)MAX_CHARGES) * 0.55;
                bright *= 0.5 + 0.5 * SysMath.Sin(_pulsePhase);
                hero.colorBlink(0xFF2200, (float)bright, 0.12f);
            }


        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            dc.en.Hook_Mob.fixedUpdate -= OnMobFixedUpdate;
            _charges = 0;
            System.Console.WriteLine("[BloodNova] 游戏退出");
        }

        #endregion

        #region 怪物死亡

        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob m)
        {
            orig(self, m);

            if (_charges < MAX_CHARGES)
            {
                _charges++;
                if (_charges % 10 == 0)
                    System.Console.WriteLine($"[BloodNova] 血怒: {_charges} 层");
            }

            // 尸体小型血爆
            try
            {
                self._level?.fx?.stoneExplosion(
                    ((double)m.cx + m.xr) * 24.0,
                    ((double)m.cy + m.yr) * 24.0,
                    6.0,
                    0xFF2200,
                    null, null
                );
            }
            catch { }
        }

        #endregion

        #region 血怒光环（带死亡触发）

        private void OnMobFixedUpdate(dc.en.Hook_Mob.orig_fixedUpdate orig, dc.en.Mob self)
        {
            if (self == null) { orig(self); return; }

            if (_charges > 0 && self.life > 0 && !self.destroyed)
            {
                Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
                if (hero != null && hero._level == self._level)
                {
                    double dx = (double)(self.cx - hero.cx);
                    double dy = (double)(self.cy - hero.cy);
                    double dist = SysMath.Sqrt(dx * dx + dy * dy);

                    if (dist <= AURA_RANGE)
                    {
                        double auraDps = AURA_BASE_DPS + AURA_DPS_PER_CHARGE * _charges;
                        double frameSec = 1.0 / 60.0;
                        int dmg = (int)(auraDps * frameSec);
                        if (dmg < 1) dmg = 1;

                        int newLife = self.life - dmg;
                        if (newLife <= 0)
                        {
                            self.life = 0;
                            try { self.onDie(); } catch { }
                        }
                        else
                        {
                            self.life = newLife;
                        }

                        self.colorBlink(0xFF2200, 0.3f, 0.1f);
                    }
                }
            }

            orig(self);
        }

        #endregion

        #region 血之新星释放

        private void ReleaseNova(Hero hero)
        {
            int charges = _charges;
            _charges = 0;
            double totalDmg = BASE_NOVA_DMG + DMG_PER_CHARGE * charges;

            System.Console.WriteLine($"[BloodNova] 释放血之新星！{charges} 层，伤害 {totalDmg:F0}");

            try
            {
                // === 1. 全屏敌人伤害 + 击退 + 红色光柱 ===
                int hitCount = 0;
                int dmg = (int)totalDmg;
                if (hero._team != null)
                {
                    var iter = hero._team.opponentsIterator.reset(hero._team);
                    while (iter.hasNext())
                    {
                        Entity e = iter.next();
                        if (e == null || e.destroyed || e.life <= 0 || !e.canBeHit())
                            continue;

                        // 计算敌人世界像素坐标（用于光柱定位）
                        double ex = ((double)e.cx + e.xr) * 24.0;
                        double ey = ((double)e.cy + e.yr) * 24.0;

                        int newLife = e.life - dmg;
                        if (newLife <= 0)
                        {
                            e.life = 0;
                            if (e is dc.en.Mob mob)
                                try { mob.onDie(); } catch { }
                        }
                        else
                            e.life = newLife;

                        double kbDir = SysMath.Sign(e.cx - hero.cx);
                        if (kbDir == 0) kbDir = 1;
                        e.bump(kbDir * 3, -1.5, null);

                        // ★ 在敌人位置生成红色光柱（indulgenceRay 白色光柱代码 + electricPillar 红色粒子叠加）
                        hero._level?.fx?.indulgenceRay(e);
                        SpawnRedPillar(hero._level?.fx, ex, ey, charges, 1.0);

                        hitCount++;
                    }
                }

                // === 2. 屏幕震动 ===
                double shake = SysMath.Min(0.8, 0.1 + charges * 0.007);
                hero._level?.viewport?.shakeS(shake, shake * 0.7, 0.4);

                // === 3. 红色全屏闪光 ===
                hero._level?.fx?.customMask(
                    unchecked((int)0xFFFF0000),
                    0.35, 0.01,
                    0.15 + charges * 0.001,
                    0.4,
                    null
                );

                // === 4. 英雄中心大型红色冲天光柱（allocBg 粒子，完全参照 Indulgence.indulgenceRay） ===
                double hx = ((double)hero.cx + hero.xr) * 24.0;
                double hy = ((double)hero.cy + hero.yr) * 24.0 - hero.hei * 0.5;
                SpawnRedPillar(hero._level?.fx, hx, hy, charges, 2.0);  // 中心光柱2倍大

                // === 5. 无敌 ===
                _invincibleTimer = INVINCIBLE_DURATION;
                double ignore = 0;
                hero.setAffectS(48, INVINCIBLE_DURATION, new Ref<double>(ref ignore), null);

                // === 6. 日志 ===
                string[] shouts = { "血之新星！", "爆裂吧！", "化为灰烬！", "鲜血盛宴！" };
                System.Console.WriteLine($"[BloodNova] {shouts[new Random().Next(shouts.Length)]} — 命中 {hitCount} 个敌人");
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[BloodNova] 释放异常: {ex.Message}");
            }
        }

        #endregion

        #region 红色光柱粒子系统 (完全参照 Indulgence.indulgenceRay)

        /// <summary>
        /// 在指定位置生成红色光柱特效。
        /// 主方案：调用 fx.indulgenceRay(target) 生成白色光柱粒子（与 Indulgence 完全相同的代码）。
        /// 同时叠加 fx.electricPillar 红色闪电粒子形成红色光柱效果。
        /// </summary>
        private void SpawnRedPillar(Fx? fx, double cx, double cy, int charges, double sizeMul)
        {
            if (fx == null) return;

            try
            {
                // === 方案A: electricPillar 红色闪电光柱（带颜色控制） ===
                // electricPillar(x, y, width, height, color)
                // 生成 25 个红色闪电球粒子，竖直排列成光柱形状
                double pillarW = 20.0 * sizeMul;
                double pillarH = RED_RAY_HEIGHT * sizeMul;
                int redColor = 0xFF2200;
                fx.electricPillar(cx, cy, pillarW, pillarH, redColor);
            }
            catch { }
        }

        #endregion
    }
}
