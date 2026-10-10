# HeavyBowOverhaul —— 镀金和弓强化（箭 ×20 大 / 判定 ×20 / 射程 ×3 / 伤害 ×5 / 无视墙体）

给 **HeavyBow**（镀金和弓 / Gilded Yumi，DLC「女王与海」）的强化：

| # | 效果 | 实现方式 |
|---|---|---|
| ① | 射出的箭**模型放大 20 倍** | 运行时改 `Bullet.sprScaleX/sprScaleY` |
| ② | **撞击识别放大 20 倍** | 放大 `Bullet.radius` + 每帧补一次按半径的命中扫描 |
| ③ | **飞行距离 ×3**（25 格 → 75 格） | 运行时 `Bullet.maxDist *= 3` |
| ④ | 箭的**伤害 ×5** | res.pak 数据补丁：`weapon/HeavyBow` 的 `strikeChain[*].power` 45 → 225 |
| ⑤ | 箭**无视墙体** | 运行时把 `Bullet.ignoreWalls = true` |

---

## 1. 原版这支箭是怎么造出来的

`dc.tool.weap.bow.HeavyBow.shoot(bulletsOut)`（`GamePseudocode/dc.tool.weap.bow/HeavyBow.cs:3339`）：

```csharp
AttackData a = _AttackUtils.createFromHeroWeapon(this, null);   // baseDmg = curSkillInf.power
Bullet bullet = new Bullet(hero, a, ang, speed, "smallArrow");
bullet.init();                          // → Entity.init() → initGfx()
bullet.shootFromWeapon(this, ...);
bullet.maxDist = itemInf.props.range * 24;      // range 25 格 → 600px
bullet.pierceCount += 99999;                    // 原版就是无限穿透
spr.groupName = "heavyArrow";                   // 换成大箭贴图
spr.scaleX *= 0.5;  spr.scaleY *= 0.5;          // 原版自己又缩了一半
```

对应到三项改动：

* **伤害** 就在 `strikeChain[*].power` 里（`createFromHeroWeapon` 取的就是它）
  → 走数据补丁最干净，顺带物品卡上的「每秒伤害」也会跟着 ×5；
* **模型大小** 只能在运行时改 sprite，而且必须等 `shoot()` **整个跑完**
  （原版那句 `*= 0.5` 排在后面），所以 Hook 的形态是「包住 `orig`，返回后再改」；
* **无视墙体** 就是原版自带的 `Bullet.ignoreWalls`：
  `dc.en.Bullet.fixedUpdate` 里是

  ```csharp
  if (!ignoreWalls) { ...读 map.collisions 做地图碰撞... }
  ```

  整块跳过，`onHitWall()` 也就不会被调用（不会插墙、不会掉弹）。原版潜行者的
  `Homing` / `Javelin` / `Saw` 这些穿墙弹就是这么设的。

### ⚠️ 踩坑二：原版的撞击判定只认「一格」

`Bullet.onStep()`（`GamePseudocode/dc.en/Bullet.cs:3419`）里命中判定是这样的：

```csharp
ArrayObj list = base._level.listCurrentQuadElements;      // 当前 quad 里的实体
...
if (entity.cx == base.cx && entity.cy == base.cy)         // ← 要求"同一格"（24px）
    onTouchValidTarget(entity);
```

也就是**只有箭所在的那一格里的敌人会被打到**。模型放大 20 倍后，视觉上箭早就盖住半屏了，
判定却还是那一格 —— 所以要把判定一起放大。做法两步：

1. `bullet.radius *= HitRadiusMult`（把实体自身的碰撞半径也放大，其它读半径的系统跟着一致）；
2. Hook `Bullet.fixedUpdate`，每帧给强化过的箭补一次**按半径**的扫描：

```csharp
foreach (enemy in bullet._team 的敌对队伍)
    if (dist(bullet, enemy) <= bullet.radius + enemy.radius)
        bullet.onTouch(enemy);        // ← 原版的完整命中管线
```

用 `onTouch()` 而不是直接 `onTouchValidTarget()`，是因为它自带
per-entity 冷却 → `canHit` 回调 → `ignoreTrashMobs` → 命中这一整套，
所以同一帧重复调用同一个敌人不会重复结算。

扫描放在 `fixedUpdate` 的 `orig` **之后**：这时本帧移动已经做完，用的就是画面上箭的位置。
半径（默认 20× ≈ 200px+）远大于每帧位移，不会漏。

### ⚠️ 踩坑一：只改 `spr.scaleX` 只有一帧有效

`Entity.postUpdate()`（`GamePseudocode/dc/Entity.cs:13391`）**每帧**都会重算 sprite 的缩放：

```csharp
hSprite.scaleX = sprScaleX * dir;
hSprite.scaleY = sprScaleY;
```

所以 `spr.scaleX/scaleY` 只是"结果"，真正说了算的是实体上的 **`sprScaleX` / `sprScaleY`**。
第一版只改了 `spr.scaleX *= 20`（日志里能看到 `当前 10`），表现就是
**出膛那一帧是大的，下一帧立刻被刷回原版大小**。

正确写法：

```csharp
bullet.sprScaleX *= scale;      // ← 母字段
bullet.sprScaleY *= scale;
bullet.spr.scaleX = bullet.sprScaleX * bullet.dir;   // 顺手同步一次，让当帧就是大的
bullet.spr.scaleY = bullet.sprScaleY;
```

## 2. 怎么拿到那支箭

`shoot()` 不返回 bullet，单发时 `bulletsOut` 还是 `null`，所以拿不到返回值。
但 `bullet.init()` 一定会走 `Entity.init() → initGfx()`，于是：

```csharp
Hook_HeavyBow.shoot   : 进入前 _inHeavyBowShoot = true; orig(...); 结束后再改箭
Hook_Bullet.initGfx   : 若 _inHeavyBowShoot 则把这一发 bullet 记下来
```

Hook 挂载点（都已用 Mono.Cecil 核对过签名）：

```
Hook_HeavyBow.shoot        orig_shoot(HeavyBow self, ArrayObj bulletsOut) -> Void
Hook_Bullet.initGfx        orig_initGfx(Bullet self) -> Void
```

## 3. 文件

```
HeavyBowOverhaul/
├── patch_heavybow_cdb.py               # Python：power ×5 -> HeavyBowOverhaul/data.cdb
├── README.md
└── HeavyBowOverhaul/                   # csproj 目录
    ├── HeavyBowOverhaul.csproj         # MDK：cdb diff -> Assets -> res.pak -> 自动安装
    ├── HeavyBowOverhaulMain.cs         # ModBase + 两个 Hook + res.pak 挂载 + 配置
    ├── data.cdb                        # 脚本生成的修改版 CDB（构建输入）
    └── Assets/data.cdb_/weapon/HeavyBow.json   # 差异补丁（打进 res.pak）
```

## 4. 数据补丁内容

`weapon` 表 / `HeavyBow`（两段 `strikeChain` 都改）：

| 字段 | 原版 | 现在 |
| --- | --- | --- |
| `strikeChain[0].power` | `[45]` | `[225]` |
| `strikeChain[1].power` | `[45]` | `[225]` |

其余（`speed` / `range` / `pct` / `power2` / `bump` / `critMul` / 特效 / 音效）全部保持原版。

## 5. 配置

`coremod/config/HeavyBowOverhaul.json`（首次运行自动生成）：

| 字段 | 默认 | 作用 |
| --- | --- | --- |
| `EnableMod` | `true` | 总开关（关掉就不再放大 / 不再穿墙） |
| `ArrowScale` | `20.0` | 箭模型放大倍数（1.0 = 原版） |
| `HitRadiusMult` | `20.0` | 撞击识别半径倍数（`<= 1` = 原版"只认一格"的判定） |
| `FlightDistanceMult` | `3.0` | 飞行距离倍数（原版 25 格 → 75 格） |
| `IgnoreWalls` | `true` | 箭是否无视墙体 |

> 伤害倍率在 `patch_heavybow_cdb.py` 顶部的 `DAMAGE_MULT`，改完重跑脚本 + `dotnet build` 即可。

日志里每射一箭会打一行，可以直接核对生效值：

```
[HeavyBowOverhaul] 箭已强化：sprScaleX/Y=20/20（sprite=20）、判定半径=240、maxDist=1800、ignoreWalls=True
```

## 6. 构建

```powershell
# 改过伤害倍率才需要重跑
python HeavyBowOverhaul/patch_heavybow_cdb.py

dotnet build HeavyBowOverhaul/HeavyBowOverhaul/HeavyBowOverhaul.csproj -c Debug
```

## 7. 注意

* **`res.pak` 必须挂载**：MDK 只把它复制到 `coremod/mods/HeavyBowOverhaul/`，
  DCCM 不会自动挂载，所以 `IOnAfterLoadingAssets` 里手动 `loadPak`。
  日志里应出现 `[HeavyBowOverhaul] res.pak 已挂载，伤害补丁生效`；
  没有这行就说明伤害不会 ×5。
* 只有 **HeavyBow** 的箭会被改（靠 `HeavyBow.shoot` 的作用域识别），
  其它弓、其它子弹不受影响。
* 箭变大 20 倍后视觉上会非常夸张，但**判定范围没变**（只改了 sprite 的 scale）。
  想让判定也跟着变，可以顺手改 `Bullet.radius`。
* `GamePseudocode/`、`res/` 全程只读，未做任何修改。
