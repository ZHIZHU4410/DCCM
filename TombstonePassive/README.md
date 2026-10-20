# TombstonePassive —— 墓碑被动灾厄

> 目标武器：**Tombstone**（墓碑 / Pierre tombale）
> 需求：**不再攻击**；带上墓碑后，**每移动一格**就以英雄为中心触发一次「灾厄」，改成被动。

---

## 1. 原版第三下到底做了什么

`GamePseudocode/dc.tool.weap/Tombstone.cs` 的 `hitFromWeapon()` 里，`isLastCycle()` 分支：

```csharp
double r = get_curSkillInf().props.range - 0.5;      // strikeChain[2].props.range = 12 格
fx.radiusExplosion(x, y, r, 5587165, null);          // ① 半径爆环（圆心 = 被命中的敌人）
var targetMobs = [];
foreach (Mob mob in hero._team.opponentMobsIterator) {
    if (mob == e) continue;                          //    命中目标本身不算
    if (dist(mob, e) <= props.range * 24) {          // ② 半径内的敌人
        targetMobs.push(mob);
        mob.customStatuses.remove("affectTombstone");// ③ 挂"灾厄"状态图标
        mob.refreshIcons();
        mob.customStatuses.push("affectTombstone");
        mob.refreshIcons();
    }
}
doGlas(0, targetMobs);                               // ④ 每个目标头顶落墓碑
```

而 ④ `Tombstone.doGlas(wave, targetMobs)`（`Tombstone.cs:575`）会对**每个目标**：

1. 用 `hero.getDiminishingFactor(69, 1, item.props.limit, null)` 算伤害比例；
2. 在目标头顶 `item.props.height = 1.5` 格处生成 `TombstoneEntity`（`doomedMob` = 该目标）；
3. 墓碑落地 → `glasDamage()` → 对目标 `_AttackUtils.hit` + `setAffectS(8, duration3=1s)`；
4. 再 `doWeaponGlas()` 继续向**它周围**的怪扩散（这就是"灾厄连锁"）。

---

## 2. 本模组怎么改成被动

**只把"圆心"从「被命中的敌人」换成「英雄自己」**，①②③④ 全部照抄：

| 步骤 | 原版 | 本模组 |
|---|---|---|
| 触发条件 | 第三下平 A 命中且目标是 Mob | **英雄的格子坐标（`hero.cx / cy`）发生变化**，即"移动一格" |
| 圆心 | `e`（被命中敌人）的中心 | `hero` 的身体中心 |
| 半径 | `props.range = 12` 格 | 滑条「灾厄半径」，默认 12 格 |
| ①②③④ | — | 原样复刻（见第 1 节） |

顺手补了两个**不改就没法用**的点：

1. **灾厄伤害不递减**（默认开）
   原版 `getDiminishingFactor` 对 key 69 有个硬规则（`GamePseudocode/dc/Entity.cs:4133`）：

   ```csharp
   count = 1 + diminishingReturns[key];
   if (count >= absoluteMaxUses) return 0.0;     // limit = 3 → 5 秒内第 3 次起伤害直接归零
   ```

   那是给"三连击"设计的节流。改成每格触发后，几乎每次都是第 3 次以后 → **伤害恒为 0**。
   打开本开关后，key 69 的递减因子直接由「灾厄伤害系数」决定（其它 key 的递减完全不动）。

2. **英雄脚下也落一块墓碑**（默认关，纯视觉）
   `<c>doomedMob = null</c>` 的构造分支：自由落体 + 落地碎裂 + 放 `weapon_tombstone_hit3.wav`，**不结算伤害**。
   注意 `modelNum` 必须 1..`props.count-1`（帧名 `Weapon_Tombstone{modelNum*10}`），
   0 会拼不出帧导致 `spr == null` 崩游戏（MobBuffCombo 踩过这个坑）。

---

## 3. 目录结构（纯运行时代码，不需要 CDB 补丁 / res.pak）

```
TombstonePassive/
├── 构建.bat                          # 一键 dotnet build（含自动安装）
├── README.md
└── TombstonePassive/
    ├── TombstonePassive.csproj
    ├── TombstonePassiveMain.cs      # 入口：格子检测 + 递减拦截 + 选项菜单
    ├── TombstoneDoom.cs             # 灾厄本体：找武器 / 半径筛选 / 挂状态 / doGlas / 落墓碑
    ├── TombstoneConfig.cs          # Config<DoomConfig> + 按键名解析
    └── TombstoneFeatures.cs        # 功能开关总控
```

---

## 4. 构建 / 安装

```powershell
cd "…\coremod\DCCMDEAD CELLS\TombstonePassive"
dotnet build -c Debug .\TombstonePassive\TombstonePassive.csproj
```

或双击 `构建.bat`。产物安装到 `coremod/mods/TombstonePassive/`。

> 只想要产物：加 `-p:AutoInstallMod=false`，结果在 `bin/Debug/net10.0/output/TombstonePassive/`。
> 报 `MSB3021 文件被 DeadCellsModding 锁定` 就是游戏开着，关掉再 build。

---

## 5. 游戏内开关

「选项 → 模组 → **墓碑被动**」：

| 开关 | 默认 | 说明 |
|---|---|---|
| 总开关 | 开 | 关掉 = 全部恢复原版 |
| 被动灾厄（每移动一格） | 开 | 带墓碑走路，每跨一格触发一次灾厄，无需攻击 |
| 灾厄伤害不递减 | 开 | 绕过 `getDiminishingFactor` 的"第 3 次归零" |
| 英雄脚下也落墓碑 | 关 | 纯视觉，走路会刷得比较密 |

| 滑条 | 范围 | 默认 | 说明 |
|---|---|---|---|
| 灾厄半径(格) | 1 – 30 | 12 | 原版 `strikeChain[2].props.range` |
| 最小触发间隔(秒) | 0 – 3 | 0 | 0 = 严格每格触发；拉大可以降频 |
| 灾厄伤害系数 | 0.05 – 3.0 | 0.5 | 1.0 = 原版满伤害（`item.props.power = 160`） |

* 触发条件是**格子坐标变化**（`hero.cx / hero.cy`）：走路、跑、下落、被击退都算，**站着不动不触发**。
* 配置文件：`coremod/config/TombstonePassive.json`；每个开关都能配热键（`KeyToggleXxx`，默认空 = 不绑键）。

---

## 6. 已知边界 / 取舍

1. **只认主手/副手装备的墓碑**（`weaponsManager.findInMain(Tombstone.Class)` + 扫 `mainWeapons`）。
   放在背包里的墓碑不会触发。
2. **一个格子 = 一次触发**：如果一帧内跨了多格（比如高速下落），也只触发一次。
   想更密/更疏就用「最小触发间隔」。
3. **平 A 完全不受影响**：第三下的原版灾厄照旧，模组只是**额外**加了一条被动触发路径。
4. **伤害密度**：因为触发频率远高于原版三连击，默认 `伤害系数 = 0.5`；
   觉得太强就往下调，觉得不够就拉高（上限 3.0）。每次触发里每个目标最多落一块墓碑，
   同一目标还有原版 `props.duration2 * 0.5 = 0.1s` 的重复保护。
5. **没有改任何游戏数据**：半径/伤害/时限全部来自原版 `data.cdb` + 运行时参数，所以不会和其它
   改墓碑数据的模组打架（它们改的是 CDB，本模组只读）。
6. **数值常量与 v35 对应**：`props.range = 12`、`props.count = 5`、`props.height = 1.5`、
   `props.duration3 = 1`、`props.limit = 3`、`item.props.power = [160]`。
