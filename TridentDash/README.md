# TridentDash — 深渊三叉戟 · 常驻自动冲锋

把 **Abyssal Trident（Trident / 深渊三叉戟）** 里"一直往前冲"的那一击（蓄力冲锋）
做成**常驻自动状态**：装备着就一直冲，可随时转向，时间无限，不用按攻击键。

## 一、哪一击才是"一直冲刺"

`res/data.cdb → weapon@strikeChain`（item id = `Trident`）三段：

| cycle | 动画 | power | 关键字段 | 是什么 |
|---|---|---|---|---|
| 0 | `AtkTridentB` | 65 | `props.bump 0.1` | 普通挥击 |
| **1** | **`AtkTridentC`** | 120 | **`props.speed 0.5`**、`props.tick 0.05`、`charge 1.25` | **★蓄力冲锋（一直往前冲）** |
| 2 | `AtkTridentD` | 60 | `charge 0`、`props.bump 0.1` | 收尾突刺（小 bump） |

`GamePseudocode/dc.tool.weap/Trident.cs` 里，`initSkill` **只给 `idx == 1`** 挂了
`dynOnChargeStart / dynOnCharging / dynOnInterrupt`；`doChargeAttack()` 逐帧：

```
没刺中：owner.dx = dir * props.speed(=0.5) * 跑速加成     // 单位：格/帧
刺中时：owner.dx = 目标速度（封顶 props.speed * 1.3）
每 props.tick(0.05s) 对身前敌人反复结算伤害，暴击时钉住目标
```

## 二、改了什么

| 需求 | 做法 |
|---|---|
| **只保留冲锋那一击** | `Hook_Weapon.get_cycle` 对三叉戟恒返回 `1`，普通挥击与收尾突刺都不再出现 |
| **攻击一次 = 无限冲刺** | `Hook_Trident.onExecute`（攻击出手）时进入冲刺状态，之后 `Hook_Trident.fixedUpdate` 每帧把 **`hero.bdx`** 顶成 `dir × props.speed` |
| **翻滚打断** | `Hook_DodgeRoll.onStart`（翻滚 = `dc.tool.mainSkills.DodgeRoll`）停掉冲刺，并把 `bdx` 清零 |
| **随时转向** | `Hook_Trident.allowTurnDuringCharge` 恒 `true`（`HeroWeaponsManager` 判断"冲锋中能否掉头"的唯一开关），`onOwnerTurned` 后把 `canTurnNbLeft` 顶满，并且每帧让方向跟随 `hero.dir` |
| **伤害** | 按原版 `props.tick` 节奏调 `doChargeAttack(1.0)`（1.0 > 暴击阈值 0.52），不会因为逐帧调用而翻倍 |

冲刺只会在这些情况结束：**翻滚**、换下武器、角色死亡、（可选的）过场动画。

### 手感与特效（让它"爽"的部分）

| 效果 | 做法 |
|---|---|
| **弹射起步** | 出发那 9 帧按 `巡航速度 × 2.4` 冲，再用缓出曲线（`1-(1-t)²`）落回巡航速度 —— 前段猛、后段稳 |
| **引擎自带冲刺拖尾** | 每 3 帧调一次 `Fx.dash(hero, dir, color, …)`，这是原版怪物冲刺用的同一个特效；颜色取三叉戟 `strikeChain[1].glowColor`（青色系） |
| **起步冲击波** | 出发瞬间 `Fx.dispShockWave` |
| **击杀加速** | `Hook_Entity.onDie`：冲刺中每杀一个怪，速度 +12%（上限 +60%），逐帧衰减；同时在尸体位置爆一圈冲击波 —— 越杀越冲 |
| **冲刺无视墙体** | 冲刺开始时把 `hero.collisionMode` 切成 `CollisionMode.IgnoreWalls`（引擎枚举第 7 项），冲刺结束还原成原来的模式 |
| **冲刺获得护盾** | 每帧刷新 `hero.setAffectS(5, 0.25)` —— **5 号就是 `toggleFullInvincibility` 用的那个无敌 affect**，所以冲刺期间无敌，结束后还会残留 0.25 秒 |
| **必定暴击** | 伤害走 `doChargeAttack(1.0)`，1.0 > 原版暴击阈值 0.52 |

> 相关 CDB 事实：`Fx.dash(Entity, int dir, int c, ref int dx, ref double scaleMul)`
> 是引擎里 `Arbiter` / `Archer` / `CrossbowMan` / `Enforcer` / `KunaiMaster`
> 这些怪冲刺时调的那个函数（颜色都传 `7628951`），所以直接用它是"原生手感"。

### 为什么必须写 `bdx` 而不是 `dx`

之前写的是 `hero.dx`，结果**只有按攻击（原版蓄力）时才动**。原因是：

* `Hero` 的走路代码每帧都会重算水平速度
  （[Hero.cs:83189](GamePseudocode/dc.en/Hero.cs#L83189)：`dx = runSpd * accelFactor * dir * frameWalkSpd * …`），
  所以外部写的 `dx` 立刻被覆盖 —— 只有武器动作锁住控制的那几帧才留得住；
* 而 `Entity` 的物理里**总移动 = `dx + bdx`**
  （[Entity.cs:26273](GamePseudocode/dc.Entity.cs#L26273)），`bdx` 只被 `bump()` 和
  `cancelVelocities()` 改动，输入完全不碰它
  （[Entity.cs:20415](GamePseudocode/dc.Entity.cs#L20415)：`bdx = bdx * bumpFrict`）。

顺带一提：翻滚 `DodgeRoll` 全程只用 `hero.dx`、从不碰 `bdx`
（[DodgeRoll.cs:1994](GamePseudocode/dc.tool.mainSkills/DodgeRoll.cs#L1994) 等），
所以冲刺用 `bdx` 不会和翻滚抢同一个通道。

速度与节奏都不写死：`props.speed` / `props.tick` 从 CDB 的 cycle 1 现读。

### 几个保护

* 只对**握在手上**（`mainWeapons`）的三叉戟生效，背包里的不会把角色推着走；
* 过场动画期间暂停（`Game.hasCinematic()`），避免把角色推出剧情演出；
* 角色死亡 / 武器销毁 / 关闭模组时会立刻停止冲刺。

## 三、配置

`coremod/config/TridentDash.json`（首次运行自动生成），或游戏内
**选项 → 模组 → 深渊三叉戟·常驻冲锋**：

| 项 | 默认 | 说明 |
|---|---|---|
| `EnableMod` | `true` | 总开关 |
| `OnlyChargeAttack` | `true` | 只打冲锋这一击 |
| `AlwaysReady` | `true` | 随时可出手 |
| `InfiniteDash` | `true` | **攻击一次 = 无限冲刺** |
| `InterruptOnRoll` | `true` | **翻滚打断** |
| `TurnAnyTime` | `true` | **随时转向** |
| `AutoDamage` | `true` | 冲刺期间按原版 tick 结算伤害 |
| `DashSpeedMul` | `1.0` | 巡航速度倍率（基准 0.5 格/帧） |
| `LaunchMul` | `2.4` | **起步爆发倍率** |
| `LaunchFrames` | `9` | 爆发持续帧数 |
| `UseTrail` | `true` | 冲刺拖尾 |
| `TrailInterval` | `3` | 拖尾间隔帧 |
| `StartBurst` | `true` | 起步冲击波 |
| `KillSpeedBoost` | `0.12` | **每杀一个怪的速度加成** |
| `KillBoostMax` | `0.6` | 加成上限 |
| `KillBoostDecay` | `0.985` | 加成衰减系数 |
| `IgnoreWalls` | `true` | **冲刺穿墙** |
| `ShieldWhileDashing` | `true` | **冲刺无敌护盾** |
| `ShieldSeconds` | `0.25` | 护盾刷新时长（结束后残留） |
| `DashFrames` | `14` | 仅「无限冲刺」关闭时生效 |
| `DashInAir` | `true` | 空中也保持冲刺 |
| `PauseInCinematic` | `true` | 过场暂停 |

> 觉得太快：`DashSpeedMul` 调到 0.7、`LaunchMul` 调到 1.8。
> 想要更疯：`KillSpeedBoost` 0.2 + `KillBoostMax` 1.2。
> 卡顿（粒子太多）：`TrailInterval` 调到 6~8。

## 四、构建 / 安装

```powershell
cd TridentDash\TridentDash
dotnet build -c Debug          # AutoInstallMod=true，自动装到 coremod\mods\TridentDash
```

装好后 **重启游戏** 才会加载。

## 五、边界说明

* 只依赖 DCCM 框架，不改游戏资源（不需要 res.pak / CDB 补丁）。
* 自动冲刺会让角色一直前进 —— 这是需求本身的效果。想停下来时把
  `AutoDash` 关掉，或直接换武器。
* 伤害沿用原版 `doChargeAttack`，间隔为 CDB 里的 `props.tick`。
