# KatanaParry —— 武士刀前两刀带盾反

> 目标武器：**Katana**（武士刀）
> 需求：武士刀的**前两段挥砍**加上「盾反」效果。

---

## 1. 原版盾反是怎么运作的

参考 `GamePseudocode/dc.tool.weap/BaseShield.cs`：

```
举盾 → startParry()                                   // 1050
    fx.shieldParryStart(hero, 4480637, null)          //   起手光环
    hero.setAffectS(96, 窗口剩余秒数, ...)             //   affect 96 = 盾反窗口
    hero.lastParryShield = this
敌人攻击命中 → 引擎把 AttackData.hitResult 算成 HitResult.Block()
    → BaseShield.onOwnerAttackResultReceived(a)        // 2776
        if (hero.lastParryShield == this && a.hitResult == Block && !a.hasTag(7))
            triggerParryFeedbacks()                    // 3161：文字/闪屏/盾击光/放射线/音效
            applyStunAndBumpFromParry(a)               // 3229：来源速度减半 + 反向击退 + affect 8 眩晕
            a.addTag(7)                                //   mark：已处理，防重复
```

关键点是：**"格挡判定"发生在盾的格挡区域**（`hitResult = Block`），没有盾就没有这条链路。

---

## 2. 本模组怎么实现

没有盾，所以直接做在**攻击结算**这一层，等价于"这次伤害被吃掉了 + 播盾反反馈"：

| 步骤 | 钩子 | 做的事 |
|---|---|---|
| ① 开窗口 | `Hook_Weapon.prepare` | `self is Katana` 且 `get_cycle() <= 1`（第 1、2 段）→ 打开 `_window`（默认 0.45s），播 `shieldParryStart` 起手光环 |
| ② 吃伤害 | `Hook_Hero.applyAttackResult` | 窗口内 + 来源敌对 + `!a.hasTag(7)` → **不调 orig**（不掉血、不进受击硬直），并把 `a.hitResult` 标成 `Block` |
| ③ 反馈 | 同上 | `popText("Parade !")` + `customMask` 闪屏 + `frontShieldHit` + `hitLines` + 格挡音（全部用原版常量） |
| ④ 反击 | 同上 | 近战来源：速度减半 → 反向击退 → `setAffectS(8, 眩晕秒)` **＋ 一次盾反伤害**；弹丸：**掉头打回去** |
| ⑤ 兜底 | `Hook_Entity.applyAttackResult` | 给"静态派发到 Entity"的路径兜底（Hero 的钩子和它是互斥的） |

> **关于 `tag 7`**：它的真实含义是「**不可格挡**」，不是"已处理"。
> 引擎自己在 `GamePseudocode/dc.tool.atk/AttackTargetImpl.cs:191-193` 就是
> `if (!atk.hasTag(7)) { ...算出 HitResult.Block()... }` 来跳过不可格挡攻击的；
> `createRetaliation`（反击/反弹伤害）和环境伤害也都打这个 tag。
> 所以本模组把它当**门槛**：带 tag 7 的攻击不盾反、照常吃伤害 —— 和原版盾一致。

### ⚠ `hitResult = Block` 是必须的（修的就是 Pitcher 那个"瞬移"bug）

怪物的命中回调普遍长这样（例：Pitcher 扔 Hardy / 扔石头，`GamePseudocode/dc.en.mob/Pitcher.cs:1846-1857`）：

```csharp
if (atk.isSuccess()) {                    // ← 只有"命中成功"才执行
    e.bump(pitcher.dir * props.bump, 0);  // 把英雄推开（扔 Hardy 的 bump = 1 格）
    e.setAffectS(8, props.duration);      // 并眩晕英雄（duration = 1s）
}
```

原版盾反时，`AttackTargetImpl.updateHitResult` 早就把 `hitResult` 算成 `Block` 了，这些回调自然不生效。
而只"跳过 applyAttackResult"是**不够**的 —— `hitResult` 还停在"成功"，怪物那段回调照样把
**英雄**推开 + 眩晕，看起来就像"盾反之后英雄瞬移了一段距离"。

所以盾反成功时要补一刀 `a.hitResult = new HitResult.Block()`
（`AttackData.isSuccess()` 对 `Block` 返回 false），再顺手把英雄身上可能已经挂上的 `bdx/bdy` 清零兜底。

### 弹幕打回去（直接复用原版盾的反射）

调原版 `_BaseShield.createCounterBullet(owner, item, sourceAtk, cBullet)`
（[`_BaseShield.cs:73-384`](GamePseudocode/dc.tool.weap/_BaseShield.cs)）造一发 **`CounteredBullet`**，
速度/贴图/穿透/物理标记全部与原版一致，然后只把它的伤害换成本模组的"盾反伤害 × 75%"。

原版反射的关键参数：

```csharp
double ang = atan2(来源中心 - 弹丸中心);              // 朝来源；无来源则原方向 + π
double spd = cBullet.isHeavy ? 1.5 : 1.0;            // ★ 速度倍率，注意不是原弹丸的速度
var countered = new CounteredBullet(owner, cBullet, atk, ang, spd, color);
countered.pierceCount = cBullet.pierceCount + 1;
```

而 `_Bullet.__inst_construct__` 里 `dx = cos(ang) * 0.9 * spd`、`dy = sin(ang) * 0.9 * spd`
（[`_Bullet.cs:205-216`](GamePseudocode/dc.en/_Bullet.cs)）——
**基础弹速 0.9 px/帧**，原版反射就是 `0.9 × 1.0 = 0.9 px/帧`（重型弹 1.35）。

> 上一版是**复用原弹丸**、保留它原来的绝对速度，所以遇到高速弹丸（例如 Pitcher 的石头
> `props.speed = 1.6` → 1.44 px/帧，或更快的箭）打回去之后就"嗖"一下飞出去 —— 那就是"子弹太快"。
> 现在速度完全按原版走。

⚠ 访问方式：`createCounterBullet` 是 Haxe 静态对象上的 `HlFunc` 字段，
必须写 `BaseShield.Class.createCounterBullet?.Invoke(...)`（与 `AttackUtils.Class.*` 同一个坑）；
拿不到时返回 false，调用方退回"把弹丸打掉"。

### 盾反伤害（一级 15 (30)，随卷轴成长，不带任何盾词条）

照原版 `BaseShield.shieldCounterAttack` 的做法：

```csharp
// ⚠ 必须用去掉下划线的 AttackUtils.Class（Haxe 静态对象上的 HlFunc 字段）
AttackData atk = AttackUtils.Class.createFromHero.Invoke(hero, (dynamic)power, (int?)tier);
atk.addTag(13); atk.setTag(25, true); atk.setTag(2, true);   // 反制 / 完美招架 / 暴击
AttackUtils.Class.hit.Invoke(atk, src);
```

* **一级 15、暴击 30**：暴击是基础伤害的 2×，`setTag(2,true)` 就得到 30；
* **随卷轴成长**：`_AttackData._create` 只把 tier 记进 `sourceTier`，
  真正的 ×1.15/级 缩放发生在 `_AttackUtils.hit → updateDamages`；
* **不加任何盾的词条**：攻击数据用 `createFromHero`（基于英雄构造），
  不读任何盾的 `InventItem` / `props` / affix，也不调 `addAllAffixesFrom`。

> `Hero.applyAttackResult` 是**完整重写**（开头 `activeSkillsManager.onTryApplyAttackResult(a)`，不调 base），
> 所以英雄伤害只走 `Hook_Hero`；`Hook_Entity` 那份是兜底。

### 酒桶 / 炸弹也打回去（照原版 `parryInArea`）

原版盾对投掷物有两条通道（[`BaseShield.cs:1409-1445`](GamePseudocode/dc.tool.weap/BaseShield.cs)）：

* **弹丸** → `counterBullet` → `createCounterBullet` 造 `CounteredBullet`（见上一节）；
* **炸弹** → `parryInArea(area, fullParry)`：举盾期间从 `level.entitiesByClass` 取 `Grenade`（clid 27931），
  逐个 `counterGrenade(g)` → `g.counter(item, tier, playSound)`。

我们没有盾的 `Area` 组件，所以等价成：**窗口内每 0.05s 扫一遍 `level.entities`，
以英雄为中心、半径 `BounceRadiusCells`（默认 2.5 格）内的敌方炸弹/酒桶直接打回去**。

| 目标 | 做法 | 出处 |
|---|---|---|
| 炸弹 `Grenade` | 原版 `g.counter(katanaItem, tier, playSound)`：播 `sfx/env/arrow_block.wav`，造一发 **`RepeledGrenade(hero, 这枚炸弹, item, tier)`**（英雄阵营、朝敌人飞），原炸弹 `destroy()` | [`Grenade.cs:5388`](GamePseudocode/dc.en/Grenade.cs) |
| 酒桶 `ExplosiveBarrel` / `LauncherExplosiveBarrel` | `set_team(hero._team)` + `setActivatedByHero(true)`，把 **`bdx/bdy`** 反向 + 翻转 `dir`，飞回扔桶的怪 | [`ExplosiveBarrel.cs:2165`](GamePseudocode/dc.en.inter/ExplosiveBarrel.cs) |

酒桶那几项都是官方语义：`explode()` 用 `_team.get_targetHelper()` 选目标（所以换阵营后爆炸才打怪），
`createAttack()` 里 `activatedByHero ? createFromHero : createFromMob`（所以英雄化后伤害算英雄的，
半径同时 1.0 → 1.2 格）。

> **⚠ 掉头必须改 `bdx / bdy`，不是 `dx / dy`** —— 这个坑踩过一次（表现为"酒桶照原方向飞"）。
> 酒桶是被 `bump()` 扔出去的，位移走的是 bump 速度：`BarrelLauncher.cs:185` 的发射就是
> `launcherExplosiveBarrel.bump(rotation, 0.2, null)`；原版 `ExplosiveBarrel.bounce()` 也是
> ```csharp
> bumpResistance = 0.0;  bdx = 0.0;  bdy = 0.0;
> bump(dir * forceX, -forceY, null);      // ← 飞行速度在这儿
> ```
> 整个 `ExplosiveBarrel` 里根本没有对 `dx/dy` 的赋值，所以只改 `dx/dy` 等于没改。

> 只跳过原版 `counterGrenade` 里"给反射弹套盾词条"的那一步（`addAllAffixesFrom(盾)`）—— 按需求不加任何盾的词条。
> 另外炸弹/酒桶的**爆炸伤害**不会走"近战反击"分支（它们炸的时候实体已经 `destroy` 了），
> 提前打回去才有意义。
> 酒桶**不按阵营过滤**：场景里的桶多半是 `teamNeutral`，用 `isOpponent` 会全被筛掉；
> 判断"是不是自己扔的"看 `activatedByHero`，是就不动。

### "前两刀"怎么认出来

`Weapon.get_cycle()`：连击序号在**上一刀结算时**由 `incrementCycle()` 递增（最后一段回到 0），
而 `prepare` 发生在**这一刀开始时**，所以那里读到的 cycle 就是这一刀的段号。

Katana 的 `strikeChain` 共 **4 段**（0/1/2/3，第 4 段是蓄力居合），
本模组只对 **0 和 1** 开窗口 —— 即"前两刀"。

---

## 3. 目录结构（纯运行时代码，不需要 CDB 补丁 / res.pak）

```
KatanaParry/
├── 构建.bat                      # 一键 dotnet build（含自动安装）
├── README.md
└── KatanaParry/
    ├── KatanaParry.csproj
    ├── KatanaParryMain.cs      # 钩子 + 窗口 + 盾反判定 + 选项菜单
    ├── KatanaParryFx.cs        # 盾反反馈（原版 FX 常量 + "Parade !" + 音效）
    ├── KatanaParryConfig.cs    # Config<ParryConfig> + 按键名解析
    └── KatanaParryFeatures.cs  # 功能开关总控
```

> 与之前的 [`KatanaBoost`](../KatanaBoost/README.md) 的关系：结构、钩子风格、日志约定都照它来，
> 但那个模组改的是**伤害数据**（需要 `patch_katana_data.py` + `Assets/` + `res.pak`），
> 本模组只加**运行时行为**，所以没有数据补丁，`GenerateSinglePakFile=false`。

---

## 4. 构建 / 安装

```powershell
cd "…\coremod\DCCMDEAD CELLS\KatanaParry"
dotnet build -c Debug .\KatanaParry\KatanaParry.csproj
```

或双击 `构建.bat`。产物安装到 `coremod/mods/KatanaParry/`。

> 只想要产物：加 `-p:AutoInstallMod=false`，结果在 `bin/Debug/net10.0/output/KatanaParry/`。
> 报 `MSB3021 文件被 DeadCellsModding 锁定` 就是游戏开着，关掉再 build。

---

## 5. 游戏内开关

「选项 → 模组 → **武士刀盾反**」：

| 开关 | 默认 | 说明 |
|---|---|---|
| 总开关 | 开 | 关掉 = 武士刀回到原版 |
| 前两刀盾反 | 开 | 第 1、2 段挥砍期间被打 = 盾反 |
| 反击：击退+眩晕 | 开 | 近战来源速度减半 + 反向击退 + 眩晕（弹丸走「弹幕打回去」） |
| 反击伤害 | 开 | 近战来源吃一次盾反伤害（一级 15，暴击 30，随卷轴成长） |
| 弹幕打回去 | 开 | 远程弹丸掉头射回来源（伤害 = 盾反伤害 × 75%） |
| 酒桶/炸弹打回去 | 开 | 窗口内附近的敌方炸弹/酒桶被打回去（原版 `Grenade.counter` / 酒桶英雄化） |
| 盾反特效与文字 | 开 | 起手光环 + "Parade !" + 闪屏 + 盾击光 + 放射线 + 格挡音 |

| 滑条 | 范围 | 默认 | 说明 |
|---|---|---|---|
| 盾反窗口(秒) | 0.05 – 1.5 | 0.45 | 原版前两段单段耗时 = `charge 0.2 + lockCtrlAfter 0.2` = 0.4s |
| 反击眩晕(秒) | 0.1 – 4.0 | 1.0 | 命中来源的眩晕时长 |
| 盾反伤害(一级) | 1 – 500 | 15 | 一级数值，暴击 = 2×（即 30）；卷轴会再乘 ×1.15/级 |
| 弹幕伤害系数 | 0.05 – 3.0 | 0.75 | 打回去那一发 = 盾反伤害 × 该系数 |
| 酒桶/炸弹范围(格) | 0.5 – 8.0 | 2.5 | 窗口内这个距离内的敌方炸弹/酒桶会被打回去 |

* 配置文件：`coremod/config/KatanaParry.json`；每个开关都能配热键（`KeyToggleXxx`，默认空 = 不绑键）。
* 日志里会打一条「首次开盾反窗口 —— 段号=X」的自检，能直接确认"前两刀"判定是否正确。

---

## 6. 已知边界 / 取舍

1. **不是"真·盾反"**：原版盾反依赖盾的格挡区域把 `hitResult` 算成 `Block`；本模组是在
   `applyAttackResult` 这一层直接不吃这次结算（并手动把 `hitResult` 标成 `Block`），所以：
   * 表现上基本一致（不掉血 + 同样的一套特效/文字/眩晕 + 怪物回调里的推开/眩晕也不会生效）；
   * 但**不会**触发"盾反系"天赋/词条（那些要求真的用盾格挡）。
2. **窗口是时间窗口**，不是"那一刀的判定框"：开在挥砍开始，默认 0.45s 覆盖整段；
   觉得太宽松就调小（0.2s 左右就只有前半段能反）。
3. **环境伤害不盾反**（`source == null`，比如陷阱/坠落/毒），也不反友方来源（`isOpponent` 判定）。
4. **反击伤害**：只对**近战来源**结算一次（弹丸的伤害由打回去的那一发承担，避免同一发弹丸吃两次伤害）。
5. **弹幕打回去**走的是原版 `_BaseShield.createCounterBullet` → 生成一发 `CounteredBullet`
   （速度 = 基础弹速 0.9 × 1.0，重型弹 1.5；贴图/穿透/物理标记照原版），原弹丸随即 `destroy()`。
   所以它和"盾反弹丸"在同一条链上，只是伤害数值换成了本模组的（盾反伤害 × 75%）。
   唯一的差异：原版反射弹的基础伤害固定 80，本模组按 15(30) 起算，所以打回去的伤害更低——
   想调就拉「弹幕伤害系数」滑条。
6. **酒桶/炸弹是"提前"打回去的**：原版盾用 `parryInArea` 在举盾期间逐帧扫描，
   本模组同样在窗口内每 0.05s 扫一遍 `level.entities`（半径见滑条）。
   * 炸弹走原版 `Grenade.counter()`，反射弹的爆炸伤害由**炸弹自己的爆炸数据 × 武士刀的等级**决定
     （不是本模组的 15(30)）—— 因为这是原版行为；
   * 酒桶没有原版 `counter`，用官方英雄化路径（换阵营 + `activatedByHero`）+ 反向 `bdx/bdy` 掉头；
   * 酒桶**不按阵营过滤**（场景桶多是中立），只跳过 `activatedByHero`（自己扔的）；
     炸弹仍按阵营过滤（避免把自己的炸弹弹回来）。
7. **只认主手/副手的武士刀**：`self is Katana`（走 `prepare` 钩子），背包里的不算。
