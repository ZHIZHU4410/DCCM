# 时之刃 ChronoBlade

Dead Cells（v35 / DCCM）新武器模组。一把以 **Katana 为基类** 的三段连击武器，三段分别致敬三个原版机制：

| 段 | 招式 | 参考对象 |
|---|---|---|
| 第 1a | **斩击刻印** —— 前冲斩，斩击路径上的敌人依次被斩，并在各自身上刻下罗马数字 I / II / III … | 武器 `Katana` 的斩击 |
| 第 2a | **一周飞镖** —— 原地以自身为中心，向一整圈（12 枚）甩出飞镖，附带时之守护者的金色法阵 | Boss `TimeKeeper` 的 `levelUpRadius` |
| 第 3a | **时钟剑雨** —— 唤出时之守护者的背景时钟，随后数把巨剑从天而降砸向附近怪物 | Boss `TimeKeeper` 的 `swordRain` |

> `GamePseudocode/`（反编译伪代码）与 `res/`（游戏资源）只读引用，本模组不修改其中任何文件。

---

## 目录结构

```
ChronoBlade/
├── 构建.bat                     一键构建（python 生成数据 + dotnet 编译安装）
└── ChronoBlade/
    ├── ChronoBlade.csproj       MDK 工程（CDB diff / 资源打包 / 自动安装）
    ├── ChronoBladeMod.cs        模组入口：Hook Weapon.create / Katana.hitFromWeapon、加载 res.pak、热键
    ├── ChronoBlade.cs           武器本体（继承 Katana，按连击段分派三套招式 + 罗马数字刻印）
    ├── ChronoFx.cs              特效层（罗马数字、飞镖圈、背景时钟、剑雨；HSprite 手动推进）
    ├── ChronoPanels.cs          两个全屏选择面板（P 选武器 / X 选弹药）+ 真暂停（Process 进程栈）
    ├── ChronoMobFinder.cs       附近怪物搜索（剑雨/飞镖选目标）
    ├── patch_chronoblade_cdb.py Python 秒级生成新武器的 data.cdb（item + weapon 表）
    └── data.cdb                 由脚本生成（构建时 diff 成 data.cdb_ 打进 res.pak）
```

---

## 构建

前置：`.NET SDK 10`、`Python 3`（本机均已具备）。

```powershell
cd "coremod\DCCMDEAD CELLS\ChronoBlade"
.\构建.bat
```

或手动：

```powershell
cd ChronoBlade
python patch_chronoblade_cdb.py
dotnet build -c Debug
```

构建产物会自动安装到 `coremod\mods\ChronoBlade\`：

```
ChronoBlade.dll     武器逻辑
modinfo.json        模组信息
res.pak             data.cdb_（新武器的 item + weapon 数据）
```

---

## 游戏内获取 / 测试

**先重启游戏**（`res.pak` 只在启动时加载；如果之前装过带 `lang/*.mo` 的旧版本，重启后文本才会恢复正常）。

进任意关卡后按 `P` 打开「选择武器」面板 —— 里面**只列本模组新增的两把武器**
（时之刃 ChronoBlade / Zaphkiel）。`← →` 选、`Enter` 确认，还能用面板底部的原版控件改
**等级 / 品质（含传奇）/ 无色**；选完武器会从英雄**上方掉下来**，走过去捡起即可装备。
`Esc` 返回。

> **打开面板期间游戏是「真暂停」的**：英雄、怪物、弹幕、粒子、动画全部停在那一帧
> （走的是原版暂停菜单 / 训练场选武器那套 `Process` 进程栈 + `Game.modalPause()`）。
> 关闭由游戏自己负责（`GridSelector.close()` → `Game.resume()`），模组不参与解锁，
> 所以**结构上不可能卡在暂停里**。

> ⚠️ 早先那套"按 `\` 直接掉一把时之刃 / 按 `P` 直接掉一把 Zaphkiel"的**直召热键已经删除**，
> 现在拿到武器的唯一途径就是 `P` 面板。（删直召和加面板是同一次改动，不存在中间态。）

按 `X` 打开「选择弹药」面板换 Zaphkiel 的十二之弹，同样是真暂停。

然后连续按主手攻击键打出三段：

1. **第 1 下**：前冲斩，路径上的敌人依次被斩并冒金色罗马数字
2. **第 2 下**：原地一圈飞镖 + 金色法阵
3. **第 3 下**：背景时钟展开旋转，数把剑从天上砸向最近的怪物；同时保留 Katana 的冲刺斩位移与群伤

控制台日志关键字 `[ChronoBlade]`：

* `CDB 自检：ChronoBlade（本模组新增）存在，group=4` —— data.cdb 补丁生效
* `武器就绪：skills=3 段 / strikeChain=3 段` —— 捡起武器后技能建起来了
* `选择武器面板已打开（真暂停 / Process 栈）：网格载入 2 项` —— P 面板生效
* `选择弹药面板已打开（真暂停 / Process 栈）` —— X 面板生效
* `已召唤时之刃（Lv1 / 品质0）：从英雄上方掉落` —— 面板确认后掉落生效

如果按 `P` 没有任何反应，按顺序看：

* 完全没有 `[ChronoBlade] 时之刃已注册` → 模组没被加载（检查 `coremod\mods\ChronoBlade\`）
* `CDB 自检：找不到 ChronoBlade` → data.cdb 补丁没生效
* `选择武器面板：CDB 里找不到 ...` → 同上，面板会**主动不打开**（避免空网格）
* `选择武器面板：网格载入 0 项` → 过滤逻辑没放行，把日志发我
* `打开选择武器面板失败，已强制恢复: ...` + 堆栈 → 面板构造中途抛异常（已自动解暂停），把堆栈发我
* 掉出来了但捡不起来 → 看是否报 `武器就绪` 那行是"⚠ 武器初始化异常"

---

## 实现要点

### 1. 新武器数据（Python 生成 data.cdb）
MDK v35 的模板 `data.cdb` 是 JSON。`patch_chronoblade_cdb.py` 直接读它、追加两行再写回：

* `item` 表：`id="ChronoBlade"`（group=4 近战、Brutality、可掉落、图标沿用 `cardIcons.png`）
* `weapon` 表：`item="ChronoBlade"`，3 条 `strikeChain`（第 1~3 段）

构建时 `DCCMTool cdb diff` 只把差异部分打成 `data.cdb_`，游戏加载 `res.pak` 时自动合并 —— 不需要改原版 `res/data.cdb`。

### 2. 武器类（ChronoBlade.cs）
继承原版 `Katana`，复用它的居合冲刺/斩击执行流程，只按**连击段**分派：

```csharp
public override bool onExecute()
{
    switch (_cycle)
    {
        case CycleShuriken:  ...  // 第 2a：一周飞镖
        case CycleSwordRain: ...  // 第 3a：时钟 + 剑雨
        default:             return base.onExecute();   // 第 1a：原版斩击
    }
}
```

第 3 段通过 `set_cycle(2)` 时置 `nextIsChargeAtk = true`，使原版走满蓄力冲刺斩分支（位移 + 路径群伤），时钟与剑雨特效再叠加在上面。

### 3. 罗马数字刻印
`Katana.hitFromWeapon` 在哈希链接绑定里不是可被 C# 重写的虚方法，因此走全局 Hook：

```csharp
Hook_Katana.hitFromWeapon += OnKatanaHitFromWeapon;
```

命中 `Mob` 且武器是 `ChronoBlade` 时，用 `Assets.gameElements` 图集的字形 `I`、`II`、`III`… 在敌人头顶生成金色发光的 `HSprite`，向上飘散（`ChronoFx`）。

### 4. 特效层（ChronoFx.cs）
全部复用游戏自带图集 `atlas/fxTimeKeeper.atlas`，不新增美术资源：

* `fxKingsBladeCast` —— TimeKeeper 放 `levelUpRadius` 时的金色法阵，放大 4.4 倍并旋转即"背景时钟"
* `fxThrowShuriken`  —— 飞镖镖身
* `kingsBladeFxDash`  —— 王者之剑冲刺剑影，当作砸下来的剑

做法是把 `HSprite` 挂到 `level.scroller` 的 `DP_ROOM_FRONT` 层，每帧在 `IOnHeroUpdate` 里手动推进透明度 / 缩放 / 旋转 / 下落，生命周期结束自动摘除。这样不需要新增 Entity 类，也不依赖哈希链接的自定义对象构造。

### 5. 为什么不打包 lang/*.mo（重要教训）
早期版本用 Python 重新生成 `res/lang/main.*.mo` 并打进 `res.pak` 想汉化武器名，结果**全游戏文本错乱**。

原因在 `dc.libs.data.MoReader.parse()` + `GetText.readMo()`：它是"**整表替换**"语义 —— `parse()` 只把新 `.mo` 的条目装进 `StringMap`，`readMo()` 直接 `texts = stringMap` 覆盖整个文本字典。只含 2~3 个条目的补丁 `.mo` 等于把全游戏文本换掉了，于是 UI 文本全乱。

结论：**要覆盖 .mo 必须提供与原版条目数量、排序、哈希表完全一致的完整文件**，否则不要打包任何 `lang/` 内容。武器名现在直接走 CDB 的 `name` 字段（`GetText.get()` 查不到就原样返回）。

### 6. Hook 委托签名必须精确匹配（踩过的坑）
第一版把 `tool.$Weapon.create` 的 Hook 处理器第一个参数写成了 `object`：

```csharp
// ❌ 会崩游戏
private Weapon OnWeaponCreate(object orig, Hero hero, InventItem item)
```

游戏在 `Hero.init()` → `HeroActiveSkillsManager.onEquippedItemsUpdated()` 里会为初始武器调用 `tool.$Weapon.create`，于是启动瞬间就炸：

```
System.MissingMethodException: Method 'System.Object.Invoke' not found.
   at Hashlink.Utils.GetDelegateInvoke(Type type)
   at Hashlink.UnsafeUtilities.UtilityDelegates.CreateAdaptDelegate(...)
   at ModCore.Hooks.HashlinkHookManager.HookEntry(Object[] args)
```

正确写法是声明一个与目标函数签名一致的委托（见 `ChronoWeaponFactory.cs`）：

```csharp
public delegate Weapon orig_create(Hero hero, InventItem item);          // 委托类型必须精确匹配
public static Weapon Hook_create(orig_create orig, Hero hero, InventItem item)
```

**规则：`orig` 绝不能用 `object` / `Delegate` 糊，必须是 DCCM 能按原函数签名适配的强类型委托。** 其余 Hook 走框架生成的 `Hook_XXX.方法名 +=` 事件包装（如 `Hook_Katana.hitFromWeapon`）最稳，不会踩这个坑。

### 7. 新物品必须落在正确的 CDB 分组里（踩过的坑）
`item` 表是按 `separator` 切段的，段名列表在 `item` 表自己的 `props.separatorTitles` 里：

```
[DeployedTrap, Grenade, SideKick, Power, Melee, Ranged, Shield, Talisman,
 BagItem, Meta, Consumable, PreciousLoot, Perk, Skin, Head, Aspect, BossRushStatueUnlock]
                                 ↑ 下标 = group 值，Melee = 4
```

`CDBManager` 加载时会执行 `group = separatorByName(行的 __separator_group_Name)`，**用段名反查出的分组 ID 覆盖每行的 `group` 字段**（`dc.tool.mod.CDBManager.cs:1599-1607`）。

第一版把新物品 `append` 到 `item` 表末尾 → 落进最后一个段 `BossRushStatueUnlock`，`group` 被算成 **16**。Boss Rush 雕像解锁项不是装备，于是：

* 背包 `add()` / `equip()` 直接无视它 → 按了热键也看不到武器
* 日志里体现为 `group=16`

修法：`patch_chronoblade_cdb.py` 现在把新行 **插进 Melee 段的内部**（段起点 + 32 行），DCCMTool 便会按位置算出正确元数据：

```json
{ "group": 4, "__separator_group_Name": "Melee", "__separator_group_ID": 4 }
```

⚠️ `__separator_group_Name` 不是"我填什么就是什么"——DCCMTool 会按行的插入位置重算，所以**位置必须真的落在目标段里**。

### 8. 召唤武器走 ItemDrop，不要手工塞背包（踩过的坑）
为了"快速拿到武器测招式"，最初两版分别试过：

1. `hero.inventory.add(item)` —— 武器格满了直接抛 hashlink 异常：
   `You can't stock Weapon : ChronoBlade - Use ReplaceItem UI`
2. 手工 `item._itemData = Data.Class.item.byId.get("ChronoBlade")` —— `byId.get()`
   返回的是 `HashlinkObj` 代理（`HaxeDynObj`），赋给 `_itemData` 会炸：
   `InvalidCastException: Unable to cast HaxeDynObj to virtual_ambiantDesc_...`，
   第二次调用更糟：`LockRecursionException: A read lock may not be acquired with the write lock held`

最终采用 `chuanqiang+canying` 模组的做法 —— 走原版掉落
（见 `ChronoPanels.cs` 的 `ChronoWeaponPanel.SpawnWeaponDrop`）：

```csharp
var item = new InventItem(new InventItemKind.Weapon(ChronoBlade.name.AsHaxeString()));
bool inArmory = false;
var drop = new ItemDrop(hero._level, hero.cx, hero.cy - 7, item, true, new Ref<bool>(ref inArmory));
drop.init();            // 必须调用，否则崩
drop.onDropAsLoot();    // 交给原版掉落 / 拾取流程
drop.setPosPixel(...);  // 挪到英雄上方，让它自己掉下来（训练场那套手感）
```

**结论：给玩家塞自定义武器，最稳的是 `ItemDrop` + `onDropAsLoot()`，让游戏自己去做
拾取判定、HUD 刷新、技能初始化与武器替换。** 不要手工构造 `_itemData`，也不要用
`Inventory.add()` 绕过"背包已满"检查。

### 9. 全屏选择面板 + 真暂停：压 Process 栈，别手写 `Game.paused`（踩过的坑）

两个面板（`P` 选武器 / `X` 选弹药）都在 `ChronoPanels.cs`，都继承原版的
`dc.ui.sel.GridSelector`（`FreeWeaponSelector` 也是它的后代）。

**上一版为什么"暂停后恢复不了"**：曾经在 `dc.pr.Game.update` 的钩子里切换 `paused`，
但 `paused = true` 之后**这个 `update` 根本不再被调用** → 解除暂停的代码永远跑不到，
连 60 秒兜底也写在同一个钩子里，救不回来。

> **教训：解除开关不能装在它自己要关掉的那扇门后面。**

现在的做法就是原版暂停菜单 / 训练场那套 —— `GridSelector` 的构造函数里：

```
_Process.__inst_construct__(arg1, Main.Class.ME);   // 挂到 Main 进程栈（不是 Game 底下！）
arg1.pauseGame();                                    // HUD.hide() + Game.modalPause() = 真暂停
arg1.initGrid();                                     // 虚方法，我们重写它来定网格内容
```

**面板是 `Main` 的子进程，暂停的是 `Game` 这个兄弟进程。** `Process.updateAll` 每帧从
ROOTS（Main）递归，遇到 `paused == true` 的节点直接跳过 → `Game` 连同它底下的整个 `Level`
（英雄 / 怪物 / 弹幕 / 粒子 / 动画）全停在那一帧，而面板自己照常 update。
关闭走 `GridSelector.close()`，里面就是 `Game.Class.ME.resume()` ——
**模组完全不参与解锁，结构上不可能锁死。**

两个面板的实现差别只在"网格内容"：

| 面板 | 基类 | 重写了什么 |
|---|---|---|
| `ChronoWeaponPanel`（P） | `FreeWeaponSelector` | **只**重写 `itemIsFiltered()` 放行两个 id |
| `ChronoAmmoPanel`（X） | `GridSelector` | `initGrid()` 出 12 格、`getIconBmp()` 用 TIMEKASAN 图集画罗马数字 |

* `FreeWeaponSelector` 的 `weaponSpawner` **故意传 `null`**：它 `onValidate()` 里对 `null`
  有专门短路，而 base 会照常 invoke 我们的 `validateCb` 再关闭面板 —— 于是既拿到
  "等级/品质控件 + 确认回调"，又不需要训练场那个实体。

#### 选择武器框：严格用原版尺寸（不重写任何尺寸方法）

尺寸**一个都不重写** —— `ItemSelector` 给的就是原版训练场那套：`get_wid() = 6` 列、格子 24×24，
于是 `GridSelector.onResize()` 算出来的选择框就是原版大小：

```
选择框宽 = pixelScale * (6 * (24 + padH*2) + 22)
选择框高 = pixelScale * (内容高 + 行数 * padV*2 + 22)
```

> ⚠️ 曾经为了"框高一点"把 `get_wid()` 收成 1 列 —— 那确实会让两把武器分成两行、框高约 3 倍，
> 但**已经不是原版尺寸了**，已改回。
> 另外**绝对不能**直接把 `hei` 调大：`moveSelection()` 拿 `hei` 当上下移动边界，
> 调大之后能移动到不存在的行，`getEntryAt()` 返回 `null` → 当场崩。
> 真要调高度，唯一安全的杠杆就是 `get_wid()`（列数）。

#### 弹药格子：复用斩击刻印那套帧映射

`idle_0000…idle_0011` ↔ 罗马数字 I…XII，映射表就是斩击刻印用的
`ChronoFx.FrameIndexForRoman()`（现已是 public），三处（刻印 / 开火蹦字 / 弹药面板）共用一张表。

这里有两个必须踩对的坑：

1. **锚点必须用左上角 `(0, 0)`，不能用居中 `(0.5, 0.5)`。**
   原版默认的格子图标是 `dc.h2d.Bitmap`（`Icon : Bitmap`），包围盒 `[0,w]×[0,h]` —— 左上角对齐格子，
   `GridSelector` 就是按这个约定摆格子的。HSprite 一旦用居中锚点，包围盒变成
   `[-w/2,w/2]×[-h/2,h/2]`，整张图往左上偏半个身位，表现出来正好是
   "选择框的中心对上了图集的右下角"。改成 `centerFactor = (0,0)` 后图集右下角就落在框的右下角上。

2. **缩放必须加在「外层容器」上，不能加在 sprite 自己身上。**
   `GridSelector.addEntryAt()` 会给 `getIconBmp()` 的返回值挂一个 `onBeforeReflow`，
   里面**强制**把 `scaleX/scaleY` 设成 `pixelScale`。加在 sprite 上下一帧就被覆盖，
   数字会撑爆整个格子。所以做法是 `dc.h2d.Object` 容器 + 里层 HSprite 自己缩到
   `格子高/430`（铺满 36×32 的格子）。

* 兜底：`pauseGame()` 在基类构造里就跑了，所以两个 `Open()` 都先查 CDB、再
  `try/catch` + `ChronoPanelLog.EmergencyCleanup()`（`resume()` + `HUD.show()` + 关掉残留进程）。

#### 选择框高度：原版公式在"单行"时会退化

题外话但很关键。宽度用原版的 6 列就准确了，但高度只够显示 **80% 个武器**，因为

```
内容高   = max( (entry.cy - entry.sectionIdx) * (条目高 + pixelScale*10) )
选择框高 = pixelScale * (内容高 + 行数 * padV*2 + 22)
```

两把武器只占一行 → `cy` 全是 0 → 内容高算成 0 → 框高只剩 `pixelScale*32`，
比一张武器卡片图标还矮一点，底边被裁掉。

修法是把 `fbItems.padV` 调大（`BoxPadV = 12`）：**padV 只进高度公式，宽度用的是 padH**，
所以宽度仍然是严格原版尺寸。钩子必须挂在 `initGrid()` 里 ——
构造顺序是「建 fbItems → initRightFlow → **initGrid** → **onResize**」，
onResize 是运行时读 `fbItems.padV` 的，晚于 initGrid 改就没用了。

#### 弹药面板会显示"这一发是干什么的"（在框内底部）

`GridSelector` 的 `initRightFlow()` / `updateRightFlow()` 默认是**空实现**（框架留的钩子，
`ItemSelector` 就是拿它画右侧物品说明的）。弹药不是 CDB 物品、没有 `NewItemDesc` 可用，
所以这里自己放一个 `dc.ui.Text`：

```
第 3 / 12 发 · 三之弹 Gimel
开火即生效：回复 30% 生命 + 移速 ×2.0（affect 116 +1.0），持续 10 秒
```

文案取自 `ChronoBullets.BulletDef.Desc` / `.DescLegendary`，
会根据**当前这把枪是不是传奇**自动切换（`TimeBullet.IsLegendaryDouble`）。

> ⚠️ 这个 Text **必须挂在 `mask` 上**（框内可视区），自己算 x/y 贴到框内**左下角**；
> **绝不能挂到 `mainFlow` 上**。挂 mainFlow 时文字高度一变，mainFlow 就重新居中，
> 光标换到下一行时整块面板会跟着上下跳 —— 就是"到下一行集体往下移"那个 bug。
> 挂 `mask` 且手动定位则完全不影响任何 Flow 布局。
>
> 另外 `BoxPadV = 14` 是为了在框内底部腾出这段文字的高度（padV 只影响高度，不影响宽度）。

#### 条目网格必须每帧钉回框顶

撑高框之后带出一个副作用：原版 `updateScrollingBox()` 的落点是

```
num6  = mask.height - 内容高 - 5*px        // 框比内容高的"富余量"
num10 = (cy2 < num6) ? num6 : ...           // 富余量更大 → 直接把内容推到 num6
```

也就是**"框有富余高度就把内容压到底部"**。框一高，富余量就大，
条目整块被推到框底、上面空一大片（截图里"对齐到最下面"就是这个）。

拦不住的地方在于：GameProxy 里 `updateSelection(ref bool)` 是**普通方法**，
只有 `updateSelection(Ref<bool>)` 才是 virtual —— 重写哪个都不能保证拦得住。
所以改成在 `postUpdate()`（virtual、每帧在 update 之后 / 渲染之前执行）里把
`wrapperItem.y` 钉成 `(int)(pixelScale*5)`：`ChronoPanelLog.PinGridToTop()`，**两个面板都调用**。

> 注意要跟着原版一样**截断成 int**，否则每帧差不到 1px，会一直置 `posChanged`。

---

## 传奇词条（legendAffixes）

`item.legendAffixes` 是"这件装备当传奇时**可以 Roll 到的词条池**"。两条都是 CDB 侧改的
（`patch_chronoblade_cdb.py`）：

| 武器 | 词条 | 说明 |
|---|---|---|
| 时之刃 ChronoBlade | `IgnoreGlobalShield` | **无视防御盾**。原版已有词条，`props` 是空壳、行为写在游戏代码里，挂 id 即生效，不需要我们实现 |
| 刻刻帝 Zaphkiel | `ChronoBulletDouble` | **本模组在 affix 表里新建的一条**，让 12 发子弹的效果整体翻倍 |

### 新建 `ChronoBulletDouble` 的两个要点

1. **affix 表和 item 表一样是"按位置反解分组"的**：
   `props.separatorTitles = [Tier, Special, Basic, Advanced, LegendaryOnly]`、
   `separators = [0, 4, 22, 87, 127]`。传奇词条都在最后一个分组 `LegendaryOnly(group=4)`，
   它一直延伸到表尾 —— 所以**直接追加到表尾**就会被算成 group=4。
   （⚠️ 这点和 item 表相反：item 表最后一个分组是 `BossRushStatueUnlock`，追加会落错组，
   所以那两行必须插进 `Melee` 段中间。）
   生成的 diff 里能看到 DCCMTool 回填的 `__separator_group_Name: "LegendaryOnly"`。
2. **affix 的 `props` 是空壳，行为必须自己实现**：
   原版所有传奇词条（`IgnoreGlobalShield`、`DoubleSpeed`…）在 CDB 里 `props` 都是空的，
   效果写死在游戏代码里按 id 判定。我们的 `ChronoBulletDouble` 同理 ——
   CDB 那条只负责"有名字、有图标、能被 `legendAffixes` 引用"，
   真正的翻倍逻辑在 `ChronoBullets` 里，靠 `InventItem.hasAffix("ChronoBulletDouble")` 判定。

> ⚠️ 判定要用 `InventItem.hasAffix()`，**不能**去读 `_itemData.legendAffixes` ——
> 后者是"可 Roll 的池子"，不是"已经 Roll 到的词条"，永远为真。

### 12 发子弹的翻倍对照（`ChronoBullets`）

| # | 子弹 | 普通 | 传奇（翻倍） |
|---|---|---|---|
| 1 | Aleph | 移速 ×5.0（affect +4.0），10s | 移速 **×10.0**（affect +9.0） |
| 2 | Bet | 目标移速 ×0.45（减速） | 目标移速 **×0.225**（减速幅度翻倍） |
| 3 | Gimel | 回复 30% 生命 + 移速 ×2.0 | 回复 **60%** + 移速 **×4.0** |
| 4 | Dalet | 拽回 5 秒前 | 拽回 **10 秒**前 |
| 5 | Hei | 全图视野 | **不变**（无"量"可翻） |
| 6 | Vav | 拽回 15 秒前 | **不变**（按要求） |
| 7 | Zayin | 时停 3 秒 | 时停 **6 秒** |
| 8 | Het | 召唤 3 只，存活 10s | 召唤 **6 只**，存活 **20s** |
| 9 | Tet | 随机传送 | **不变**（无"量"可翻） |
| 10 | Yud | 记忆动画 3 秒后处决 | 记忆动画 **1.5 秒**后处决 |
| 11 | YudAleph | 前突 6 格 + 无敌 2s | 前突 **12 格** + 无敌 **4s** |
| 12 | YudBet | 回到上一关 | **不变**（无"量"可翻） |

两个容易写错的地方：

* **"移速翻倍"要先翻倍倍率再减 1**。affect 116 是**加上去**的值（基础跑速倍率 1.0），
  所以 ×2.0 传的是 +1.0。想把倍率翻成 ×4.0 必须传 +3.0 —— 直接把 +1.0 翻倍成 +2.0
  只有 ×3.0。见 `SpeedAffectFromMultiplier()`。
* **负面效果（减速）要缩小倍率**：×0.45 → ×0.225。把 0.45 × 2 变成 ×0.90 是"减速减弱"，方向反了。
* 传奇的 `Yud` 处决延时和记忆动画时长**必须是同一个值**（都 1.5s），
  否则会出现"动画播完了人还活着"或者反过来，所以 `PlayMemoryAt()` 现在接收时长参数。

### 兜底：为什么"面板选传奇出来的是普通货"

原版训练场选武器能出传奇，是因为它**一定在训练场里**：传奇那一步靠
`TrainingWeaponSpawner.Class.lootGen.finalizeLegendaryItem()`，而那个 LootGen

> **只在 `_TrainingWeaponSpawner.__inst_construct__`（真的摆了一个武器生成器实体）时才创建**；
> 普通关卡里 `TrainingWeaponSpawner.Class.lootGen` 是 **null**。

所以任意关卡按 `P` 时，以前 `gen == null` → 整段 LootGen 调用被跳过 →
物品**根本没有 `"Legendary"` 词条** → 既不是传奇外观、也不会把 `legendAffixes`
里的词条算进说明（"选了传奇却不出传奇、也不显示传奇词条"就是这个原因）。

现在 `MakeItem()` 不再依赖它：

1. LootGen 拿得到就照原版走（顺带处理等级 / 基础数值）；
2. 拿不到就**自己显式补上 `"Legendary"` 词条**，再挂本模组的传奇词条；
3. 传奇**不叠 `QualityUp`**（原版传奇分支里 `set_weaponQuality(0)`，那条循环加 0 次）。

### 十二之弹回不了上一关

关卡 id 的正确取法参考 `ModEntry.cs`（那个模组是能用的）：

```csharp
string currentMapId = me._level.map.id.ToString();
```

**`dc.level.LevelMap` 上直接就有 `id`**（`LevelMap.cs:40 public extern String id`）。
之前两次都错了：

1. `dynamic d = entry; d.id` —— DCCM 的 hashlink 代理上**动态绑定不可靠**，每次抛异常
   → 归一化成空串 → 只打一行"读不出上一关的 id"就 return 了；
2. 改成读 `map.infos.id` —— 那是 CDB 的**关卡定义**行，语义和 `map.id` 不一定一致。

现在：

* 主路径 **强类型** `map.id`（+ 反射读 `id` 作兜底，照 `ChronoCdbProbe` 的做法）；
* **自己记录关卡历史**：`ChronoBullets.TrackLevel()` 每帧在 `OnHeroUpdate` 里比较
  `hero._level.map.id`，一变就记一笔，十二之弹直接取上一条。
  不再依赖 `serverStats.history` 的语义（是否含当前关、会不会重复入栈、读档后还在不在都不确定）；
* `serverStats.history` 只作为二级兜底。

> 副作用：**刚进游戏还没换过关时用不了**（历史里只有一关），日志会提示
> "找不到上一关（当前关 X，自记录 0 关）—— 换过关之后就能用"。这是预期行为。

---

## 已知限制

* 三段连击共用 `AtkKatanaA` 起手动画（没有为武器新增帧动画），区分靠特效与音效。
* 剑雨的伤害由第三段保留的原版冲刺斩提供（路径群伤）；落在远处的剑是视觉表现。
* 罗马数字用 `gameElements` 图集的字形拼装，数字超过 XX 会退化为阿拉伯数字。
* 目前只有 `P` 面板召唤（地面掉落），没有做"收藏家蓝图解锁"（要进商店/正常掉落需要额外的 blueprint 流程）。
* 面板里的"等级 / 品质"通过 `TrainingWeaponSpawner.Class.lootGen` 套用；若该 LootGen
  取不到，物品会以"裸物品"掉出（召唤本身不会失败），日志里会有
  `LootGen 处理等级失败（改用裸物品）`。
* `ChronoBulletDouble` 的说明文字是**直接写在 CDB 里的英文**（`desc` 字段），
  没有走 `lang/*.mo` —— 那会把整张文本表替换掉，详见第 5 节。

