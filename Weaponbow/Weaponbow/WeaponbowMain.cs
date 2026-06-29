using dc;
using dc.hl.types;
using dc.tool;
using dc.tool.hero;
using dc.tool.weap;
using MagicSalveWeapon = dc.tool.weap.MagicSalve;
using MagicSalveBullet = dc.en.bu.MagicSalve;
using ModCore.Events.Interfaces.Game;
using ModCore.Events.Interfaces.Game.Hero;
using ModCore.Mods;

namespace Weaponbow
{
    /// <summary>
    /// MagicSalve：短按 20 发，每发随机位置 + 随机颜色。
    /// </summary>
    public class WeaponbowMain : ModBase, IOnGameExit, IOnHeroUpdate
    {
        private const int VOLLEY_COUNT = 20;
        private const int SPREAD = 3;
        private const int BULLET_CLID = 1428;

        private MagicSalveWeapon? _magicSalve;
        private int _lastBulletCount;

        public WeaponbowMain(ModInfo info) : base(info) { }

        public override void Initialize()
        {
            base.Initialize();
            Hook_HeroWeaponsManager.onWeaponUse += OnWeaponUse;
            System.Console.WriteLine("[Weaponbow] MagicSalve 20连发+随机色 已加载");
        }

        void IOnGameExit.OnGameExit()
        {
            Hook_HeroWeaponsManager.onWeaponUse -= OnWeaponUse;
        }

        // ═══════════════
        // 短按 → 20 发，每发不同位置
        // ═══════════════

        private void OnWeaponUse(
            Hook_HeroWeaponsManager.orig_onWeaponUse orig,
            HeroWeaponsManager self,
            Weapon w,
            int slot)
        {
            orig(self, w, slot);

            if (self?.hero == null || w == null) return;
            if (w is not MagicSalveWeapon ms) return;

            _magicSalve = ms;
            _lastBulletCount = 0;

            var hero = self.hero;
            int baseCx = hero.cx;
            int baseCy = hero.cy;

            try
            {
                for (int i = 1; i < VOLLEY_COUNT; i++)
                {
                    hero.cx = baseCx + System.Random.Shared.Next(-SPREAD, SPREAD + 1);
                    hero.cy = baseCy + System.Random.Shared.Next(-SPREAD, SPREAD + 1);
                    ms.onExecute();
                }
            }
            finally
            {
                hero.cx = baseCx;
                hero.cy = baseCy;
            }
        }

        // ═══════════════
        // 每帧 → 新子弹随机颜色
        // ═══════════════

        void IOnHeroUpdate.OnHeroUpdate(double dt)
        {
            if (_magicSalve == null) return;

            try
            {
                var level = _magicSalve.owner?._level;
                if (level == null) return;

                var arr = (ArrayObj)(object)level.entitiesByClass.get(BULLET_CLID);
                if (arr == null) return;

                int cur = arr.length;
                if (cur > _lastBulletCount)
                {
                    for (int j = _lastBulletCount; j < cur; j++)
                    {
                        if (arr.getDyn(j) is MagicSalveBullet bullet && bullet != null)
                        {
                            // 随机 HSV → RGB 鲜艳颜色
                            int c = RandomVividColor();
                            bullet.colorIn = c;
                            bullet.colorOut = c;
                        }
                    }
                }
                _lastBulletCount = cur;
            }
            catch { }
        }

        private static int RandomVividColor()
        {
            // HSV hue 随机，饱和度和明度拉满 → 鲜艳
            double hue = System.Random.Shared.NextDouble() * 360.0;
            double s = 0.9;
            double v = 1.0;
            int hi = (int)(hue / 60.0) % 6;
            double f = hue / 60.0 - System.Math.Floor(hue / 60.0);
            double p = v * (1.0 - s);
            double q = v * (1.0 - f * s);
            double t = v * (1.0 - (1.0 - f) * s);
            double r, g, b;
            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return ((int)(r * 255) << 16) | ((int)(g * 255) << 8) | (int)(b * 255);
        }
    }
}
