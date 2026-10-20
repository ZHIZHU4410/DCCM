# AdeleScytheOverhaul —— 死亡镰刀强化

> 目标武器：**AdeleScythe**（死亡镰刀 / Death's Scythe，紫色 DLC）
> 对应需求：**只保留最后一击 · 范围 ×3 · 伤害 ×3 · 攻速 ×3 · 无视墙体**

---

## 1. 效果一览

| 功能 | 实现位置 | 具体改动 |
|---|---|---|
| 只保留最后一击 | C#：`Hook_Weapon.set_cycle` | 连击序号永远钉在 `strikeChain.Length - 1`（= 3，第 4 段），前三段不再播 |
| 范围 ×3 | 数据补丁 `Assets/data.cdb_/weapon/AdeleScythe.json` | `strikeChain[3].area`：`width 8.5→25.5`、`height 6.5→19.5`、`offsetCaseX -3.7→-11.1`、`offsetCaseY -1→-3`（圆形判定半径 **102px → 306px**） |
| 伤害 ×3 | 同上 | `strikeChain[3].power[0]`：`80 → 240` |
| 攻速 ×3 | C#：`Hook_Weapon.prepare` | `attackSpeed *= 3`（原版 `WeaponSkill.getCastSpeed() = base * weapon.get_attackSpeed()`），同时数据补丁把追判间隔 `props.tick 6→2` |
| 取消攻击后摇（间隔→0） | 数据补丁 + C#：`AdeleWeaponData.ApplyLockAfter` | `strikeChain[3].lockCtrlAfter 0.37 → 0`，并把运行中技能的 `lockControlsAfterUseS` 一起改掉（见第 7 节第 8 条） |
| 无视墙体 | C#：`Hook_Weapon.canHit` + `Hook_AdeleScythe.checkAreahit` | 换成本模组自己的 `AdeleHitBox.Test`：只做"存活校验 + 攻击框几何"，**不做**墙体/视线/门/逐格 Bresenham 那些判定 |
| 刀光跟着变大（纯视觉） | C#：`Hook_AdeleScythe.dynOnAttackAnim` + `Hook_Fx.playWeaponFx` + 逐帧 `AdeleFxAnchor` | 最后一击的刀光 `FXAtkReaperToolD`（存在 `Weapon.lastFx`）放大，并按当帧 `frameData` 把可见中心补偿回原版/人物身上（见第 7 节第 7 条） |

**要点**：范围是"按攻击框放大 ×3"，不是"全图必中" —— 因为判定用的还是武器自己那份 `Area`（已经被数据补丁放大过），只是把"墙"这一层去掉了。

---

## 2. 为什么"只保留最后一击"要钩 `set_cycle`，而不是把 CDB 的 strikeChain 删成 1 段

`AdeleScythe` 的源码里对最后一击有**硬编码的 `get_cycle() == 3`** 分支：

* `AdeleScythe.onExecute`（`GamePseudocode/dc.tool.weap/AdeleScythe.cs`）
  * `case 3:` → 震屏 `viewport.shakeS(0.4, 0.4, 0.2)`
  * `if (get_cycle() == 3)` → 用 `delayer` 排 `scythe_atk_Shake{n}` / `scythe_atk{n}`（最后一击的**追加判定**，`props.count=2, tick=6`）
* `AdeleScythe.hitFromWeapon` → `if (cycle == 3) return;`（最后一击不再把敌人推开）
* `AdeleScythe.dynOnAttackAnim` → `if (cycle == 3)` 才播刀光 `FXAtkReaperToolD`

如果把 `strikeChain` 砍成只剩 1 段，`cycle` 会恒为 **0**，上面这些 `== 3` 的分支**一次都不会触发** ——
最后一击特有的前冲（`num4 = isLastCycle() ? 0.25 : 0.15`）、震屏、追加判定、专属刀光全部丢失。

所以本模组用的是游戏**自己**给这种做法准备的机制：
`Weapon.set_cycle` 里有一条 `if (item.hasAffix("LastCycleSpam")) return _cycle = skills.length - 1;`
（`LastCycleSpam` = "该武器只播放最后一段连击"的词条，见 `data.cdb` 的 `affix` 表）。
我们只是把这条判断对 `AdeleScythe` 无条件地做掉，于是**整段连击状态机保持在 cycle=3**，
最后一击的所有原版行为原封不动地保留。

---

## 3. 目录结构（仿 DamageAuraBoost：`Assets` 数据补丁 + C# 行为代码）

```
AdeleScytheOverhaul/
├── 构建.bat                        # 一键：python 生成 data.cdb → dotnet build（含打包安装）
├── patch_adele_data.py             # 数据补丁脚本（读游戏模板 cdb，改完写出 mod 的 data.cdb）
├── README.md                       # 本文件
└── AdeleScytheOverhaul/
    ├── AdeleScytheOverhaul.csproj   # ModType/ModName/ModMain + Assets→res.pak 打包 + 自动安装
    ├── AdeleScytheOverhaulMain.cs   # 主入口：钩子挂载/注销、res.pak 加载、数值同步、选项菜单
    ├── AdeleHitBox.cs               # "无视墙体"的几何命中判定（替换原版含墙判定）
    ├── AdeleFxAnchor.cs              # 刀光的缩放 + 可见中心补偿（解决"放大后跑到人物上方"）
    ├── AdeleWeaponData.cs           # 运行时把 strikeChain[3]/areas[3] 在"原版值 ↔ 强化值"间切换
    ├── AdeleConfig.cs               # 持久化配置 Config<AdeleConfig> + 按键名解析
    ├── AdeleFeatures.cs             # 功能开关总控（总开关 / 热键 / 菜单文案）
    ├── data.cdb                     # 脚本生成（对应游戏模板的完整副本 + 改动）
    └── Assets/data.cdb_/weapon/AdeleScythe.json   # cdb diff 产出的最终数据补丁
```

`data.cdb` 与 `Assets/**` 的关系与 DamageAuraBoost 完全一致：

```
patch_adele_data.py
   └─ 读 coremod/core/mdk/databases/v35/data.cdb（游戏模板）
   └─ 改 weapon 表 item == "AdeleScythe" 的 strikeChain[3]
   └─ 写出 AdeleScytheOverhaul/data.cdb
dotnet build
   └─ DCCMTool cdb diff  (data.cdb vs 模板)  → _diffCDB.pak
   └─ DCCMTool pak unpack                    → Assets/data.cdb_/weapon/AdeleScythe.json
   └─ MDK PackAssets (GenerateSinglePakFile) → res.pak
   └─ AutoInstallMod                         → coremod/mods/AdeleScytheOverhaul/
```

模组运行时由 `IOnAfterLoadingAssets` 里 `FsPak.Instance.FileSystem.loadPak("<mod dir>/res.pak")` 把这份数据补丁挂进游戏资源系统。

---

## 4. 资源引用说明

本模组**不新增也不需要复制任何美术/音频资源**，`res/` 只读引用（不修改、不打包）：

| 用途 | 游戏内的原始位置 | 模组里的对应路径 |
|---|---|---|
| 武器数据（被改的那份） | `coremod/core/mdk/databases/v35/data.cdb` 的 `weapon` 表 | `Assets/data.cdb_/weapon/AdeleScythe.json`（只含这一条记录的差异） |
| 最后一击的刀光 `FXAtkReaperToolD` | `res/atlas/fxWeapon.atlas` + `fxWeapon*.png` | 不复制，直接复用 |
| 挥砍动画 `AtkReaperToolA~D` | `res/atlas/beheaded.atlas`（及各皮肤图集，武器动画在英雄图集里） | 不复制，直接复用 |
| 武器贴图 / 图标 | `res/atlas/AdeleSythe.atlas` + `AdeleSythe0.png`；图标 `res/atlas/cardIcons.png` | 不复制，直接复用 |
| 音效 | `res/sfx/weapon/purpleDLC_scythe_*.wav` | 不复制，直接复用 |

> 也就是说：**模组的 res.pak 里只有 1 个 JSON**（约 3.5 KB），完全不含游戏原始资源。

---

## 5. 构建 / 安装

前置：装好 DCCM（`coremod/` 已存在）、`dotnet` SDK、`python`。

```powershell
cd "…\coremod\DCCMDEAD CELLS\AdeleScytheOverhaul"
python patch_adele_data.py                  # 1) 生成 data.cdb（改了数值才需要重跑）
dotnet build -c Debug .\AdeleScytheOverhaul\AdeleScytheOverhaul.csproj   # 2) 编译 + 打包 + 安装
```

或者直接双击 `构建.bat`。

产物安装到 `coremod/mods/AdeleScytheOverhaul/`：`AdeleScytheOverhaul.dll`、`modinfo.json`、`res.pak`。

> 只想要产物、不想装进 `coremod/mods/`：加 `-p:AutoInstallMod=false`，
> 结果在 `AdeleScytheOverhaul/bin/Debug/net10.0/output/AdeleScytheOverhaul/`。

---

## 6. 游戏内开关

「选项 → 模组 → **死亡镰刀强化**」里有 7 个复选框（开关值落到 `coremod/config/AdeleScytheOverhaul.json`）：

| 开关 | 默认 | 说明 |
|---|---|---|
| 总开关 | 开 | 关掉 = 数值还原原版 + 连击恢复 4 段 + 不穿墙 + 攻速还原 |
| 只保留最后一击 | 开 | 见第 2 节 |
| 范围 ×3 | 开 | 运行时切 `strikeChain[3].area` + 刷新 `areas[3]` |
| 伤害 ×3 | 开 | 运行时切 `strikeChain[3].power[0]` |
| 攻速 ×3 | 开 | 运行时 `attackSpeed *= 3` + 追判间隔 `tick ÷ 3` |
| 取消攻击后摇（间隔→0） | 开 | `lockCtrlAfter 0.37 → 0`，挥完立刻能再挥（见第 7 节第 8 条） |
| 无视墙体 | 开 | 见第 1 节 |
| 刀光特效（纯视觉） | **开** | 放大最后一击的刀光 `FXAtkReaperToolD`，并把**视觉质心**补偿回原版构图 / 人物身上 |

滑条（4 条，5px / 0.5 一档，改完立刻落盘）：

| 滑条 | 范围 | 默认 | 说明 |
|---|---|---|---|
| 攻速倍率 | 1.0 – 20.0 | 3.0 | 出招/收招动画与追判间隔一起变快；后摇已单独抹平 |
| 刀光倍率 | 0.5 – 8.0 | 3.0 | 刀光放大倍数 |
| 刀光前后偏移(px) | −400 – 400 | +43 | 正数 = 人物朝向的前方 |
| 刀光上下偏移(px) | −400 – 400 | +60 | 正数 = 向下 |

* 数值类开关（范围/伤害/攻速/后摇）**在下一次挥刀时生效**（同步点挂在 `Weapon.prepare` 上）。
* 每一项都能配一个热键（配置里 `KeyToggleXxx`，默认空 = 不绑键，避免和游戏/其它模组撞键）。

---

## 7. 已知边界 / 取舍

1. **追加判定的防御校验**：`checkAreahit` 被接管后，只保留原版开头的合法性校验
   （`destroyed` / `owner` / `isUnconscious` / `hasCinematic`）+ 几何判定，
   墙体和门的检查被有意去掉 —— 这就是"无视墙体"本身。
2. **矩形判定分支**：`AdeleScythe` 最后一击是 `shape: 0`（圆），实际不会走矩形分支；
   `AdeleHitBox` 里仍写了矩形兜底（照抄原版的 `e.radius * 0.75` 外扩），只为万一改数据后形状变了不至于崩。
3. **`useRectHitbox` 的实体**（少数大型怪/Boss）：本模组对圆判定统一按"圆 vs 圆"算
   （半径 = `area.widPx/2 + e.radius`），比原版那条矩形近似略宽松一点。
4. **`e.useRectHitbox` / 门 / 单向板**都不再影响命中 —— 属于"无视墙体"的预期代价。
5. **CDB 记录是共享的**：运行时的数值切换改的是 `Data.Class.weapon` 里那条共享记录，
   所以同屏所有死亡镰刀一起变（这正是想要的行为）。
6. **数值常量与模板版本绑定**：`AdeleWeaponData` 里的原版值（80 / 8.5 / 6.5 / -3.7 / -1 / 6）
   抄自 **v35** 模板 `data.cdb`；换游戏版本后要跟着模板核对一次。
7. **"特效中心的坑"（已修）**：
   最后一击在 CDB 里**没有 `fxId`**（只有 `animId: AtkReaperToolD`），
   所以它唯一的特效是 `AdeleScythe.dynOnAttackAnim` 里为 `cycle == 3` 播的那一发
   `fx.playWeaponFx(owner, "FXAtkReaperToolD", ...)`（精灵存在 `Weapon.lastFx`）。

   **只设 `scaleX/scaleY` 会把特效顶到人物上方**，原因在
   `HSprite.sync()`（`GamePseudocode/dc.libs.heaps.slib/HSprite.cs:436-443`）：
   ```csharp
   rawTile.dx = -(frameData.realWid * pivot.centerFactorX + frameData.realX);
   rawTile.dy = -(frameData.realHei * pivot.centerFactorY + frameData.realY);
   ```
   pivot 的 `0.5` 是相对**未裁剪的原始画布** `realWid × realHei` 算的，
   而美术实际画在画布里的位置是 `(realX, realY) + (wid × hei)`。
   武器 FX 的 pivot 被设成 `(0.5, 0.5)`、精灵又挂在英雄精灵的局部原点上（原点=脚底），
   于是"画布中心"落在脚底，而刀光在画布里偏上偏后 —— 一放大就按倍率往人物上方/身后窜，
   表现就是"人物跑到了特效下半部的中心"。

   **修法**（[AdeleFxAnchor.cs](AdeleScytheOverhaul/AdeleScytheOverhaul/AdeleFxAnchor.cs)）：
   逐帧按当帧 `frameData` 算出裁剪块的**包围盒中心**
   ```
   bx = wid / 2 - realX - realWid  * centerFactorX
   by = hei / 2 - realY - realHei * centerFactorY
   ```
   放大 k 倍后补 `sprite.x = (1-k)*bx + 偏移X`、`sprite.y = (1-k)*by + 偏移Y`，
   于是"整体放大"带来的位移被抵消，而**动画自身的位移原样保留**（不会压成贴纸）。
   之所以要逐帧（在 `IOnHeroUpdate` 里维持），是因为 `frameData` 每帧都在变。
   （渲染侧已确认缩放会把 `tile.dx/dy` 一起放大：`dc.h2d/RenderContext.cs:820-827`
     用的就是 `tile.dx + tile.width*0.5`，再乘对象矩阵 `matA/matD`。）

   ⚠️ **包围盒中心 ≠ 看起来的中心**：镰刀月牙的包围盒中心明显偏上偏后。
   所以默认偏移不是拿包围盒中心算的，而是拿**按 alpha 加权的像素质心**算的：
   对 `res/atlas/fxWeapon.atlas` + `fxWeapon-*.png` 逐帧算 alpha 质心，
   得到原版 `FXAtkReaperToolD` 的视觉质心平均落在精灵原点**身后 43px、上方 60px**，
   于是默认 `FxOffsetX = 43`、`FxOffsetY = 60`（`AdeleConfig.cs` 里有完整推导）。
   14 个可见帧的质心离散只有 ±16px，所以每一帧看都在人物身上。
   想恢复"原版构图、只是变大"→ 两条滑条都拉到 0。
   「选项 → 模组 → 死亡镰刀强化」里有三条滑条：**刀光倍率 / 刀光前后偏移 / 刀光上下偏移**
   （正 X = 人物朝向的前方，正 Y = 向下），5px 一档，改完立刻写进配置并落盘，**下一刀生效**。

   进游戏后日志里会打一条**锚点自检**（一局一条），形如
   `刀光锚点自检 —— frame=7 wid=222 hei=187 realX=33 realY=6 realWid=290 realHei=290 usingFactor=True centerFactor=(0.5,0.5) 包围盒中心=(-67.0,-57.5) 补偿后 sprite=(134.0,115.0) 倍率=3 偏移=(43,60)`。
   这条能直接核对"运行时读到的帧数据是否与 atlas 实测一致"，
   如果还要继续微调，把这条日志发出来就能精确算出要补多少像素。

   注意**英雄自己的挥砍动作**（`beheaded.atlas` 里的 `AtkReaperToolD`）属于人物动画，
   不随攻击框缩放 —— 那不在本模组的改动范围内。

8. **"攻击间隔"到底在哪（已抹平）**：
   原版最后一击的 `strikeChain[3].lockCtrlAfter = 0.37`（秒）。它的去向是
   `GamePseudocode/dc.tool.skill/OldSkill.cs`：
   ```csharp
   // useWeaponSkillInfos(inf) → 武器【构造时】跑一次，把 CDB 的值抄进技能对象
   setTimers(inf.charge, inf.coolDown, inf.lockCtrlAfter);   // → lockControlsAfterUseS = 0.37
   // execute() → 每次挥完
   if (0.0 < lockControlsAfterUseS) ((Hero)owner).lockControlFromSkill(lockControlsAfterUseS);
   else                             ((Hero)owner).cancelSkillControlLock();
   ```
   也就是"挥完这一刀之后 0.37 秒不能动"。注意它会 `lockControlFromSkill`，
   **不受攻速倍率影响**（攻速只让 `chargeF -= tmod * getCastSpeed()` 变快），
   所以把攻速调到 ×3 之后它照样是那 0.37 秒的间隔 —— 这就是"明显感受到攻击间隔"的来源。

   修法两层：
   * **数据补丁**把 CDB 里的 `lockCtrlAfter` 改成 0（新捡到的刀/新构造的技能直接就是 0）；
   * **运行时**（`AdeleWeaponData.ApplyLockAfter`）把已经拿在手上的那把刀的
     `skills[3].lockControlsAfterUseS` 也改成 0 —— 因为那一份是构造时抄的拷贝，
     不直接改它就得重新装备武器才生效。改成 0 之后 `execute()` 走的是
     `cancelSkillControlLock()`，完全不锁，挥完立刻能再挥。

   还想再快就拉「攻速倍率」滑条（1–20，默认 3）：它同时乘在 `Weapon.prepare(attackSpeed)`
   和追判间隔的除数上，压的是出招动画本身剩下的那点时间。
