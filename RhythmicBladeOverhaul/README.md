# RhythmicBladeOverhaul — 节奏之刃（Oud）

把节奏之刃改成"**必定节奏打击、以主角为圆心的 360° 圆形打击、无视墙体，
判定范围 / 伤害 / 特效统一固定 3 倍**"。

## 一、原版机制（反编译 + 游戏资源双向确认）

### strikeChain（`res/data.cdb → weapon@strikeChain`）

| cycle | animId | power | critMul | area |
|---|---|---|---|---|
| 0 | `atkOudeA` | 55 | – | shape 1（矩形）width 5 / height 5 |
| 1 | `atkOudeB` | 80 | 1.0 | shape 1（矩形）width 5 / height 4 |
| 2 | `atkOudeC` | 130 | **1.5** | shape 0（圆）width 9 |

CDB 里这一行**没有** `fxScale`，`fxProps` 只有 `fxOffset`；
`res` 里也没有独立的「打击范围」配置 —— 范围是**代码里硬编码**的。

### 初始范围：6 格（原版就这么大）

`RhythmicBlade.getRealCurArea()`：

```haxe
if (!shouldCrit) return areas[cycle];        // 只有非暴击才用 CDB 的 area
if (cycle == 2) return new Area(144.0, null);
```

圆形构造 `heiPx = 入参*2; widPx = heiPx` → `widPx = 288`；
`canHit` 圆形分支的门限是 `widPx * 0.5 + e.radius` → **144px = 6 格半径**。

> 所以"初始范围大"是原版数值，不是本模组加的。以前感觉小，是因为原版还要过
> 墙体检测 + 必须踩准节拍。本模组现在只是把它 ×3。

### 命中判定链

`onExecute` 的命中循环把 `getRealCurArea()` 的结果**显式**传给 `canHit`：

```haxe
realCurArea = getRealCurArea();
if (canHit(entity, realCurArea)) hitFromWeapon(entity, ...);
```

而 `canHit` 在几何判定**通过之后**还有两关：

1. **沿主角→目标做 Bresenham 直线、逐格检查地图碰撞位** → 被墙挡住就 `false`
2. **目标自己是否站在实心格上** → 站在墙里也 `false`

→ 「特效碰到了却打不到」的原因：**特效不查墙，判定查墙**。

## 二、改了什么

| 需求 | 做法 |
|---|---|
| **取消判定，100% 节奏打击** | `RhythmicBlade.prepare` / `onAnticipate` 之后强制 `shouldCrit = true`、`shouldCritNext = true`，并把节拍灯刷绿 `updateBeatFx(true)` |
| **恒为节奏打击** | `Weapon.get_cycle` 对节奏之刃恒返回 `2`（`isLastCycle()` 恒 true → 永久循环最后一下） |
| **删掉前两下打击** | `Weapon.initSkill` 把每段 strikeChain 的 `animId` 统一成 `atkOudeC` |
| **无视墙体 + 360° 圆形打击** | `Weapon.canHit` 对节奏之刃直接接管：**只保留**存活 / `canBeHit` / 敌对阵营 / **圆心到目标中心距离 ≤ 半径**，跳过 Bresenham 墙体检测与朝向判定 |
| **范围 ×3** | `RhythmicBlade.getRealCurArea` 把判定框 `widPx`/`heiPx` × 3（6 格 → 18 格半径） |
| **伤害 ×3** | `_AttackUtils.hit`（伤害计算之前）把 `AttackData.dmgMultiplier` × 3 → 暴击/词条/伤害上限仍走原版 |
| **特效 ×3** | `RhythmicBlade.dynOnFxFrame` 里把 **`cinf.fxProps.fxScale`** 设成 3（见下） |

### 特效大小是 `fxProps.fxScale` 管的

`res/data.cdb` 里 `weapon@strikeChain@fxProps` 的列是：

```
fxStartFrame / fxOffset / fxScale / fxInnerColor / fxOuterColor
```

`RhythmicBlade.dynOnFxFrame` 里：

```haxe
if (cinf.fxProps.fxScale != null) {
    hSprite.scaleX = cinf.fxProps.fxScale[0];
    hSprite.scaleY = cinf.fxProps.fxScale[1];
}
```

而节奏之刃三段 strikeChain 的 `fxProps` **只写了 `fxOffset`，没有 `fxScale`** ——
所以特效一直是原始尺寸。

模组在 `orig` 之前把 `fxProps.fxScale` 设成 `[n, n]`（n = 倍率），
`orig` 里就会照它缩放特效，读完立刻还原。
`fxProps` 是 CDB 里的**共享对象**，不还原的话倍率会越乘越大。

> 之前只钩 `Fx.dispShockWave` 不生效：它在 `GameProxy.dll` 里
> `isVirtual = False`（普通方法），而且传进去的半径是固定 `0.2`，
> 并不是玩家看到的那道横扫特效。

## 三、配置

`coremod/config/RhythmicBladeOverhaul.json`（首次运行自动生成），或游戏内
**选项 → 模组 → 节奏之刃·节奏打击**：

| 项 | 默认 | 说明 |
|---|---|---|
| `EnableMod` | `true` | 总开关，关掉完全恢复原版 |
| `Multiplier` | `3.0` | **固定倍率**：判定范围 / 伤害 / 特效共用。游戏内是滑条（0.5~6.0） |
| `EnableWallPierce` | `true` | 无视墙体（跳过 Bresenham 墙体/朝向检测） |
| `ScaleDamage` | `true` | 伤害是否也乘倍率 |
| `ScaleEffect` | `true` | 特效是否也乘倍率 |
| `EffectScaleTweak` | `1.0` | 特效微调：觉得特效比判定范围大就调小（0.8） |

> 旧版配置（`RangeGainPerKill` / `BaseRangeScale` 等）已弃用；
> 换成本版后建议删掉旧 json 让它按新结构重新生成。

## 四、构建 / 安装

```powershell
cd RhythmicBladeOverhaul\RhythmicBladeOverhaul
dotnet build -c Debug          # AutoInstallMod=true，自动装到 coremod\mods\RhythmicBladeOverhaul
```

装好后 **重启游戏** 才会加载。

## 五、依赖与冲突

* 只依赖 DCCM 框架，不依赖 CoreLibrary、不需要 res.pak / CDB 补丁。
* `Hook__AttackUtils.hit` 是全局攻击入口，本模组只在"这一下是节奏之刃打的"
  才动 `dmgMultiplier`，其它武器不受影响。
* 与 `InfiniteRangeWallPierce` 这类全局改 `Weapon.canHit` 的模组会叠加。
