# WingsNoFall —— 乌鸦之翼（Wings）飞行时不再自动下落

游戏里那个"飞在天上"的主动技能 **Wings**（CDB id `Wings`，法文名 *Ailes de Corbeau*），
原版只会把你吊在**脚下地面往上 3 格**的"悬停高度"上：一旦飞得比它高，
`Wings.fixedUpdate` 里那根"朝 targetCy 收敛"的弹簧（`dy *= 0.33`）就会把你慢慢拽回去 ——
也就是"超过一定高度会下落"。

本模组把这条"自动下落"去掉，同时**完整保留原版的"方向下"**（按下方向下依旧会取消飞行、正常往下掉）。

## 效果

| 操作 | 原版 | 本模组 |
| --- | --- | --- |
| 按住**方向上** | 被悬停高度卡住，飞不高 | 想飞多高飞多高（速度可在选项菜单里调） |
| 松开方向键 | 被弹簧慢慢拽回悬停高度 | **停在原地**，不再下沉 |
| 按住**方向下** | 取消飞行、往下掉 | **和原版一模一样**（代码侧直接 return，不介入） |
| 技能时间到 | 翅膀消失、正常下落 | 同原版 |

## 目录结构（与 DamageAuraBoost 保持一致）

```
WingsNoFall/
├── patch_wings_data.py            # 生成 data.cdb 的脚本（.*.py 被 .gitignore 忽略，只在本地用）
├── README.md
└── WingsNoFall/                   # csproj 所在目录
    ├── WingsNoFall.csproj         # ModName / ModMain + Assets → res.pak 打包管线
    ├── WingsNoFallMain.cs         # 主逻辑：Hook Wings.fixedUpdate + 选项菜单
    ├── WingsConfig.cs             # 持久化配置（coremod/config/WingsNoFall.json）+ 热键名解析
    ├── WingsFeatures.cs           # 功能开关表（总开关 / 不再自动下落 / 自由爬升）
    ├── data.cdb                   # 脚本生成的完整 CDB（含修改）
    └── Assets/data.cdb_/item/Wings.json   # cdb diff 出来的数据补丁，会被打进 res.pak
```

## 数值分工（和 DamageAuraBoost 一个套路：数据管数值、代码管逻辑）

* **数据侧**（`patch_wings_data.py` → `res.pak`）
  `item/Wings` 的 `props.limit`：`48 → 120`。
  原版 `dy = clamp((targetCy - 当前高度) * 24 / limit, -1, 1)`，
  `limit` 是这条弹簧的"满速距离"（48 = 2 格）；放大到 120（5 格）后，
  近距离往回拽的力度同比柔和很多 —— 这是**兜底**：万一代码侧钩子没挂上，也只是极缓慢地漂移。
* **代码侧**（`WingsNoFallMain.cs`）
  Hook `Wings.fixedUpdate`，先让原版跑完，再按玩家真实方向输入修一次 `hero.dy`：
  * 方向下 → 直接 return，一个字节都不改（原版会移除 affect 61 并让你下落）；
  * 方向上 + 「自由爬升」→ 强制 `dy = -AscendSpeed`（复刻原版读法：`get_bindings()` 的动作码
    12 = 下 / 10 = 上，跟随 `invertPlayerMovements`）；
  * 其它 → 「不再自动下落」把 `dy > 0` 归零。
  * 判定"翅膀还在不在"用 `hero.countAffect(61)`（`_Wings.__inst_construct__` 挂的飞行 affect）。
    ★ 就算方向码判断反了也不会出错：按方向下时原版**先**把 affect 61 移除，
      本模组发现翅膀没了就什么都不做。

关掉「不再自动下落」或关掉总开关时，代码会把 CDB 里的 `limit` **还原成原版 48**
（`SyncWingsLimit()`，对应 DamageAuraBoost 的 `SyncStats()`），所以关得干净。

## 开关 / 热键

配置文件：`coremod/config/WingsNoFall.json`（第一次进游戏自动生成）。
游戏内：**选项 → 模组 → 乌鸦之翼(Wings)**，三个复选框 + 一个"爬升速度"滑条。

```json
{
  "EnableMod": true,          // 总开关：关掉 = 其余全部停用（limit 也还原 48）
  "KeyToggleMod": "",         // 默认不绑键，留空 = 不监听
  "EnableNoFall": true,       // 不再自动下落
  "KeyToggleNoFall": "",
  "EnableFreeAscend": true,   // 按住“上”自由爬升
  "KeyToggleFreeAscend": "",
  "AscendSpeed": 0.3          // 爬升速度（参照：Hero 起跳冲量是 -0.4）
}
```

## 构建 / 安装

```powershell
cd WingsNoFall
python patch_wings_data.py          # 重新生成 data.cdb（改了数值才需要）
cd WingsNoFall
dotnet build -c Debug               # 自动: cdb diff → Assets/data.cdb_ → res.pak → 安装到 coremod/mods/WingsNoFall/
```

## 真机验证要点

1. 用翅膀飞高以后松开方向键 → 应该**停在原地**，不再慢慢往下沉；
2. 飞行中按住方向下 → 依旧和原版一样往下 / 取消飞行；
3. 飞行中按住方向上 → 可以一直往上升（原版会被悬停高度卡住）；
4. 关掉总开关后再飞 → 恢复原版手感（回到悬停高度、飞高会被拽回去）。
   日志 `logs/log_latest.log` 里会打印 `[WingsNoFall] 数据 limit = 48/120`，
   可用来确认数据开关真的切过去了。
