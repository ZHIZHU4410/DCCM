# 面板与暂停（已完成）

> 这份文件原来是"下一轮继续做"的交接说明。下面的需求**已经全部实现**，
> 这里保留下来作为"当时为什么要这么做"的记录 —— 尤其是那个踩过的深坑。

## 需求（用户 2026-09-13 定稿）

1. 热键 **X** = 弹药更换面板（12 发子弹，选中后 `TimeBullet.SetBullet(i)`）。
2. 热键 **P** = 选择武器面板，**只列新增的两把武器**（时之刃 ChronoBlade / Zaphkiel）。
3. **删掉** `\`（KeySummonWeapon）和 `P`（KeySummonPistol）的**直召**；`P` 改绑面板。
   注意：删直召和加面板必须在**同一个改动**里完成，否则会出现"没有召唤途径"的中间态。
4. 两个面板打开时都要**真暂停**，参照原版训练场：
   训练场有三个按钮（对应不同属性的武器）→ 按下弹出**武器选择 UI** → 选完武器从上方出现，
   还能改等级/品质；**选择期间游戏是暂停的**。

## 实现方式：压 Process（进程栈），不要手写 `Game.paused`

### 上一版为什么"暂停后恢复不了"

曾经在 `dc.pr.Game.update` 的钩子里切换 `paused`。但 `paused = true` 之后
**这个 `update` 根本不再被调用** → 解除暂停的代码永远跑不到，
连 60 秒兜底也写在同一个钩子里，救不回来。

**教训：解除开关不能装在它自己要关掉的那扇门后面。**

### 现在的做法（= 原版暂停菜单 / 训练场选武器那套）

原版所有"全屏选择界面"都是 `dc.ui.sel.GridSelector` 的子类，它的构造函数里：

```
_Process.__inst_construct__(arg1, Main.Class.ME);   // 挂到 Main 进程栈（不是 Game 底下！）
arg1.pauseGame();                                    // HUD.hide() + Game.modalPause() = 真暂停
arg1.createRootInLayers(parent.root, ROOT_DP_MENU);
arg1.setControlLabel();
arg1.initRightFlow();
arg1.initGrid();                                     // 虚方法
arg1.onResize();
```

关键：**面板是 `Main` 的子进程，而暂停的是 `Game` 这个兄弟进程。**
`Process.updateAll` 每帧从 ROOTS（Main）递归，遇到 `paused == true` 的节点直接跳过 →
`Game`（连同它底下的整个 `Level`：英雄 / 怪物 / 弹幕 / 粒子 / 动画）全停在那一帧，
而面板自己照常 update，输入 / 关闭 / 恢复全由游戏主循环负责。

关闭走 `GridSelector.close()`：里面就是 `Game.Class.ME.resume()`，
`onDispose()` 再把 HUD 显示回来。**我们的代码完全不参与解锁，结构上不可能锁死。**

### 两个面板（都在 `ChronoBlade/ChronoPanels.cs`）

| 面板 | 基类 | 做法 |
|---|---|---|
| `ChronoWeaponPanel`（P） | `dc.ui.sel.FreeWeaponSelector` | **只**重写 `itemIsFiltered()` 放行两个 id；尺寸一个都不重写（严格原版 6 列 / 24×24） |
| `ChronoAmmoPanel`（X） | `dc.ui.sel.GridSelector` | `initGrid()` 出 12 格、`getIconBmp()` 用 TIMEKASAN 图集画罗马数字 |

- **`FreeWeaponSelector` 的 `weaponSpawner` 故意传 `null`**：
  它在 `onValidate()` 里对 `null` 有专门短路（`base.onValidate(); return;`），
  而 base（`ItemSelector.onValidate`）会照常 invoke 我们的 `validateCb` 再关闭面板 ——
  于是拿到了"等级 / 品质控件 + 确认回调"，又不需要训练场那个实体。
- **选完的武器从上方掉下来**：`new ItemDrop(level, cx, cy - 7, ...)` + `onDropAsLoot()`，
  靠重力落到英雄脚下。等级 / 品质走原版 `TrainingWeaponSpawner.Class.lootGen.finalizeItem()`
  + `addAffix("QualityUp")`，任何一步失败都退回"裸物品"，绝不把召唤本身搞挂。
- **选择武器框严格用原版尺寸**：不重写 `get_wid()` / `get_entryWid()` / `get_entryHei()`。
  ⚠️ 曾经把 `get_wid()` 收成 1 列来把框变高 —— 那不是原版尺寸，已改回。
  ⚠️ 也**不能**直接调大 `hei`：`moveSelection()` 用它当上下边界，
  会出现不存在的行 → `getEntryAt()` 返回 null → 崩。唯一安全的高度杠杆是 `get_wid()`。
- **弹药格子用 TIMEKASAN 图集**：`idle_0000…idle_0011` ↔ 罗马数字 I…XII，
  帧映射复用斩击刻印的 `ChronoFx.FrameIndexForRoman()`（已改 public）。两个坑：
  1. **锚点要左上角 `(0,0)`**：原版格子图标是 `dc.h2d.Bitmap`，包围盒 `[0,w]×[0,h]`（左上对齐）；
     HSprite 用居中锚点会偏半个身位，看起来就是"选择框中心对上了图集右下角"。
  2. **缩放加在外层 `dc.h2d.Object` 容器上**：`addEntryAt()` 会给 `getIconBmp()` 的返回值
     挂 `onBeforeReflow`，强制把 `scaleX/scaleY` 设成 `pixelScale`，加在 sprite 上会被覆盖。

### 兜底（构造中途抛异常时）

`pauseGame()` 在基类构造函数里就执行了，若之后（建 UI 时）抛异常，游戏会停在暂停里。
所以两个 `Open()` 都有：

1. **开面板前**用 `Data.item.byId` 确认两把武器真的进了 CDB（空网格 = 最难受的情况）；
2. `try/catch` 里调 `ChronoPanelLog.EmergencyCleanup()`：`Game.resume()` + `HUD.show()`
   + 关掉 `Main` 底下残留的半成品 `GridSelector` 进程；
3. 开完自检 `panel.items.length`，是 0 就立刻关掉。

## 按键（`coremod/config/ChronoBlade.json`，也可在 选项 → 模组 里改）

| 配置项 | 默认 | 作用 |
|---|---|---|
| `KeyWeaponPanel` | `P` | 选择武器面板（只列时之刃 / Zaphkiel） |
| `KeySelectBullet` | `X` | 选择弹药面板（十二之弹） |
| `KeySkill1` / `KeySkill2` | `U` / `I` | 一周飞镖 / 时钟剑雨 |
| `KeyTestNumeral` | `RightBracket` | 刻印渲染自测 |

**已删除**：`KeySummonWeapon`（`\`）与 `KeySummonPistol`（`P`）两个直接召唤热键，
以及它们的实现路径（原 `ChronoBladeMod.SummonWeaponDrop` / `DescribeItem` / `StripKey`）。
现在拿到武器的唯一途径就是 P 面板。

## 面板内操作（和原版一致）

`← →` 选择 · `Enter` 确认 · `Esc` 返回 ·
武器面板底部还有原版控件：品质（含传奇）/ 无色 / 等级。

## 相关文件

```
ChronoBlade/ChronoBlade/ChronoPanels.cs          两个面板 + 共用日志/兜底（新增，替代 ChronoSelectUi.cs）
ChronoBlade/ChronoBlade/ChronoBladeMod.cs        热键处理（只负责"读键 → 开面板"）
ChronoBlade/ChronoBlade/ChronoConfig.cs          按键配置（KeyWeaponPanel / KeySelectBullet）
coremod/config/ChronoBlade.json                  已落地的配置
coremod/logs/log_latest.log                      排查用日志（[ChronoBlade] 前缀）
```

参考（只读）：

```
GamePseudocode/dc.ui.sel/_GridSelector.cs            ← 压栈 + pauseGame() 的真身
GamePseudocode/dc.ui.sel/GridSelector.cs             ← pauseGame() / close() / onValidate()
GamePseudocode/dc.ui.sel/FreeWeaponSelector.cs       ← 等级/品质控件 + onValidate 的 weaponSpawner==null 分支
GamePseudocode/dc.pr/Game.cs                         ← modalPause() / resume()
GamePseudocode/dc.en.inter/TrainingWeaponSpawner.cs  ← 训练场：onActivate → FreeWeaponSelector → spawnItem
```

## 约束

- `GamePseudocode/` 与 `res/` **只读**，不要修改。
- 构建前必须关游戏（DLL 被占用会报 MSB3021）。
- 参考文档：https://dead-cells-core-modding.github.io/docs/zh/docs/

---

## 后续追加的四项（同样已完成）

1. **选完武器从英雄当前位置上方掉落**：`SpawnWeaponDrop` 改成和原版
   `TrainingWeaponSpawner.spawnItem` 一致 —— 构造时就落在 `hero.cy - 7` 格，
   `init()` → `onDropAsLoot()` → `setPosCase(drop.cx, drop.cy, hero.xr, hero.yr)`，
   之后靠重力落下。（原来用 `setPosPixel` 手工挪位，会脱离合法格子。）
2. **选择框高度只显示 80% 个武器**：原版高度公式在"单行"时退化（`cy - sectionIdx = 0`
   → 内容高 = 0 → 框高只剩 `pixelScale*32`，比卡片图标还矮）。
   修法：`initGrid()` 里把 `fbItems.padV` 调到 `BoxPadV = 12`。
   **padV 只进高度公式，宽度用 padH** → 宽度仍是严格原版尺寸。
3. **传奇词条**：时之刃 → `IgnoreGlobalShield`（原版词条，挂 id 即生效）；
   刻刻帝 → **在 affix 表里新建 `ChronoBulletDouble`**，并把 12 发子弹效果整体翻倍。
   要点：affix 表按位置反解分组，**追加到表尾**正好落进最后一个分组 `LegendaryOnly(group=4)`
   （和 item 表相反，那边表尾是 BossRushStatueUnlock，必须插进 Melee 段中间）；
   affix 的 `props` 是空壳，行为由 `ChronoBullets` 按 `InventItem.hasAffix()` 判定实现。
4. **弹药面板显示"这一发是干什么的"**：`GridSelector` 的 `initRightFlow/updateRightFlow`
   是空钩子，自己往 `mainFlow` 挂一个 `dc.ui.Text`，光标一动就刷新；
   文案按"当前这把枪是不是传奇"自动切换普通 / 翻倍两套说明。

