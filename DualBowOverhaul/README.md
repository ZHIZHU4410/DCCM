# DualBowOverhaul — 多槽弓 · 十箭追踪

把 **Arc à multiples encoches（DualBow / 多槽弓）** 改成
"**每次射出 10 支箭、必定可暴击、无视墙体、自动追踪敌人**"。

## 一、原版机制（反编译 + 游戏资源双向确认）

`res/data.cdb`：

```json
"item": {
  "id": "DualBow",
  "props": { "range": 9, "count": 3 },
  "commonProps": { "ammo": 21 },
  "tags": [ "Ranged", "NoCritical", "HasBullets", "LimitedAmmo", ... ]
}
```

关键点：

* **`props.count = 3`** → 每次射 3 支箭；
* **`NoCritical`** 标签 → 这把弓**天生不能暴击**；
* **`ammo = 21`** → 只有 21 发弹药。

`GamePseudocode/dc.tool.weap.bow/DualBow.cs` 的 `shoot()`：

```csharp
int num2 = item._itemData.props.count;
if (item.hasAffix("DoubleBullets")) num2 *= 2;   // 词条翻倍
AttackData atk = createFromHeroWeapon(this, null);
atk.overrideBaseDamage(power / num2);            // 总伤害摊到每支箭
for (i in 0..num2) {
    if (!consumeAmmo()) break;
    Bullet bullet = new Bullet(owner, atk, ang, speed ≈ 1.0, "smallArrow");
    bullet.shootFromWeapon(this, ...);
}
```

> 注意：**所有箭共享同一份 `AttackData`**，且单支伤害 = 总伤害 / 箭数。
> 所以改成 10 箭不会让总伤害翻 3 倍，只是把伤害摊到 10 个可以各自命中的箭上。

`Bullet` 自带两个我们需要的字段：

```csharp
public extern bool ignoreWalls { get; set; }   // fixedUpdate 里 `if (!ignoreWalls)` 才查墙
public extern AttackData atk { get; set; }     // 可以据此认出"这是多槽弓的箭"
```

## 二、改了什么

| 需求 | 做法 |
|---|---|
| **每次 10 支箭** | `Hook_DualBow.shoot`：射之前把 `item._itemData.props.count` 临时改成目标值，`orig` 之后还原。带 `DoubleBullets` 词条时原版会 ×2，所以基础值取一半，保证**最终恰好 10** |
| **可以暴击** | `Hook__AttackUtils.hit`：命中时给这份 `AttackData` 打上暴击 tag `2`，并把 `critMul` 设为配置值。用 `hasTag(2)` 做一次性判断 —— 因为 10 支箭共享同一份 AttackData，不加判断会把倍率反复乘爆 |
| **无视墙体** | `Hook_Bullet.fixedUpdate`：在 `orig` **之前**把 `bullet.ignoreWalls = true`（`orig` 第一件事就是查墙，晚设置会晚一帧） |
| **追踪敌人** | 同一个钩子：用 `Team.opponentsIterator` 找最近的、活着的、`canBeHit()` 的敌对目标，把箭的速度向量 `(dx, dy)` 改指向它，速度取配置值（默认 1.0 格/帧，约等于原版箭速）。**瞄准点是身体中心**：`Entity` 的锚点是脚底中心，中心 Y = `cy+yr - hei/2`（原版 `Fx.dash()` 用的就是这个式子），X 本身就是中心 |
| **无限弹药** | `Hook_Weapon.consumeAmmo`：多槽弓直接返回 true 不扣弹药（可关） |
| **攻速 ×3** | `Hook_Weapon.get_attackSpeed`：引擎里攻速的唯一乘数就是 `WeaponSkill.getCastSpeed() = base.getCastSpeed() * weapon.get_attackSpeed()`，在这里放大 3 倍，蓄力与动画会一起变快 |

只对**多槽弓射出的箭**生效：`bullet.atk.sourceWeapon is DualBow`，其它弓箭/子弹完全不受影响。

## 三、配置

`coremod/config/DualBowOverhaul.json`（首次运行自动生成），或游戏内
**选项 → 模组 → 多槽弓·十箭追踪**：

| 项 | 默认 | 说明 |
|---|---|---|
| `EnableMod` | `true` | 总开关 |
| `ArrowCount` | `10` | 每次箭数（1~30） |
| `InfiniteAmmo` | `true` | 不消耗弹药 |
| `CanCrit` | `true` | 允许暴击 |
| `CritMul` | `1.5` | 暴击倍率 |
| `IgnoreWalls` | `true` | 穿墙 |
| `Homing` | `true` | 追踪 |
| `HomingSpeed` | `1.0` | 箭速（格/帧） |
| `AimAtCenter` | `true` | 瞄准身体中心（关掉 = 脚底） |
| `AttackSpeedMul` | `3.0` | **攻速倍率**（1.0 = 原版） |

> 追踪太快觉得不真实：`HomingSpeed` 调到 0.7~0.8。
> 箭太多看不清：`ArrowCount` 调到 5。

## 四、构建 / 安装

```powershell
cd DualBowOverhaul\DualBowOverhaul
dotnet build -c Debug          # AutoInstallMod=true，自动装到 coremod\mods\DualBowOverhaul
```

装好后 **重启游戏** 才会加载。

## 五、边界说明

* 只依赖 DCCM 框架，不改游戏资源（不需要 res.pak / CDB 补丁）。
* 第一次射击时如果 `item` 数据还没被引擎加载，那一发会按原版 3 箭射出，
  之后恢复正常（日志里会提示一次）。
* 追踪只在有合法目标时改向；没有目标时保持原方向飞行。
