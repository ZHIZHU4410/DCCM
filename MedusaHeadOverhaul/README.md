# MedusaHeadOverhaul —— 美杜莎的头加强（v2）

> 目标武器：**MedusaHead**（美杜莎的头）
> 原版：扔出去 → 碰到敌人 / 再按一次攻击 → 爆炸（范围伤害 + 击退），命中的敌人被**石化**（affect 46）。

| 改动 | 原版 | 现在（默认） |
|---|---|---|
| **爆炸范围** | 半径 5 格（`area.width = 10` 格直径） | **半径 25 格（×5）** |
| **翻滚距离** | 20 格 | **20 格（×1，可调）** |
| **石化时长** | 2.5 秒 | **25 秒（×10）** |
| **Boss 石化** | Boss 100% 免疫 | **也能被石化** |
| **爆炸无视墙体** | 石化只覆盖视线内 | **隔墙的敌人也石化** |

全部运行时完成，**不改 `data.cdb`**。

---

## ⚠ v1 的坑（为什么"只有翻滚距离变长、爆炸没变化"）

v1 是在 `Hook_Weapon.prepare` 里**持久**改 `wInfos.strikeChain[1].area.width`。
问题：`aoeSkillInf`（爆炸用的技能数据）和 `wInfos.strikeChain[1]` 是**同一个共享对象**，
提前改它会牵动到别的逻辑（表现出来就是"头滚得更远了，爆炸还是原样"）。

**v2 改成"只在爆炸那一刻临时改，跑完立刻还原"**：

```csharp
// Hook_MedusaHeadBullet.trigger
double baseWidth = aoeSkillInf.area.width;         // 10 格
aoeSkillInf.area.width = baseWidth * 5.0;          // 临时 → 50 格（半径 25 格）
try { orig(self); }                                // 原版 trigger() 用这个值算半径
finally { aoeSkillInf.area.width = baseWidth; }    // ★ 立刻还原，滚距/其它逻辑零影响
```

爆炸半径的出处（[MedusaHeadBullet.cs:1284-1292](GamePseudocode/dc.en.bu/MedusaHeadBullet.cs)）：

```csharp
double width = aoeSkillInf.area.width;   // strikeChain[1].area.width
double radiusPx = 0.5 * width * 24.0;    // 0.5 × 10 × 24 = 120px = 5 格
Area area = new Area(radiusPx, null);
fx.kickShockWave(x, y, radiusPx, 12231073);   // 冲击波同半径
```

伤害判定循环、震屏、冲击波都读同一个 `area` → 判定和视觉一起变大。

## 翻滚距离（新增滑条，默认原版）

投掷距离来自 `itemInf.props.range`（构造 `MedusaHeadBullet` 时读来当 maxDist，CDB 里是 **20 格**），
所以在 `Hook_Weapon.prepare` 里按"原始值 × N"设一次（幂等，反复 prepare 不会越乘越大）。

* 默认 **1.0 = 原版**；
* 嫌扔太远就拉到 0.6 / 0.5；想更远再往上。

## 石化时长 ×10

石化只有一个施加点 —— `MedusaHead` 的 onHit（[MedusaHead.cs:40-72](GamePseudocode/dc.tool.weap/MedusaHead.cs)）：

```csharp
e.setAffectS(46, get_curSkillInf().props.duration, ...);   // 投掷段 props.duration = 2.5s
```

拦 `Hook_Entity.setAffectS`，只对 affect 46 把秒数乘 N → **2.5s → 25s**。

> **不改 `props.duration` 数据**：同一份数据还被用来算武器 CD（`MedusaHead.cs:322` 的
> `props.duration * cd.baseFps`），改数据会连带把 CD 也 ×10。

## Boss 也能被石化

`Entity.setAffectS` 里时长会乘 `1 - getAffectResist(x)`，而 Boss 构造函数普遍写了 `setAffectResist(46, 1.0)`：

| Boss | 石化抗性 |
|---|---|
| `_Giant`、`_DookuBeast`、`_Collector`、`_GardenerBoss`、`_HandBase`、`_Eye` | 1.0（完全免疫） |
| `_KingsHand`、`_Collector`（二阶段） | 0.7 |
| `_TimeKeeper_` | 0.5 |
| `_Beholder_` | 0.3 |
| `_MamaTick`、`_Behemoth` | 0.25 |
| 精英怪（`Mob.cs:47818`） | 0.15 |

检测到 `getAffectResist(46) > 0` 就用 **`ignoreResist: true`** 重新施加 → 抗性被绕过，Boss 也能变石头。

## 爆炸无视墙体（新增）

原版 `trigger()` 的**伤害**循环本来就是纯距离判定（没有视线/墙体检查），
但**石化**未必能覆盖到隔墙的敌人。所以爆炸结束后再补一遍：

```
爆炸 → orig(trigger) → 还原 area.width
     → 以爆炸点为中心、按放大后的半径遍历 level.entities
     → 对每个敌对 Mob 做纯距离判定（NearPx），命中就补一次 setAffectS(46, 基础时长)
```

* 纯距离 = **隔着墙也算**；
* 只补石化（伤害本来就穿墙，避免同一发爆炸对同一只怪造成两次伤害）；
* 基础时长取武器"当前技能"的 `props.duration`（投掷段 2.5s），之后照样被 `setAffectS` 钩子 ×10。

---

## 目录结构

```
MedusaHeadOverhaul/
├── 构建.bat                          # 一键 dotnet build（含自动安装）
├── README.md
└── MedusaHeadOverhaul/
    ├── MedusaHeadOverhaul.csproj
    ├── MedusaHeadOverhaulMain.cs   # 钩子 + 爆炸半径/滚距/石化/穿墙 + 选项菜单
    ├── MedusaHeadConfig.cs         # Config<MedusaConfig> + 按键名解析
    └── MedusaHeadFeatures.cs       # 功能开关总控
```

## 构建 / 安装

```powershell
cd "…\coremod\DCCMDEAD CELLS\MedusaHeadOverhaul"
dotnet build -c Debug .\MedusaHeadOverhaul\MedusaHeadOverhaul.csproj
```

或双击 `构建.bat`，产物安装到 `coremod/mods/MedusaHeadOverhaul/`。

> 只想要产物：加 `-p:AutoInstallMod=false`。
> 报 `MSB3021 文件被 DeadCellsModding 锁定` 就是游戏开着，关掉再 build。

## 游戏内开关

「选项 → 模组 → **美杜莎的头**」：

| 开关 | 默认 | 说明 |
|---|---|---|
| 总开关 | 开 | 关掉 = 回到原版 |
| 爆炸范围 ×N | 开 | 只影响爆炸，不影响滚多远 |
| 翻滚距离 ×N | 开 | 默认 ×1 = 原版 |
| 石化时长 ×N | 开 | 挂到目标身上的时长（不改 CD） |
| Boss 也能石化 | 开 | 无视 affect 46 的抗性 |
| 爆炸无视墙体 | 开 | 爆炸后按纯距离补一遍石化（隔墙也吃） |

| 滑条 | 范围 | 默认 | 说明 |
|---|---|---|---|
| 爆炸范围倍数 | 0.5 – 20 | **5.0** | ×5 → 半径 5 → 25 格 |
| 翻滚距离倍数 | 0.1 – 5.0 | **1.0** | 原版 20 格 |
| 石化时长倍数 | 1 – 50 | **10.0** | 2.5s → 25s |

* 配置文件：`coremod/config/MedusaHeadOverhaul.json`；每个开关都能配热键（`KeyToggleXxx`，默认空 = 不绑键）。
* 日志自检（各只打前几次）：
  * `爆炸范围：area.width 10 → 50 格（×5，半径 25 格）`
  * `翻滚距离：range 20 → 20 格（×1）`
  * `石化时长 2.5s → 25s（×10）目标=...`
  * `石化抗性 resist=1 → 强制石化（无视抗性）目标=Giant`
  * `无视墙体：半径 25 格内敌人 N 个，补石化 M 个`

## 已知边界

1. **25 格半径 ≈ 半个房间**：原版 5 格。嫌大就把滑条往下拉（×2~3 更像"加强"而不是"清屏"）。
2. **穿墙补的是石化，不是伤害**：伤害循环本来就无视墙体；如果隔墙的怪没掉血，那多半是它在半径外，拉高倍数即可。
3. **石化是全局 affect 46**：拦的是 affect id，不过游戏里只有 MedusaHead / 美杜莎 Boss 会施加 46，不会误伤别的机制。
4. **Boss 变石头后不保证 AI 完全停摆**：affect 挂上了、时长完整，但个别 Boss 的状态机可能无视控制效果。
5. **不改爆炸伤害**（`power: 45` 保持原版）。
