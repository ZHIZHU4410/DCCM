# Sukuna · 宿傩斩击（DCCM 模组）

> 以**原版女王细剑（Queen's Rapier）的斩击机制**为骨架，实现《咒术回战》宿傩的三招：
> **解 / 捌 / 伏魔御厨子（领域展开）**。
> 目录格式与构建方式完全仿照 `DamageAuraBoost`：`Assets`（res.pak 数据补丁）+ `cs`（C# 逻辑）。

---

## 1. 原版 QueenRapier 斩击机制（读自 `GamePseudocode/dc.tool.weap/QueenRapier.cs`）

| 环节 | 原版行为 |
| --- | --- |
| `onExecute()` | 挥剑命中 `canHit()` 的敌人 → `hitFromWeapon(e)`，然后 `queenStrikeTrigger()` |
| `hitFromWeapon(e)` | 普通武器命中 + 延迟 `item.props.duration` 秒后对同一目标调 `queenStrike(e, e中心X, e中心Y, getStrikeAngle(cycle))` |
| `queenStrike(t,x,y,a)` | 目标无效 / 在 0.1 秒冷却（key `1390411776`）里 → 直接返回；否则造 `AttackData = _AttackUtils.createFromHeroWeapon(this, null)`，`addTag(14/7/2)`，`overrideBaseDamage(curSkillInf.props.power2)`，`dmgType = DamageType.ExtraDamage` → `_AttackUtils.hit(atk, t)`；命中成功后 `viewport.shakeS(0.6,0.1,0.25)` + 播 `sfx/enm/enm_queen_split_release.wav` + 在 `(x,y)` 处以角度 `a` 生成 `fxQueenRapierCut` |
| `queenStrikeTrigger()` | 从英雄出发沿斩击方向画一条 `±get_sliceLength()`（=`item.props.distance`）的线段，对线段附近（`radius + props.width*24`）的敌人再 `queenStrike()` —— "斩断现实"的连锁 |
| `Queen.doCutLineAttack(cutLine, width, power)` | 女王 Boss 版：目标中心到**过 (cx,cy)、方向 angle 的无限长直线**的距离 `< radius + width*24*0.5` → 命中（`dc.en.mob.boss/Queen.cs`） |
| `dc.en.bu.Orb.postUpdate()` | **死亡球**的外观来源：每 0.06 秒调一次 `dc.Fx.orb(x, y, dx, radius, color)`，沿半径撒一圈闪电粒子（原版颜色紫蓝 `0x7752D8`） |

## 2. 宿傩化映射

| 招式 | 实现 |
| --- | --- |
| **解**（Dismantle） | Hook `QueenRapier.queenStrike`：原版那一刀打完后把目标排进队列，之后每 `DismantleInterval`（0.12s）在目标周围 `DismantleRadius`（230px）内**随机落点 + 随机角度**追加一发原版 `queenStrike`，共 `DismantleSlashes`（8）段 |
| **捌**（Cleave） | 目标是**精英 / 体型大（hei ≥ 2）/ 血厚（life ≥ 260）**时，延迟 `CleaveDelay`（0.18s）放一条贯穿斩线：判定复刻 `Queen.doCutLineAttack`，线上所有敌人用**同一个角度**各吃一发原版 `queenStrike`（`fxQueenRapierCut` 于是连成一条贯穿斩） |
| **伏魔御厨子**（领域展开） | 见下 |

### 伏魔御厨子（领域展开）

按释放键（默认 `V`）展开：

* **外观 —— 红色死亡球，缓入变大**
  * 调用**原版死亡球**同一套 API `dc.Fx.orb(x, y, dx, radius, color)`（`dc.en.bu.Orb.postUpdate()` 就是这么画那颗紫蓝球的），把球画在英雄身上；
  * 颜色参数由 `DomainOrbColor` 控制，**默认改成红色 `FF2020`**（原版是紫蓝 `0x7752D8`）；
  * 半径按**缓入**曲线长大（先慢后快）：

    ```
    t      = clamp(elapsed / DomainGrowTime, 0, 1)
    radius = DomainStartRadius + (DomainRadius - DomainStartRadius) * t ^ DomainGrowEase
    ```

    `DomainGrowEase` 默认 `2.0`（二次缓入）；`1.0` = 匀速，越大起步越慢、后段越猛；
  * 每 `DomainOrbTick`（0.08s）画一次，并叠一圈 0.55× 的内环让球更实；
  * 展开瞬间再来一层**红色屏幕染色 + 闪光**（原版 `Fx.customMask` / `Fx.multiFlashBangS`）。
* **伤害 —— 跟着球一起长大**
  * 领域的**伤害半径与死亡球半径共用同一个值**，所以是"边展开边斩"；
  * 每 `DomainInterval`（0.25s）对范围内全体敌人各放一发原版斩击。

### 斩击特效黑红化

原版 `queenStrike()` 里那句 `fx.allocMultiBatch(batchGroup, t, x, y, ...)` 就是在 `(x,y)` 生成
`fxQueenRapierCut` 斩痕粒子（女王细剑的青色斩痕，原版不设 `r/g/b`，所以显示图集原色）。

模组侧的处理：

1. `Hook_QueenRapier.queenStrike` 在调 `orig(...)` 之前先记下这一刀的 `(x, y)`；
2. 订阅 `Hook_Fx.allocMultiBatch`，比对坐标 —— 对得上就说明这是**女王细剑那一刀的斩痕粒子**；
3. 把这颗粒子的 `r/g/b` 设成 `SlashFxColor`（默认 `E01414` 黑红）。

因为原版 `queenStrike()` 用的 `x, y` 和 `allocMultiBatch` 的实参**完全相同**，坐标比对是精确的；
位移条纹粒子走的是 `allocExternal` 而不是 `allocMultiBatch`，所以不会被误伤。
`TintSlashFx = false` 可关掉染色，恢复原版青色斩痕。

> 所有伤害都走**原版 `queenStrike`**，因此伤害公式、tag、`DamageType.ExtraDamage`、屏幕震动、音效、`fxQueenRapierCut` 全部是原版链路；宿傩只重新编排"在哪里、朝哪个方向、斩几次"。
> 追加斩击统一走 `_depth` 复入保护，不会无限套娃；间隔全部 `> 0.1s`，避开原版的目标冷却。

### 无视墙体（挥砍 + 斩击）

原版 `Weapon.canHit()` 会走 `map.collisions` / `sightCheckCase` 做**地图碰撞 + 视线遮挡**检测，
所以墙后的敌人砍不到，也就不会触发斩击。模组侧的处理：

* `Hook_Weapon.canHit`：先走原版；原版返回 `false` 时，如果武器是女王细剑且开了「无视墙体」开关，
  就再走一遍 `SukunaSlash.InWallIgnoreRange()` —— **完全不看碰撞**，只按"身前 `WallIgnoreRange` 格、
  上下 `WallIgnoreHeight` 格"判定，于是挥砍能"打穿"墙、把斩击送到墙后。
* 斩击本体（`queenStrike`）原版本就不做墙体检测，所以「解 / 捌 / 伏魔御厨子」一直是穿墙的。

### 删除挥砍伤害，只保留斩击伤害

原版 `QueenRapier.hitFromWeapon()` 里有两件事：

```
_AttackUtils.hit(createFromHeroWeapon(this, null), e)        ← ① 挥砍伤害
delayer.addS(null, queenStrike, itemInf.props.duration)      ← ② 延迟斩击
```

处理方式：

* `Hook_QueenRapier.hitFromWeapon`：在 `orig(...)` 外面打一个 `_inSwingHit` 标记；
* `Hook__AttackUtils.hit`：处于该标记窗口内、且 `atk.sourceWeapon is QueenRapier` → **直接 `return`，不调 orig**。

于是①被吞掉，②保留。`queenStrike` 是延迟回调，跑在窗口之外，所以斩击伤害（`dmgType = ExtraDamage`）
完全不受影响。开关：菜单里的「只保留斩击伤害」。

### 自定义图标

`Assets/sunuo.png`（24×24）随 `PackAssets` 打进 `res.pak` 根目录（pak 内路径 `sunuo.png`）。
`Hook__Assets.getItem` 在问到 `QueenRapier` 时返回它，其余物品照旧走原版：

```csharp
Res.Class.get_loader.Invoke()                       // 取 hxd 资源加载器
   .loadCache("sunuo.png", Image.Class)             // 与原版 _Assets.init() 加载 cardIcons.png 同一套 API
   .toTile()
   .sub(0, 0, w, h, 0, 0)                           // ← 关键：把 dx/dy 归零
```

> ⚠️ **坑：`hxd.res.Image.toTile()` 拿到的是「居中轴心」的 tile**（`dx = -w/2, dy = -h/2`），
> 而原版物品图标 `Assets.itemIcons.sub(x, y, size, size, null, null)` 的 `dx = dy = 0`。
> 直接把 `toTile()` 的结果交给 UI，图标会整体**左上偏移半个图标**
> —— 表现就是"选择框左上角正好对着图标中心"。
>
> 游戏自己也是这么处理的：`dc._Assets` 里
> `_Res.load(id).toTile().sub(0, 0, w, h, null, null)`（加载关卡图）、
> `dc._Assets.getItem()` 里 `itemIcons.sub(x, y, size, size, null, null)`
> —— `sub(..., null, null)` 会把 `dx/dy` 归零。照抄即可。
>
> 另外每次调用都返回**新建**的 `sub(...)`（原版也是每次新建），
> 避免某个 UI 改了 `dx/dy` 之后连累所有调用点。

懒加载 + 最多重试 30 次（加载器可能还没热起来）；失败就退回原版图标，不影响其它物品。
`Assets.getItem(id)` 是蓝图 / 收藏家 / HUD 等所有物品图标的唯一出口，所以一处替换全局生效。
加载成功时日志会打印
`[Sukuna] 图标已替换：sunuo.png 24x24（原始 dx=-12 dy=-12 → 已归零…）`，
便于核对原始轴心。

### 随机斩击角度

原版三段连击的斩击角度是写死在 `weapon` 表 `strikeChain[i].props.angle` 里的：

```
1a: props.angle =  0.5   → 斜向下
2a: props.angle = -0.4   → 斜向上
3a: props.angle =  0     → 水平
```

`QueenRapier.getStrikeAngle(c)` 就是

```csharp
angle = (owner.dir < 0 ? π : 0) + owner.dir * strikeChain[c].props.angle
```

所以每一次挥砍的**第一刀斩击角度都是固定的**。

模组侧的处理：`Hook_QueenRapier.getStrikeAngle` —— 先取原版角度作为"这一刀本来的朝向"，
再以它为中心在 `StrikeAngleRangeDeg` 范围内均匀随机：

```csharp
half  = StrikeAngleRangeDeg / 2   (转弧度)
angle = base + uniform(-half, +half)
```

| `StrikeAngleRangeDeg` | 效果 |
| --- | --- |
| **360（默认）** | 完全随机（`uniform(-π, +π)` 覆盖整圈） |
| 90 | 正向 ±45° 内随机 |
| 0 | 等于原版（固定角度） |

这个角度同时决定两件事，所以改了之后一起变：
① `queenStrike` 里 `fxQueenRapierCut` 的朝向；
② `queenStrikeTrigger` 那条"斩断现实"连锁线的方向（它决定连锁能扫到哪些敌人）。

> 注意 `getStrikeAngle` 每次挥砍会被调用两次（`hitFromWeapon` 排延迟斩击时一次、
> `queenStrikeTrigger` 撒连锁时一次），两次各自独立随机 —— 所以方向会更"乱"，这正是宿傩的味道。

## 3. 数据补丁内容（`patch_sukuna_data.py`）

### 3.1 `item` / `QueenRapier` —— 名字 / 介绍

| 字段 | 原版 | 宿傩 |
| --- | --- | --- |
| `name` | `Queen's Rapier` | **`宿傩`** |
| `gameplayDesc` | `Attacks that hit a target also slice through reality, hitting anything on their path again.` | **`命中后追加「解」的多段斩击；精英与强敌额外吃一记「捌」的贯穿斩线；可展开领域「伏魔御厨子」。`** |
| `ambiantDesc` | `DLC3_WEAPON_QUEENRAPIER_AMBIANTDESC`（翻译键） | **`不义的游戏，开始了。`** |

> `dc.libs.data.GetText.get()` 是"查得到就翻译、查不到就原样返回"
> （`texts.exists(str) ? texts.get(str) : str`），所以直接写中文（或任何语言）都能原样显示；
> `res/fonts/noto_sans_cjk_kr_regular_pixel.*` 也带了 CJK 字形。
> 想换英文：把 `patch_sukuna_data.py` 顶部的 `ITEM_NAME` / `GAMEPLAY_DESC` / `AMBIANT_DESC`
> 换成注释里的英文三行即可。
> 注意 GetText 会按 `||` 截断并 rtrim，文本里不要出现 `||`。

### 3.2 `item` / `QueenRapier` —— 斩击几何（攻击距离拉远）

| 字段 | 原版 | 宿傩 | 说明 |
| --- | --- | --- | --- |
| `props.distance` | 50 | **420** | 斩击线半长 `get_sliceLength()`，决定连锁斩能伸多远 |
| `props.width` | 0.5 | **1.8** | 斩击线厚度 / 连锁命中半径系数 `radius + width*24` |
| `props.duration` | 0.2 | **0.1** | 命中 → 触发斩击的延迟 |

### 3.3 `weapon` / `QueenRapier` —— 动作 / 特效 / 判定框

三段 `strikeChain` 全部改成原版**空手连击**那一套（`QuickFists` 用的就是它）：

| 字段 | 原版 | 宿傩 |
| --- | --- | --- |
| `animId` | `AtkQueenRapierA/B/C` | **`atkPunchA` / `atkPunchB` / `atkPunchC`** |
| `fxId` | （无） | **`fxAtkPunchA` / `fxAtkPunchB` / `fxAtkPunchC`** |
| `fxProps` | `{}` | **黑红**：`fxInnerColor 0xFF1E14`（内核亮红）/ `fxOuterColor 0x2A0000`（外沿近黑红） |
| `glowColor` | `0xA14000` 橙 | **`0xE01414` 红** |
| `area` | `3~3.5 × 0.5~1`，offset 0.5~1 | **`6 × 2`**，`offsetCaseX 0.5`，shape 1 |
| `hitFrame` / `charge` / `lockCtrlAfter` / `animSpd` | 8~9 / 0.3 / 0.2 / 默认 | **2,2,3 / 0.1 / 0.08 / 2.0**（快拳节奏） |
| `sfxRelease` / `sfxHit` | `weapon_queensw_release1.wav` / `hit_blade.wav` | **`weapon_multikick_release.wav` / `weapon_kick_hit.wav`** |
| `props.power2` | `[35]` | **`[58]`**（斩击本体伤害） |

> `animId` / `fxId` 都取自原版 `QuickFists` 的 `atkPunchA/B/C` + `fxAtkPunchA/B/C`，是英雄模型自带的动作与特效，不需要额外资源。

## 4. 文件清单

```
Sukuna/
├── patch_sukuna_data.py          # Python 数据补丁脚本：从 MDK v35 模板 CDB 生成 Sukuna/Sukuna/data.cdb
├── README.md                     # 本文件
└── Sukuna/                       # csproj 目录
    ├── Sukuna.csproj             # MDK 构建配置（cdb diff → Assets → res.pak → 自动安装）
    ├── SukunaMain.cs             # ModBase 入口：Hook 挂载、res.pak 挂载、事件、选项菜单、释放键
    ├── SukunaSlash.cs            # 斩击引擎：解 / 捌 / 伏魔御厨子（含红色死亡球领域）
    ├── SukunaConfig.cs           # coremod/config/Sukuna.json 配置 + 按键名/颜色解析
    ├── SukunaFeatures.cs         # 功能开关总控 + 热键轮询
    ├── data.cdb                  # 由脚本生成的修改版 CDB（构建输入）
    └── Assets/
        ├── sunuo.png             # 女王细剑的自定义图标（24×24，打进 res.pak 根目录）
        └── data.cdb_/            # 由 DCCMTool cdb diff 生成的差异补丁（打包进 res.pak）
            ├── item/QueenRapier.json
            └── weapon/QueenRapier.json
```

## 4.1 ⚠️ 数据补丁生效的关键：必须自己挂载 res.pak

MDK 只是把 `res.pak` **复制**到 `coremod/mods/Sukuna/`，**DCCM 不会自动挂载它**。
必须在模组里实现 `IOnAfterLoadingAssets`，手动把 pak 挂进 `FsPak`：

```csharp
void IOnAfterLoadingAssets.OnAfterLoadingAssets()
{
    FsPak.Instance.FileSystem.loadPak(Info.ModRoot.GetFilePath("res.pak").AsHaxeString());
}
```

挂载之后，DCCM 的 `CDBManager.GetAlteredCDB()` 才能从 `FsPak` 里读到
`data.cdb_/item/QueenRapier.json`、`data.cdb_/weapon/QueenRapier.json`，
在 `Assets.init` 时把改动合进 `data.cdb`（`CDBManager.LoadJsonData` → `Data.loadFrom("CDBManager_Override")`）。

> 合并规则（读自 `ModCore.dll` 与 `GameRes.Core.dll`）：
> `data.cdb_/<sheet 名>/<行名>.json`，行名取该行 JSON 里
> `NAME_JSON_ID = ["id", "item", "name", "room", "animId"]` 中**第一个存在的字段**。
> `item` 表用 `id`，`weapon` 表用 `item` → 所以 `weapon/QueenRapier.json` 能正确**替换**原本那一条武器数据。

**怎么确认生效？** 第一次挥出女王细剑时，日志（`coremod/logs/log_latest.log`）会打印一行自检：

```
[Sukuna] 数据补丁自检（实际生效值）：item.props.distance=420 width=1.8 duration=0.1；strikeChain[0].animId=atkPunchA fxId=fxAtkPunchA
[Sukuna] 期望值：distance=420 width=1.8 duration=0.1；animId=atkPunchA fxId=fxAtkPunchA（若不一致说明 res.pak 没挂上）
```

启动时也应有：

```
[Sukuna] res.pak 已挂载，数据补丁生效: ...\coremod\mods\Sukuna\res.pak
```

## 5. 构建

```powershell
# 1) 生成数据补丁 CDB（改了 patch_sukuna_data.py 里的数值才需要重跑）
python Sukuna/patch_sukuna_data.py

# 2) 构建：自动 cdb diff -> Assets -> res.pak -> 安装到 coremod/mods/Sukuna/
dotnet build Sukuna/Sukuna/Sukuna.csproj -c Debug
```

## 6. 配置与游戏内开关

* 配置文件：`coremod/config/Sukuna.json`（首次运行自动生成）
* 游戏内：**选项 → 模组 → 宿傩·斩击**
  * 7 个复选框：总开关 / 解 / 捌 / 御厨子 / **无视墙体** / **只保留斩击伤害** / **随机斩击角度**
  * 10 条滑条（写法照抄 `ZoomVision` 的 `addSliderWidget`）：

    | 滑条 | 范围 / 步进 | 作用 |
    | --- | --- | --- |
    | 领域持续时间 (秒) | 1–20 / 0.5 | **伏魔御厨子持续多久**（下次展开生效） |
    | 领域最终半径 (px) | 100–900 / 10 | 长满后的半径（= 伤害半径） |
    | 领域展开时长 (秒) | 0.2–10 / 0.1 | 缓入长满所需时间 |
    | 领域缓入指数 | 1–4 / 0.5 | 1=匀速，2=先慢后快（默认） |
    | 领域初始半径 (px) | 0–300 / 10 | 刚展开那一瞬间的半径 |
    | 领域结算间隔 (秒) | 0.1–2 / 0.05 | 每隔多久斩一次 |
    | 解·追斩段数 | 0–32 / 1 | 每次命中追加几段斩击 |
    | 无视墙体·身前距离 (格) | 1–30 / 0.5 | 穿墙能打到多远（24px = 1 格） |
    | 无视墙体·高度差 (格) | 1–20 / 0.5 | 穿墙判定允许的上下高度差 |
    | **斩击角度·随机范围 (度)** | 0–360 / 10 | 360=完全随机，90=正向±45°，0=原版 |

* 热键：各功能开关默认**不绑键**；「伏魔御厨子」释放键默认 `V`（`KeyCastDomain`，留空 = 不放）

常用可调参数：

| 参数 | 默认 | 作用 |
| --- | --- | --- |
| `DomainOrbColor` | `"FF2020"` | 死亡球颜色（`RRGGBB` / `#RRGGBB` / `0xRRGGBB`） |
| `SlashFxColor` | `"E01414"` | **斩击特效（fxQueenRapierCut）颜色**，黑红 |
| `TintSlashFx` | true | 是否把斩击特效染色（关掉 = 原版青色斩痕） |
| `DomainStartRadius` | 60 | 领域张开时的初始半径（px） |
| `DomainRadius` | 360 | 领域最终半径（px，也是伤害半径） |
| `DomainGrowTime` | 2.5 | 缓入长满所需秒数 |
| `DomainGrowEase` | 2.0 | 缓入指数（先慢后快） |
| `DomainDuration` | 6.0 | 领域持续秒数 |
| `DomainInterval` | 0.25 | 领域每多少秒结算一次斩击 |
| `DomainOrbTick` | 0.08 | 死亡球粒子每多少秒画一次 |
| `DomainScreenMask` | true | 展开瞬间的红色屏幕染色 + 闪光 |
| `EnableWallIgnore` / `WallIgnoreRange` / `WallIgnoreHeight` | true / 10 / 4 | 无视墙体 / 身前格数 / 高度差格数 |
| `EnableSlashOnly` | true | 删除挥砍伤害，只保留斩击伤害 |
| `EnableRandomAngle` / `StrikeAngleRangeDeg` | true / 360 | 随机斩击角度 / 随机范围（度） |
| `DismantleSlashes` / `DismantleInterval` / `DismantleRadius` | 8 / 0.12 / 230 | 「解」的段数 / 间隔 / 随机落点半径 |
| `CleaveWidth` / `CleaveDelay` / `CleaveLifeThreshold` | 1.2 / 0.18 / 260 | 「捌」的斩线厚度 / 延迟 / 强者判定血量 |

## 7. 注意事项

* **必须手持女王细剑**（`dc.tool.weap.QueenRapier`）才能触发挥砍连锁与领域展开。
* **动作 / 特效 / 距离全部来自 res.pak 数据补丁** → 必须先确认日志里有
  `[Sukuna] res.pak 已挂载，数据补丁生效`，否则只会看到原版女王细剑的刺剑动作。
* 领域半径 = 死亡球半径，也就是说领域刚张开时打得近、张开后打得远，这是有意为之。
* 伤害相关的原版链路：`queenStrike` 内部自带 0.1 秒**按目标**冷却，所以一切追加斩击的间隔都 `≥ 0.1s`。
* DCCM 的 `Hashlink.Virtuals.virtual_*` 类型构造函数是 `throw null` 桩，**模组侧无法自行 new**，
  所以没有复用 `Fx.queenCutLine` 那条全屏斩线视觉（斩击特效来自原版 `queenStrike` 自带的 `fxQueenRapierCut`，
  现在武器本身的出招特效则由数据补丁改成 `fxAtkPunchA/B/C` 黑红配色）。
* `GamePseudocode/`、`res/` 全程只读，未做任何修改。
