using dc;
using dc.en;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;
using ModCore.Modules;
using System;
using System.Runtime.InteropServices;

using SysMath = System.Math;

namespace ShrinkOnKill
{
    /// <summary>
    /// 杀怪缩小 — 每击杀一个怪物，玩家缩小1%
    /// 参考 ModEntry 中的随机大小变（ChangeSize: hero.sprScaleX / hero.sprScaleY）
    /// 参考 hutao 项目：用成员变量保存状态 + OnHeroUpdate 每帧恢复，跨关卡不丢失
    /// </summary>
    public class ShrinkOnKillMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        // ===== 缩小参数 =====
        private const float SHRINK_RATIO = 1.1f;   
        private const float MIN_SCALE = 20f;        // 最小缩放，防止缩小到看不见

        // ===== 跨关卡持久化的缩放值 =====
        // 参考 hutao：成员变量在 mod 实例生命周期内不丢失，
        // 进入新关卡时 Hero 的 sprScaleX/Y 会被游戏重置为 1.0，
        // 通过每帧从 _currentScale 恢复来保持效果。
        private float _currentScale = 1.0f;

        // ===== T键恢复原始大小 =====
        private bool _isTKeyDown = false;
        private const int VK_T = 0x54;

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        private static extern short GetAsyncKeyState(int vkey);

        public ShrinkOnKillMain(ModInfo info) : base(info) { }

        #region 生命周期

        public override void Initialize()
        {
            base.Initialize();
            Hook_Hero.onMobDeath += OnHeroKillMob;
            System.Console.WriteLine("[ShrinkOnKill] 杀怪缩小模组已加载");
            System.Console.WriteLine("  按 T 键 → 恢复原始大小");
            System.Console.WriteLine($"  最小缩放限制: {MIN_SCALE * 100}%");
            System.Console.WriteLine("  跨关卡持久化：通过 OnHeroUpdate 每帧自动恢复");
        }

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            Hero? hero = ModCore.Modules.Game.Instance.HeroInstance;
            if (hero == null) return;

            // T 键恢复原始大小（同时重置持久化值）
            bool isTPressed = GetAsyncKeyState(VK_T) < 0;
            if (isTPressed && !_isTKeyDown)
            {
                _currentScale = 1.0f;
                hero.sprScaleX = 1.0f;
                hero.sprScaleY = 1.0f;
                System.Console.WriteLine("[ShrinkOnKill] 已恢复原始大小 (1.0x)");
            }
            _isTKeyDown = isTPressed;

            // 每帧从持久化变量恢复缩放（参考 hutao 每帧重新应用状态）
            // 这样即使关卡切换导致 Hero 重置，缩放也能立即恢复
            if (SysMath.Abs(hero.sprScaleX - _currentScale) > 0.001f ||
                SysMath.Abs(hero.sprScaleY - _currentScale) > 0.001f)
            {
                hero.sprScaleX = _currentScale;
                hero.sprScaleY = _currentScale;
            }
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_Hero.onMobDeath -= OnHeroKillMob;
            _currentScale = 1.0f;
            System.Console.WriteLine("[ShrinkOnKill] 游戏退出，模组已卸载");
        }

        #endregion

        #region 击杀缩小

        /// <summary>
        /// 玩家击杀怪物时触发，参考 ModEntry.ChangeSize 的大小变逻辑。
        /// 每次击杀将缩放乘以 0.99（缩小1%），最小不低于 MIN_SCALE。
        /// 同时更新 _currentScale 持久化变量。
        /// </summary>
        private void OnHeroKillMob(Hook_Hero.orig_onMobDeath orig, Hero self, dc.en.Mob m)
        {
            orig(self, m);
            float newScale = _currentScale * SHRINK_RATIO;

            // 限制最小值，防止缩小到看不见
            if (newScale < MIN_SCALE) newScale = MIN_SCALE;

            _currentScale = newScale;
            self.sprScaleX = _currentScale;
            self.sprScaleY = _currentScale;
        }

        #endregion
    }
}
