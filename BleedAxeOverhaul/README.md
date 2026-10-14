# BleedAxeOverhaul（流血斧强化）

把 **BleedAxe（流血斧 / Hache sanglante / Ventrèche）** 改成一把「一次丢十把、穿墙、
自动追敌、范围与伤害都是原版二倍」的武器。

基于 **DCCM（Dead Cells Core Modding）**，目录格式与 `Assets + cs` 搭配方式
仿照本仓库的 `DamageAuraBoost` 项目。

---

## 一、需求 → 实现对照

| 需求 | 实现方式 | 关键 Hook / 数据 |
| --- | --- | --- |
| 一次丢出十把斧子 | 原版 `onExecute` 造 1 把；在其**之后**补 9 把克隆斧，10 把均匀铺在一个可配置扇面里 | `Hook_BleedAxeWeapon.onExecute` |
| 无视墙体 | `ignoreWalls = true` + `ignoreOneWays = true`，并跳过「落墙」与「撞墙」两条销毁路径 | `Hook_Bullet.fixedUpdate` / `blockOnCollision` / `onHitWall` |
| 自动追击敌人 | 每帧把速度矢量朝最近的敌人限速转向（`0.22 rad/帧`），保持速率不变 | `Hook_Bullet.fixedUpdate` + `Team.get_targetHelper()` |
| 范围是原版二倍 | `maxDist` × 2 → `174.24px`（7.26 格）变成 `348.48px`（14.52 格） | `Hook_Bullet.fixedUpdate` |
| 伤害是原版二倍 | `item.props.dps` 25 → 50（`res.pak` 数据补丁） | `patch_bleedaxe_data.py` + `IOnAfterLoadingAssets` |

---

## 二、目录结构

```
BleedAxeOverhaul/                              ← 项目根（工作目录下）
├─ patch_bleedaxe_data.py                      ← 生成 CastleDB 数据补丁 data.cdb
└─ BleedAxeOverhaul/                           ← MDK 模组工程
   ├─ BleedAxeOverhaul.csproj                  ← net10.0 + MDK 1.0.1（Debug 自动安装）
   ├─ BleedAxeOverhaulMain.cs                  ← 主逻辑：十把斧 / 穿墙 / 索敌 / 范围×2
   ├─ BleedAxeConfig.cs                        ← 持久化配置（coremod/config/…json）
   ├─ BleedAxeFeatures.cs                      ← 功能开关总控 + 热键轮询
   ├─ data.cdb                                 ← 由 patch 脚本生成（勿手改）
   ├─ Assets/
   │  └─ data.cdb_/item/BleedAxe.json          ← 由 csproj 的 cdb diff 自动生成
   └─ bin/Debug/net10.0/output/BleedAxeOverhaul/
      ├─ BleedAxeOverhaul.dll
      ├─ modinfo.json
      └─ res.pak                              ← 内含 data.cdb_/item/BleedAxe.json
```

安装结果：`<游戏目录>/coremod/mods/BleedAxeOverhaul/{dll, modinfo.json, res.pak}`

`BleedAxeOverhaul.sln` 在同级（VS / Rider 可直接打开）。

---

## 三、构建 / 安装

在仓库根目录（`DCCMDEAD CELLS/`）执行：

```powershell
python build_bleedaxe_overhaul.py
```

脚本会依次完成：环境检查 → 生成 `data.cdb` → `dotnet build`（编译 + CDB diff +
打包 `res.pak` + 自动安装）→ **Hook 挂点静态自检** → 校验补丁字段 →
校验安装目录 / `modinfo.json` / `res.pak` 内容 → 打印结论。

其中 **Hook 挂点静态自检** 会把源码里每一处 `Hook_Xxx.method += handler`
拿去和 `coremod/core/mdk/ref/GameProxy.dll` 里真实存在的 `hook_<method>` 委托比对。
编译通过只证明「类型/成员能被引用」，这一步额外证明「它真的是 DCCM 暴露的挂点」，
用来避免「订阅成功但永不触发」的静默失效（参考 `SonicCrossbowOverhaul` 踩过的坑）：

```
[3b/6] Hook 挂点静态自检（对照 GameProxy.dll 的 hook_* 委托）
      解析到 5 处 Hook 订阅（挂点清单来源：Mono.Cecil 类型表）：
        [OK] Hook_BleedAxeWeapon.onExecute
        [OK] Hook_Bullet.fixedUpdate
        [OK] Hook_Bullet.blockOnCollision
        [OK] Hook_Bullet.onHitWall
        [OK] Hook_Bullet.onDispose
```

> 如果构建时因为权限 / 游戏占用导致 MDK 的「自动安装到 `coremod/mods/`」被拒，
> 脚本会自动改用 `-p:AutoInstallMod=false` 重编（产物仍在 `bin/.../output/`），
> 再在第 5 步手动复制过去，保证「跑完脚本即可进游戏」。

只改数值时也可以手动：

```powershell
cd BleedAxeOverhaul
python patch_bleedaxe_data.py
dotnet build BleedAxeOverhaul\BleedAxeOverhaul.csproj -c Debug
```

---

## 四、游戏内开关

* 配置文件：`<游戏目录>/coremod/config/BleedAxeOverhaul.json`（首次运行自动生成）
* 游戏菜单：**选项 → 模组 → 流血斧强化**

| 开关 | 默认 | 说明 |
| --- | --- | --- |
| 总开关 | 开 | 关掉后其余功能全部停用（本项仍可切换） |
| 一次丢出十把斧子 | 开 | 原版 1 把 → 10 把扇形散开 |
| 无视墙体 | 开 | 撞墙/落墙都不再销毁，可穿过墙体与单向平台 |
| 自动追击敌人 | 开 | 每帧限速转向，隔墙也能锁敌 |
| 范围 ×2 | 开 | 关闭后 `maxDist` 还原原版 |
| 伤害 ×2 | 开 | 关闭后 `props.dps` 运行时还原 25（重新丢斧后生效） |

热键默认全部留空（不绑键），想用自己在 json 里填，例如 `"KeyToggleHoming": "F8"`
（支持 `A~Z`、`0~9`、`F1~F12`、`Space`/`Tab`/`Enter`/`Arrow` 等键名，或 `"0x77"` 形式的键码）。

扇面总张角也可调：`"FanTotalDegrees": 51.6`（10 把斧子均匀铺在这个角度里）。

---

## 五、实现要点（踩坑记录）

1. **为什么挂在 `Hook_Bullet` 而不是 `Hook_BleedAxeBullet`**
   MDK 的 Hook 拦的是 Hashlink 函数入口，子类 override 的函数槽与实际调用点不一定同一个
   （见 `SonicCrossbowOverhaul` 的踩坑记录）。`BleedAxeBullet.fixedUpdate` 只是
   `base.fixedUpdate()`，挂在基类的唯一出口 `dc.en.Hook_Bullet.fixedUpdate` 上最稳，
   在处理器里用 `self is BleedAxeBullet` 过滤。

2. **穿墙不能只设 `ignoreWalls`**
   `Bullet.ignoreWalls` 只被 `fixedUpdate()` 和 `onStep()` 的格子判定读到；
   而 `Bullet.onTouchGround()` 是**无条件**调用 `blockOnCollision()` 的：
   ```csharp
   public override void onTouchGround() { base.onTouchGround(); blockOnCollision(); }
   ```
   `blockOnCollision() → onHitWall() → block(false) → destroy()`，
   所以斧子一旦落到墙/地面上，即便 `ignoreWalls = true` 也会被打掉。
   必须额外把 `blockOnCollision` / `onHitWall` 对斧子跳过。

3. **十把斧为什么在 `onExecute` 的 postfix 里补**
   `BleedAxeBullet` 走 `Bullet.onTouchValidTarget()`，里面**无条件**调用
   `this.beforeHit.Invoke(...)` 和 `this.onBulletHit.Invoke(...)`。
   如果自己 `new` 一把斧子却不设这两个回调（原版是在 `onExecute` 里赋值的），
   一碰到敌人就是 `Null access`。在 `onExecute` 之后补发，就能复用同一套
   「武器 + 技能」上下文，并且回调里所有数值都从武器/物品数据现算，
   不依赖原版那把斧子的存活状态。

4. **伤害为什么用 CDB 补丁而不是代码**
   `BleedAxeWeapon` 的命中闭包（`onExecuteContext_22794.ArrowFunctionEntry_36229`）里
   ```csharp
   xr = _Const.scaleHeroValueToTier((object)Param0.props.dps,
                                    Param2.owner.getRelevantTierFor(Param2.item));
   e.setAffectS(89, dx, ref xr, null);      // 89 = 斧子直接伤害 affect
   ```
   `props.dps` 是斧子唯一的伤害基数，改它最干净 —— 卷轴/词条加成照旧照常叠，
   游戏内的物品介绍页也会跟着显示翻倍后的数字。

5. **范围为什么不在 CDB 里改**
   BleedAxe 没有 `range` 字段，射程是 `onExecute` 里写死的常量：
   `maxDist = (11 × 0.66) × 24 = 174.24px`。所以只能在代码侧对每把斧子的
   `maxDist` 乘 2；开关联动时用「记录下来的原版基准值 × 倍率」，
   反复开关不会越乘越大。

6. **扇形角度为什么用 `Hero.dir` 而不是传入的 `ang`**
   原版传进来的 `ang` 只有两个取值（`0` 或 `3.14`），而且用的是
   `base.owner.dir` 而不是 `hero.dir`，方向上容易踩坑。
   这里直接 `double baseAngle = (hero.dir >= 0) ? 0.0 : Math.PI;`
   与 `onExecute` 里 `num3 = (dir != 1) ? 3.14 : 0.0` 语义一致。

7. **索敌为什么不用 `filterBySight`**
   斧子已经穿墙，隔墙锁敌才一致（游戏自带的 `Homing / HomingSoul` 也是这个思路）。
   索敌走游戏自己的 `TargetHelper`：
   `axe._team.get_targetHelper().init(axe)` → `filterByDistance` →
   `filterUntargetables` → `removeBreakables` → `@is(Mob.Class, 10.0)` → `getBest()`。

---

## 六、日志

运行时日志在 `<游戏目录>/coremod/logs/log_latest.log`，关键行：

```
[BleedAxeOverhaul] 已加载 —— 一次 10 把 / 穿墙 / 索敌 / 范围 ×2 / 伤害 ×2（伤害走 res.pak 数据补丁）
[BleedAxeOverhaul] res.pak 已加载: …\coremod\mods\BleedAxeOverhaul\res.pak
[BleedAxeOverhaul] 开关状态 —— 总开关=True 十把斧=True 穿墙=True 索敌=True 范围×2=True 伤害×2=True
[BleedAxeOverhaul] 十把斧 #1：共 10 把、扇面 51.6°、单把间隔 5.73°、dps=50 speed=1.1
[BleedAxeOverhaul] 伤害强化: dps=[50.0] duration=6.0
```

---

## 七、只读目录声明

本模组的所有产出（`data.cdb`、`Assets/**`、`bin/**`、`obj/**`、`*.cs`、`*.csproj`、
`README.md`、`build_bleedaxe_overhaul.py`）都写在 **`BleedAxeOverhaul/` 与仓库根目录**，
没有修改、重命名、删除 `GamePseudocode/` 与 `res/` 中的任何文件，也没有在其中创建新文件。
游戏数据只被**读取**（模板 CDB 来自 `coremod/core/mdk/databases/v35/data.cdb`）。
