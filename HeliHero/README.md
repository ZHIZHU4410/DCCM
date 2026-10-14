# HeliHero（直升机主角）

Dead Cells **DCCM** 模组。把玩家角色换成一架**武装直升机**：自由飞行、无视墙体，机身碰到敌人直接斩杀。

## 改了什么

| 需求 | 实现方式 | 位置 |
|---|---|---|
| **人物模型换成直升机** | 原版角色整体隐藏（`Entity.visible = false` + `heroHead.heroHasHead = false`），另挂一张裸 `HSprite` 读 `atlas/heli.atlas`（24 帧旋翼循环）到 `level.scroller` 的 `DP_ROOM_MAIN` 层 | `HeliHeroMain.cs` + `Assets/atlas/heli.atlas` |
| **可以随意飞行开动** | 每帧清掉 `dx/dy/bdx/bdy`，自己按格写 `cx/xr/cy/yr`（`MoveBy`），方向键 / WASD 八方向归一化；按键走 PlayableMOB 死神的 `KeyBind` + `held` | `HeliHeroMain.cs` |
| **无视墙体 / 删掉墙内保护** | `Entity.disableAllPhysics`（= `CollisionMode.None` + `hasGravity = false` + `baseWeight = 0`，同 `PlayableMOB.disableHero`）：位移绕过碰撞求解，原版"卡进墙里久了被强制拉回地面"那套保护一并失效 | `HeliHeroMain.cs` |
| **碰到敌人就斩杀** | 遍历 `level.entities`，半径内**不做视线/墙体判定**，直接走原版 `AttackUtils` 伤害管线（击杀计数、细胞、掉落全走原版） | `HeliHeroMain.cs` |
| **无敌** | affect 5（`Entity.canBeHit()` 只查 `affects[5]`）：每帧检查，列表空了才补 `setAffectS(5, 99999.0, …)`，和 `PlayableMOB.disableHero` 同一招 | `HeliHeroMain.cs` |
| **模型 / 碰撞范围放大一倍** | `spriteScale` 0.5 → 1.0、`killRadius` 3.0 → 6.0；旧的 `HeliHero.json` 由 `MigrateConfig()` 按 `configVersion` 一次性升级 | `HeliHeroMain.cs` |
| **F7 切换：直升机 ⇄ 原版角色** | 接管期间 `manualLock = true` 会把原版交互（开门 / 下一关 / 商店 / 剧情）一起锁死，硬拆锁跑 `activateFocus` 走不通换关流程。所以按 **F7** 直接切回原版角色：`visible` / 物理 / 头部 / `manualLock` 全部交还，100% 原版行为；交互完再按 F7 切回来继续飞 | `HeliHeroMain.cs` |
| **切到原版角色后仍无敌 + 碰敌即斩** | `heroMode` 下不接管位移/物理/交互，每帧只跑 `MaintainInvincible` + `KillNearby`（两者都不碰 hero 的坐标与速度），免得走向门口的路上被秒 | `HeliHeroMain.cs` |
| **飞行中也能交互（实验，默认关）** | 想试就打开 `config.interactWhileFlying`：按下交互键的那一帧临时解锁 → `hero.updateInteraction()` → `hero.activateFocus()` → 再锁回；触发信号优先用游戏自己的 `Controller.framePresses[6]`，配置里的 `interact` 键兜底 | `HeliHeroMain.cs` |
| **不走路（交给飞行）** | `hero.controller.manualLock = true` + 每帧清速度 | `HeliHeroMain.cs` |

## 操作

- 移动：**方向键** 或 **WASD**（八方向，斜向已归一化，不会斜着更快）
- **切换飞机 / 原版角色：F7**（切回原版角色后就能正常开门、进下一关、逛商店、走剧情；再按 F7 变回直升机。
  换键改 `bindings.toggle`，值 = VK 码，F7 = 118）
- 飞行中交互（默认关，实验）：**E** / **回车** / **F**，需先在选项里打开「飞行中也能交互」
  （触发优先读游戏自己的交互动作 `Controller.framePresses[6]`，改绑/手柄都能认；想换键改 `bindings.interact`）
- 按键读取照抄 **PlayableMOB 的死神**：`KeyBind`（一个动作最多三个键）+ `held`，优先读游戏自己的按键表
  `dc.hxd.Key.isDown`（原版 `Beheaded` 判断方向键用的同一份数据，不依赖窗口焦点），Win32 `GetAsyncKeyState` 只作兜底
- 想换键：改 `coremod/config/HeliHero.json` 里的 `bindings`（`left` / `right` / `up` / `down`，值 = VK 码）
- 飞行速度 / 模型大小（最大 3.0 ×）/ 斩杀半径 / 无敌：游戏内 **选项 → 模组 → HeliHero** 里拖滑条或开关（存成 `coremod/config/HeliHero.json`）
- 想关掉：同一页里的「启用直升机」复选框，关掉立刻还原原版角色

## 素材（`Assets/atlas/`）

`heli.atlas` + `heli0.png` 由 `tools/build_heli_atlas.py` 生成（源图 `D:\MODSWXB\mod\atlasb\FEI.atlas` / `FEI.png`）：

- 组名 **`idle`**，帧名 `idle_0000` .. `idle_0023`（24 帧旋翼，机头朝**右**）
- 单帧 240×69 像素；图集页 1024×512，4 列 × 6 行
- BATL 格式（`[u8 len][len bytes]` 块 → 帧名 + 9×u16 LE `page, rectX, rectY, rectW, rectH, realX, realY, realWid, realHei`）
- 帧名尾号解析（`dc.libs.heaps.slib.assets.Atlas.convertToSlib` 的 `(.*?)(_?)([0-9]+)$`）：`idle_0000` → 组 `idle` 第 0 帧；
  组内帧数决定自动动画（`Atlas.Class.CACHE_ANIMS`），所以 24 帧的 `idle` **不需要额外 tracks json**，`AnimManager.play("idle")` 直接可用
- C# 侧打包路径必须与 `Assets.Class.lib.get("atlas/heli.atlas")` 一致：`csproj` 用 `<PackAssets Include="Assets/**/*" RootInPak="" />`

## 单位约定

`Entity.cx/cy` 是格、`xr/yr` 是格内 0~1 小数；`dx/dy` 是**格/帧**；`radius / hei` 是**像素**（1 格 = 24 像素）。
世界像素坐标 `px = (cx + xr) * 24`。

## 关键 API 事实（本模组依赖，均已核对）

- `hero.get_targetSprPosX() = (cx + xr) * 24`（`GamePseudocode/dc/Entity.cs`）
- `Entity.spriteUpdate()` 每帧重算 `spr.scaleX = sprScaleX * dir`、并按 `visible` 设置 `spr.visible` ——
  所以**隐藏角色要写 `visible`**，而裸精灵要直接写 `scaleX/scaleY` + `posChanged = true`
- 裸 `HSprite` 的帧推进：`HSprite.sync()` 在 `anim.needUpdates` 时调 `AnimManager._update(SpriteLib.Class.TMOD)`；
  `syncOnlyIfVisible` 默认 `true`，精灵本身可见即可（挂在 `scroller` 上会随渲染遍历被 sync）
- `CollisionMode` 是 Haxe 枚举（`Normal / All / None / Ladder / Pully / WallGrab / IgnoreOneWay / IgnoreWalls`），
  `IgnoreWalls` 只免**水平墙体阻挡**，悬空仍需自己关 `hasGravity`
- `Entity.disableAllPhysics` / `enableAllPhysics`（`dc/Entity.cs:4213` / `:4240`）= `CollisionMode.None` + `hasGravity = false` + `baseWeight = 0`；
  `updatePositionXY`（`Entity.cs:17694`）里 `dx` 累加进 `xr` **不受** `CollisionMode` 影响（枚举只影响坡/墙求解），所以过场把 `dx` 交还原版照样能走门
- 原始交互判定：`dc.en/Hero.cs:38597` —— `!controller.manualLock && !parent.isLocked && … && framePresses[6] == 1` → `activateFocus(false)`；
  而 `controlsLocked()`（`Hero.cs:39744`）也查 `controller.manualLock`，所以"按下交互那一帧临时把 `manualLock` 置 false"正是原版判定认的那个锁
- `Entity.canBeHit()`（`Entity.cs:15027`）只查 `affects[5]` 非空，所以 `setAffectS(5, 99999.0, …)` 就是原版"打不中"
- 按键表 `dc.hxd.Key.isDown`（`dc.hxd/_Key.cs:276`）就是查 `Key.keyPressed[vk]`，而 `keyPressed` 由 Haxe 的
  `EKeyDown` 处理器按 `e.keyCode` **通用**写入（`_Key.cs:127`），所以 F7 这种"游戏没绑定过"的键照样读得到
- 换关过场：`Exit` 被激活后由 `dc.cine.ExitLevelWalk`（`_ExitLevelWalk.cs`）→ `GameCinematic.heroWalkUntil` / `walkUntil` 驱动，
  靠**写 `hero.dx`** 让 `updatePositionXY` 把 hero 走到门口（超时 5 秒），期间不能清 `dx`、不能抢写坐标
  （本模组用 `HandsOff` 在这段时间把位移交还原版，只跟精灵；`{ "interact": … }` 见 `Configs.DefaultBindings`）
- 伤害管线：`AttackUtils.Class.createFromHero.Invoke(owner, (dynamic)dmg, null)` → 设 `useHeroScaling=false`、`sourceTier=1`
  → `AttackUtils.Class.hit.Invoke(atk, mob)`（1e9 基础伤害 + 关闭等级缩放，避免 `computeFinalDmg` 的 `(int)` 溢出成负数）
- 合法敌人过滤：`!destroyed && life > 0 && _targetable && owner.isOpponent(mob) && mob.canBeDetected() && mob.canBeHitBy(owner)`

## 参考实现

- 移动/接管写法：`PlayableMOB/PlayableMOB/HeroDeath.cs`（每帧直接写 `dir`、隐藏本体）、`DashOverhaul`（`MoveBy` + `IgnoreWalls`）
- 隐藏 / 无敌 / 关物理：`PlayableMOB/PlayableMOB/PlayableMOB.cs:226-249`（`disableHero` / `enableHero`）
- 按键读取：`PlayableMOB/PlayableMOB/KeyBind.cs` + `Utils.held`（`Key.Class.isDown`）、`GojoLimitless/Input.cs`（`IsDownViaGame` 的实机结论）
- 精灵生命周期：`Retinue`（`HSprite` + `get_anim().play`）、`ChronoBlade/ChronoFx.cs`（`scroller.addChildAt(sp, Const.Class.DP_ROOM_MAIN)`）、`MCDcrit`（`Assets.Class.lib.get`）
- 斩杀管线：`ClusterBombClearMap`、`IndulgenceWallPierce`

## 构建

```powershell
cd "D:\steama\steamapps\common\Dead Cells\coremod\DCCMDEAD CELLS\HeliHero\HeliHero"
dotnet build
```

Debug 构建会通过 MDK 的 `AutoInstallMod` 自动装到 `coremod\mods\HeliHero\`（`HeliHero.dll` + `res.pak`）。