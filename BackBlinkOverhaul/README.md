# BackBlinkOverhaul —— 位移（Déphasage / BackBlink）全面改造

把原版"闪现到**近距离**敌人背后、冷却 2 秒、落地只打一下"的位移技能，
改造成一个**无视墙体 / 无视距离 / 无冷却**、落地带**范围冲击波**、还能**连锁叠层**的突袭技能。

---

## 1. 目录结构（仿 DamageAuraBoost）

```
coremod/DCCMDEAD CELLS/
├── BackBlinkOverhaul/                   ← 工程根（可读写的仓库区）
│   ├── patch_blink_data.py              数据补丁脚本（改 data.cdb 里的 BackBlink 条目）
│   ├── _vanilla_data.cdb                首次运行时自动留存的原版模板备份
│   └── BackBlinkOverhaul/
│       ├── BackBlinkOverhaul.csproj     工程文件（MDK / 打包 / 自动安装）
│       ├── BackBlinkOverhaulMain.cs     主模组：Hook 挂载 + 释放流程 + 连锁
│       ├── BlinkCore.cs                 核心逻辑：锁敌 / 落点 / 特效 / 冲击波
│       ├── BlinkConfig.cs               持久化配置 + 按键名解析
│       ├── BlinkFeatures.cs             功能开关总控（菜单 / 热键 / IsOn 统一入口）
│       ├── data.cdb                     ← 脚本生成，cdb diff 的输入
│       └── Assets/
│           └── data.cdb_/item/BackBlink.json   ← cdb diff 产物（打包进 res.pak）
│
└── ../mods/BackBlinkOverhaul/           ← 构建后自动安装
    ├── BackBlinkOverhaul.dll / .pdb
    ├── modinfo.json
    └── res.pak                         数据补丁（运行时由模组自己 loadPak）
```

## 2. 构建

```powershell
# 1) 生成 data.cdb（改数值后重跑；dotnet build 也会按时间戳自动重跑）
python BackBlinkOverhaul\patch_blink_data.py

# 2) 编译 + 打包 + 安装到 coremod/mods/BackBlinkOverhaul/
dotnet build BackBlinkOverhaul\BackBlinkOverhaul\BackBlinkOverhaul.csproj -c Debug
```

`patch_blink_data.py` 会把模板 data.cdb 里的 `item/item/BackBlink` 改成：

| 字段 | 原版 | 改造后 | 作用 |
|---|---|---|---|
| `props.distance` | 11 | 9999 | 无视距离锁敌 |
| `castCD` | 2.0 | 0.0 | 无冷却 |
| `props.power` | `[70]` | `[210]` | 冲击波基础伤害基数 |
| `legendAffixes` | `[PoisonOnUse]` | `[PoisonOnUse, IgnoreGlobalShield]` | 追加"无视全局护盾"词条 |

> `ShockwaveDamageMult` 会再乘一次，所以实际冲击波伤害 = `210 × ShockwaveDamageMult × (1 + 暴虐×0.25 + 战术×0.25) × 连锁倍率`，
> 再交给原版 `_AttackUtils.createFromHero` 走完整的英雄缩放 / 词条 / 暴击 / 背刺管线。

## 3. 配置

`coremod/config/BackBlinkOverhaul.json`（第一次进游戏自动生成），
也可以直接在游戏里 **选项 → 模组 → 位移·闪现突袭** 逐项开关。

### 传送规则
| 键 | 默认 | 说明 |
|---|---|---|
| `EnableWallPierce` | true | 无视墙体（传送路径不再需要视线） |
| `EnableInfiniteRange` | true | 无视距离（整关任意敌人） |
| `EnableAnyEnemy` | true | 锁定任意敌人（关掉则回到原版 11 格内锁敌） |
| `EnableLandingFix` | true | 落点被堵时自动挪到最近可站立格 |
| `EnableFallbackBlink` | true | 没有敌人时朝面向方向盲闪 |
| `FallbackBlinkCells` | 6.0 | 盲闪距离（格） |
| `ShockwaveOnFallbackBlink` | false | 盲闪是否也结算冲击波 |

### 冷却与充能
| 键 | 默认 | 说明 |
|---|---|---|
| `EnableNoCooldown` | true | 移除冷却（castCD 0） |
| `EnableInfiniteCharges` | true | 释放后立刻清空冷却与充能 |
| `CooldownMult` | 1.0 | 关掉"移除冷却"后的冷却倍率 |
| `MinCastIntervalS` | 0.08 | 两次传送最小间隔（防无限连按） |
| `MaxChainPerFrame` | 8 | 同帧最多结算的连锁次数（递归保护） |

### 视觉 / 音效
| 键 | 默认 | 说明 |
|---|---|---|
| `EnableTrail` | true | 传送路径彩色拖尾 |
| `TrailColor` | `#8A2BE2` | 拖尾颜色 |
| `TrailThickness` / `TrailAlpha` | 6.0 / 0.85 | 拖尾粗细 / 透明度 |
| `EnableAfterImage` | true | 残影 + 起点终点烟雾 / 光柱 / 冲击环 / 落地烟尘 |
| `AfterImageColor` / `AfterImageDurationS` | `#FFFFFF` / 0.25 | 残影颜色 / 时长 |
| `EnableSpeedFeel` | true | 传送瞬间慢动作 |
| `SlowMoScale` / `SlowMoDurationS` | 0.18 / 0.1 | 慢动作倍率 / 时长 |
| `EnableFeedback` | true | 音效 + 屏幕震动总开关 |
| `TeleportSfx` / `LandingSfx` | `sfx/active/active_phaser.wav` | 传送 / 落地音效（可换成 res.pak 里自带的） |
| `SfxVolume` | 0.8 | 音效音量 |
| `ShakePower` / `ShakeDurationS` | 4.0 / 0.12 | 屏幕震动强度 / 时长 |

### 落地冲击波 Blink Strike
| 键 | 默认 | 说明 |
|---|---|---|
| `EnableShockwave` | true | 落地冲击波总开关 |
| `ShockwaveDamageMult` | 1.0 | 伤害系数 |
| `ShockwaveRadius` | 2.0 | 半径（格）—— 贴脸爆发，只覆盖落点周围两格 |
| `BrutalityScaling` / `TacticScaling` | 0.25 / 0.25 | 暴虐 / 战术每点属性加成 |
| `ShockwavePierceWall` | true | 冲击波是否穿墙 |
| `ForceIgnoreGlobalShield` | true | 强制给冲击波挂 `IgnoreGlobalShield`（原版 legendAffixes 只对传说品质生效，这里是保险） |
| `BossDamageMult` / `EliteDamageMult` | 0.35 / 0.7 | 对 Boss / 精英的伤害衰减 |
| `ShockwaveTag` | 0 | 冲击波附加 tag（0 = 不加） |
| `ShockwaveColor` | `#8A2BE2` | 冲击波特效颜色 |

### 连锁传送 Chain Blink
| 键 | 默认 | 说明 |
|---|---|---|
| `EnableChain` | true | 连锁总开关 |
| `ChainWindowS` | 2.0 | 连锁时间窗口（秒） |
| `ChainMaxStacks` | 5 | 连锁上限（0 = 无限） |
| `ChainDamageBonusPerStack` | 0.15 | 每层连锁的冲击波加成 |
| `ChainDamageFalloff` | 0.9 | 超过 1 层后每层的衰减系数 |
| `EnableChainHud` | true | 连锁状态提示（右上角连击 UI） |

**热键**：每个功能都有一个 `KeyToggleXxx`，**默认全部留空 = 不绑键**。
写法：`A`~`Z` / `0`~`9` / `F1`~`F12` / `Space` `Tab` `Shift` `LeftBracket` … / 十六进制 `0x77`。

### 便捷功能（T 键召唤卷轴）

参考 `chuansong+shengcheng` 的 T 键：在主角脚下掉落一个 **AllUp（全属性提升）卷轴**。

| 键 | 默认 | 说明 |
|---|---|---|
| `EnableScrollHotkey` | true | 该功能总开关 |
| `KeyToggleScrollHotkey` | `""` | 给"开关这个功能"本身绑的切换热键（默认不绑） |
| `SpawnScrollKey` | `"T"` | 召唤键（写法和上面一样，支持 `0x54` 这类十六进制） |
| `SpawnScrollId` | `"AllUp"` | 召唤的消耗品 id（可改成 `AnyUp` 等） |
| `SpawnScrollIntervalS` | 0.35 | 两次召唤的最小间隔（秒），防按住刷物品 |
| `SpawnScrollDir` | 0.0 | 抛出方向：`-1` 向后 / `0` 原地 / `1` 向前 |

---

## 4. 需求 → 实现对照

### 1. 基础传送机制改造

| 需求 | 实现 |
|---|---|
| 1.1.1 无视墙体传送 | `Hook_TargetHelper.filterBySight`：在 `BlinkActive` 期间**不调用 orig**，等价于"没有墙挡着"，原版寻路（Bresenham）必然成立 → 直接穿墙（与 chuanqiang / ThrowableStuff / LightningWhipBoost 同一手法）。落点由 `safeTpTo(..., ignoreColl: true)` 写入。 |
| 1.1.2 无视距离传送 | 数据补丁把 `props.distance` 改成 9999；同时 C# 侧在 `disabled` 时会还原成 11（`SyncStats`，可在游戏里热切）。 |
| 1.1.3 锁定近距离怪物 / 任意敌人 | `BlinkCore.FindNearestEnemy`：遍历 `hero._level.entities`，用**与原版完全一致的距离公式**（`1/48` 高度修正）挑最近的合法敌人；`IsValidTarget` 复刻原版条件（`_targetable` / `canBeDetected` / `isOpponent`）。`EnableAnyEnemy` 关掉才回退到 `props.distance` 限制。 |
| 1.1.4 落点判定与安全位置修正 | `BlinkCore.SafeLanding` + `FindSafeCell`：先看落点是否在墙格里，是则按**环形由近到远**搜 6 格内的可站立格，再用 `safeTpTo(ignoreColl:true)` 挪过去。`IsWall` 直接读 `map.collisions` 的 bit0，不依赖实体查询，判据最可靠。 |
| 1.1.5 传送失败 / 无目标处理 | 三手准备：① `FallbackBlink`——有目标就到目标背后/周围 3 格找空位，无目标就朝面向盲闪 `FallbackBlinkCells` 格；② 都不行就 `TryReturnToOrigin` 回起点；③ 回不去就放弃本次结算（但仍清冷却，不会卡住）。原版"没找到目标"的信号由 `OnStartCooldownForItem` 捕获（`BlinkActive` + 物品判定的精确识别）。 |
| 1.2.1 移除冷却 | 数据补丁 `castCD = 0`；同时 `Hook_HeroActiveSkillsManager.startCooldownForItem` 在无冷却开启时把 override 强制改成 `0.0`。 |
| 1.2.2 无充能限制 / 无限释放 | 同一 Hook 里立刻调用 `resetCooldownForItem(item)`；另加 `Hook_canUseActiveSkill` 兜底——原版因"充能没转好"返回 false 时，只要装备的是 BackBlink 且没被定身就放行。 |
| 1.2.3 与连锁传送的兼容逻辑 | 因为已经"无冷却"，连锁不再表达为"重置冷却"，而是 **3.2.3 的"刷新强化窗口 / 叠加层数"**（见 3.2）。连锁层数在每次传送开始时快照进 `BlinkCast.ChainStacks`。 |
| 1.2.4 防止无限连按导致性能问题 | `MinCastIntervalS` 最小施放间隔 + `_depth`/`MaxDepth` 重入保护 + `MaxChainPerFrame` 同帧连锁上限 + `MaxShockwaveTargets = 64` 单次冲击波结算上限。 |
| 1.3.1 传送路径彩色拖尾 | `Fx.tailLineFree(起点 → 落点, 颜色, alpha, 粗细)` + 原版 `Fx.shadowStep`。 |
| 1.3.2 拖尾颜色配置 | `TrailColor`（支持 `#RRGGBB` / `#AARRGGBB` / `0xRRGGBB`）；连锁时自动换成金色 `#FFD24A`。 |
| 1.3.3 残影 / 粒子 / 起点终点特效 | 起点/落点 `smokeBomb`、`entityTeleport` 光柱、`impact` 冲击环、`landSmoke` 落地烟尘、`OnionSkin` 残影（颜色 / 时长可配）。 |
| 1.3.4 传送方向与速度感表现 | `Fx.shadowStep` + `Boot.ME.slowMo(SlowMoDurationS, SlowMoScale)` + 残影 offset 朝移动方向。 |
| 1.4.1 传送音效 | `enableFeedback` 开启时 `lAudio.playEventOn(TeleportSfx, hero, volume)`。 |
| 1.4.2 落地音效 | `PlayLandingSfx`，在冲击波结算时播放 `LandingSfx`。 |
| 1.4.3 屏幕震动 / 闪光 | `hero._level.viewport.shakeS(ShakePower, ShakePower*0.35, ShakeDurationS)`；闪光由 `Fx.impact` / `entityTeleport` 的颜色闪光承担。 |
| 1.4.4 命中与击杀反馈 | 冲击波走原版 `_AttackUtils.hit`，命中反馈（伤害数字 / 血 / 暴击 / 音效 / 硬直）全部由原版管线给出；击杀另见 §3。 |

### 2. 落地冲击波 Blink Strike

| 需求 | 实现 |
|---|---|
| 2.1.1 传送到达瞬间触发 | `Finish()` 在 `orig()` 返回后立刻结算。 |
| 2.1.2 以落点为圆心 | `BlinkCore.Shockwave` 用 `cast.LandX/LandY` 与敌人中心算像素距离。 |
| 2.1.3 每个敌人单次传送只结算一次 | 一次传送只调用一次 `Shockwave`，内部再用 `HashSet<Mob>` 去重。 |
| 2.2.1 范围伤害 | 半径内所有合法敌人逐个走 `hit`。 |
| 2.2.2 基于暴虐属性缩放 | `× (1 + brutalityTier × BrutalityScaling)` |
| 2.2.3 基于战术属性缩放 | `× (1 + tacticTier × TacticScaling)` |
| 2.2.4 伤害类型 / 元素 / 暴击判定 | 用 `AttackUtils.Class.createFromHero` + `useItemAffixes(item)` + `hit`，伤害类型、元素词条、暴击判定全部原版。 |
| 2.2.5 与背刺、处决等机制联动 | 同上——`_AttackUtils.hit` 内部会触发 `Mob.onDirectHitFromHero` / 处决 / 背刺等原版链路。 |
| 2.3.1 冲击波半径 | `ShockwaveRadius`（格），换算成像素并额外加上敌人半径。 |
| 2.3.2 是否穿墙 | `ShockwavePierceWall`；关掉时对每个敌人做一次 `sightCheckCase` 视线判定。 |
| 2.3.3 对 Boss / 精英的伤害衰减 | `BossDamageMult`（按原版 `Boss` 基类 + 各 Boss 类型判定）/ `EliteDamageMult`（`Mob.elite`）。 |
| 2.3.4 对护盾 / 无敌帧的处理 | 不加自定义判定，交给原版 `AttackTargetImpl`。物品数据里追加了 `{ affix: "IgnoreGlobalShield" }`，并且每发冲击波都用原版 `AttackData.addAffix("IgnoreGlobalShield")` 再补一道保险（原版判据是 `atk.hasTag(8) \|\| atk.hasAffix("IgnoreGlobalShield")`）—— 与 KingsSpear / 岩浆同一条链路。开关：`ForceIgnoreGlobalShield`。 |
| 2.4.1 冲击波特效 | `Fx.shockwave` + `Fx.impact` + `Fx.smokeBomb` |
| 2.4.2 地面裂痕 / 光环 | `Fx.groundStones`（半径换算成强度） |
| 2.4.3 命中反馈 | 原版 `hit` 管线；另加 `Fx.landHeavy` |
| 2.4.4 屏幕震动 | 冲击波用 `ShakePower × 1.6`、时长 `× 1.6`，比普通传送更重 |

### 3. 连锁传送 Chain Blink

| 需求 | 实现 |
|---|---|
| 3.1.1 目标死亡判定 | 主路径：`Hook_Mob.onDie` —— 直接由原版宣告死亡，不依赖"伤害后 life 是否同步归零"的时序假设。兜底：`Hook_Entity.applyAttackResult` 里比较 `lifeBefore` / `life<=0 \|\| destroyed`；`Shockwave` 命中循环内还有第三重判定。三重判定共用同一份 `_chainMarks`，`GrantChainStack` 会先 `Remove` 标记，**不会重复叠层**。 |
| 3.1.2 直接击杀 / 间接击杀 | `Mob.onDie` 覆盖一切死法；`applyAttackResult` 兜底路径再检查 `attack.source is Hero \|\| attack.carrier is Hero`，所以反弹 / 持续伤害等间接击杀也算。 |
| 3.1.3 连锁时间窗口 | `ChainWindowS`，与 `_chainEndTime` 比较，由 `BlinkCore.GameTime`（`game.data.gameTimeS`）驱动。 |
| 3.1.4 击杀来源识别 | 靠 **3.3 的激活标记**：只有被本次传送标记过的敌人死亡才叠连锁。 |
| 3.2.1 立即重置冷却 | 因为已无冷却，改为"立刻刷新强化窗口"（见 3.2.3）。 |
| 3.2.2 返还充能 | `resetCooldownForItem` 已经保证每次释放后充能立即回满。 |
| 3.2.3 与"无冷却"设定兼容：刷新强化窗口 / 叠加层数 | `AddChainStack()`：层数 +1、`_chainEndTime = now + ChainWindowS`；到 `ChainMaxStacks` 后只刷新窗口不再叠层。 |
| 3.2.4 防止无限递归与性能保护 | `MaxChainPerFrame` + `_frameChainCount` 每帧清零 + `_depth/MaxDepth`。 |
| 3.3.1 传送后添加激活标记 | `MarkForChain(mob)` 写入 `_chainMarks[mob.__uid] = now + ChainWindowS`；冲击波还会对**每个命中目标**在造成伤害**之前**补标记。 |
| 3.3.2 标记持续时间 | 与 `ChainWindowS` 相同。 |
| 3.3.3 击杀时检测标记 | `Hook_Mob.onDie` / `Hook_Entity.applyAttackResult` 里 `_chainMarks.ContainsKey(mob.__uid)`。 |
| 3.3.4 标记消失与刷新规则 | 每次新传送 `_chainMarks.Clear()`；`TickChain` 每帧清理过期标记。 |
| 3.4.1 传送 → 击杀 → 重置 → 再传送 | 层数 / 窗口 → 下一次传送直接把 `ChainStacks` 带进伤害计算。 |
| 3.4.2 连续击杀奖励 | `ChainDamageBonusPerStack`（每层 +15%）。 |
| 3.4.3 断链惩罚或重置 | 窗口过期 → `_chainStacks = 0` + HUD 复位。 |
| 3.4.4 连锁上限与衰减 | `ChainMaxStacks` 上限；`ChainDamageFalloff` 超过 1 层后每层乘算衰减。 |
| 3.5.1 可连锁状态提示 | `EnableChainHud`：复用原版右上角连击 UI，主数字 = 当前冲击波倍率(%)，`xN` = 连锁层数。 |
| 3.5.2 重置特效 | `BlinkCore.PlayChainFx`：角色脚下金色 `impact` + `smokeBomb` + `groundStones`，层数越多半径越大。 |
| 3.5.3 连杀计数 / UI 反馈 | `TickChain` 每帧刷新剩余窗口 + 日志 `[BackBlinkOverhaul] 连锁 +1 → N 层`。 |

### 4. 系统整合

| 需求 | 实现 |
|---|---|
| 4.1.1 与变异联动 | 冲击波走原版 `hit` 与 `useItemAffixes`，所有"命中 / 击杀"类变异（连击 P_DmgKill、处决、吸血等）自动生效。`ShockwaveTag` 可用来对接自定义 tag 词条。 |
| 4.1.2 与装备 / 词条联动 | `atk.useItemAffixes(cast.Item)`：BackBlink 自带的传说词条（如 `PoisonOnUse`）会正常参与。 |
| 4.1.3 与诅咒体系联动 | 传送 / 伤害都走原版接口，诅咒计数、无伤限制等照旧；模组不绕过任何原版校验。 |
| 4.1.4 与 Boss 战平衡 | `BossDamageMult`（默认 0.35）/ `EliteDamageMult`（默认 0.7）专项衰减；`ShockwavePierceWall` 可在 Boss 房关掉以尊重地形。 |
| 4.2.1 ~ 4.2.7 配置项 | 全部在 `coremod/config/BackBlinkOverhaul.json`，并且每一项都有对应的游戏内开关菜单项。 |

### 便捷功能（额外）

| 需求 | 实现 |
|---|---|
| T 键召唤 AllUp 卷轴 | 参考 `chuansong+shengcheng` 的 `TriggerSpawnEvent`：`new InventItem(new InventItemKind.Consumable("AllUp"))` → `new dc.en.inter.ItemDrop(level, cx, cy, item, true, new Ref<bool>(...))` → `init()` → `onDropAsLoot()` → `dx = hero.dir * SpawnScrollDir`。按 `SpawnScrollKey`（默认 `T`）触发，带边沿检测 + `SpawnScrollIntervalS` 最小间隔双重防抖，开关为 `EnableScrollHotkey`。 |

---

## 5. 已知边界

* **"无视墙体"依赖原版寻路**：`filterBySight` 只负责放行视线，真正的坐标写入仍然是原版的 `hero.safeTpTo(...)`。
  当目标背后的格子本身是实心墙时，原版会传送到**路径上最后一个合法格**（可能离目标较远），
  此时 1.1.4 的落点修正 / `FallbackBlink` 会把它挪到目标周围的空位。极端地形下可能停在目标旁边 1~3 格。
* 传送落点只使用 `isPositionEmpty` + 碰撞位图判定，**不考虑坠落伤害 / 尖刺 / 熔岩**，这些交给游戏本身的机制。
* 连锁的击杀识别基于"本次传送标记过的敌人"。被标记敌人在窗口内被**其它来源**（比如毒、宠物）打死也会算连锁——
  这是有意为之（等价于"这个敌人是在你这次闪现的回合里死的"）。
* 本模组不改动 `GamePseudocode/` 与 `res/`，所有产物都在 `BackBlinkOverhaul/` 与 `coremod/mods/BackBlinkOverhaul/`。
