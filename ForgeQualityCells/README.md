# ForgeQualityCells —— 铁匠学徒：用细胞提升品质（最高到 L）

给休息房（关卡之间的通道）里那个「花**金币**刷新词条」的 NPC —— **铁匠学徒 / Reforger**
（`dc.en.inter.npc.SmallBlacksmith` → 面板 `dc.ui.ForgeUnderground`，标题 `FORGE MINEURE`）——
改造成：

> **品质不再用金币买 —— 原版那条「用金币提升品质」被删掉了，
> 改成花细胞一档一档往上提：无印 → + → ++ → S → L（传奇）。**

「品质」就是原版那套 `QualityUp` 档位（也就是被删掉的第二行
「Augmenter la puissance」花金币干的那件事），现在只有**细胞**这一条通道，
而且**能一路走到 L**（原版金币通道 `f_getMaxUpgradeLevel() == 3`，封顶在 S）。
休息房里金币经常被词条刷新抢光、细胞却剩一大堆，多这一条就等于把那种局救回来；
反过来，金币再也不能用来堆品质了。

---

## 1. 用起来什么样

打开铁匠学徒的面板，每一件可锻造的装备下面**只剩两行**（原版的金币品质行已经删掉），
我们这一行的**文案会跟着当前档位变**：

```
[图标]  Reforger les modificateurs      1200{金币}          ← 原版：金币刷新词条
        提升品质 -> +                       50{细胞}          ← 无印 → +
        提升品质 -> ++                     100{细胞}          ← + → ++
        提升品质 -> S                      200{细胞}          ← ++ → S
        提升品质 -> L                      400{细胞}          ← S → L（传奇）

        （原版的 Augmenter la puissance · 800{金币} —— 已删除）
```

（同一件物品只会出现其中一行 —— 就是它"下一档"那一行。）

* 光标能正常走到这一行（上/下就是顺序切换，和原版那两行一样）；
* 细胞不够 → 这一行**变暗 + 光标变红 + 报错音**（和原版买不起金币行时的表现完全一致）；
* 买完立刻生效：细胞余额、行的变灰/变亮、下一档的文案与价格**当帧刷新**（和原版扣金币一样实时）；
* 已经到 **L**（带 `Legendary` 词条的物品）或本来就不可锻造 → 这一行直接不显示；
* 面板右上角原来只有金币，现在会**多一个细胞余额**，边看边花不用退出面板；
* 面板顶部原版那句说明里的「…或者提升它的力量」已经被换成现状说明（见第 7 节）。

## 2. 「L」到底是什么

不是"第 4 层 QualityUp"——游戏里没有这玩意儿，四处证据：

| 证据 | 内容 |
|---|---|
| `_Lang.getRawItemUpgradeSuffix(up, legend)` | `legend` 为真 → `"L"`；否则 `up: 0 ""` / `1 "+"` / `2 "++"` / `3 "S"`，**`up >= 4` → `"??"`** |
| `ItemMetaManager.f_getMaxUpgradeLevel()` | 恒返回 `3`（金币通道封顶 S） |
| `FreeWeaponSelector.maxQuality` | `3`（训练场自选武器的品质滑条只到 S） |
| `res/atlas/gameElements.atlas` | `itemUpgrade` 只有 `itemUpgrade0/1/2` 三帧，**没有 L 的角标 art** |

所以 **L = 传奇（Legendary）**。这也是 truelle 表自己的写法：
`ForgeRerollCost` 的注释是 `"v0 = % du prix de l'arme ajouté à chaque reroll (0, +, ++, S, L)"`，
而 `getForgeRerollCost()` 取下标 4 的条件正是 `hasAffix("Legendary") ? 4 : getUpgradeLevel()`。

**S → L 这一步具体做了什么**（照抄原版 `ItemGen.generateStats` 的传奇分支）：

1. `addAffix("Legendary")` —— 原版就是靠这条把物品做成传奇的
   （`getRawItemUpgradeSuffix` 见到它才把名字后缀写成 `"L"`，图标/边框同时变金色）；
2. 把物品 CDB 里 `item.legendAffixes` 池的每一条都挂上 —— **这才是 L 真正的收益**
   （传奇专属词条，例如撕裂光环的 `BleedOnHit`）；
3. `removeAllAffixes("QualityUp")` —— 传奇的 `getAdjustedItemLevel()` 是
   `itemLevel + 6` 的**固定值、忽略 QualityUp 层数**，而 S 也正好是 +6（3 层 × 2），
   所以清掉**一点战力都不掉**；原版生成的传奇物品同样带 0 层
   （spawn 的传奇分支会 `set_weaponQuality(0)`），清掉才和原版传奇长得一模一样。

## 3. 价格怎么定

价格写在 **data.cdb 的 `truelle` 表**里（原版放全局调参的那张表：
`ForgeRerollCost` / `ForgeRefineCost` / `ForgeCellCosts` 都在里面），
由 `patch_forge_quality_cdb.py` 生成：

```
truelle / ForgeQualityCellCost / value0 = [50, 100, 200, 400, 800]
                                          ↑ 下标 = 物品**当前**的品质档位
```

| 当前档位 | 无印 | + | ++ | S | L（到顶） |
|---|---|---|---|---|---|
| 买到下一档要花 | 50 | 100 | 200 | **400** | —（隐藏） |
| 得到 | + | ++ | S | **L（传奇）** | — |

* 倍率统一 **×2**（400 → 200 → 100 → 50），所以 0 → L 全程共 750 细胞；
* 下标 4 那一格（800）只是让表形状和原版 `ForgeRerollCost` 的 5 档对齐，实际用不到；
* **训练场免费**（和原版 `getForgeRerollCost/RefineCost` 在训练场返回 0 一致）；
* CDB 读不到（比如没打上 res.pak）→ 用 C# 里的兜底表，功能照常。

改价：编辑 `patch_forge_quality_cdb.py` 里的 `CELL_COSTS`，重新 `dotnet build` 即可
（csproj 的 `RegenCdb` 目标检测到脚本比 data.cdb 新就会自动重新生成）。

## 4. 目录结构（照 DamageAuraBoost / ChronoBlade 那套）

```
ForgeQualityCells/
├── README.md                       ← 本文
└── ForgeQualityCells/
    ├── ForgeQualityCells.csproj    ← MDK 工程；BuildResPak = cdb diff + unpack + 打包进 res.pak
    ├── ForgeQualityCellsMain.cs    ← ModBase：挂 Hook_ForgeUnderground.addItem、加载 res.pak
    ├── ForgeQualityForge.cs        ← 面板那一行：创建 / 刷新 / 扣细胞 / 升档 / 做成传奇
    ├── patch_forge_quality_cdb.py  ← 数据侧：以 MDK v35 模板为底，truelle 表追加一行
    ├── data.cdb                    ← 脚本产物（和 ChronoBlade 一样留在工程里）
    └── Assets/
        └── data.cdb_/truelle/ForgeQualityCellCost.json   ← DCCMTool diff 出来的补丁（打包进 res.pak）
```

构建（会自动装到 `coremod/mods/ForgeQualityCells/`）：

```powershell
cd ForgeQualityCells\ForgeQualityCells
dotnet build
```

## 5. 代码干了什么

| 位置 | 说明 |
|---|---|
| `Hook_ForgeUnderground.addItem` | 原版每造一件装备的两行，我们**先删掉其中的金币品质行**，再补上细胞那一行 |
| `StripGoldQualityRow` | 见第 7 节：把原版「用金币提升品质」的可选项与行本体都摘掉 |
| `FindBoxParent` | 从物品块里认出"装着 FlowBox 的那个竖排 Flow"（不靠类型，靠内容，稳） |
| `FlowBox.Class.createBoxMain` | 和原版同款的行容器（背景框、内边距、光标尺寸全自动） |
| `forge.registerChoice(...)` | 注册成面板可选项：`canBeUsed` / `cb` / `onSelect` 三个回调语义照抄原版 |
| `flow2.onBeforeReflow` 包一层 | 原版那些行的文案/置灰就是在自己的 `onBeforeReflow` 里算的；我们把刷新串上去 |
| `NextStep(item)` | 决定这一行现在做哪一步：`QualityUp`（0→+→++→S）还是 `Legendary`（S→L）还是 `None`（到顶隐藏） |
| `forge.reforge(item, 1)` | 前几档：**原版 refine 的同一条路**（叠一层 `QualityUp` + 补上随品质增长的新词条） |
| `GrantLegendary()` | 最后一档：`Legendary` 词条 + `legendAffixes` 池 + 清 `QualityUp` + 通知英雄刷新 |
| `hero.substractCells(cost, ...)` | 扣细胞，`Hero.hudSetCells` 会顺带刷新 HUD 上的细胞数 |
| `HeroesCells()` | 读 **`hero.cells`**（运行中的实时值）—— 千万别读 `game.data.cells`，见第 8 节 |
| `EnsurePanelSubtitleOnce()` | 把面板顶部那句已经过时的说明换成现状（见第 7 节末） |
| `reforge` 的收尾 | 和原版一样 `removeAllAffixes("Rerolled")` + `addAffix("ForgeRefined")` + 买/锻造音效 |

数值/表现全部走原版：物品等级、词条、说明面板（`setItemDesc`）、装备变化回调
（`hero.onEquipedItemsChange`）都是游戏自己那套，模组不额外造数值。

## 6. 可调参数

都在 `ForgeQualityForge.cs` 顶部：

| 常量 | 默认 | 说明 |
|---|---|---|
| `RemoveVanillaGoldQualityRow` | `true` | `true` = 删掉原版那条「用金币提升品质」；改回 `false` 就恢复原版行为 |
| `RewritePanelSubtitle` | `true` | `true` = 把面板顶部那句「…或者提升它的力量」换成现状说明 |
| `SubtitleText` | `刷新词条：花金币；提升品质：花细胞，最高到 L（传奇）。` | 上面那条用的文案 |
| `LabelText` | `提升品质` | 面板上那一行的文案前缀（后面自动接 `-> +` / `-> S` / `-> L`） |
| `MaxCellTier` | `4` | 细胞通道的上限档位：`4` = L（传奇，默认）；改成 `3` 就退回"只到 S" |
| `VanillaMaxQuality` | `3` | 原版金币通道上限（S），不建议改 |
| `CdbRowId` | `ForgeQualityCellCost` | 对应 data.cdb `truelle` 表的行 id |
| `FallbackCosts` | `{50,100,200,400,800}` | 读不到 CDB 时的兜底价格 |

## 7. 删掉原版「用金币提升品质」是怎么做的

原版 `ForgeUnderground.addItem` 里，**每件装备**会在 `flow2` 里造两个 `FlowBox` 并各注册一条可选项：

```
registerChoice(FlowBox 1)   // Reforger les modificateurs  → 金币刷新词条（保留）
registerChoice(FlowBox 2)   // Augmenter la puissance     → 金币提升品质（删掉）
```

`StripGoldQualityRow` 做的两件事：

1. `choices.remove(最后一条)` —— 从面板可选项里摘掉，**光标从此走不到它**，
   `select()` / `onValidate()` / `onAfterReflow()` / `postUpdate()` 全都会自然绕开；
2. `box.parent.removeChild(box)` —— 把行本体从 `flow2` 摘下来，
   它的 `Interactive` 是它的子节点、会一起离开场景树，所以也不会再吃鼠标事件。

⚠️ **只删"最后一条"，前提是挂钩时机**：我们挂在 `addItem` 的**收尾**（`orig()` 之后），
而原版这两条注册顺序固定（先词条行、后金币行），所以末条必然是这一件装备的金币行。

⚠️ **必须判 `flow != null`**：`addItem` 对不可锻造的物品会在注册任何可选项**之前**就
`return null`，那时 `choices` 的末条是**上一件装备**的金币行，删错就出事。

实现里刻意**不做跨包装器的引用比较**（`getDyn()` 读出来的 C# 代理不一定两次是同一个对象）：
只把"从数组里读出来的同一个对象"原样传回 `remove`，以及在同一个 choice 上取 `f` 再传回
`removeChild`，命中的都是 Haxe 那一侧的对象。

副作用都验证过：`flow2.onBeforeReflow` 里仍会对被摘掉的盒子调一次 `reflow()` —— 只是空转
（它只改自己的可见性/宽度，不碰父节点）；而每件装备的金币行被换成我们的行，
`choices.length`**和原版一模一样**（原版 2 行/装备，我们也是 2 行/装备），
所以原版那些"用 `choices.length` 判断要不要画分隔线"的逻辑（`addItem` 里的 `lines`）
行为完全不变。

顺手还把面板顶部那句说明改了（`EnsurePanelSubtitleOnce`）：原版写的是
「Reforgez un objet équipé … ou pour augmenter sa puissance.」——
后半句指的就是被删掉的那条通道，留着会误导。

## 8. 踩坑记录：细胞一定要读 `hero.cells`

**金币和细胞的存储位置是不对称的**，这是本模组第一个版本"扣细胞不实时"的根因：

| | 权威字段 | 谁在写 |
|---|---|---|
| 金币 | `game.data.money`（`GameData`） | `Hero.addMoney` / `Hero.substractMoney` 里**双向同步**（`data.money -= v; hudSetMoney(data.money)`） |
| 细胞 | **`hero.cells`**（`Hero`） | 只有 `Hero.addCells` / `Hero.substractCells` 写 `Hero.cells` + `hudSetCells(...)`；**全工程没有任何一处写回 `game.data.cells`** |

`game.data.cells` 只在 `_Hero.__inst_construct__` 里被读一次
（`arg1.cells = game.data.cells`）—— 那是"进这一关时"的快照。读它的话，花完细胞数不降，
于是余额显示不变、行不变灰；更糟的是 `Hero.substractCells` 内部会把超额部分夹掉
（`v = min(v, cells)`），等于**白拿**。

游戏自己所有花细胞的界面都统一用 `Game.Class.ME.hero.cells`
（`_ForgeLegendary` / `CollectorPanel` / `HUD` / `dc.ui._CollectorPanel.CellCount` …），
本模组现在也照这个来。

## 8. 已知取舍

* **不复制原版的自定义游戏限制** `cgData.numForgeRefine`（自定游戏里"本局最多精炼几次"）——
  那条限制本来就只挂在被删掉的金币品质行上，现在品质由细胞通道负责，不受它约束。
* **L（传奇）不可逆**：升级到传奇后这一行就会消失，物品也不能再被任何通道升级
  （原版传奇物品本来就被排除在锻造之外）。请在按下去之前想清楚 —— 这是"毕业"那一步。
* **删掉金币品质行是"全局"的**：训练场、Boss Rush 之类只要开这个面板的地方都不会再有它。
  想让它在别处回来，把 `RemoveVanillaGoldQualityRow` 关掉即可（`false`）。
* 面板文案是写死的中文 + ASCII 箭头（不走 `lang/*.mo`）—— 游戏里的 `.mo` 是**整表替换**
  语义，塞精简版会把全部文本字典冲掉（ChronoBlade 踩过这个坑，见它的 csproj 注释）。
* 拆掉这个模组：删掉 `coremod/mods/ForgeQualityCells/` 即可，存档不受影响
  （`QualityUp` / `Legendary` 都是原版词条）；原版的金币品质行会随之恢复。
