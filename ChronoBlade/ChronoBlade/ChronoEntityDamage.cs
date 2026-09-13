using dc;
using dc.tool.atk;

namespace ChronoBlade
{
    /// <summary>
    /// 挂在 `Entity.onDamage(AttackData a)` 上的 Hook 委托声明。
    ///
    /// 这是 MCDcrit 模组验证过的"命中反馈"挂点族（它挂的是 Fx.critical）：
    /// 每次伤害结算都会走到 Entity.onDamage，因此比 Katana.hitFromWeapon /
    /// tryHitDash / AttackUtils.hit 都可靠 —— 后三者在实测里都不触发。
    ///
    /// 原函数签名：public override void onDamage(AttackData a)
    /// </summary>
    public static class ChronoEntityDamage
    {
        public delegate void orig_Entity_onDamage(Entity self, AttackData a);
    }
}
