# SonicCrossbowOverhaul（音波弩改造）

给 **SonicCrossbow（音波弩 / Carabine sonique）** 加上
**随机角度散射 / 距离翻倍 / 攻速翻 6 倍（在 3 倍基础上再加倍）/ 每发随机颜色 / 穿墙**。

## ⚠ 攻速为什么前两版都「没效果」——机关枪有**第三个**、也是真正的限速器

这是本次最关键的修正。音波弩的射速一共被三层东西管着，前两版只碰到了不痛不痒的两层：

| 层 | 控制者 | 用途 | 前两版是否碰到 |
|---|---|---|---|
| 1 | `chargeF -= tmod * getCastSpeed()` | 蓄力 / 起手推进 | ✅ ×3 → ×6，但**不卡射速** |
| 2 | `coolDownF -= tmod * getCooldownSpeed()` | 技能冷却 | ✅ ×3 → ×6，但**不卡射速** |
| 3 | **`autoFireTickS`** | **按住不放时的自动连射间隔** | ❌ **一直没碰到 ← 真瓶颈** |

第 3 层的证据在 `_BaseBow` 的连射回调 `ArrowFunctionEntry_36220(sr)` 里：

```csharp
// _BaseBow.__inst_construct__ 里不管条件如何都会：
arg1.autoFireTickS = 0.1;                     // ★ 默认 0.1 秒一发

// 连射回调里（蓄满后持续按住 → sr > 1.0）：
if (!(sr <= 1.0))
{
    double num3 = autoFireTickS * owner.cd.baseFps;
    IntMap fastCheck = cd.fastCheck;
    int length = 1277165568;                  // ★ 专用冷却 key
    if (fastCheck.exists(1277165568)) flag2 = true;              // 冷却中 → 本帧不开火
    else { ...; new CdInst(1277165568, num5); fastCheck.set(...); }  // 建立间隔 → 本帧开火
}
// flag2 为真才真正 execute / 开火
```

也就是说每发之间被一个**独立的冷却 key（1277165568）**锁死 0.1 秒，
和 `chargeF` / `coolDownF` **完全是两套计时器**。所以：
**改 `getCastSpeed` / `getCooldownSpeed` 动不了机关枪的射速。**

**本次修正**：挂 `Hook_BaseBow.fixedUpdate`，每帧把音波弩的 `autoFireTickS`
压到 `0.1 / 6 ≈ 0.01667s`，也就是射速 ×6。

> 为什么每帧都要压：`BaseBow.onBowCharging()` 系列回调每帧都会把 `autoFireTickS`
> 重新写回 baseline，一次性设置会被下一帧覆盖。
> 时序上放在 `orig(self)` **之前**写是安全的 —— 原版 `BaseBow.fixedUpdate()`
> 只推 `bowChargeF` 和处理满蓄力打断，**不读** `autoFireTickS`；真正读它的是
> `dynOnCharging` 触发的连射回调，而它每帧都会重置该值，所以「原版之前压低」
> 无论回调在本帧之前还是之后执行都成立。

### 关于 `P_AttackSpeed_Combo` 变异（用户提示的参考）

顺着这条线索查清了「正统」攻速入口，供以后参考：

```csharp
// HeroWeaponsManager.getWeaponAttackSpeed(w) —— 只有装了 P_AttackSpeed_Combo 才 != 1
double num6 = props.prct + commonProps.customScaling * (relevantPerkTier - 1);
return 1.0 + num6;

// HeroWeaponsManager 用武器时（约 2976 行）：
double weaponAttackSpeed = getWeaponAttackSpeed(weapon);
onWeaponUse(weapon, slot);
weapon.prepare(weaponAttackSpeed);            // → set_attackSpeed(attackSpeed)
```

`Weapon.prepare(attackSpeed)` 只做 `set_attackSpeed(attackSpeed)`，
而 `_attackSpeed` **唯一**的消费者就是
`WeaponSkill.getCastSpeed() = base.getCastSpeed() * weapon.get_attackSpeed()`
—— 又绕回了第 1 层（蓄力），对机关枪的连射间隔没有作用。
所以即使复刻这个变异，也解决不了音波弩的射速问题；**必须动 `autoFireTickS`**。

## 需求对照

| # | 需求 | 实现 | 位置 |
|---|------|------|------|
| 1 | **散射范围再翻一倍** | 速度矢量 `(dx,dy)` 随机旋转 `±SPREAD_MAX = 0.88` rad（±50.4°，总张角 ≈ **100.8°**） | `ApplySpread()` |
| 2 | **距离再翻一倍** | 第一帧 `maxDist *= 4.80`：`264px` → **1267.2px（52.8 格）** | `OnBulletFixedUpdate()` |
| 3 | **攻速 ×6** | **`autoFireTickS` 0.1s → 0.01667s**（真瓶颈）+ `getCastSpeed`/`getCooldownSpeed` ×6（辅助） | `OnBowFixedUpdate()` 等 |
| 4 | **每发子弹随机颜色** | `initGfx` 的 `orig` **之前**改 `bolt.color` | `OnEntityInitGfx()` |
| 5 | **穿墙** | `ignoreWalls = true` | `OnEntityInit()` |

## ⚠ 攻速为什么上一版 ×3「没效果」——两个计时器走的是不同公式

这是本次最关键的一处修正。`OldSkill.fixedUpdate` 里：

```csharp
chargeF   -= base.tmod * getCastSpeed();        // 起手
...
coolDownF -= base.tmod * getCooldownSpeed();    // 冷却  ← 注意不是 getCastSpeed！
```

- `getCastSpeed()` —— `WeaponSkill` 把它重写成 `base.getCastSpeed() * weapon.get_attackSpeed()`，
  所以挂 `Weapon.get_attackSpeed` **能**影响起手；
- `getCooldownSpeed()` —— `OldSkill.getCooldownSpeed()` 只返回 `cooldownSpeedMul`
  （只有 Taunt 时再乘个常数），**完全不含 `weapon.get_attackSpeed()`**。

冷却 `coolDownF` 由 `startCooldown()` 置为 `coolDownMaxF`，也就是**每发之间的硬间隔**。
对音波弩这种「冷却即射速」的机关枪来说，只缩短起手、不缩短冷却 →
表现就是「**射速没变**」。这正是上一版只挂 `get_attackSpeed` 无效的原因。

**本次修正**：改挂 `dc.tool.skill.Hook_OldSkill` 的 `getCastSpeed` 与 `getCooldownSpeed`，
两者都 ×6，并通过 `(self as WeaponSkill).weapon is SonicCrossbow` 精确定位到音波弩，
不波及其它武器。

> ⚠ 同时**必须撤掉** `Hook_Weapon.get_attackSpeed`：
> 它已经被 `WeaponSkill.getCastSpeed()` 乘进 `castSpeed` 了，
> 如果那里也 ×6，`castSpeed` 会变成 6×6 = **36 倍**（双重叠乘）。
> 只留 `OldSkill.getCastSpeed` 即可，它同时覆盖「起手」与「动画速度」
> （`Weapon.cs:9261` `castSpeed = get_curSkill().getCastSpeed()`）。

## ⚠ 随机颜色为什么必须挂在 `initGfx` 的 `orig` **之前**

`Entity.init()` 内部就调用了 `initGfx()`，而 `SonicBolt.initGfx()` 里：

```csharp
num = color; ... hSprite.color = ...          // 精灵染色（读 color）
if (hasLight) createLight(color, ...)         // 光照颜色（读 color）
```

都是**读** `color` 来算的。等 `init` 跑完（即 `orig` 之后）再改，
精灵与光照已经染好了，改了也看不见。所以：

```csharp
private void OnEntityInitGfx(Hook_Entity.orig_initGfx orig, Entity self)
{
    if (RANDOM_COLOR && self is SonicBolt bolt && !bolt.destroyed)
    {
        bolt.color = NextRandomColor();   // ← 先改
    }
    orig(self);                            // ← 再让原版用新颜色初始化图形
}
```

`SonicBolt.doTail()` 每次开尾迹读的也是当前 `color`，所以尾迹也会跟着变色。

## 原版机制（逐行核对 GamePseudocode）

来自 `GamePseudocode/GamePseudocode/dc.tool.weap.bow/SonicCrossbow.cs`：

```csharp
public override void shoot(ArrayObj bulletsOut)
{
    base.shoot(bulletsOut);                                  // 音效 / 后坐 / 弹药消耗
    int dir = owner.dir;
    double angle = (dir != 1) ? 3.14 : 0.0;                  // 面朝方向
    angle += _Math.random() * 0.01 * (random(2)*2 - 1);      // 原版只有 ±0.01 rad(≈0.57°) 抖动
    AttackData atk = _AttackUtils.createFromHeroWeapon(this, null);
    int color = (int)get_curSkillInf().glowColor;
    SonicBolt bolt = new SonicBolt(hero, atk, angle, 1.5, color, hasLight: true);
    bolt.init();                                            // ← 内部会调 initGfx()
    bolt.radius = 19.2;
    bolt.shootFromWeapon(this, ...);                        // 只读 dx/dy 定方向，不改它
    bolt.offsetOrigin(0, 3 * Math.sin(shootIdx * 2.1));     // 连射上下摆动
    bolt.maxDist = get_curSkillInf().props.range * 24;      // ★ range=11 → 264px ≈ 11 格
    bolt.sprScaleX = bolt.sprScaleY = 1.0;
    bolt.pierceCount += 999;                                // 全穿透（对敌，不对墙）
    bulletsOut.push(bolt);
    // 之后是 sonicShoot / shoot 特效 + FlashLight
}
```

数据表：**weapon** `props.range = 11`、`props.tick = 0.13`、`power = 30`、`charge = 0.17`；
**item** tags `HasBullets` / `Ranged` / `LimitedAmmo` / `IsCrossbow`。

### 为什么散射和射程都放在 `fixedUpdate` 第一帧

```text
new SonicBolt(...)          →  构造函数内部 init()        dx/dy 已按 angle/spd 写好
bolt.shootFromWeapon(...)   →  写入出生点 ox/oy，并读 dx/dy 定方向
bolt.maxDist = range * 24   →  ★ maxDist 到这里才写入（264px）
bulletsOut.push(bolt)
```

- 在 `init` 里改 `maxDist` → 会被原版**覆盖**，白改；
- 到 `fixedUpdate` 第一帧，`dx/dy` 已是最终值、`maxDist` 已是基线 264 →
  **一次到位，既不被覆盖也不会重复叠乘**。

### 为什么穿墙就是 `ignoreWalls`

```csharp
// Bullet.fixedUpdate() 开头
if (!ignoreWalls) { ...读 levelMap.collisions 判断是否撞墙... } else { flag = false; }
```

`ignoreWalls = true` 时整段墙体判定走 `else` 分支，弹丸既不会 `blockOnCollision()`
也不会 `onHitWall()`。（同一套做法见 `ThrowableStuff` 的 `OnStuffInit`。
注意 `SonicBolt` 自带的 `pierceCount += 999` 是「穿透敌人」，**对墙无效**。）

### 为什么射程只用 `maxDist`

```csharp
// Bullet.getCoveredDistSqr()：以出生点 ox/oy 为圆心算真实飞行距离
return (x - ox) * (x - ox) + (y - oy) * (y - oy);

// Bullet.fixedUpdate() 末尾
if (!(getCoveredDistSqr() <= maxDist * maxDist)) { onReachMaxDist.Invoke(); reachMaxDist(); }
```

`maxDist` 就是「真实飞行距离上限」，乘 4.8 即精确翻倍（264px → 1267.2px）。
不需要自管射程 / 逐帧记账 / 手动 `destroy()`。

## ⚠ 踩坑记录：不能挂在 `SonicCrossbow.shoot` 上

**最早一版**把逻辑挂在 `Hook_SonicCrossbow.shoot`，实测**一次都没触发**：

```
[ChronoBlade] Weapon.onExecute 触发 #1..#20: 类型=SonicCrossbow item=SonicCrossbow   ← 确实开了 20 次火
[SonicCrossbowOverhaul] 游戏退出（射击 0 次，改写 0 发，异常 0 次）                    ← 一次都没进
```

MDK 的 Hook 拦截的是 **Hashlink 函数槽入口**，而「基类 virtual `shoot` → 子类 override」
这条链实际执行时并不走 `SonicCrossbow.shoot` 那个槽 —— **挂在子类 override 上的 Hook
会「订阅成功但永不触发」**（不报错、不抛异常，静默失效）。

同理反查 GameProxy 的生成包装类：

| 想挂的 | 实际是否存在 |
|--------|--------------|
| `dc.en.bu.Hook_SonicBolt.init` | ❌ 不存在 |
| `dc.en.Hook_Bullet.init` | ❌ 不存在 |
| **`dc.Hook_Entity.init`** | ✅ `orig_init(Entity self)` |
| **`dc.Hook_Entity.initGfx`** | ✅ `orig_initGfx(Entity self)` |
| `dc.en.Hook_Bullet.fixedUpdate` | ✅ `orig_fixedUpdate(Bullet self)` |
| **`dc.tool.skill.Hook_OldSkill.getCastSpeed` / `getCooldownSpeed`** | ✅（命名空间是 `dc.tool.skill`） |
| `dc.tool.Hook_Weapon.get_attackSpeed` | ✅ 但**本模组刻意不挂**（会双重叠乘，见上） |

### 最终 Hook 一览

| Hook | 干什么 |
|------|--------|
| `Hook_Entity.init` | 认出 `SonicBolt` → 登记 + `ignoreWalls = true` |
| `Hook_Entity.initGfx` | **orig 之前** `color = 随机色` |
| `Hook_Bullet.fixedUpdate` | 第一帧：散射 + `maxDist ×4.8`，做完即删表项 |
| **`Hook_BaseBow.fixedUpdate`** | **每帧把 `autoFireTickS` 压到 0.1/6 ← 射速真瓶颈** |
| `Hook_OldSkill.getCastSpeed` | 音波弩技能 → 蓄力 / 动画 ×6（辅助） |
| `Hook_OldSkill.getCooldownSpeed` | 音波弩技能 → 技能冷却 ×6（辅助） |
| `Hook_SonicCrossbow.shoot` | 兜底，带去重不会重复叠乘 |

## 验收：直接量连射间隔

日志现在带**毫秒**时间戳（`[HH:mm:ss.fff]`），可以直接量射速：

```
[21:50:22.416] [SonicCrossbowOverhaul] 音波弹 #1 出生：dx=-1.35 dy=0.003 ...
[21:50:22.433] [SonicCrossbowOverhaul] 音波弹 #2 出生：...
[21:50:22.449] [SonicCrossbowOverhaul] 音波弹 #3 出生：...
```

相邻两条「音波弹 #N 出生」的时间差 = **实际每发间隔**。
期望 ≈ `0.0167s`（16.7ms）；若仍是 ~100ms，说明 `autoFireTickS` 没被压住，把日志发我。

一键统计（在 `coremod/logs` 目录下运行）：

```powershell
$t = Select-String -Path log_latest.log -Pattern '音波弹 #\d+ 出生' |
     ForEach-Object { if ($_.Line -match '\[(\d\d):(\d\d):(\d\d)\.(\d\d\d)\]') {
        [int]$matches[1]*3600000 + [int]$matches[2]*60000 + [int]$matches[3]*1000 + [int]$matches[4] } }
$d = 0..($t.Count-2) | ForEach-Object { $t[$_+1] - $t[$_] }
"平均间隔 = {0:N1} ms" -f ($d | Measure-Object -Average).Average
```

> 注意：`autoFireTickS` 换算成帧时是 `ffloor(tick * baseFps * 1000)/1000`，
> 所以间隔不可能小于 1 帧（60fps 时 ≈16.7ms）。×6 已经压到 1 帧下限，
> 想再快只能提高帧率或改 GameSpeed —— 数值上已经到头了。

## 目录结构

```
SonicCrossbowOverhaul/
├── SonicCrossbowOverhaul.sln
├── README.md
└── SonicCrossbowOverhaul/
    ├── SonicCrossbowOverhaul.csproj
    ├── SonicCrossbowOverhaulMain.cs     # 全部逻辑
    └── 构建.bat
```

排版对齐 `DamageAuraBoost` / `ThrowableStuff`：`GenerateDiffCDB = false`、
`GenerateSinglePakFile = false` → 不需要 `Assets/` 与数据补丁；
`GameVersion = 35`、`AutoInstallMod = true`（Debug）→ 构建完自动装到
`coremod/mods/SonicCrossbowOverhaul/`。

## 构建

```bat
cd SonicCrossbowOverhaul\SonicCrossbowOverhaul
dotnet build
```

或直接双击 `构建.bat`。

## 可调参数

| 常量 | 默认 | 含义 |
|------|------|------|
| `SPREAD_MAX` | `0.88` | 散射角最大偏移（弧度，≈ ±50.4°，总张角 ≈100.8°） |
| `RANGE_MULT` | `4.80` | 射程倍率（264px → 1267.2px ≈ 52.8 格） |
| `ATTACK_SPEED_MULT` | `6.0` | 攻速倍率（`autoFireTickS` 0.1s → 0.01667s，另含蓄力/冷却 ×6） |
| `DEFAULT_AUTO_FIRE_TICK` | `0.1` | 原版连射间隔基线（`_BaseBow` 构造里写死），改 `ATTACK_SPEED_MULT` 即可 |
| `RANDOM_COLOR` | `true` | 每发随机颜色 |
| `WILD_COLOR` | `true` | `true`=三通道全随机；`false`=只随机色相、亮度稳定 |
| `WALL_PIERCE` | `true` | 是否穿墙 |
| `LOG_SAMPLES` | `12` | 细节日志采样上限 |
| `DEBUG_LOG` | `true` | 细节日志开关 |

调参提示：想更聚拢 → `SPREAD_MAX = 0.2`；想更远 → `RANGE_MULT = 8.0`；
攻速过载掉帧 → `ATTACK_SPEED_MULT = 3.0`；不想要颜色 → `RANDOM_COLOR = false`。

> 注意：`autoFireTickS` 换算成帧是 `ffloor(tick * baseFps * 1000)/1000`，
> 间隔不可能小于 **1 帧**（60fps ≈ 16.7ms）。×6 已经把 0.1s（6 帧）压到 1 帧下限，
> **数值上到头了** —— 再想快只能提高帧率。
> 若掉帧明显，优先调小 `ATTACK_SPEED_MULT`。

## 验证

看 `coremod/logs/log_latest.log`：

```
[SonicCrossbowOverhaul] 已加载：散射 ±50.4° / 射程 ×4.8（1267px ≈ 52.8 格）/ 攻速 ×6（连射间隔 0.1s → 0.0167s）/ 穿墙=True / 随机颜色=True | hooks: Entity.init + Entity.initGfx + Bullet.fixedUpdate + BaseBow.fixedUpdate + OldSkill.getCastSpeed + OldSkill.getCooldownSpeed (+SonicCrossbow.shoot 兜底)
[21:50:22.416] [SonicCrossbowOverhaul] 音波弹 #1 出生：dx=1.5 dy=0 maxDist=1e+09 ignoreWalls=True
[21:50:22.416] [SonicCrossbowOverhaul] 音波弹 #1 随机颜色 = #A73F1C
[21:50:22.416] [SonicCrossbowOverhaul] 散射=+0.153 rad（8.8°）
[21:50:22.416] [SonicCrossbowOverhaul] 射程 264 → 1267.2px（×4.8）
[21:50:22.416] [SonicCrossbowOverhaul] 连射间隔 0.1s → 0.0167s（攻速 ×6）
...
[SonicCrossbowOverhaul] 游戏退出（生成 N 发 / 散射 N / 射程 N / 随机颜色 N / 攻速改写 N / 异常 0）
```

**验收要点**：

- `生成 N 发` > 0（说明 `Entity.init` 进了，已验证过）；
- `随机颜色 N` ≈ `生成 N`（说明 `initGfx` 也进了）；
- 单位时间内的 `生成` 增速应约为上一版的 **2 倍**（攻速 ×3 → ×6）。

肉眼验收：

1. 子弹呈约 **100°** 大扇形散开；
2. 飞约 **52 格**才消失；
3. 每一发**颜色都不同**（精灵、光晕、尾迹一起变色）；
4. 子弹**穿墙**；
5. 射速明显比上一版更快。
