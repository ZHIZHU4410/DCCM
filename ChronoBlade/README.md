# 时崎狂三 · ChronoBlade（时之刃）

Dead Cells（v35 / DCCM）武器模组。新增 **两把武器** 与 **两个全屏选择面板**（真暂停）。

* **时之刃 ChronoBlade** —— 近战，三段连击，每段致敬一个原版机制
* **Zaphkiel 刻刻帝** —— 远程手枪，十二发子弹各自带一套时间系效果，可换弹

> `GamePseudocode/`（反编译伪代码）与 `res/`（游戏资源）只读引用，本模组不修改其中任何文件。

---

## 功能

### 1. 时之刃 ChronoBlade（近战 / 三段连击）

继承原版 `Katana`，复用它的居合冲刺与斩击流程，只按**连击段**分派招式：

| 段 | 招式 | 效果 | 参考对象 |
|---|---|---|---|
| 第 1a | **斩击刻印** | 前冲斩，斩击路径上的敌人依次被斩，各自刻上罗马数字 I / II / III … | 武器 `Katana` 的斩击 |
| 第 2a | **一周飞镖** | 原地以自身为中心，向一整圈（12 枚）甩出飞镖，附带时之守护者的金色法阵 | Boss `TimeKeeper` 的 `levelUpRadius` |
| 第 3a | **时钟剑雨** | 唤出时之守护者的背景时钟旋转展开，随后数把巨剑从天而降砸向附近怪物 | Boss `TimeKeeper` 的 `swordRain` |

第 2a / 3a 都是**表现层 + 实体层**双份 —— 除了特效，还有真正会飞 / 会砸、会打伤害的投射物：

| 段 | 表现层（特效） | 实体层（真投射物） |
|---|---|---|
| 第 2a | 金色法阵 + 一圈飞镖贴图 | **12 枚 `dc.en.bu.Saw`（旋转刃）**，以英雄为中心向一整圈甩出，每枚 20 基础伤害 |
| 第 3a | 背景时钟展开 + 砸下来的剑影 | **最多 6 柄 `dc.en.bu.Stalactite`（从天而降）**，每个目标点一柄，每柄 55 基础伤害 |

伤害走原版统一入口 `AttackUtils.Class.createFromHero(hero, power, null)` —— 它会把
`useHeroScaling` 打开，**伤害自动跟英雄的属性 / 等级 / 变异缩放**，不需要自己算。

> ⚠️ **为什么实体不用时之守护者本人那两套贴图**（引擎限制，不是偷懒）：
> * `dc.en.bu.TimeKeeperShuriken` 的发光逻辑要 `be.onions`（boss 专属贴图池）、取色要 `be._infos`；
> * `dc.en.mob.boss.TimeKeeperSword` 的**构造函数里**就 `be.getOldSkillInfos("swordRain").props.duration`，
>   update 里还要 `be.brutalityTier` / `be.cy` / `be.get_tmod()`。
>
> 两者的构造参数类型都是 `TimeKeeper`，**普通关卡里没有 boss 实例**，传 Hero 进去必 NPE。
> 所以改用上面那两个"玩家可拥有"（构造函数只收泛型 `Entity from`）的同类投射物，
> 表现层仍然是时之守护者的原版特效。

* **罗马数字刻印**：命中怪物时用 `atlas/TIMEKASAN.atlas` 的帧（`idle_0000…idle_0011` ↔ I…XII）在敌人头顶生成金色数字，向上飘散。与「开火蹦字」「弹药面板图标」**共用同一张帧映射表**（`ChronoFx.FrameIndexForRoman`）。
* **传奇词条**：`IgnoreGlobalShield`（**无视防御盾**）—— 原版已有词条，挂 id 即生效。

> 第 2a / 3a **只按连击段触发**（连续按主手攻击键打出第 2、第 3 下）。
> 早先那套 `U` / `I` 独立热键技能**已经删除** —— 它们被并回武器本体，不再需要额外按键。

### 2. Zaphkiel 刻刻帝（远程 / 十二之弹）

以原版 `Pistol` 为模板。**开火只发射当前装填的那一发**（不会自动换弹），换弹只能用 `X` 面板。

十二发子弹，`一之弹` 到 `十二之弹` 依次对应罗马数字 I…XII：

| # | 名称 | 效果（普通） | 触发方式 | 传奇（带 `ChronoBulletDouble`） |
|---|---|---|---|---|
| 1 | 一之弹 Aleph | 自身移速 ×5.0（affect 116 +4.0），持续 10 秒 | 开火即生效 | 移速 **×10.0** |
| 2 | 二之弹 Bet | 目标移速 ×0.45（减速），持续 10 秒 | 需命中 | 移速 **×0.225**（减速翻倍） |
| 3 | 三之弹 Gimel | 回复 30% 生命 + 移速 ×2.0，持续 10 秒 | 开火即生效 | 回复 **60%** + 移速 **×4.0** |
| 4 | 四之弹 Dalet | 把目标拽回 5 秒前的位置与生命 | 需命中 | 拽回 **10 秒**前 |
| 5 | 五之弹 Hei | 获得全图视野（等同探险家符文） | 开火即生效 | 不变 |
| 6 | 六之弹 Vav | 把目标拽回 15 秒前的位置与生命 | 需命中 | 不变 |
| 7 | 七之弹 Zayin | 时停 3 秒（移速归零 + 禁止攻击） | 需命中 | 时停 **6 秒** |
| 8 | 八之弹 Het | 在命中点召唤我方怪物，最多 3 只、存活 10 秒 | 需命中 | 最多 **6 只**、存活 **20 秒** |
| 9 | 九之弹 Tet | 随机传送到本关任意位置 | 开火即生效 | 不变 |
| 10 | 十之弹 Yud | 目标头顶播放记忆动画 3 秒，动画结束立即处决 | 需命中 | 动画 **1.5 秒**后处决 |
| 11 | 十一之弹 Yud-Aleph | 向前突进 6 格 + 无敌 2 秒，2 秒后拉回原位 | 开火即生效 | 突进 **12 格** + 无敌 **4 秒** |
| 12 | 十二之弹 Yud-Bet | 回到上一关 | 开火即生效 | 不变 |

* **传奇词条**：`ChronoBulletDouble`（说明文字「**每个弹药效果增强**」）—— 本模组**在 CDB 的 affix 表里新建**的一条，让上表右列全部生效。
* **身后背景**：主手拿着它时，英雄身后循环播放 `TIMEBEIJING` 背景。
* **拾取音效**：自带 `sfx/CHUXIAN.WAV`。

### 3. 两个全屏选择面板（**真暂停**）

| 面板 | 默认键 | 内容 |
|---|---|---|
| 选择武器 | `P` | **只列本模组新增的两把武器**；可用面板内原版控件改**等级 / 品质（含传奇）/ 无色** |
| 选择弹药 | `X` | Zaphkiel 的十二之弹；**底部显示当前这一发是干什么的**（左对齐，在框内） |

* 面板内操作：`← →` 选择 · `Enter` 确认 · `Esc` 返回。
* 打开期间游戏是**真暂停**：英雄、怪物、弹幕、粒子、动画全部停在那一帧。
* 选完武器从英雄**当前位置上方 7 格**掉落，走过去捡起即可装备。
* 弹药面板的说明会根据**当前这把枪是不是传奇**自动切换普通 / 翻倍两套文案。

### 4. 其它

* **怪物死亡特效**：怪物死亡时在尸体位置播放 `TIMEJIBAI`（可在配置里关）。
* **刻印渲染自测**：默认 `]`，在最近的怪物身上直接画一个罗马数字，单独验证渲染链路。

---

## 按键

全部走 `coremod/config/ChronoBlade.json`（也可在游戏的 **选项 → 模组** 菜单里改）。
支持 `A~Z`、`0~9`、`F1~F12`、`Space/Tab/Shift/Backslash/RightBracket…`，也可以直接写十六进制 `"0x51"`。

| 配置项 | 默认 | 作用 |
|---|---|---|
| `KeyWeaponPanel` | `P` | 选择武器面板 |
| `KeySelectBullet` | `X` | 选择弹药面板 |
| `KeyTestNumeral` | `RightBracket` | 刻印渲染自测 |
| `EnableDeathEffect` | `true` | 是否启用死亡特效 |

> ⚠️ 早先那套「按 `\` 直接掉一把时之刃 / 按 `P` 直接掉一把 Zaphkiel」的**直召热键已经删除**，
> 现在拿到武器的唯一途径就是 `P` 面板。（删直召和加面板是同一次改动，不存在"没有获取途径"的中间态。）

---

## 获取与操作

**先重启游戏**（`res.pak` 只在启动时加载）。

1. 进任意关卡，按 `P` → 选武器（可选等级 / 品质 / 传奇 / 无色）→ `Enter` → 武器从上方掉下来 → 捡起装备。
2. 连续按主手攻击键打出三段：前冲斩（冒罗马数字）→ 一圈飞镖 → 背景时钟 + 剑雨。
3. 想换 Zaphkiel 的弹种：拿起它，按 `X` → 看说明选一发 → `Enter`。

控制台日志关键字 `[ChronoBlade]`：

```
CDB 自检：ChronoBlade（本模组新增）存在，group=4        —— data.cdb 补丁生效
武器就绪：skills=3 段 / strikeChain=3 段              —— 捡起武器后技能建起来了
选择武器面板已打开（真暂停 / Process 栈）：网格载入 2 项   —— P 面板生效
选择弹药面板已打开（真暂停 / Process 栈）               —— X 面板生效
已召唤时之刃（Lv1 / 品质0）：从英雄当前位置上方 7 格掉落    —— 面板确认后掉落生效
```

---

## 构建

前置：`.NET SDK 10`、`Python 3`。

```powershell
cd "coremod\DCCMDEAD CELLS\ChronoBlade"
.\构建.bat
```

或手动：

```powershell
cd ChronoBlade
python patch_chronoblade_cdb.py     # 生成 data.cdb（item / weapon / affix 表）
dotnet build -c Debug               # 自动 diff CDB + 打包 + 安装
```

> ⚠️ **构建前必须关游戏**，否则 DLL 被占用会报 `MSB3021`。

构建产物自动安装到 `coremod\mods\ChronoBlade\`：

```
ChronoBlade.dll     武器逻辑
modinfo.json        模组信息
res.pak             data.cdb_（两把武器的 item + weapon 数据、新 affix）
```

---

## 目录结构

```
ChronoBlade/
├── 构建.bat                        一键构建
├── README.md
└── ChronoBlade/
    ├── ChronoBlade.csproj          MDK 工程（CDB diff / 资源打包 / 自动安装）
    ├── ChronoBladeMod.cs           模组入口：Hook、加载 res.pak、热键、每帧驱动
    ├── ChronoBlade.cs              时之刃本体（继承 Katana，按连击段分派）
    ├── TimeBullet.cs               Zaphkiel 本体（继承 Pistol）+ 传奇词条判定
    ├── ChronoBulletFx.cs           十二之弹的效果层（ChronoBullets）
    ├── ChronoPanels.cs             两个全屏选择面板 + 真暂停
    ├── ChronoFx.cs                 特效层（罗马数字 / 飞镖圈 / 背景时钟 / 剑雨 / 死亡特效 / 身后背景）
    ├── ChronoWeaponFactory.cs      Hook tool.$Weapon.create，把 item id 映射到上面的类
    ├── ChronoWeaponExecute.cs      Weapon.onExecute Hook 的委托声明
    ├── ChronoEntityDamage.cs       Entity.onDamage Hook 的委托声明
    ├── ChronoMobFinder.cs          附近怪物搜索（剑雨 / 飞镖选目标）
    ├── ChronoConfig.cs             按键配置（Config<ChronoConfig> + 键名解析）
    ├── ChronoDiag.cs               诊断日志（定时打印手里拿的是什么）
    ├── ChronoCdbProbe.cs           开局自检 data.cdb 补丁是否生效
    ├── patch_chronoblade_cdb.py    生成 data.cdb（item + weapon + affix 表）
    ├── data.cdb                    由脚本生成（构建时 diff 成 data.cdb_ 打进 res.pak）
    └── Assets/
        ├── atlas/TIMEKASAN.*       罗马数字 I…XII（刻印 / 蹦字 / 面板图标共用）
        ├── atlas/TIMEZHANJI.*      技能施放 + 十之弹记忆动画
        ├── atlas/TIMEJIBAI.*       怪物死亡特效
        ├── atlas/TIMEBEIJING.*     拿着 Zaphkiel 时英雄身后的背景
        └── sfx/CHUXIAN.WAV         Zaphkiel 拾取音效
```

---

## 实现要点

### 1. 新武器数据（Python 生成 data.cdb）

MDK v35 的模板 `data.cdb` 是 JSON。`patch_chronoblade_cdb.py` 直接读它、追加/修改行再写回：

* `item` 表：`ChronoBlade` + `TimeBullet`（group=4 近战、可掉落、图标沿用 `cardIcons.png`）
* `weapon` 表：`ChronoBlade`（4 条 `strikeChain`）+ `TimeBullet`（照搬 Pistol）
* `affix` 表：新增 `ChronoBulletDouble`

构建时 `DCCMTool cdb diff` 只把差异部分打成 `data.cdb_`，游戏加载 `res.pak` 时自动合并 —— 不需要改原版 `res/data.cdb`。

### 2. 武器类（ChronoBlade.cs）

继承原版 `Katana`，复用它的居合冲刺 / 斩击执行流程，只按**连击段**（`_cycle`）分派额外效果：

```csharp
public void AddCycleEffect(int cycle)
{
    switch (cycle)
    {
        case CycleShuriken:  ChronoFx.CastShurikenCircle(...);   // 第 2a：一周飞镖特效
                             SpawnShurikenEntities(...);         //        + 12 枚实体
                             break;
        case CycleSwordRain: CastSwordRain(hero);                // 第 3a：时钟 + 剑雨特效
                             SpawnSwordRainEntities(...);        //        + 实体落剑
                             break;
        default:             /* 第 1a 居合 / 第 4a 平砍：不额外做事 */ break;
    }
}
```

> ⚠️⚠️ **调用链只能有一条，而且必须挂在"每次按下保证进一次"的地方。**
>
> 这里栽过两次：
>
> 1. `AddCycleEffect` 一开始只在 `ChronoBlade.RunAttack` 里被引用，而 `RunAttack` 挂的是
>    **`Hook_Katana.onExecute`** —— 那个挂点**实测整局都不触发**（见 `Initialize` 里的注释）。
>    结果 `AddCycleEffect` 成了**死代码**，2a/3a 从来没生效过。
> 2. 改挂到 `tool.Weapon.onExecute` 钩子后**仍然不出**：那个钩子是否"每一刀都进"并不确定。
> 3. 现在挂在 **`ChronoBlade.fixedUpdate` 的 `shouldDash` 分支**
>    （`_consumed` 把关，每次按下保证只进一次，松手才复位）
>    → `AdvanceCombo()` → `AddCycleEffect(comboStep)`。
>    这条链是本模组自己的判定，不依赖任何原版钩子是否触发。
>
> 另外**"第几下"是自己数的**（`_comboStep`：1→2→3→1，超过 1.5 秒没出刀就重新起手），
> **不能**用原版 `_cycle` —— 本模组对每一次攻击都强制注入满蓄力 + `nextIsChargeAtk`，
> 原版一律走居合冲刺分支，`_cycle` 早就和玩家看到的连击脱钩了（而且 `set_cycle(3)` 还被折回 0）。
> 拿 `_cycle` 去判断"第几下"，实际永远匹配不到 2/3。

* **第 1a**：攻击前由 `ChronoBladeMod` 统一置 `nextIsChargeAtk = true` + 满蓄力，让原版走**居合冲刺斩**分支（瞬移前冲 + 路径群伤）；命中时由 `Hook_Katana.hitFromWeapon` 逐个刻罗马数字。
* **第 2a / 3a**：在**原版那一刀之上**叠加飞镖圈 / 时钟剑雨的表现层，不改原版的连击、蓄力、判定任何一处。
* **第 4a**：普通平砍。按住攻击键时基类会 `set_cycle(3)`，**必须有这一段**，否则 `get_curSkill()` 返回 `null` → `Null access .chargeF` 崩游戏。

**原则：绝不去改写原版的连击 / 蓄力状态机**，只在外面按段套一层"额外效果"。

### 3. 罗马数字刻印

`Katana.hitFromWeapon` 在哈希链接绑定里不是可被 C# 重写的虚方法，因此走全局 Hook：

```csharp
Hook_Katana.hitFromWeapon += OnKatanaHitFromWeapon;
```

命中 `Mob` 且武器是 `ChronoBlade` 时，用 `atlas/TIMEKASAN.atlas` 的帧在敌人头顶生成金色 `HSprite`，向上飘散。

⚠️ 坐标必须在**调用 `orig` 之前**取好：`orig(self, a)` 会把伤害真正结算掉，如果是斩杀，怪物当场死亡、之后读 `mob.life` 就是 0、`spr` 也可能没了（这就是"斩杀时看不到罗马数字"的原因）。

### 4. 特效层（ChronoFx.cs）

做法是把 `HSprite` 挂到 `level.scroller` 的深度层上，每帧在 `IOnHeroUpdate` 里手动推进透明度 / 缩放 / 旋转 / 下落，生命周期结束自动摘除 —— 不需要新增 Entity 类，也不依赖哈希链接的自定义对象构造。

* 罗马数字：`atlas/TIMEKASAN.atlas`（`idle_0000…idle_0011`）
* 技能 / 记忆动画：`atlas/TIMEZHANJI.atlas`
* 死亡特效：`atlas/TIMEJIBAI.atlas`
* 身后背景：`atlas/TIMEBEIJING.atlas`
* 飞镖镖身：复用原版 `atlas/fxTimeKeeper.atlas`

> 每张自建图集都用 `Assets.Class.lib.get(...)`**现取**、不长期缓存 —— 换关后旧 `SpriteLib` 可能已被销毁，缓存了贴图就显示不出来。

### 5. 为什么不打包 lang/*.mo（重要教训）

早期版本用 Python 重新生成 `res/lang/main.*.mo` 并打进 `res.pak` 想汉化武器名，结果**全游戏文本错乱**。

原因在 `dc.libs.data.MoReader.parse()` + `GetText.readMo()`：它是"**整表替换**"语义 —— `parse()` 只把新 `.mo` 的条目装进 `StringMap`，`readMo()` 直接 `texts = stringMap` 覆盖整个文本字典。只含 2~3 个条目的补丁 `.mo` 等于把全游戏文本换掉了。

**结论：要覆盖 .mo 必须提供与原版条目数量、排序、哈希表完全一致的完整文件**，否则不要打包任何 `lang/` 内容。武器名与词条说明现在直接走 CDB 的 `name` / `desc` 字段（`GetText.get()` 查不到就原样返回）。

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

**规则：`orig` 绝不能用 `object` / `Delegate` 糊，必须是 DCCM 能按原函数签名适配的强类型委托。** 其余 Hook 走框架生成的 `Hook_XXX.方法名 +=` 事件包装（如 `Hook_Katana.hitFromWeapon`）最稳。

### 7. 新物品必须落在正确的 CDB 分组里（踩过的坑）

`item` 表是按 `separator` 切段的，段名列表在 `item` 表自己的 `props.separatorTitles` 里：

```
[DeployedTrap, Grenade, SideKick, Power, Melee, Ranged, Shield, Talisman,
 BagItem, Meta, Consumable, PreciousLoot, Perk, Skin, Head, Aspect, BossRushStatueUnlock]
                                 ↑ 下标 = group 值，Melee = 4
```

`CDBManager` 加载时会执行 `group = separatorByName(行的 __separator_group_Name)`，**用段名反查出的分组 ID 覆盖每行的 `group` 字段**。

第一版把新物品 `append` 到 `item` 表末尾 → 落进最后一个段 `BossRushStatueUnlock`，`group` 被算成 **16**。Boss Rush 雕像解锁项不是装备，于是背包 `add()` / `equip()` 直接无视它 → 按了热键也看不到武器。

修法：把新行 **插进 Melee 段的内部**（段起点 + 32 行），DCCMTool 便会按位置算出正确元数据：

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
   `InvalidCastException: Unable to cast HaxeDynObj to virtual_ambiantDesc_...`。

最终走原版掉落（见 `ChronoPanels.cs` 的 `SpawnWeaponDrop`）：

```csharp
var item = new InventItem(new InventItemKind.Weapon(ChronoBlade.name.AsHaxeString()));
bool inArmory = false;
var drop = new ItemDrop(hero._level, hero.cx, hero.cy - 7, item, true, new Ref<bool>(ref inArmory));
drop.init();                                        // 必须调用，否则崩
drop.onDropAsLoot();                                // 交给原版掉落 / 拾取流程
drop.setPosCase(drop.cx, drop.cy, hero.xr, hero.yr); // 补上格内小数偏移，之后靠重力落下
```

**结论：给玩家塞自定义武器，最稳的是 `ItemDrop` + `onDropAsLoot()`**，让游戏自己去做拾取判定、HUD 刷新、技能初始化与武器替换。不要手工构造 `_itemData`，也不要用 `Inventory.add()` 绕过"背包已满"检查。

### 9. 全屏选择面板 + 真暂停：压 Process 栈，别手写 `Game.paused`（踩过的坑）

两个面板（`P` 选武器 / `X` 选弹药）都在 `ChronoPanels.cs`，都继承原版的 `dc.ui.sel.GridSelector`（`FreeWeaponSelector` 也是它的后代）。

**上一版为什么"暂停后恢复不了"**：曾经在 `dc.pr.Game.update` 的钩子里切换 `paused`，但 `paused = true` 之后**这个 `update` 根本不再被调用** → 解除暂停的代码永远跑不到，连 60 秒兜底也写在同一个钩子里，救不回来。

> **教训：解除开关不能装在它自己要关掉的那扇门后面。**

现在的做法就是原版暂停菜单 / 训练场那套 —— `GridSelector` 的构造函数里：

```
_Process.__inst_construct__(arg1, Main.Class.ME);   // 挂到 Main 进程栈（不是 Game 底下！）
arg1.pauseGame();                                    // HUD.hide() + Game.modalPause() = 真暂停
arg1.initGrid();                                     // 虚方法，我们重写它来定网格内容
```

**面板是 `Main` 的子进程，暂停的是 `Game` 这个兄弟进程。** `Process.updateAll` 每帧从 ROOTS（Main）递归，遇到 `paused == true` 的节点直接跳过 → `Game` 连同它底下的整个 `Level` 全停在那一帧，而面板自己照常 update。关闭走 `GridSelector.close()`，里面就是 `Game.Class.ME.resume()` —— **模组完全不参与解锁，结构上不可能锁死。**

两个面板的差别只在"网格内容"：

| 面板 | 基类 | 重写了什么 |
|---|---|---|
| `ChronoWeaponPanel`（P） | `FreeWeaponSelector` | **只**重写 `itemIsFiltered()` 放行两个 id |
| `ChronoAmmoPanel`（X） | `GridSelector` | `initGrid()` 出 12 格、`getIconBmp()` 用 TIMEKASAN 图集画罗马数字 |

* `FreeWeaponSelector` 的 `weaponSpawner` **故意传 `null`**：它 `onValidate()` 里对 `null` 有专门短路，而 base 会照常 invoke 我们的 `validateCb` 再关闭面板 —— 于是既拿到"等级/品质控件 + 确认回调"，又不需要训练场那个实体。
* 兜底：`pauseGame()` 在基类构造里就跑了，所以两个 `Open()` 都**先查 CDB**、再 `try/catch` + `ChronoPanelLog.EmergencyCleanup()`（`resume()` + `HUD.show()` + 关掉残留的半成品进程）。

#### 选择武器框：严格用原版尺寸（不重写任何尺寸方法）

尺寸**一个都不重写** —— `ItemSelector` 给的就是原版训练场那套：`get_wid() = 6` 列、格子 24×24，于是 `GridSelector.onResize()` 算出来的选择框就是原版大小。

> ⚠️ 曾经为了"框高一点"把 `get_wid()` 收成 1 列 —— 那确实会让两把武器分成两行、框高约 3 倍，但**已经不是原版尺寸了**，已改回。
> 另外**绝对不能**直接把 `hei` 调大：`moveSelection()` 拿 `hei` 当上下移动边界，调大之后能移动到不存在的行，`getEntryAt()` 返回 `null` → 当场崩。
> 真要调高度，唯一安全的杠杆是 `get_wid()`（列数）。

#### 弹药格子：复用斩击刻印那套帧映射

`idle_0000…idle_0011` ↔ 罗马数字 I…XII，映射表就是斩击刻印用的 `ChronoFx.FrameIndexForRoman()`，三处（刻印 / 开火蹦字 / 弹药面板）共用一张表。

两个必须踩对的坑：

1. **锚点必须用左上角 `(0, 0)`，不能用居中 `(0.5, 0.5)`。**
   原版默认的格子图标是 `dc.h2d.Bitmap`（`Icon : Bitmap`），包围盒 `[0,w]×[0,h]` —— 左上角对齐格子，`GridSelector` 就是按这个约定摆格子的。HSprite 一旦用居中锚点，包围盒变成 `[-w/2,w/2]×[-h/2,h/2]`，整张图往左上偏半个身位，表现出来正好是"选择框的中心对上了图集的右下角"。
2. **缩放必须加在「外层容器」上，不能加在 sprite 自己身上。**
   `GridSelector.addEntryAt()` 会给 `getIconBmp()` 的返回值挂一个 `onBeforeReflow`，里面**强制**把 `scaleX/scaleY` 设成 `pixelScale`。加在 sprite 上下一帧就被覆盖，数字会撑爆整个格子。所以做法是 `dc.h2d.Object` 容器 + 里层 HSprite 自己缩到 `格子高/430`。

#### 选择框高度：原版公式在"单行"时会退化

宽度用原版的 6 列就准确了，但高度只够显示 **80% 个武器**，因为

```
内容高   = max( (entry.cy - entry.sectionIdx) * (条目高 + pixelScale*10) )
选择框高 = pixelScale * (内容高 + 行数 * padV*2 + 22)
```

两把武器只占一行 → `cy` 全是 0 → 内容高算成 0 → 框高只剩 `pixelScale*32`，比一张武器卡片图标还矮一点，底边被裁掉。

修法是把 `fbItems.padV` 调大（`BoxPadV`）：**padV 只进高度公式，宽度用的是 padH**，所以宽度仍然是严格原版尺寸。钩子必须挂在 `initGrid()` 里 —— 构造顺序是「建 fbItems → initRightFlow → **initGrid** → **onResize**」，onResize 是运行时读 `fbItems.padV` 的，晚于 initGrid 改就没用了。

#### 弹药面板会显示"这一发是干什么的"（在框内左下角）

`GridSelector` 的 `initRightFlow()` / `updateRightFlow()` 默认是**空实现**（框架留的钩子，`ItemSelector` 就是拿它画右侧物品说明的）。弹药不是 CDB 物品、没有 `NewItemDesc` 可用，所以这里自己放一个 `dc.ui.Text`：

```
第 3 / 12 发 · 三之弹 Gimel
开火即生效：回复 30% 生命 + 移速 ×2.0（affect 116 +1.0），持续 10 秒
```

文案取自 `ChronoBullets.BulletDef.Desc` / `.DescLegendary`，会根据**当前这把枪是不是传奇**自动切换（`TimeBullet.IsLegendaryDouble`）。

> ⚠️ 这个 Text **必须挂在 `mask` 上**（框内可视区），自己算 x/y 贴到框内**左下角**；**绝不能挂到 `mainFlow` 上**。挂 mainFlow 时文字高度一变，mainFlow 就重新居中，光标换到下一行时整块面板会跟着上下跳。

#### 条目网格必须每帧钉回框顶

撑高框之后带出一个副作用：原版 `updateScrollingBox()` 的落点是

```
num6  = mask.height - 内容高 - 5*px        // 框比内容高的"富余量"
num10 = (cy2 < num6) ? num6 : ...           // 富余量更大 → 直接把内容推到 num6
```

也就是**"框有富余高度就把内容压到底部"**。框一高，富余量就大，条目整块被推到框底、上面空一大片。

拦不住的地方在于：GameProxy 里 `updateSelection(ref bool)` 是**普通方法**，只有 `updateSelection(Ref<bool>)` 才是 virtual —— 重写哪个都不能保证拦得住。所以改成在 `postUpdate()`（virtual、每帧在 update 之后 / 渲染之前执行）里把 `wrapperItem.y` 钉成 `(int)(pixelScale*5)`：`ChronoPanelLog.PinGridToTop()`，**两个面板都调用**。

> 注意要跟着原版一样**截断成 int**，否则每帧差不到 1px，会一直置 `posChanged`。

---

## 传奇词条（legendAffixes）

`item.legendAffixes` 是"这件装备当传奇时**可以 Roll 到的词条池**"。两条都是 CDB 侧改的：

| 武器 | 词条 | 说明 |
|---|---|---|
| 时之刃 ChronoBlade | `IgnoreGlobalShield` | **无视防御盾**。原版已有词条，`props` 是空壳、行为写在游戏代码里，挂 id 即生效 |
| 刻刻帝 Zaphkiel | `ChronoBulletDouble` | **本模组在 affix 表里新建的一条**，说明文字「每个弹药效果增强」，让 12 发子弹的效果整体翻倍 |

### 新建 `ChronoBulletDouble` 的两个要点

1. **affix 表和 item 表一样是"按位置反解分组"的**：
   `props.separatorTitles = [Tier, Special, Basic, Advanced, LegendaryOnly]`、`separators = [0, 4, 22, 87, 127]`。传奇词条都在最后一个分组 `LegendaryOnly(group=4)`，它一直延伸到表尾 —— 所以**直接追加到表尾**就会被算成 group=4。
   （⚠️ 这点和 item 表相反：item 表最后一个分组是 `BossRushStatueUnlock`，追加会落错组。）生成的 diff 里能看到 DCCMTool 回填的 `__separator_group_Name: "LegendaryOnly"`。
2. **affix 的 `props` 是空壳，行为必须自己实现**：
   原版所有传奇词条（`IgnoreGlobalShield`、`DoubleSpeed`…）在 CDB 里 `props` 都是空的，效果写死在游戏代码里按 id 判定。我们的 `ChronoBulletDouble` 同理 —— CDB 那条只负责"有名字、有图标、能被 `legendAffixes` 引用"，真正的翻倍逻辑在 `ChronoBullets` 里。

> ⚠️ 判定要用 `InventItem.hasAffix()`，**不能**去读 `_itemData.legendAffixes` —— 后者是"可 Roll 的池子"，不是"已经 Roll 到的词条"，永远为真。

### 翻倍的两条易错规则

* **"移速翻倍"要先翻倍倍率再减 1**。affect 116 是**加上去**的值（基础跑速倍率 1.0），所以 ×2.0 传的是 +1.0。想把倍率翻成 ×4.0 必须传 +3.0 —— 直接把 +1.0 翻倍成 +2.0 只有 ×3.0。见 `SpeedAffectFromMultiplier()`。
* **负面效果（减速）要缩小倍率**：×0.45 → ×0.225。把 0.45 × 2 变成 ×0.90 是"减速减弱"，方向反了。
* 传奇的 `Yud` 处决延时和记忆动画时长**必须是同一个值**（都 1.5s），否则会出现"动画播完了人还活着"或者反过来，所以 `PlayMemoryAt()` 现在接收时长参数。

### 兜底：为什么"面板选传奇出来的是普通货"

原版训练场选武器能出传奇，是因为它**一定在训练场里**：传奇那一步靠 `TrainingWeaponSpawner.Class.lootGen.finalizeLegendaryItem()`，而那个 LootGen

> **只在 `_TrainingWeaponSpawner.__inst_construct__`（真的摆了一个武器生成器实体）时才创建**；普通关卡里 `TrainingWeaponSpawner.Class.lootGen` 是 **null**。

所以任意关卡按 `P` 时，以前 `gen == null` → 整段 LootGen 调用被跳过 → 物品**根本没有 `"Legendary"` 词条** → 既不是传奇外观、也不会把 `legendAffixes` 里的词条算进说明。

现在 `MakeItem()` 不再依赖它：

1. LootGen 拿得到就照原版走（顺带处理等级 / 基础数值）；
2. 拿不到就**自己显式补上 `"Legendary"` 词条**，再挂本模组的传奇词条；
3. 传奇**不叠 `QualityUp`**（原版传奇分支里 `set_weaponQuality(0)`，那条循环加 0 次）。

### 十二之弹回不了上一关

关卡 id 的正确取法是 **`hero._level.map.id`**（`dc.level.LevelMap` 上直接就有 `id`）。之前两次都错了：

1. `dynamic d = entry; d.id` —— DCCM 的 hashlink 代理上**动态绑定不可靠**，每次抛异常；
2. 读 `map.infos.id` —— 那是 CDB 的**关卡定义**行，语义和 `map.id` 不一定一致。

现在：

* 主路径**强类型** `map.id`（+ 反射读 `id` 作兜底）；
* **自己记录关卡历史**：`ChronoBullets.TrackLevel()` 每帧在 `OnHeroUpdate` 里比较 `hero._level.map.id`，一变就记一笔，十二之弹直接取上一条。不再依赖 `serverStats.history` 的语义；
* `serverStats.history` 只作为二级兜底。

> 副作用：**刚进游戏还没换过关时用不了**（历史里只有一关），日志会提示"找不到上一关 —— 换过关之后就能用"。这是预期行为。

---

## 已知限制

* 三段连击共用 `AtkKatanaA` 起手动画（没有为武器新增帧动画），区分靠特效与音效。
* 第 2a / 3a 的实体是**玩家可拥有的同类投射物**（旋转刃 `Saw` / 落石 `Stalactite`），
  不是时之守护者本人那两套贴图 —— 原因见第 1 节的框注（boss 专属实体在普通关卡里生成不出来）。
  表现层（金色法阵 / 背景时钟 / 剑影）仍然是原来的时之守护者特效。
* 罗马数字只有 12 帧（I…XII），超过 XII 会退回最后一帧。
* 目前只有 `P` 面板召唤（地面掉落），没有做"收藏家蓝图解锁"（要进商店/正常掉落需要额外的 blueprint 流程）。
* 面板里的"等级"通过 `TrainingWeaponSpawner.Class.lootGen` 套用；该 LootGen 在普通关卡里是 `null`（见上），此时等级不会被套用，但物品、品质与传奇词条都正常。
* `ChronoBulletDouble` 的说明文字是**直接写在 CDB 的 `desc` 字段**里的，没有走 `lang/*.mo`（原因见第 5 节）。

---

## 排错

模组日志：`coremod\logs\log_latest.log`，关键字 `[ChronoBlade]`。

**按 `P` 没反应**，按顺序看：

| 日志 | 含义 |
|---|---|
| 完全没有 `[ChronoBlade] 时之刃已注册` | 模组没被加载（检查 `coremod\mods\ChronoBlade\`） |
| `CDB 自检：找不到 ChronoBlade` | data.cdb 补丁没生效 |
| `选择武器面板：CDB 里找不到 ...` | 同上，面板会**主动不打开**（避免空网格） |
| `选择武器面板：网格载入 0 项` | 过滤逻辑没放行 |
| `打开选择武器面板失败，已强制恢复: ...` + 堆栈 | 面板构造中途抛异常（已自动解暂停） |
| 掉出来了但捡不起来 | 看 `武器就绪` 那行是不是"⚠ 武器初始化异常" |

**其它常见现象**：

| 现象 | 原因 / 处理 |
|---|---|
| 传奇武器不显示传奇词条 | 看日志有没有 `已附加词条: Legendary` / `已附加词条: ChronoBulletDouble` |
| `LootGen 不可用（不在训练场，属正常）` | 预期行为，等级不套用，其余正常 |
| 十二之弹回不去 | 看日志 `Yud-Bet 回到上一关：<id>（来源=...）`；若提示"换过关之后就能用"，先正常换一关 |
| 构建报 `MSB3021` | 游戏还开着，DLL 被占用，关掉游戏再构建 |
