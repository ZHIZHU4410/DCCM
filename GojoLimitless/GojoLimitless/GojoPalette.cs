#nullable disable
namespace GojoLimitless
{
    /// <summary>
    /// 咒术配色（0xRRGGBB 整数，直接喂给原版 FX / 灯光的 <c>setColor(int)</c>）。
    /// </summary>
    public static class GojoPalette
    {
        /// <summary>六眼·亮青白。</summary>
        public const int SixEyes = 0xAEF4FF;

        /// <summary>无下限·青蓝（光圈主色）。</summary>
        public const int Infinity = 0x2FE0FF;

        /// <summary>术式顺转「苍」。</summary>
        public const int Blue = 0x1560FF;

        /// <summary>术式反转「赤」。</summary>
        public const int Red = 0xFF3A22;

        /// <summary>虚式「茈」。</summary>
        public const int Purple = 0xB14CFF;

        /// <summary>领域展开「无量空处」。</summary>
        public const int Void = 0x6A4CFF;
    }
}
