#nullable disable

using dc.libs.heaps.slib;

namespace AdeleScytheOverhaul
{
    /// <summary>
    /// 把"最后一击的刀光"放大 N 倍，同时保证**视觉上仍然以人物为中心**。
    ///
    /// 为什么光设 scaleX/scaleY 会把特效顶到人物上方：
    ///   <c>HSprite.sync()</c>（GamePseudocode/dc.libs.heaps.slib/HSprite.cs:436-443）里
    ///   <c>usingFactor</c> 分支算的是
    ///       rawTile.dx = -(frameData.realWid * pivot.centerFactorX + frameData.realX)
    ///       rawTile.dy = -(frameData.realHei * pivot.centerFactorY + frameData.realY)
    ///   也就是说 **pivot 的 0.5 是相对"未裁剪的原始画布"(realWid × realHei) 算的**，
    ///   而美术实际画在画布里的位置是 (realX, realY) + (wid × hei)。
    ///   武器 FX 的 pivot 被 <c>Fx.playWeaponFx</c> 设成 (0.5, 0.5)，
    ///   精灵又挂在英雄精灵的局部原点上（英雄精灵原点是**脚底**），
    ///   于是"画布中心"落在脚底，而刀光在画布里偏上偏后 —— 一放大就按倍率往人物上方/身后窜，
    ///   看起来就是"人物跑到特效下半部的中心"。
    ///
    /// 渲染侧确认（dc.h2d/RenderContext.cs:820-827）：绘制用的中心就是
    /// <c>tile.dx + tile.width * 0.5</c>，再乘上对象矩阵 matA/matD（即 scaleX/scaleY），
    /// 所以"缩放会把 tile.dx/dy 一起放大"这一点是确定的。
    ///
    /// 解决：放大之后把精灵整体平移，抵消掉缩放带来的位移：
    ///   裁剪后的**包围盒中心**在精灵局部坐标里（缩放前）是
    ///       bx = wid / 2 - realX - realWid  * centerFactorX
    ///       by = hei / 2 - realY - realHei * centerFactorY
    ///   放大 k 倍后它会被拉到 (k*bx, k*by)，所以要补
    ///       sprite.x = (1 - k) * bx + 手工偏移X
    ///       sprite.y = (1 - k) * by + 手工偏移Y
    ///   这样动画自身的位移（每帧包围盒中心的变化）被原样保留，只有"整体放大"被补偿掉；
    ///   而"把**视觉**中心（月牙的 alpha 质心，不是包围盒中心）挪到人物身上"这件事
    ///   由那两个常量偏移完成 —— 默认值是从 res/atlas/fxWeapon.atlas + png 实测的
    ///   （见 AdeleConfig.FxOffsetX/FxOffsetY 的注释）。
    ///
    /// frameData 每帧都在变（动画本身在动），所以本方法必须**逐帧**调用，
    /// 用当帧的 frameData 重算；否则动画自身的位移会被压平、看着像贴纸。
    /// </summary>
    internal static class AdeleFxAnchor
    {
        /// <summary>
        /// 设置刀光的缩放与位置。
        /// <paramref name="scale"/> = 倍率；<paramref name="offsetX"/>/<paramref name="offsetY"/> = 手工微调（像素）。
        /// </summary>
        public static void Apply(HSprite spr, double scale, double offsetX, double offsetY)
        {
            if (spr == null) return;

            try
            {
                double k = scale <= 0.0 ? 1.0 : scale;
                double cx = 0.0;
                double cy = 0.0;

                FrameData f = spr.frameData;
                if (f != null)
                {
                    SpritePivot p = spr.pivot;
                    double cfx = 0.0;
                    double cfy = 0.0;
                    if (p != null && p.usingFactor && !p.isUndefined)
                    {
                        cfx = p.centerFactorX;
                        cfy = p.centerFactorY;
                    }

                    // 裁剪后的可见像素中心（精灵局部坐标）
                    cx = f.wid / 2.0 - f.realX - f.realWid * cfx;
                    cy = f.hei / 2.0 - f.realY - f.realHei * cfy;
                }

                spr.scaleX = k;
                spr.scaleY = k;
                // 放大是绕"精灵原点"做的，所以补 (1-k) 倍的可见中心，把它拉回原位
                spr.x = (1.0 - k) * cx + offsetX;
                spr.y = (1.0 - k) * cy + offsetY;
                spr.posChanged = true;
            }
            catch
            {
                // 纯视觉功能：任何异常都不该影响战斗逻辑
            }
        }
    }
}
