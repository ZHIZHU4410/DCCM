# InfiniteBowOverhaul — 无尽弓 · 环绕箭

把 **Arc et carquois sans fond（InfiniteBow / 无尽弓）** 改成
"**射出的箭一直绕着英雄转，打到敌人才消失**"，同时攻速 ×3、任意箭都能暴击、无视墙体。

## 一、原版机制（反编译 + 游戏资源双向确认）

`res/data.cdb`：

* `item`：`InfiniteBow`，**没有 `commonProps.ammo`**（天生无限弹药），
  游戏内描述原文就是 "Munitions illimitées. Le dernier tir inflige un coup critique."
* `weapon.strikeChain` 三段，`canCrit` 依次是 **false / false / true**
  —— 也就是说**只有第三段（最后一射）能暴击**。

`GamePseudocode/dc.tool.weap.bow/InfiniteBow.cs`：

```csharp
public override void shoot(ArrayObj bulletsOut)
{
    base.shoot(bulletsOut);                   // BaseBow 的通用造箭流程
    consumeAmmo();
    AttackData atk = _AttackUtils.createFromHeroWeapon(this, null);
    atk.setTag(2, get_curSkillInf().canCrit);  // 暴击 tag 只在最后一射为 true
    Bullet bullet = new Bullet(owner, atk, getBowTargetAng(11.0), 1.2, "smallArrow");
    bullet.maxDist = 11.0 * 24.0;              // 264px 后自动消失
    bullet.onBulletHit = ArrowFunction_shoot_36401;   // MobiusString 词条
}
```

`Bullet` 自带我们需要的两个字段：`ignoreWalls`（无视墙体）、`atk`（识别箭是谁射的）。

**环绕半径的依据**：查询 `res/data.cdb` 的 `DamageAura` 行，`props.distance = 5`（格）。
"比它再大一倍" → **10 格 = 240px**，这是默认值。

## 二、改了什么

| 需求 | 做法 |
|---|---|
| **一次射 10 支箭** | `Hook_InfiniteBow.shoot`：**直接连调 N 次原版 `shoot`**。每次都会完整走「造箭 → init → enableHeroFalling → shootFromWeapon → push(bulletsOut)」，箭才真正进入关卡实体表。（手工 `new Bullet` 造出来的箭不会进实体表，表现就是"只有一支"） |
| **每支独立轨迹** | 环绕逻辑用 `bullet.__uid` 做散列，每支箭拿到自己的轨道半径 / 角速度 / 方向 / 二级摆动，互不干扰 |
| **箭矢在范围内乱窜** | `Hook_Bullet.fixedUpdate` 里接管位置：主轨道（半径每支箭不同）+ 二级小圆叠加，再夹到最大半径内 |
| **过场动画不被清掉** | 原版 `_GameCinematic` 会遍历子弹表，对 `destroyOnCinematic == true` 的 `bullet.destroy()`。所以给环绕箭设 `destroyOnCinematic = false`，并把 `hasGravity = false` / `endGravity = 0` —— 就算过场里某几帧没人接管位置，箭也不会往下掉 |
| **轨迹随机、范围固定** | **不是**所有箭贴外圈匀速转。每支箭用自己的 `uid` 散列出：<br>· 轨道半径 = `[InnerRadiusFrac, 1] × 最大半径`（默认 30%~100%）<br>· 主轨道角速度（区间内随机，正/反转随机）<br>· 二级小圆（幅度 + 非整数倍频率 + 相位）<br>两个频率不成整数比 → **轨迹不闭合**，看起来像电子云在范围里乱窜；最后把向量长度夹到最大半径，保证不越界 |
| **打到敌人才消失** | `bullet.maxDist` 拉到 1e6（永不过期）+ 速度清零；命中仍走原版逻辑（`pierceCount = 0` → 打中即消失） |
| **攻速 ×3** | `Hook_Weapon.get_attackSpeed`：引擎里 `WeaponSkill.getCastSpeed() = base.getCastSpeed() * weapon.get_attackSpeed()` |
| **任意箭可暴击** | `Hook__AttackUtils.hit`：命中时打暴击 tag `2` 并设 `critMul`；用 `hasTag(2)` 一次性判断，避免同一份 AttackData 被反复乘 |
| **无视墙体** | 在 `orig` **之前**设 `bullet.ignoreWalls = true`（`orig` 第一件事就是查墙） |

只对 `bullet.atk.sourceWeapon is InfiniteBow` 的箭生效，别的弓/子弹完全不受影响。

## 三、配置

`coremod/config/InfiniteBowOverhaul.json`（首次运行自动生成），或游戏内
**选项 → 模组 → 无尽弓·环绕箭**：

| 项 | 默认 | 说明 |
|---|---|---|
| `EnableMod` | `true` | 总开关 |
| `ArrowCount` | `10` | 每次箭数（1~30），各箭独立轨迹 |
| `OrbitRadiusCells` | `10.0` | **最大**半径（格）= DamageAura 的 5 格 × 2 |
| `InnerRadiusFrac` | `0.30` | 内侧占比：各箭轨道半径在 30%~100% 最大半径之间随机 |
| `WobbleFrac` | `0.18` | 乱窜幅度（二级小圆）；0 = 纯圆轨道 |
| `SpinMin` / `SpinMax` | `1.2` / `2.8` | 主轨道角速度区间（弧度/秒） |
| `MaxOrbitingBullets` | `80` | 同时环绕上限，超出的按原版飞出去（防掉帧） |
| `KeepAlive` | `true` | 箭矢不因射程消失 |
| `IgnoreWalls` | `true` | 穿墙 |
| `SurviveCinematic` | `true` | 跨过场保留（不被清场、不掉落） |
| `AttackSpeedMul` | `3.0` | 攻速倍率 |
| `CanCrit` | `true` | 任意箭暴击 |
| `CritMul` | `1.5` | 暴击倍率 |

> 觉得箭太密：`MaxOrbitingBullets` 降到 40，或把 `AttackSpeedMul` 调回 2。
> 想要"更疯"的乱窜：`WobbleFrac` 调到 0.35~0.5。
> 想让箭更靠里：`InnerRadiusFrac` 调到 0.5（轨道更集中在中心区域）。
> 觉得圈太大/太小：`OrbitRadiusCells` 直接改（10 格 ≈ 半个屏幕多）。

## 四、构建 / 安装

```powershell
cd InfiniteBowOverhaul\InfiniteBowOverhaul
dotnet build -c Debug          # AutoInstallMod=true，自动装到 coremod\mods\InfiniteBowOverhaul
```

装好后 **重启游戏** 才会加载。

## 五、边界说明

* 只依赖 DCCM 框架，不改游戏资源。
* 环绕箭会一直存在直到命中敌人，所以**射空太多时屏幕上会积很多箭** ——
  上限与半径都可调；实在太多可以把 `MaxOrbitingBullets` 压到 30。
