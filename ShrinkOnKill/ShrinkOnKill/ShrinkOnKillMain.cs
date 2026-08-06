using dc;
using dc.en;
using dc.tool;
using dc.tool.hero;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SysMath = System.Math;

namespace ShrinkOnKill
{
    /// <summary>
    /// 杀怪变大 — 每击杀一个怪物变大，前期快、后期慢，约 200 杀接近 5 倍上限；
    /// 攻击范围与移动速度随体型同步放大；按 F4 恢复原始大小。
    /// 攻击范围缩放参考 MultiKickBoots / SpeedBladeP 的武器 areas 处理方式。
    /// </summary>
    public class ShrinkOnKillMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        // ===== 变大参数 =====
        private const float MAX_SCALE = 5f;         // 最大缩放 5 倍
        private const float GROW_STEP = 0.03f;      // 每次击杀向 5 倍靠近 3% 的剩余差距（前期快、后期慢，约 200 杀接近上限）

        // ===== 跨关卡持久化的缩放值 =====
        private float _currentScale = 1.0f;

        // ===== F4 键恢复原始大小 =====
        private bool _isF4KeyDown = false;
        private const int VK_F4 = 0x73;

        // ===== 攻击范围缩放 =====
        // 记录每个 Area 的原始位置/尺寸（弱引用表，武器销毁后自动释放），
        // 每次挥动前按当前体型重算；F4 恢复原大小后自动还原。
        private readonly ConditionalWeakTable<object, AreaBase> _areaBase = new();
        private bool _rangeLogDone = false;

        // ===== 移动速度缩放 =====
        private double _originalRunSpd = -1.0;

        private sealed class AreaBase
        {
            public readonly double X, Y, WidPx, HeiPx;

            public AreaBase(double x, double y, double widPx, double heiPx)
            {
                X = x; Y = y; WidPx = widPx; HeiPx = heiPx;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public ShrinkOnKillMain(ModInfo info) : base(info) { }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onMobDeath += OnHeroKillMob;
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUseHook;
            System.Console.WriteLine("[ShrinkOnKill] 杀怪变大模组已加载");
            System.Console.WriteLine("  每次击杀变大，前期快后期慢，约 200 杀接近 5 倍上限");
            System.Console.WriteLine("  攻击范围、移动速度随体型同步放大");
            System.Console.WriteLine("  按 F4 键 → 恢复原始大小");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null) return;

            // F4 键恢复原始大小（同时重置持久化值）
            bool isF4Pressed = GetAsyncKeyState(VK_F4) < 0;
            if (isF4Pressed && !_isF4KeyDown)
            {
                _currentScale = 1.0f;
                hero.sprScaleX = 1.0f;
                hero.sprScaleY = 1.0f;
                System.Console.WriteLine("[ShrinkOnKill] 已恢复原始大小 (1.0x)");
            }
            _isF4KeyDown = isF4Pressed;

            // 每帧从持久化变量恢复缩放，跨关卡不丢失
            if (SysMath.Abs(hero.sprScaleX - _currentScale) > 0.001f ||
                SysMath.Abs(hero.sprScaleY - _currentScale) > 0.001f)
            {
                hero.sprScaleX = _currentScale;
                hero.sprScaleY = _currentScale;
            }

            // 攻击范围、移动速度随体型同步
            ApplyAttackRangeScale(hero);
            ApplySpeedScale(hero);
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUseHook;
            _currentScale = 1.0f;
            System.Console.WriteLine("[ShrinkOnKill] 游戏退出，模组已卸载");
        }

        #endregion

        #region 攻击范围缩放

        /// <summary>
        /// 每次挥动武器前，把该武器的攻击 Area 按当前体型放大。
        /// </summary>
        private void OnWeaponUseHook(Hook_HeroWeaponsManager.orig_onWeaponUse orig, HeroWeaponsManager self, Weapon w, int slot)
        {
            try
            {
                ScaleWeaponAreas(w);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"[ShrinkOnKill] 攻击范围缩放失败: {ex.Message}");
            }

            orig(self, w, slot);
        }

        private void ApplyAttackRangeScale(Hero hero)
        {
            try
            {
                HeroWeaponsManager? wm = hero.weaponsManager;
                if (wm == null) return;

                Weapon? w = wm.lastWeaponUsed;
                if (w != null) ScaleWeaponAreas(w);
            }
            catch
            {
                // 每帧同步失败不影响攻击时缩放
            }
        }

        private void ScaleWeaponAreas(Weapon? w)
        {
            if (w == null) return;

            dynamic areas = w.areas;
            if (areas == null) return;

            int len = (int)areas.length;
            for (int i = 0; i < len; i++)
            {
                dynamic a = areas.getDyn(i);
                if (a == null) continue;

                object key = (object)a;
                if (!_areaBase.TryGetValue(key, out AreaBase? baseArea))
                {
                    baseArea = new AreaBase((double)a.x, (double)a.y, (double)a.widPx, (double)a.heiPx);
                    _areaBase.Add(key, baseArea);
                }

                a.x = baseArea.X * _currentScale;
                a.y = baseArea.Y * _currentScale;
                a.widPx = baseArea.WidPx * _currentScale;
                a.heiPx = baseArea.HeiPx * _currentScale;
            }

            if (!_rangeLogDone)
            {
                _rangeLogDone = true;
                System.Console.WriteLine($"[ShrinkOnKill] 攻击范围缩放已应用（{w.GetType().Name}，{len} 个 Area）");
            }
        }

        #endregion

        #region 移动速度缩放

        private void ApplySpeedScale(Hero hero)
        {
            try
            {
                if (_originalRunSpd < 0.0) _originalRunSpd = hero.runSpd;
                hero.runSpd = _originalRunSpd * _currentScale;
            }
            catch
            {
                // 速度缩放失败不影响体型变化
            }
        }

        #endregion

        #region 击杀变大

        /// <summary>
        /// 每次击杀向 5 倍上限靠近：体型小时每次涨得多（前期快），
        /// 越接近上限涨得越少（后期慢），最大不超过 5 倍。
        /// </summary>
        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob m)
        {
            orig(self, m);

            float newScale = _currentScale + (MAX_SCALE - _currentScale) * GROW_STEP;
            if (newScale > MAX_SCALE) newScale = MAX_SCALE;

            _currentScale = newScale;
            self.sprScaleX = _currentScale;
            self.sprScaleY = _currentScale;
        }

        #endregion
    }
}
