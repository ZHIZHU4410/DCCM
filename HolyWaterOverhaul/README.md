# HolyWaterOverhaul —— 圣水 · 满地火 + 全图烧

把 **HolyWater**（圣水，DLC「Purple」）改成一瓶就能把整张地图点着的技能。

| # | 需求 | 实现 |
|---|---|---|
| ① | 普通：**散射丢出 15 个火堆** | Hook `HolyWater.onTrigger`，不调 orig，自己铺 15 个 `addAreaAffectS` |
| ② | 传奇：**灼烧整个地图的所有敌人** | Hook `HolyWater.onTrigger` 认出 `ItemCrash`，每 tick 遍历全图敌人 `setAffectS(88, ...)` |
| ③ | **敌人在烧**这件事**无上限** | `setAffectS(88, 99999)` —— 不占地面池，可以放心永久 |
| ④ | **CD 改为 0** | 数据补丁：`item/HolyWater.castCD` 12 → 0 |
| ⑤ | **进下一关 / 重开一局要重置** | `IOnHeroInit` 清状态 + 每帧比对关卡引用兜底 |

> 地面火堆的存活时间改成了**有限值（默认 15s）** —— 这不是偷懒，是因为原来的"永久火堆"
> 正是卡顿的根源。原因见下面「为什么会卡」。

---

## 0. 为什么会卡（根因）

`Level.addAreaAffectS`（`GamePseudocode/dc.pr/Level.cs:7498`）找一个空位是这样写的：

```csharp
do
{
    int wid = arrayObj.length;
    if (num2 >= wid) { return; }                 // ← 扫完整个池都没空位 → 白扫一场，直接返回
    levelAreaAffect2 = (LevelAreaAffect)arrayObj.array[num2];
    num2++;
    frames = levelAreaAffect2.frames;
}
while (!(0.0 >= frames));                        // ← 从 0 号开始线性扫，找 frames <= 0 的空位
levelAreaAffect2.init(cx, cy, aoeDurationS, a, aDurationS, aValue, affixes);
```

而这个 `areaAffects` 池在 `_Level.__inst_construct__`（`GamePseudocode/dc.pr/_Level.cs:135-172`）里
**只建了 512 个**（`_uid = 511` → 512 项）：

```csharp
listCurrentQuadElements = alloc_array(LevelAreaAffect, 0);
_uid = 511;                                      // ← 就 512 个
...
while (_uid < num) { areaAffects[num4] = new LevelAreaAffect(arg1); }
```

于是链条是这样的：

1. 我把 `props.duration` 设成 **99999** → 地面火堆和雨柱**永不释放**；
2. 池子被占满后，**每一次** `addAreaAffectS` 都要**线性扫 512 项**才发现没位置、然后什么都不做；
3. 传奇雨的圆盘半径是 `props.distance(2) × 7.2 = 14.4 格`
   （`_Bresenham.iterateDisc(cx, cy, (int)num20, cb)`），**一帧要建 ≈615 根雨柱** ——
   比整个池子还多。615 × 512 ≈ **31 万次**空转，就卡那一下。

**修法**（三条一起）：把地面火堆/雨柱的存活时间改回有限值（默认 15s，池子能循环使用）、
把雨的圆盘半径从 14.4 格收到 5 格（≈615 根 → ≈81 根）、
把"无上限"挪到**敌人身上的灼烧**上（`setAffectS`，根本不经过这个池）。

---

## 1. 原版的两条路

`dc.en.gr.HolyWater.onTrigger()`（`GamePseudocode/dc.en.gr/HolyWater.cs:1369`）一进来就分叉：

```csharp
if (item.hasAffix("ItemCrash"))
{
    new HolyRain(base.parent, base.item, base._level).init();   // ← 传奇：下雨
    return;
}
// 普通：TargetHelper 找落点附近（commonProps.explosionRange = 1 格）的目标，对每个目标：
//   double duration  = props.duration;                                        // 5
//   double duration2 = props.duration2;                                       // 0.7
//   double value     = _Const.scaleHeroValueToTier(props.dps, getRelevantTierFor(item));
//   level.addAreaAffectS(x, y, duration, 88, duration2, value, affixes);
```

`dc.pr.Level.addAreaAffectS(cx, cy, aoeDurationS, a, aDurationS, aValue, affixes)`
就是"在某格生成一片带持续时间的区域效果"：`a = 88` 是灼烧，`aoeDurationS` 是它活多久，
`aValue` 是每秒伤害。**注意参数是格坐标（int），不是像素。**

传奇那条走 `dc.en.gr.HolyRain`（`HolyRain.cs`）：

```csharp
init()        tickRate = props.tick * ItemCrash.props.multiplier;
              level.addAreaAffectS(x, y, props.duration, 88, props.duration, 0.0, null);   // 雨柱
fixedUpdate() fx.holyRain(...);
              TargetHelper → filterByArea(area) → filterBySight(...)      // ← 只烧"视野内"
              对每个目标：
                double doT = hero.getDoTValue(item, props.dps, entity, null);
                entity.setAffectS(88, props.duration, ref doT, null);      // ← 直接挂灼烧
```

所以"视野内"就是那个 `filterBySight`；要烧全图，就不走它的 TargetHelper，自己遍历。

CD 的来源（`GamePseudocode/dc.tool/InventItem.cs:8578`）：

```csharp
double castCD = _itemData.castCD;
...
castCD *= num7;   // 按 group / 属性再算一遍
```

## 2. 本模组的改动

### ① CD 0 + 持续时间无上限 —— 数据补丁

`patch_holywater_cdb.py` 改 `item` 表里的 HolyWater：

| 字段 | 原版 | 现在 |
| --- | --- | --- |
| `castCD` | `12` | **`0`** |
| `props.duration` | `5` | **`15`**（地面火堆/雨柱存活；**故意不设永久**，见「为什么会卡」） |

其余（`dps [30]` / `dps2 [45]` / `tick 0.35` / `duration2 0.7` / `width` / `height` / `distance` / `color`）全部保持原版。

### ①.5 无上限放在"敌人"身上，不放在"地面"上

`props.duration` 同时管三件事：地面火堆的存活、雨柱的存活、**雨自己实体的存活**
（`_HolyRain.__inst_construct__:166`：`double num = props.duration * cd.baseFps`，
到点 `onCooldownEnd("destroy")` → `destroy()`）。

前两件都在那个 512 格的池里，所以必须是有限值；
而"敌人一直在烧"用的是 `entity.setAffectS(88, BurnSeconds, doT)`，
**根本不经过那个池** —— 所以 `BurnSeconds` 放心给 99999（≈无上限）。

雨柱数量由 `props.distance` 决定（`_Bresenham.iterateDisc(cx, cy, (int)(props.distance × 7.2))`），
本模组在 `HolyRain.init` 之前把它改成 `RainDiscCells / 7.2`，默认 **5 格 ≈ 81 根**
（原版 14.4 格 ≈ 615 根）。全图灼烧不依赖这些雨柱，所以收小只影响表现。

### ② 普通：散射 15 个火堆

```csharp
int count = cfg.PoolCount;                       // 15
double perTile = cfg.PoolSpreadTiles / (count - 1);
for (int i = 0; i < count; i++)
{
    int cx = self.cx + (int)Math.Round((i - count / 2.0 + 0.5) * perTile);
    int cy = cfg.PoolSnapToGround ? level.map.getGroundY(cx, self.cy) : self.cy;
    level.addAreaAffectS(cx, cy, duration, 88, duration2, value, null);
}
```

* 以**落点为中心左右对称**铺开，总宽 `PoolSpreadTiles`（默认 13 格）；
* 每个火堆都用 `LevelMap.getGroundY(cx, cy)` **吸附到该列地面**，
  免得在斜坡上悬空或埋进地里；
* `value` 走原版同一套 `dc.Const.Class.scaleHeroValueToTier(props.dps, getRelevantTierFor(item))`，
  所以伤害照常吃玩家属性 —— 写法是 `dc.Const.Class.scaleHeroValueToTier.Invoke(dps, tier)`
  （Haxe 静态在 GameProxy 里挂在类对象上，和 `AttackUtils.Class.xxx` 是同一回事）。

### ③ 传奇：灼烧整个地图

`onTrigger` 认出 `ItemCrash` 时**照常调 orig**（保留原版那场雨的表现），
另外记下 `item / props.dps / props.duration`，之后每 `LegendTick`（默认 0.35s = 原版 `props.tick`）
遍历**整张地图**的敌人：

```csharp
var it = hero._team.opponentsIterator.reset(hero._team);
while (it.hasNext()) {
    var e = it.next();
    double doT = hero.getDoTValue(_legendItem, _legendDps, e, null);
    e.setAffectS(88, _legendDuration, Ref<double>.In(doT), null);    // 原版同一套 affect 88
}
```

用的是**原版同一个 affect 88、同一个 `getDoTValue`**，只是去掉了 `filterByArea` + `filterBySight`
这两道限制 —— 所以**墙后、屏幕外、整张地图**的敌人都会烧起来。
配合 `props.duration = 99999`，一旦点上就是永久燃烧。

### ④ 进下一关 / 重开一局就重置

"全图灼烧"是个**跨帧的持久开关**，如果不管它，进了下一关还会继续烧 —— 所以要把状态绑在关卡上：

```csharp
void IOnHeroInit.OnHeroInit()          // 每次进新关卡 / 重开一局，英雄都会重建
{
    ResetState("进入新关卡 / 新的一局");
}

void IOnHeroUpdate.OnHeroUpdate(double dt)
{
    ...
    // 兜底：IOnHeroInit 万一没触发，靠关卡引用变化也能发现切关
    if (_boundLevel != null && hero._level != _boundLevel) { ResetState("检测到关卡切换"); return; }
    ...
}
```

`ResetState()` 会清掉：`_legendActive`（关掉全图灼烧）、`_legendItem` / `_legendDps` /
`_legendDuration`、`_boundLevel`、以及两个计数器；本来就没状态时不刷日志。
施放时用 `_boundLevel = self._level` 记下"在哪一关开的"。

> 上一关的**火堆和雨本来就是关卡实体**（`addAreaAffectS` 的区域效果 + `HolyRain` 实体），
> 关卡一换就跟着没了，不需要额外处理。
> `props.duration` 那个数据改动是**数值**不是"效果实例"，保留不动。

## 3. 文件

```
HolyWaterOverhaul/
├── patch_holywater_cdb.py               # Python：castCD=0 / duration=99999 -> data.cdb
├── README.md
└── HolyWaterOverhaul/
    ├── HolyWaterOverhaul.csproj         # MDK：cdb diff -> Assets -> res.pak -> 自动安装
    ├── HolyWaterOverhaulMain.cs         # onTrigger Hook + 全图灼烧 + res.pak 挂载
    ├── HolyWaterOverhaulConfig.cs       # 配置
    ├── data.cdb                         # 脚本生成的修改版 CDB（构建输入）
    └── Assets/data.cdb_/item/HolyWater.json   # 差异补丁（打进 res.pak，994 B）
```

## 4. 配置

`coremod/config/HolyWaterOverhaul.json`（首次运行自动生成）：

| 字段 | 默认 | 作用 |
| --- | --- | --- |
| `EnableMod` | `true` | 总开关 |
| `PoolCount` | `15` | 普通一次铺几个火堆 |
| `PoolSpreadTiles` | `13.0` | 散射总宽度（格） |
| `PoolSnapToGround` | `true` | 火堆吸附到该列地面 |
| `PoolDurationSeconds` | `15.0` | **地面火堆/雨柱**存活时间（秒）。⚠ 别设成 99999 |
| `LegendBurnWholeMap` | `true` | 传奇烧全图（关掉就只剩原版视野内） |
| `LegendTick` | `0.35` | 全图灼烧刷新间隔（秒） |
| `BurnSeconds` | `99999.0` | **敌人身上**灼烧时长（秒）。不占地面池，可以无上限 |
| `RainDiscCells` | `5.0` | 传奇雨圆盘半径（格）。原版 14.4 → ≈615 根雨柱 |

**CD 和持续时间不在这个配置里** —— 它们是数据补丁，在 `patch_holywater_cdb.py` 顶部的
`CAST_CD` / `BURN_DURATION`，改完要重跑脚本 + `dotnet build`。

## 5. 构建

```powershell
# 改过 CD / 持续时间才需要重跑
python HolyWaterOverhaul/patch_holywater_cdb.py

dotnet build HolyWaterOverhaul/HolyWaterOverhaul/HolyWaterOverhaul.csproj -c Debug
```

## 6. 日志核对

```
[HolyWaterOverhaul] 已加载：普通散射 15 个火堆（宽 13 格）/传奇全图灼烧=True（每 0.35s）/ CD 与持续时间走数据补丁
[HolyWaterOverhaul] res.pak 已挂载，CD=0 / 持续时间无上限 生效: ...\mods\HolyWaterOverhaul\res.pak
[HolyWaterOverhaul] 普通：落点 (42,17) 散射铺了 15/15 个火堆、每格 0.93、火堆存活 15s、每段 78.31（affect 88）
[HolyWaterOverhaul] 传奇：已开启全图灼烧（每 0.35s 刷一次、灼烧 99999s = 无上限、仅限本关）
[HolyWaterOverhaul] 传奇雨已生成：圆盘半径 5 格（约 79 根雨柱）、雨柱存活 15s（地面池只有 512 格，故意不设永久）
[HolyWaterOverhaul] 全图灼烧：本次点燃 23 个敌人（本关累计 23）
...
[HolyWaterOverhaul] 进入新关卡 / 新的一局：圣水状态已重置（全图灼烧关闭、绑定关卡与计数清零）
```

> **⚠ res.pak 仍然建议挂上**：它管的是 **CD=0**。没有 `res.pak 已挂载` 那行的话 CD 还是 12 秒；
> 火堆 / 雨 / 全图灼烧这三样是运行时兜底的，不受影响。

## 7. 注意

* 15 个火堆是**以落点为中心左右对称**铺的，不分朝向 —— 圣水落地就是一圈火，不需要看面向。
* "整张地图"用的是 `hero._team.opponentsIterator`，也就是**当前关卡的全部敌人**，
  不受屏幕 / 视线 / 房间限制，**但只在当前这一关** —— 进下一关要重新丢一次。
* **敌人身上的灼烧是永久**（`BurnSeconds = 99999`）；**地面火堆是 15 秒**
  （`PoolDurationSeconds`）—— 后者是为了不让那个 512 格的池被占满，见「为什么会卡」。
  真想要永久地面火堆就把 `PoolDurationSeconds` 调大，但**会重新开始卡**。
* 嫌满地火/雨碍事就把 `PoolDurationSeconds` 调小（比如 5），池子回收更快、也更省。
* 改的只有 HolyWater 这一件物品；其它技能、其它火源不受影响。
* `res/` 与 `GamePseudocode/` 全程只读，未做任何修改。
