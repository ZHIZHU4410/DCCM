# ThrowingKnifeOverhaul —— 飞刀强化

> 目标武器：**ThrowingKnife**（飞刀 / Couteau de lancer）
> 对应需求：**飞刀无视墙体 · 攻速翻三倍 · 可以锁定追击敌人**

---

## 1. 效果一览

| 功能 | 实现位置 | 具体改动 |
|---|---|---|
| **飞刀无视墙体** | C#：`Hook_Bullet.fixedUpdate`（pre-orig）+ `Hook_Bullet.blockOnCollision` / `onHitWall` 短路 | 飞刀置 `ignoreWalls = true` / `ignoreOneWays = true`，并跳过撞墙/撞地处理，直穿地形 |
| **攻速翻三倍** | C#：`Hook_Weapon.prepare` + 武器技能 `cooldownSpeedMul` | `attackSpeed *= 3`（蓄力/出招动画）+ 冷却速度 ×3（原版 0.25s 的武器冷却 ÷3） |
| **锁定追击敌人** | C#：`KnifeHoming.Steer`（挂在 `Hook_Bullet.fixedUpdate`） | 每帧找最近的怪（**不调 `filterBySight`，隔墙也能锁**），把速度矢量朝它限速转向、速率不变 |
| 追击射程 ×2.5（配套） | C#：`Bullet.maxDist` | 原版只飞 **5.5 格**，不放大根本追不到人 |
| 无限飞刀（配套） | C#：`Hook_Weapon.prepare` 前置补弹 | 原版只有 **8 发**（`commonProps.ammo = 8` + `LimitedAmmo`），配 ×3 攻速会一瞬打空 |

**为什么"攻速"要动两个地方**（只动一处会打不满）：

```csharp
// GamePseudocode/dc.tool.skill/OldSkill.cs
// ① 蓄力/出招：吃 castSpeed（= base.getCastSpeed() * weapon.get_attackSpeed()）
chargeF -= tmod * getCastSpeed();
// ② 武器冷却：吃 getCooldownSpeed()，而它【只乘 cooldownSpeedMul，不含 attackSpeed】
coolDownF -= tmod * getCooldownSpeed();   // ← 第 1149 行
```

所以：`Weapon.prepare(attackSpeed)` ×3 只压掉了 `charge 0.1s` 和动画；
真正卡射速的是 `coolDown 0.25s`（不放大 → 最多 4 发/秒），必须把技能的
`cooldownSpeedMul` 也 ×3。两处都做，射速才是整体的 3 倍。

---

## 2. "无视墙体"为什么要两条路

`ignoreWalls` 只影响 `Bullet.fixedUpdate()` 里那一段格子判定。而：

```csharp
// GamePseudocode/dc.en/Bullet.cs:3952
public override void onTouchGround()
{
    base.onTouchGround();
    blockOnCollision();      // ← 无条件！→ onHitWall() → block(false) → destroy()
}
```

所以光设 `ignoreWalls = true`，飞刀一旦落在地面上照样会被打掉。本模组同时把
`blockOnCollision` / `onHitWall` 对飞刀短路掉（`WallPierce` 开关关掉就恢复原版）。

**命中敌人不受影响** —— 那条走的是 `onTouchValidTarget`，是另一条路：
飞刀照样会命中敌人并在命中后停下/掉落（`.pierceCount` 保持原版）。

---

## 3. "锁定追击"怎么做的

原版飞刀其实**只在出手那一刻瞄一次**：`ThrowingKnife.onExecute` 用 `TargetHelper`
选目标 → 把 `atan2(目标 - 出手点)` 塞进 `new Bullet(hero, atk, ang, 1.2, "smallArrow")`，
之后就是直线飞；而且那套选目标里有 `filterBySight(...)`，**墙后的敌人会被排除**。

本模组在飞行中每帧做一次修正（与游戏自带 `HomingSoul.fixedUpdate` 同思路）：

```
cur  = atan2(dy + bdy, dx + bdx)
want = atan2(目标中心y - 自己中心y, 目标中心x - 自己中心x)   // 中心都减 hei*0.5
turn = normalizeAngle(want - cur) 并夹到 ±(可调上限，默认 20°/帧)
dx = cos(cur + turn) * speed
dy = sin(cur + turn) * speed        // 速率不变，只改方向
```

* 目标用游戏自己的 `_team.get_targetHelper()`（和 BleedAxeOverhaul / LanternOverhaul 同一套），
  **刻意不调 `filterBySight`** → 隔墙也能锁；
* 子弹的位移就是靠 `Entity.dx/dy` 每帧积分出来的（`shootFromWeapon` 也是读 `dx/dy` 算角度），
  所以改这两个字段是真的改航向，不是只改贴图朝向。

---

## 4. 目录结构（纯运行时代码，不需要 CDB 补丁 / res.pak）

```
ThrowingKnifeOverhaul/
├── 构建.bat                          # 一键 dotnet build（含自动安装）
├── README.md                         # 本文件
└── ThrowingKnifeOverhaul/
    ├── ThrowingKnifeOverhaul.csproj  # ModType/ModName/ModMain + AutoInstallMod
    ├── ThrowingKnifeOverhaulMain.cs  # 主入口：5 个钩子 + 弹药/攻速 + 选项菜单
    ├── KnifeHoming.cs                # 锁敌 + 限速转向（纯逻辑）
    ├── ThrowingKnifeConfig.cs        # 持久化配置 Config<KnifeConfig> + 按键名解析
    └── ThrowingKnifeFeatures.cs      # 功能开关总控（总开关 / 热键 / 菜单文案）
```

> 和前一个模组（AdeleScytheOverhaul）不同：这次的改动全都能在运行时完成，
> 所以**没有** `data.cdb` / `Assets/` / `res.pak`，`GenerateSinglePakFile=false`。
> 好处是模组只有几十 KB、也不需要动任何游戏数据。

---

## 5. 构建 / 安装

```powershell
cd "…\coremod\DCCMDEAD CELLS\ThrowingKnifeOverhaul"
dotnet build -c Debug .\ThrowingKnifeOverhaul\ThrowingKnifeOverhaul.csproj
```

或双击 `构建.bat`。产物安装到 `coremod/mods/ThrowingKnifeOverhaul/`
（`ThrowingKnifeOverhaul.dll` + `modinfo.json`）。

只想要产物、不想装进 `coremod/mods/`：加 `-p:AutoInstallMod=false`，
结果在 `ThrowingKnifeOverhaul/bin/Debug/net10.0/output/ThrowingKnifeOverhaul/`。

> 装的时候如果报 `MSB3021/MSB3027 文件被 DeadCellsModding 锁定`，那是**游戏开着**，
> 关掉游戏再 build 即可（不是代码问题）。

---

## 6. 游戏内开关

「选项 → 模组 → **飞刀强化**」：

| 开关 | 默认 | 说明 |
|---|---|---|
| 总开关 | 开 | 关掉 = 全部恢复原版 |
| 飞刀无视墙体 | 开 | 见第 2 节 |
| 攻速 ×3 | 开 | 见第 1 节 |
| 锁定追击敌人 | 开 | 每帧朝最近的怪限速转向，隔墙也能锁 |
| 追击射程 ×2.5 | 开 | `maxDist` 5.5 格 → 13.75 格 |
| 无限飞刀 | 开 | 每次投掷前把弹药补满（原版 8 发） |

| 滑条 | 范围 | 默认 | 说明 |
|---|---|---|---|
| 攻速倍率 | 1.0 – 20.0 | 3.0 | 出招动画 + 武器冷却一起变快 |
| 追击转向(°/帧) | 5 – 90 | 20 | 越大越"贴脸"，20°/帧 ≈ 9 帧转 180° |
| 追击射程倍率 | 1.0 – 10.0 | 2.5 | 乘在 `Bullet.maxDist` 上 |

* 数值类改动**下一次投掷 / 下一枚飞刀**就生效（攻速在 `Weapon.prepare`，射程按每枚子弹记状态）。
* 每个开关都能配热键（配置里 `KeyToggleXxx`，默认空 = 不绑键）。
* 配置文件：`coremod/config/ThrowingKnifeOverhaul.json`。

---

## 7. 已知边界 / 取舍

1. **飞刀命中敌人后仍然会停**（原版 `pierceCount = 0` 的行为）。想要"一路穿透所有怪"可以再加个
   `pierceCount = int.MaxValue`，但那会超出"无视墙体"的需求范围，所以默认没动。
2. **穿墙后弹药掉落会变少**：原版飞刀撞墙/落地会掉落可拾取的刀，现在刀会穿地形飞到 `maxDist`
   才回收。默认开了「无限飞刀」，所以不影响手感；把那个开关关掉后请留意弹药。
3. **识别方式**：靠 `Bullet.atk.sourceWeapon is ThrowingKnife` 认领，**认到一次就记进字典**
   （key = `RuntimeHelpers.GetHashCode`），子弹 `dispose` 时移除。不每帧查 `atk` 是因为
   `AttackData` 是对象池（GamePseudocode/dc.tool.atk/AttackData.cs:126 会 `sourceWeapon = null`），
   飞在半路的子弹可能"失去身份"。
4. **只影响飞刀的子弹**：所有 `atk.sourceWeapon` 不是 `ThrowingKnife` 的子弹完全不受影响
   （包括别的投掷武器、弓、法杖、怪物子弹 —— 钩子里第一件事就是排除）。
5. **索敌只看"最近的怪"**：没有做 Boss/精英权重（飞刀是一次性弹丸，不需要）。
   范围内没怪时保持原航向，所以空挥的感受和原版一致。
6. **数值常量与 v35 对应**：原版 `charge 0.1s` / `coolDown 0.25s` / `maxDist 5.5 格` /
   `ammo 8` 都来自 v35 的 `data.cdb` 与 `ThrowingKnife.onExecute`；换游戏版本请核对一次。
