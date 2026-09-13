using dc.tool;

namespace ChronoBlade
{
    /// <summary>
    /// 挂在基类 tool.Weapon.onExecute 上的 Hook 委托声明。
    ///
    /// DCCM 的约定：委托签名必须与目标函数**完全一致**，第一个参数在处理器里替换为
    /// 这个委托类型的 orig。原函数是 `public bool onExecute()`（实例方法），
    /// 因此委托是 `bool orig_Weapon_onExecute(Weapon self)`。
    ///
    /// ⚠️ 绝不能用 object/Delegate 糊 orig，否则 HashlinkHookManager.CreateAdaptDelegate
    /// 会抛 MissingMethodException: Method 'System.Object.Invoke' not found 并崩游戏。
    /// </summary>
    public static class ChronoWeaponExecute
    {
        public delegate bool orig_Weapon_onExecute(Weapon self);
    }
}
