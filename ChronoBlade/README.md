# 时崎狂三 · ChronoBlade（时之刃）

Dead Cells（v35 / DCCM）武器模组。新增 **两把武器**、**两个全屏选择面板**（真暂停）与 **狂三语音**。

* **时之刃 ChronoBlade** —— 近战，三段连击，每段致敬一个原版机制
* **Zaphkiel 刻刻帝** —— 远程手枪，十二发子弹各自带一套时间系效果，可换弹
* **狂三语音** —— `kurumi01~08`，休闲 / 连杀 / 打败 Boss / 去下一关四个时刻随机触发，
  走在一条**独占的最高优先级声道**上，不会被游戏里任何其它声音压制

> `GamePseudocode/`（反编译伪代码）与 `res/`（游戏资源）只读引用，本模组不修改其中任何文件。

---

## 功能

### 1. 时之刃 ChronoBlade（近战 / 三段连击）

继承原版 `Katana`，复用它的居合冲刺与斩击流程，只按**连击段**分派招式：

| 段 | 招式 | 效果 | 参考对象 |
|---|---|---|---|
| 第 1a | **斩击刻印** | 前冲斩，斩击路径上的敌人依次被斩，各自刻上罗马数字 I / II / III … | 武器 `Katana` 的斩击 |
| 第 2a | **一周飞镖**（不斩击） | 原地以自身为中心，向一整圈（12 枚）甩出飞镖，附带时之守护者的金色法阵 | Boss `TimeKeeper` 的 `levelUpRadius` |
| 第 3a | **时钟剑雨**（不斩击） | 唤出时之守护者的背景时钟旋转展开，随后数把巨剑从**每个敌人正上方**砸下来 | Boss `TimeKeeper` 的 `swordRain` |

> **第 2a / 3a 不再有近战斩击** —— 只出飞镖与落剑。要**同时**做对两件事，缺一个就会"还有斩击"：
>
> 1. **拦掉 onExecute**：`ChronoBlade.SkipMelee` 标记（由 `AdvanceCombo()` 每刀重设）→
>    看到就**不调 `orig`**、直接 `return true`（"这一下我处理了"）。
>    ⚠️ 本模组挂了**两个** onExecute 钩子：`tool.Weapon.onExecute`（通用，在
>    `ChronoBladeMod.OnAnyWeaponExecute` 里判）和 `Hook_Katana.onExecute`（备用，走
>    `ChronoBlade.RunAttack`）。**两个入口都要判** —— 只堵通用钩子的话，
>    Katana 钩子那条会照样 `callOriginal()` 把原版那一刀斩出去。
>    `RunAttack` 是"我方武器唯一的总入口"，判断放在它开头最稳。
> 2. **不要注入蓄力**：第 2a/3a **故意不**设 `nextIsChargeAtk` / `katanaChargeF`。
>    否则原版 `Katana.fixedUpdate` 的蓄力分支在蓄满时会**自己调一次 `onExecute()` 并把
>    `AtkKatanaA` 斩击动画播出来**（`Katana.cs:2934-2970`）—— **光拦 onExecute 拦不掉这套动画**。
>    不注入 + 按住时 `controlsLocked`（`isWeaponButtonDown()` 会因此返回 false）→
>    原版两条分支都不进 → 真的一刀不出。
>
> 标记是"当前这一刀"的属性，**不是读一次就清** —— 否则同一刀里 `onExecute` 进两次的话，
> 第二次会又斩出去。
>
> 同时两段都在**英雄中心**播放 `atlas/TIMEZHANJI.atlas`（`ChronoFx.PlayCastEffect`），
> 和原本的技能释放是同一个表现。

第 2a / 3a 都是**表现层 + 实体层**双份 —— 除了特效，还有真正会飞 / 会砸、会打伤害的投射物：

| 段 | 表现层（特效） | 实体层（真投射物） |
|---|---|---|
| 第 2a | 金色法阵 + 一圈飞镖贴图 | **12 枚 `dc.en.bu.Saw`（旋转刃）**，以英雄为中心向一整圈甩出，每枚 **45** 基础伤害，速度 ×2.2 |
| 第 3a | 背景时钟展开 + 砸下来的剑影 | **最多 6 柄 `dc.en.bu.Stalactite`（从天而降）**，每个敌人正上方一柄，每柄 **120** 基础伤害，下落速度 ×5 |

伤害走 `AttackUtils.Class.createFromHeroItem(hero, item, power)` —— 它内部就是
`createFromHero`（打开 `useHeroScaling`，**吃卷轴 / 属性缩放**）**再加上**
`useItemAffixes(item)` 与 `item.getDamageBonus()`，并把 `sourceItem` 设成本武器。
所以飞镖与落剑**会随武器增强而增强，也能吃到卷轴增伤**。
（只用 `createFromHero` 的话没有武器那一份加成。）

> ⚠️ **`Stalactite` 的两个坑**（都不是想当然的默认值）：
> * `groundY` 是**构造函数里**按 `from`（我们传的是英雄）的位置算出来的地面高度 ——
>   目标是远处平台上的敌人时会对不上（原版是 Giant 自己放，两者本来就在同一片地面）。
>   所以生成后要按"敌人脚下的地面"重设 `st.groundY`。
> * 出厂 `spd = 0.8` → `dy = sin(PI/2) * 0.9 * 0.8 = 0.72` 像素/帧（约 43 像素/秒），
>   掉 9 格要 **5 秒**。所以构造完直接乘 `dx/dy` 提速（和飞镖同一个道理：
>   `_Bullet.__inst_construct__` 把 `spd` 换算成 `dx/dy` 之后就**不再重算**，也没有存 `spd` 字段）。
>
> 另外 `level.map.getCeilY()` 拿到的"天花板"经常在**屏幕外几十格**（房间上方还有岩层），
> 直接拿来当出生点会把剑丢到看不见的地方 —— 所以距离必须**夹住**（`SwordFallMaxCells`），
> 超出就退回"敌人上方 `SwordFallCells` 格"。

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
| 1 | 一之弹 Aleph | 自身移速 **×2.0**（affect 116 值 **+1.0**），持续 10 秒 | 开火即生效 | 移速 **×4.0**（值 +3.0） |
| 2 | 二之弹 Bet | 目标**减速 3.5 秒**，并且移速 ×0.45 持续 10 秒后还原 | 需命中 | 移速 **×0.225**（减速翻倍） |
| 3 | 三之弹 Gimel | **回复 30% 生命**（没有任何移速加成） | 开火即生效 | 回复 **60%** 生命 |
| 4 | 四之弹 Dalet | **把自己**拽回 5 秒前的位置与状态（生命 + 诅咒） | 开火即生效 | 拽回 **10 秒**前 |
| 5 | 五之弹 Hei | 获得全图视野（`HUD.ME.minimap.revealAll()`，等同探险家符文） | 开火即生效 | 不变 |
| 6 | 六之弹 Vav | **把自己**拽回 **25 秒**前的位置与状态（生命 + 诅咒） | 开火即生效 | 不变 |
| 7 | 七之弹 Zayin | **触发原版 TimeDistorsion（时间扭曲）**：全关卡敌人与弹幕一起变慢 3 秒 | 需命中 | 变慢 **6 秒** |
| 8 | 八之弹 Het | 在命中点召唤我方 **Melee 组**怪物，跟随英雄打敌人，最多 3 个、10 秒后消失 | 需命中 | 最多 **6 个**、**20 秒** |
| 9 | 九之弹 Tet | 随机传送到本关**任意位置** | 开火即生效 | 不变 |
| 10 | 十之弹 Yud | 目标头顶播放记忆动画 3 秒，动画结束立即处决 | 需命中 | 动画 **1.5 秒**后处决 |
| 11 | 十一之弹 Yud-Aleph | 英雄前移 6 格 + **无敌 2 秒**（affect 5），2 秒后拉回原位 | 开火即生效 | 前移 **12 格** + 无敌 **4 秒** |
| 12 | 十二之弹 Yud-Bet | 回到上一关 | 开火即生效 | 不变 |

> **affect 116 是加算的**：英雄基础跑速倍率 1.0，所以 +1.0 = ×2.0、+4.0 = ×5.0。
> 一之弹的需求是 **×2.0**，所以常量 `AlephSpeedAdd = 1.0`（早先是 4.0）。
> 想把倍率翻倍**必须先把倍率翻倍再减 1**（`SpeedAffectFromMultiplier` 就是干这个的）——
> 直接翻倍加成是错的：×2.0（+1.0）翻倍成 +2.0 只有 ×3.0，而需求要的是 ×4.0。

> **七之弹为什么是"完整触发原版效果"而不是自己写一个时停**：
> 翻 `GamePseudocode/dc.pow/_TimeDistorsion.cs` 后照抄了原版三件事 ——
> `fx.timeDistorsionStart/DistortionEnd` 环形光环（以**英雄**为中心，半径 192）、
> `Audio.fadeTimeDistortEffect(1.0 → 0.0)`（那层时间扭曲的画面滤镜 + 音频处理）、
> 以及对**全关卡** Mob / Mob 发射的 Bullet / Grenade / Interactive 施加 **affect 24** 并在结束时清掉。
> 唯一有意的差别：**跳过英雄自己队伍的 Mob** —— 原版是玩家自放技能没有"自家召唤物"，
> 而八之弹会召我方怪，照抄会把自家召唤物一起拖慢。
> 另外加了"代号"（`_distortGen`）：持续时间内又打一发七之弹时，前一发的收尾不会把后一发提前清掉。

> **四之弹 / 六之弹是"对自己"用的**（开火即生效）：把自己拽回 N 秒前的**位置 + 状态**。
> 数据来自模组自己采样的英雄历史（每 0.1 秒一条 `HeroSample`），保留时长由 `HistoryKeepS`
> 决定 —— 六之弹要 25 秒，所以那里至少要是 25（现在是 27）。改秒数**一定要同步改那个常量**，
> 否则会静默地"还没有历史数据，本次不生效"。
> 采样存的是"格坐标"，换算成像素中心的公式必须和采样处一致（y 要减半个身高）。
>
> **"状态"指哪些**（`HeroSample` 里的字段，逐个还原）：
> 位置、**生命**、**诅咒层数**（`Hero.curseCounter`）、历史最高诅咒（`curCurseMaxReached`）。
>
> ⚠️⚠️ **诅咒必须走原版接口，不能直接写 `curseCounter` 字段。**
> 身上那个诅咒图标是 `Hero.curseLabel`（一个 `LightTip`），它**只在
> `Hero.curse()` / `Hero.reduceCurse()` / `endCurse()` 里被重建**。
> 直接赋值只改了数值 —— **屏幕上那个诅咒数不会变**，
> 表现就是"回溯了但诅咒没回溯"（第一版就是这么写的，已修）。
> 所以还原时是：
> 目标更低 → `hero.reduceCurse(差值)`；目标更高 → `hero.curse(差值, null, ...)`
> （原版所有调用方 reason 都传 `null`）。
>
> ⚠️ 为什么是这几个：翻了一遍 `Hero` 的标量字段，真正算"身上持续状态"的只有
> 生命与诅咒；其余要么是**货币**（`cells` / `goldCombo` —— 回溯货币等于刷钱，不能要），
> 要么是**几秒就衰减的临时计时**（`curRally` 振作、`spdCombo` 连杀加速、
> `darknessCounter`、`invisibilityTimer`），回溯它们没有意义。
>
> ⚠️ **临时状态效果（affects）没有纳入**：那需要把"每个 affect 的 id + 剩余时长"整份快照，
> 还原时要先清空再按剩余时长重加，容易和引擎自己的短时 affect（无敌帧、隐身帧）打架。
> 要加的话是独立一件事，说一声即可。
>
> ⚠️ 采样时**取不到的字段写哨兵 -1**，还原时**跳过** —— 免得某次读失败反而把状态清成 0。
> 想再纳入别的状态：往 `HeroSample` 加字段，然后在 `Update()` 采样和 `ApplyHeroRewind()`
> 还原两处各加一行。

> ⚠️ **十一之弹的"无敌"是 affect 5，不是 48**（这里踩过一次）：
> * `dc.Entity.canBeHit()` —— 只要 `affects[5]` 非空就 `return false`，而
>   `canBeHitBy()` / `canReceiveAttack()` 全都转发到它，攻击管线也都查它。
>   **这才是"打不到我"的开关**（英雄翻滚的无敌帧就是 `setAffectS(5, 0.08)`）。
> * `affect 48` 是**隐身**（Invisibility）。它会被 `Hero.onInvisibilityBreakingAction()`
>   里的 `removeAllAffects(48)` 清掉，而那个方法被 `dc.tool.Weapon`（**开火**）、近战命中、
>   翻滚等一堆地方调用 —— 所以"开火即生效"的无敌用 48，**会被这一次开火当场清掉，等于没有无敌**。
> * 因此无敌用 5（不会被开火清掉，加一次就够），`MaintainInvulnerability()` 每帧只做
>   "`affects[5]` 还在不在，不在了按剩余时间补一次"——不重复堆实例，也能兜住原版中途清理。

> **九之弹的落点**：随机取一列，从**英雄当前所在行**往下找第一块实地站上去。
> 起点不能用地图顶端 —— `getGroundY` 扫的是第一块 solid 格子，从 0 开始会扫到
> **天花板/顶层岩壁**，英雄会被放到天花板上面（地图外）。找不到落点时退回"随机怪物坐标"。

* **传奇词条**：`ChronoBulletDouble`（说明文字「**朕即时间，朕即裁决。**」）—— 本模组**在 CDB 的 affix 表里新建**的一条，让上表右列全部生效。
* **物品描述**：`PISTOL_DESC`（写进 `item.gameplayDesc`）是一段 185 字的中文设定文
  （「它不是钟，是悬于万古之上的时间王座……」）。⚠️ 物品说明框是**固定高度**的，
  这么长大概率会被裁掉一部分；要短就直接改脚本里那个常量。
* **身后背景**：主手拿着它时，英雄身后循环播放 `TIMEBEIJING` 背景
  （`EnableZaphkielAura` 开关、`ZaphkielAuraAlpha` 不透明度；
  **两个都能在游戏的「选项 → 模组 → ChronoBlade」里改**，见上面的 `IModMenu` 说明）。
  时钟的**中心对准英雄的头部**：`y = (cy + yr) * 24 - hei`。
  ⚠️ 坐标约定：本模组里 `(cy + yr) * 24` 是实体的**底边（脚）**，
  所以"身体中心"要 `- hei * 0.5`，"头顶"要 `- hei` —— 别把底边当中心用。
  sprite 的 pivot 是居中 (0.5,0.5)，赋给 `x/y` 的就是时钟中心；微调用 `AuraOffsetY`（正数往下）。
* **拾取音效**：自带 `sfx/CHUXIAN.WAV`（和语音一样走那条独占声道）。

### 3. 两个全屏选择面板（**真暂停**）

| 面板 | 默认键 | 内容 |
|---|---|---|
| 选择武器 | `P` | **只列本模组新增的两把武器**；可用面板内原版控件改**等级 / 品质（含传奇）/ 无色** |
| 选择弹药 | `X` | Zaphkiel 的十二之弹；**底部显示当前这一发是干什么的**（左对齐、贴框内左下角、并往上抬一行） |

* 面板内操作：`← →` 选择 · `Enter` 确认 · `Esc` 返回。
* 打开期间游戏是**真暂停**：英雄、怪物、弹幕、粒子、动画全部停在那一帧。
* 选完武器在英雄**当前所在格**（就是 hero 坐标）生成，走过去捡起即可装备。
* **刻刻帝那一格是动态图标**：面板自己用 `atlas/TIMEZHANJI.atlas` 的帧逐帧播（15fps，46 帧约 3 秒一轮），
  时之刃仍然用原图标。
* **HUD / 背包上的刻刻帝图标 = 当前装填那一发的罗马数字**，切弹药会跟着变
  （原版入口 `HUD.updateIcon(item, tile)`，见下面「图标从哪来」一节）。

### 图标从哪来（以及 `icon.file` 是个坑）

**结论：物品图标只能改"从 `cardIcons.png` 切哪一格"，`icon.file` 是死数据。**

`dc._Assets.getItem(String i)` 里根本不读 `file`：

```csharp
Tile tile2 = Assets.Class.itemIcons;          // 全局唯一那张表
int x = icon.x * icon.size;                    // 只用了 x / y / size
int y = icon.y * icon.size;
return tile2.sub(x, y, size, size);            // 切 size×size 一格
```

而 `Assets.itemIcons` 的来源是 `loader.loadCache("cardIcons.png", Image.Class).toTile()` ——
**按文件名加载**。本模组的 pak 里带着一份同名的 `Assets/cardIcons.png`（2048×2048，85×85 格），
会覆盖原版，所以**直接改这张图就行，不需要任何运行时钩子**：

1. `make_icon_sheet.py` 解析 `data.cdb` 收集**已被引用**的格子，
   再找"没被引用 + 整格全透明"的空白格（实测上千个）；
2. 把图集的帧裁掉透明边、等比缩进 24×24，贴进这些格子（`--batch clock` = TIMEZHANJI 的 46 帧）；
3. 脚本把"批次 → 帧 → 格子坐标"写进 `_icon_cells.txt`；
4. `patch_chronoblade_cdb.py` 里 `PISTOL_ICON` 指向默认那一发（数字 I）的格子。

图片尺寸**不变**（还是 2048×2048），只是把原本空着的格子用起来：不多占显存、
也不会动到任何别的物品图标。

#### 12 个罗马数字（每发子弹的图标）是**手画在这张图里的**

像素区 **(0, 576) → (143, 623)**，每格 24×24：第一行（y=576，第 24 行格）是一…六，
第二行（y=600，第 25 行格）是七…十二。像素 → 格子就是 `px/24`。

代码里的坐标表 = `ChronoAmmoPanel.BulletIconCells`，公式就是
**子弹 i（0 基）→ `(x = i % 6, y = 24 + i / 6)`**：

| 子弹 | 格 | 子弹 | 格 |
|---|---|---|---|
| 1 I | (0,24) | 7 VII | (0,25) |
| 2 II | (1,24) | 8 VIII | (1,25) |
| 3 III | (2,24) | 9 IX | (2,25) |
| 4 IV | (3,24) | 10 X | (3,25) |
| 5 V | (4,24) | 11 XI | (4,25) |
| 6 VI | (5,24) | 12 XII | (5,25) |

CDB 的 `PISTOL_ICON` = `{"x": 0, "y": 24}`（数字 I）。

> ⚠️ 这一批**不要**用 `make_icon_sheet.py` 嫁接 —— 脚本里已经没有 `numerals` 批次了。
> 早期版本嫁接的是 TIMEKASAN 的 12 帧（颜色很淡），**已弃用**；
> 连同后来嫁接的 46 帧时钟，现在都没有任何引用（HUD 用这批手画数字，
> 面板是运行时直接读 `TIMEZHANJI.atlas` 逐帧播的，都不经过这张图）。
>
> ⚠️ 同步改坐标时**两边都要改**：图上的实际位置、`BulletIconCells`、`PISTOL_ICON`、
> `_icon_cells.txt` 里的记录。

#### HUD 图标会跟着"装填的是第几发"变

**不需要钩子** —— 原版就有正规入口：

```csharp
dc.ui.HUD.Class.ME.updateIcon(InventItem i, Tile t);
```

它遍历 HUD 的 `skillWeapons` / `skillPowers`，把 `ii == i` 那一格的图标换成传进去的 Tile。
所以 `ChronoAmmoPanel.SyncHudIcon()` 每帧做一次"当前装填第几发 → 切出对应数字格 → updateIcon"，
并且**只在那一发真的变了时才干活**（切弹药后第一帧、刚捡起武器时各写一次）。

> 为什么不用钩子也不用改 CDB：CDB 的 `icon` 是**静态一格**，表达不了"随装填变化"；
> `updateIcon` 是原版给状态变化用的入口，比 hook 图标创建稳得多。

> ⚠️ **`make_icon_sheet.py` 按批次幂等**：坐标表里已经有这个批次就拒绝重跑
> （重复跑会去找**另一批**空格再贴一遍 —— 不覆盖，但"哪一帧在哪一格"就乱了）。
> 要重做：先从 `res/cardIcons.png` 还原那张图、删掉坐标表，再加 `--force`。
* 弹药面板的说明会根据**当前这把枪是不是传奇**自动切换普通 / 翻倍两套文案。

### 4. 狂三语音（`kurumi01~08`，四个时刻随机触发）

`Assets/sfx/kurumi01~08.WAV`（打包后 → pak 内 `sfx/kurumiNN.WAV`）在四个时刻掷骰出声：

| 时刻 | 判定 |
|---|---|
| **休闲** | 附近 20 格内没有可打的目标、且已经安静 **12 秒** → 掷一次骰（默认 35%）。一段安静期只掷一次，没中就这一段不再追问 |
| **连杀** | **10 秒**内累计击杀 **8** 只 → 掷骰（默认 70%） |
| **打败 Boss** | `self is dc.en.mob.Boss`（原版有 `Boss : Mob` 基类，所有 boss 都继承它）→ 掷骰（默认 100%） |
| **去下一关** | 关卡 id 真的变了（**第一次记录关卡不算**，那只是记录起点）→ 掷骰（默认 85%） |

* 四个时刻**各自掷骰**，再叠一条 6 秒的全局最小间隔，所以不会连播。
* 抽句子时会避开**上一句**，不会连着两次同一句。
* 我方召唤物（同队伍）的死亡不算击杀。
* 概率 / 音量 / 总开关都在配置里：`VoiceChanceIdle`、`VoiceChanceKillStreak`、
  `VoiceChanceBoss`、`VoiceChanceLevel`、`VoiceVolume`、`EnableVoice`。
* 想给四个时刻配不同的句子：改 `ChronoVoice.MomentPool`（现在四个时刻共用全部 8 条）。

### 5. 音频统一走"独占最高优先级声道"

需求是「`Assets/sfx` 里的音频都在最高层、不被别的音频压制、强制同一音量播完」。
翻引擎源码（`GamePseudocode/dc.hxd.snd/Manager.cs`）确认，能压住一段声音的**只有三条路**，
三条都堵掉了：

| 压制来源 | 机制 | 对策 |
|---|---|---|
| 声道被顶掉 | `Manager.update()` 按 `sortChannel` 排序（**先比 `channelGroup.priority`，再比 `channel.priority`**），从前往后分配真实 OpenAL source；`sources` 用完就把后面的 Channel 标成 `isVirtual`（＝不出声） | 用一个 **`priority = 10000` 的独立 `ChannelGroup`**，永远排最前，不可能被挤掉 |
| 并发上限 | `Manager.update()` 还会按 `soundGroup.maxAudible` 限制同一个 SoundGroup 的同时发声数 | 配一个**自己的 `SoundGroup`，`maxAudible = -1`（不限）** |
| 音量链 | `Channel.updateCurrentVolume()` 算的是 `channel.volume * (channelGroup.currentVolume * soundGroup.volume)` | 自己的组 `volume` 恒为 1.0，**完全不用游戏的 `sfxChanGroup`**，所以游戏内"音效音量"滑块改不到它 |

> 另外确认引擎里**没有** sidechain（"播一个音就把别的音压低"）那种机制。唯一会整体压低音效的是
> `dc.Audio.update()` 里的 `keyFrameCineMute` —— **过场动画**时把游戏自己那 8 个 sfx 组全
> `set_volume(0)`。它动的还是**游戏自己的组**，我们的组不在名单里，
> 所以**过场动画期间语音也照常出声**（这是"不被压制"的直接结果；不想要的话把这一条加进
> `ChronoVoice` 的开关里即可）。
>
> ⚠️ 也**不能借用**游戏那几个 sfx 组：`dc.Audio.updatePriorities()` **每帧**都会重写它们的
> priority，写进去下一帧就被覆盖。所以必须是自己 new 一个组。
>
> `Assets/sfx` 里**每一个**音频都从 `ChronoVoice.PlayRaw()` 播（包括原来的拾取音 `CHUXIAN`），
> 不然就不算"所有音频都在最高层"。
>
> 参考：`dc.Audio.update()` 里 `sfxChanGroup.set_volume(options.sfxVolume * num)` ——
> 这就是游戏内"音效音量"滑块的作用点，我们**完全绕过**它。

### 6. 其它

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
| `EnableVoice` | `true` | 狂三语音总开关 |
| `VoiceVolume` | `1.0` | 语音音量（**不跟随游戏音效音量**，见功能 5） |
| `VoiceChanceIdle` | `0.35` | 休闲时刻触发概率 |
| `VoiceChanceKillStreak` | `0.70` | 连杀时刻触发概率 |
| `VoiceChanceBoss` | `1.0` | 打败 Boss 触发概率 |
| `VoiceChanceLevel` | `0.85` | 去下一关触发概率 |
| `EnableZaphkielAura` | `true` | 是否显示手持刻刻帝时**身后的时钟背景** |
| `ZaphkielAuraAlpha` | `0.9` | 那个背景的不透明度，0～1 |

> **这两个也会出现在游戏的「选项 → 模组 → ChronoBlade」里**（复选框 + 滑条）。
> 那一页是 `ChronoBladeMain.BuildMenu()` 自己建的：实现 **`ModCore.Menu.IModMenu`**
> （`GetName()` + `BuildMenu(dc.ui.Options)`），在里面
> `addToggleWidget` / `addSliderWidget` 逐个加控件，最后 `updateScroller()`。
> 同仓库的 **`ZoomVision`** 是同一个做法的样板，照它抄即可。
>
> ⚠️⚠️ **踩过的坑：别以为这一页是"反射配置字段"自动生成的。**
> 我一开始这么以为，还进一步从"ModCore.dll 里搜不到 `Double`"推断出
> "double 字段不会显示"，把不透明度改成了 int 百分比 —— **那是错的**：
> 控件是我们自己建的，`addSliderWidget` 收的就是 `double`
> （ZoomVision 的 `zoomScale` 就是 `double`）。
> **结论：想加选项就实现 `IModMenu` 自己建，别靠猜类型。**

> ⚠️ 早先那套「按 `\` 直接掉一把时之刃 / 按 `P` 直接掉一把 Zaphkiel」的**直召热键已经删除**，
> 现在拿到武器的唯一途径就是 `P` 面板。（删直召和加面板是同一次改动，不存在"没有获取途径"的中间态。）

---

## 获取与操作

**先重启游戏**（`res.pak` 只在启动时加载）。

1. 进任意关卡，按 `P` → 选武器（可选等级 / 品质 / 传奇 / 无色）→ `Enter` → 武器在**英雄当前位置**生成 → 捡起装备。
2. 连续按主手攻击键打出三段：前冲斩（冒罗马数字）→ 一圈飞镖 → 背景时钟 + 剑雨。
3. 想换 Zaphkiel 的弹种：拿起它，按 `X` → 看说明选一发 → `Enter`。

控制台日志关键字 `[ChronoBlade]`：

```
CDB 自检：ChronoBlade（本模组新增）存在，group=4        —— data.cdb 补丁生效
武器就绪：skills=3 段 / strikeChain=3 段              —— 捡起武器后技能建起来了
选择武器面板已打开（真暂停 / Process 栈）：网格载入 2 项   —— P 面板生效
选择弹药面板已打开（真暂停 / Process 栈）               —— X 面板生效
已召唤时之刃（Lv1 / 品质0）：在英雄当前位置生成    —— 面板确认后生效
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
    ├── ChronoVoice.cs              狂三语音：四个时刻 + 独占最高优先级声道
    ├── ChronoConfig.cs             按键配置（Config<ChronoConfig> + 键名解析）
    ├── ChronoDiag.cs               诊断日志（定时打印手里拿的是什么）
    ├── ChronoCdbProbe.cs           开局自检 data.cdb 补丁是否生效
    ├── patch_chronoblade_cdb.py    生成 data.cdb（item + weapon + affix 表）
    ├── make_icon_sheet.py          把 TIMEZHANJI 的帧嫁接进 cardIcons.png 的空格里
    ├── _icon_cells.txt             上面那个脚本产出的"帧 → 格子坐标"表
    ├── data.cdb                    由脚本生成（构建时 diff 成 data.cdb_ 打进 res.pak）
    └── Assets/
        ├── cardIcons.png           物品卡图标表（原版同名文件 + 手画的 12 个罗马数字 + 早期嫁接的 46 帧时钟）
        ├── atlas/TIMEKASAN.*       罗马数字 I…XII（刻印 / 蹦字 / 面板图标共用）
        ├── atlas/TIMEZHANJI.*      技能施放 + 十之弹记忆动画
        ├── atlas/TIMEJIBAI.*       怪物死亡特效
        ├── atlas/TIMEBEIJING.*     拿着 Zaphkiel 时英雄身后的背景
        └── sfx/
            ├── CHUXIAN.WAV         Zaphkiel 拾取音效
            └── kurumi01~08.WAV     狂三语音（四个时刻随机播）

> ⚠️ `ChronoBlade.csproj` 里的打包规则是 `Assets/**/*` + `RootInPak=""`，
> 所以 `Assets/sfx/xxx.WAV` 在 pak 里就是 `sfx/xxx.WAV` —— **新增音频直接丢进
> `Assets/sfx/` 即可**，不用改工程。想确认打进没打进：在 `res.pak` 里搜文件名
> （pak 是目录树存储，能看到 `sfx` 目录下挂着 `kurumi01.WAV` 这样的名字）。
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

继承原版 `Katana`，复用它的居合冲刺 / 斩击执行流程，只按**连击段**（自己数的 `_comboStep`，不是 `_cycle`）分派额外效果：

```csharp
public void AddCycleEffect(int comboStep)
{
    switch (comboStep)
    {
        case ComboShuriken:  ChronoFx.PlayCastEffect(hero);      // 第 2a：英雄中心 TIMEZHANJI
                             ChronoFx.CastShurikenCircle(...);   //        + 金色法阵
                             SpawnShurikenEntities(...);         //        + 12 枚实体
                             break;
        case ComboSwordRain: ChronoFx.PlayCastEffect(hero);      // 第 3a：英雄中心 TIMEZHANJI
                             CastSwordRain(hero);                //        + 时钟 + 剑雨特效
                             SpawnSwordRainEntities(...);        //        + 敌人上方实体落剑
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
* **第 2a / 3a**：**不走原版那一刀**（`SkipMelee` 标记 → `OnAnyWeaponExecute` 直接 `return true`，不产生斩击判定 / 位移），改成只放"飞镖圈 / 时钟剑雨"的表现层与实体层。
* **第 4a**：普通平砍。按住攻击键时基类会 `set_cycle(3)`，**必须有这一段**，否则 `get_curSkill()` 返回 `null` → `Null access .chargeF` 崩游戏。

**原则：绝不去改写原版的连击 / 蓄力状态机**，只在外面按段套一层"额外效果" / 按段整刀接管。

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
第 3 / 12 发  三之弹 Gimel
开火即生效：回复 30% 生命
```

> 分隔符只用 ASCII 和汉字：`·`(U+00B7)、`×`(U+00D7) 这类符号游戏字体没有字形，会变成方块。

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
| 刻刻帝 Zaphkiel | `ChronoBulletDouble` | **本模组在 affix 表里新建的一条**，说明文字「朕即时间，朕即裁决。」，让 12 发子弹的效果整体翻倍 |

### 新建 `ChronoBulletDouble` 的两个要点

1. **affix 表和 item 表一样是"按位置反解分组"的**：
   `props.separatorTitles = [Tier, Special, Basic, Advanced, LegendaryOnly]`、`separators = [0, 4, 22, 87, 127]`。传奇词条都在最后一个分组 `LegendaryOnly(group=4)`，它一直延伸到表尾 —— 所以**直接追加到表尾**就会被算成 group=4。
   （⚠️ 这点和 item 表相反：item 表最后一个分组是 `BossRushStatueUnlock`，追加会落错组。）生成的 diff 里能看到 DCCMTool 回填的 `__separator_group_Name: "LegendaryOnly"`。
2. **affix 的 `props` 是空壳，行为必须自己实现**：
   原版所有传奇词条（`IgnoreGlobalShield`、`DoubleSpeed`…）在 CDB 里 `props` 都是空的，效果写死在游戏代码里按 id 判定。我们的 `ChronoBulletDouble` 同理 —— CDB 那条只负责"有名字、有图标、能被 `legendAffixes` 引用"，真正的翻倍逻辑在 `ChronoBullets` 里。

> ⚠️ 判定要用 `InventItem.hasAffix()`，**不能**去读 `_itemData.legendAffixes` —— 后者是"可 Roll 的池子"，不是"已经 Roll 到的词条"，永远为真。

### 翻倍的三条易错规则

* **"移速翻倍"要先翻倍倍率再减 1**。affect 116 是**加上去**的值（基础跑速倍率 1.0），所以 ×2.0 传的是 +1.0。想把倍率翻成 ×4.0 必须传 +3.0 —— 直接把 +1.0 翻倍成 +2.0 只有 ×3.0。见 `SpeedAffectFromMultiplier()`。
* **负面效果（减速）要缩小倍率**：×0.45 → ×0.225。把 0.45 × 2 变成 ×0.90 是"减速减弱"，方向反了。
* 传奇的 `Yud` 处决延时和记忆动画时长**必须是同一个值**（都 1.5s），否则会出现"动画播完了人还活着"或者反过来，所以 `PlayMemoryAt()` 现在接收时长参数。
* **查"物品身上有没有词条"必须用 `Weapon.item`（`InventItem`），不是 `wInfos.item`**。
  `wInfos.item` 是**物品 id 字符串**（`dc.String`，例如 `"TimeBullet"`）。
  `TimeBullet.IsLegendaryDouble` 原来写的是 `wInfos?.item is InventItem item` ——
  这个 `is` **永远为 false**，于是那个属性恒返回 false，
  表现就是"**传奇刻刻帝的传奇词条从不触发**"（面板文案也不会切成翻倍版）。
  `ChronoBlade.cs` 的 `CurrentItem()` 早就踩过同一个坑并改对了，这处漏了。
  别的地方写 `wInfos.item` 都是 `.ToString()` 取 id（`IsOurWeapon` / 诊断日志），那些是对的。

### 兜底：为什么"面板选传奇出来的是普通货"

原版训练场选武器能出传奇，是因为它**一定在训练场里**：传奇那一步靠 `TrainingWeaponSpawner.Class.lootGen.finalizeLegendaryItem()`，而那个 LootGen

> **只在 `_TrainingWeaponSpawner.__inst_construct__`（真的摆了一个武器生成器实体）时才创建**；普通关卡里 `TrainingWeaponSpawner.Class.lootGen` 是 **null**。

所以任意关卡按 `P` 时，以前 `gen == null` → 整段 LootGen 调用被跳过 → 物品**根本没有 `"Legendary"` 词条** → 既不是传奇外观、也不会把 `legendAffixes` 里的词条算进说明。

现在 `MakeItem()` 不再依赖它：

1. LootGen 拿得到就照原版走（顺带处理等级 / 基础数值）；
2. 拿不到就**自己显式补上 `"Legendary"` 词条**，再挂本模组的传奇词条；
3. 传奇**不叠 `QualityUp`**（原版传奇分支里 `set_weaponQuality(0)`，那条循环加 0 次）；
4. **最后无条件 `item.setItemLevel(level)`** 把面板选的等级写进去。

> ⚠️ 第 4 条是后补的，它修的是"按 `P` 只能召唤一级武器"：
> **等级只有 `finalizeItem()` 那条路会写**，而它在普通关卡里根本不会被调用 ——
> 于是 fallback 分支从头到尾没人设过等级，物品就一直是默认的 1 级
> （连传奇也一样）。
>
> 等级存在 `InventItem._itemLevel`，读写是 `getRawItemLevel()` / `setItemLevel()`；
> `getAdjustedItemLevel()` 在这个基础上加"升级次数 ×2"、传奇再 +6，武器的伤害与
> 需求属性都走那个值 —— 所以写在这里**显示和数值一起修好**。
>
> 放在最后而不是只写在 fallback 里：面板的"等级"是玩家的明确选择，而原版训练场也是
> 把面板值当**基准等级**用的（`finalizeItem(..., overrideBaseLevel: true, ...)`），
> 两条路都对齐到它，行为才可预期。

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
* 第 2a / 3a **完全接管那一刀**（`return true` 跳过原版），所以那两下**没有任何近战判定** ——
  伤害全部来自飞镖与落剑。副作用：原版那一刀的位移 / 击退 / 命中音也一并没有了（这是刻意的）。
* 第 2a / 3a 的实体是**玩家可拥有的同类投射物**（旋转刃 `Saw` / 落石 `Stalactite`），
  不是时之守护者本人那两套贴图 —— 原因见第 1 节的框注（boss 专属实体在普通关卡里生成不出来）。
  表现层（金色法阵 / 背景时钟 / 剑影）仍然是原来的时之守护者特效。
* 罗马数字只有 12 帧（I…XII），超过 XII 会退回最后一帧。
* 目前只有 `P` 面板召唤（地面掉落），没有做"收藏家蓝图解锁"（要进商店/正常掉落需要额外的 blueprint 流程）。
* 面板里的"等级"由 `MakeItem()` **显式写入** `InventItem.setItemLevel()`（见下），不再依赖 LootGen。
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
| 弹药面板只看得见一半说明 | 排版公式是 `y = 框高 - 文字高 - DescBottomPad - DescRaiseLines × 行高`，并且**夹在框内顶部**。`textHeight` 量不到（=0）时贴底会偏大一整个文字高度、下半截被框底切掉 —— 所以量不到就按 `行数 × 行高` 估。行数由 `UpdateDescText()` 写进 `_descLineCount`。进面板会打一行 `弹药说明排版：框高=… 行高=… 行数=… 文字高=…`，拿这几个数就能判断还差多少。 |
| 语音一直不出声 | 先看启动日志有没有 `[ChronoVoice] 语音已加载 8/8 条`。若是 `✗ pak 里没找到语音` → WAV 没打进 pak（检查 `Assets/sfx/`，注意**要重启游戏**，`res.pak` 只在启动时加载）。加载成功却还是不出声，就看有没有 `[ChronoVoice] 独占声道已建立` 和 `播放 …` —— 有"播放"日志但听不见，就是 `VoiceVolume` 太小或者游戏的**主音量**被调低了（语音不受"音效音量"滑块影响，但受主音量影响）。 |
| 语音太频繁 / 太少 | 调 `VoiceChance*` 四个概率。「休闲」是一条规则：一段安静期只掷一次骰，所以想更容易听到就提高 `VoiceChanceIdle`；嫌吵就把某个概率调 0。 |
| 语音被别的音效盖住 | 不应该发生 —— 音频走 `priority = 10000` 的独立 `ChannelGroup` 且 `SoundGroup.maxAudible = -1`。如果确实发生，看日志里 `独占声道已建立` 那条的 priority 是不是 10000（见功能 5 的三条压制链路）。 |
| 第 2a / 3a 一直不出 | 看日志有没有 `连击第 2 段 → 一周飞镖` / `连击第 3 段 → 时钟剑雨`。**有这行**说明连击分派没问题，问题在实体/特效生成（看紧跟着的 `实体 N/12 枚` 与失败堆栈）；**没有这行**说明连击没推进到第 2 下（`_comboStep` 在 `AdvanceCombo` 里推进，需要连续出刀、间隔 < 1.5 秒）。特别注意 `拿到的时之刃是原版 Katana 实例` 那行 —— 出现它表示 `ChronoWeaponFactory` 的 `create` 钩子没命中，根本没有 `ChronoBlade` 对象可调。 |
| 第 2a / 3a 出了但还带斩击 | 有**两个**原因，都要排除：① `SkipMelee` 没在两个钩子入口都判（`RunAttack` + `OnAnyWeaponExecute`），看日志是 `本段不斩击（Katana 挂点路径…）` 还是 `（Weapon 挂点路径…）` —— 一条都不出现说明两个钩子都没拦住；② 蓄力被注入了 → 原版蓄力分支自己播 `AtkKatanaA` 斩击动画（**动画不受 `orig` 是否被调用影响**）。第 2a/3a 必须**不注入** `nextIsChargeAtk`。 |
| 落剑看不见 / 半天才落地 | `Stalactite` 出生行被夹在 `SwordFallMaxCells` 内（屏幕外的天花板会被丢弃），下落速度已按 `SwordFallSpeedMul` 提速；两者都在 `ChronoBlade.cs` 顶部常量里。 |
| 传奇武器不显示传奇词条 / 传奇刻刻帝不翻倍 | 分两步看：① **物品上有没有** —— `已附加词条: Legendary` / `已附加词条: ChronoBulletDouble`；② **代码认不认** —— `传奇词条检查：hasAffix(ChronoBulletDouble) = True`，开火时还会打 `传奇·效果翻倍`。第 ① 有第 ② 没有，就是 `IsLegendaryDouble` 读错了字段（历史上它读的是 `wInfos.item`，那是 id 字符串，`is InventItem` 永远 false；必须用 `Weapon.item`）。 |
| `LootGen 不可用（不在训练场，属正常）` | 预期行为 —— 传奇词条与**等级**都由面板自己补（`setItemLevel`），不影响结果 |
| 十二之弹回不去 | 看日志 `Yud-Bet 回到上一关：<id>（来源=...）`；若提示"换过关之后就能用"，先正常换一关 |
| 按 `P` 召唤出来只有 1 级 | 看日志 `物品等级已写入: LvN（getRawItemLevel=N）`。等级由 `MakeItem()` 末尾的 `setItemLevel()` 写入 —— 普通关卡里 `lootGen` 是 null，那条原版路径根本不会跑，**等级必须自己写**。若这行日志的 Lv 是对的但游戏里显示 1 级，那是 `_itemLevel` 之外还有别的显示来源，把这个日志发我。 |
| 面板里刻刻帝那格不动 | 看日志 `刻刻帝动态图标已创建（TIMEZHANJI，N 帧…）`，`N` 应是 46；没这行就是图集取不到或那一格没被认成刻刻帝。图标偏小就调 `IconArtCell`（越小越大）。 |
| HUD 刻刻帝图标不跟着弹药变 | 看日志有没有 `HUD 图标已同步：第 N 发（第 cx,cy 格）`。切一发弹药就该出现一行；完全没有就是 `FindTimeBullet()` 没拿到枪或 `HUD.updateIcon` 没匹配上（它按 `skill.ii == item` 引用比对）。同步只在"那一发真的变了"时才写，所以不会每帧刷屏。 |
| 构建报 `MSB3021` | 游戏还开着，DLL 被占用，关掉游戏再构建 |
