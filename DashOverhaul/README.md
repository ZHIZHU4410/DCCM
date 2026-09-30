# DashOverhaul（突击 Dash 强化）

Dead Cells **DCCM** 模组。把主动技能 **Dash（突击 Assault / 内部 id `Dash`）** 改成一把"任意方向穿墙突进"的技能。

## 改了什么

| 需求 | 实现方式 | 位置 |
|---|---|---|
| **只能释放 Dash**（用完不再变成 BackDash） | 数据补丁把 `Dash.commonProps.item` 从 `BackDash` 改回 `Dash` | `patch_dash_cdb.py` |
| **八方向冲刺**（方向键） | `Hook_Dash.fixedUpdate` 完全接管原版逐帧逻辑；起手瞬间采样 方向键/WASD 得到单位向量并锁死整段 | `DashOverhaulMain.cs` |
| **伤害 +500%** | 数据补丁把 `Dash.props.power` 从 `[100]` 改成 `[600]`（×6） | `patch_dash_cdb.py` |
| **无视墙体** | 冲刺期间 `collisionMode = IgnoreWalls`、`hasGravity = false`，位移直接写 `cx/xr/cy/yr` 绕过碰撞求解 | `DashOverhaulMain.cs` |
| **拖尾改淡蓝色** | `Hook_Dash.postUpdate` 不执行原版（橙色 `0xEE9B11` / 残影 `0xFFFF33`），改画 `0x99E5FF` | `DashOverhaulMain.cs` |
| **castCD = 0** | 数据补丁把 `Dash.castCD` 从 `10` 改成 `0` | `patch_dash_cdb.py` |

## 原版行为（为什么需要 hook）

读 `GamePseudocode/dc.pow.Dash.cs` + `dc.pow._Dash.cs` 得到：

- 使用 Dash → `new Dash(hero, item, isBack:false)`，沿 `dir(±1)` **水平**冲刺 `range` 格；
- `Dash.onEnd()` 里 `hero.transformInventoryItem(item, null)`，变身目标读的是
  `item._itemData.commonProps.item` → 原版变成 `BackDash`；再用一次就是反方向冲回来；
- `Dash.fixedUpdate` 里有十几处硬编码的"沿 X 轴"判定
  （剩余距离按 `|startX-curX|` 算、没位移就 `didntMoveLastFrame → endDash`），
  所以原版**天然做不出 8 方向**。

## 单位约定

`Entity.dx / dy / bdx / bdy` 是**格/帧**；`Entity.cx/cy` 是格、`xr/yr` 是格内 0~1 小数；
`Entity.radius / hei` 是**像素**。本模组位移与命中判定统一用"格"，只有画特效才 `*24` 转像素。

## 目录结构

```
DashOverhaul/
├── build_dash_overhaul.py                     # 一键构建/安装/校验
├── DashOverhaul/
│   ├── DashOverhaul.csproj                    # MDK 工程（Debug 自动安装）
│   ├── DashOverhaulMain.cs                    # 8方向 / 穿墙 / 淡蓝拖尾 / 路径伤害
│   ├── patch_dash_cdb.py                      # 生成数据补丁 data.cdb
│   └── Assets/data.cdb_/item/Dash.json        # 数据补丁（cdb diff 产物，随仓库提交）
└── README.md
```

安装结果：`<游戏>/coremod/mods/DashOverhaul/{DashOverhaul.dll, modinfo.json, res.pak}`

## 构建

```bash
python build_dash_overhaul.py
```

脚本会：校验环境 → `patch_dash_cdb.py` 生成 `data.cdb` → `dotnet build`（编译 + CDB diff + 打包 `res.pak` + 自动安装）
→ 校验数据补丁数值 / `res.pak` 内容 / 安装目录 / `modinfo.json` 入口。

需要 .NET 10 SDK 与 DCCM 的 MDK（`<游戏>/coremod/core/mdk`）。

## 踩过的坑

1. **`res.pak` 必须手动挂载。** DCCM 不会自动把模组目录下的 `res.pak` 合进游戏资源，
   少了 `IOnAfterLoadingAssets` → `FsPak.Instance.FileSystem.loadPak(pakPath)` 这一步，
   `castCD` / `props.power` / `commonProps.item` 三条补丁全部等于没写
   （表现就是"用完 Dash 依然变成 BackDash"）。`build_dash_overhaul.py` 里有对应的自检。
2. **`commonProps.item` 指回自己不会造成循环变身。**
   变身计时只在**新道具带 `AutoTransformInto` 标签**时才排
   （`HeroActiveSkillsManager.onItemTransformDone` 会提前 return），
   而 `Dash` 只有 `MoveHero` 标签，`BackDash` 才带 `AutoTransformInto`。
3. **运行时兜底。** `Hook_Dash.onEnd` 之后还会再查一次技能槽：若是 `BackDash`
   就用 `transformInventoryItem` 转回 `Dash`（`BackDash` 自身的目标就是 `Dash`），
   保证即使 `res.pak` 出问题也绝不停在 `BackDash`。
4. **编译期 API 与反编译代码不完全一致。** mod 编译的是 MDK 的 `GameProxy.dll`：
   haxe 静态方法要通过类对象调用（`AttackUtils.Class.createFromHeroItem.Invoke(...)`），
   可选参数用 `Ref<T>`；`dc` 命名空间里有个 `dc.Math`，所以 `System.Math` 必须用别名。

## 测试要点

拿一个 Dash 技能，按住 **上/下/左/右/四个斜向** 之一再按技能键，应朝该方向冲刺并穿过墙体；
连续按技能键不应变成反向冲刺。

日志（`<游戏>/coremod/logs/log_latest.log`）里会有：

```
[DashOverhaul] res.pak 已加载: .../mods/DashOverhaul/res.pak
[DashOverhaul] res.pak 挂载后 的 Dash 数据：castCD=0 power=[600.0] commonProps.item=Dash speed=5 range=13
[DashOverhaul] 首次冲刺：方向=(0.71,-0.71) 速度=5格/帧 距离上限=13格 power=ok
[DashOverhaul] 冲刺结束（冲满距离）：位移 13/13 格，3 帧，命中 2 个敌人
```

`commonProps.item=Dash` 就说明补丁生效、用完不会变 BackDash。
