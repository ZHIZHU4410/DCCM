# 怪越杀越多：怪物死亡分裂

普通敌人最终死亡后，在原位置生成 **两只同类型、原尺寸的怪物**。后代死亡后继续分裂，直到达到你设置的代数上限。Boss 和友军不参与。

**v1.2.1 崩溃修复：Boss 所在关卡内禁用所有怪物分裂。** 眼魔触手等 Boss 召唤物不会产生后代，也不会执行进入 Boss 关卡前已经排队的分裂。屏蔽覆盖过场、阶段切换和战后清场，离开 Boss 关卡后恢复普通分裂；无需改动现有菜单设置。

v1.2.0 参考项目内 `ZoomVision` 的菜单控件，在选项菜单新增“怪物死亡分裂”设置页：

| 控件 | 可选范围 | 默认值 | 含义 |
| --- | --- | --- | --- |
| 分裂怪血量 | 1%～500% | 100% | 最初母体最大血量的百分比，每代均使用相同基准，最低 1 HP |
| 普通怪分裂代数上限 | 0～20 | 0 | 原怪是第 0 代；0 表示无限 |
| 自爆蝙蝠分裂代数上限 | 0～20 | 0 | 独立覆盖普通怪上限，适用于 `BatKamikaze` 与 `GoldenBatKamikaze` |

例如原怪 120 HP、血量设为 50%、上限设为 2：原怪死亡生成两只 60 HP 的第一代；第一代死亡共生成四只 60 HP 的第二代；第二代死亡后不再分裂。血量不会逐代变成 60→30→15。

设置修改立即保存到 `coremod/config/MobDeathSplit.json`，用于下一次分裂；已生成怪物的当前血量不会被改动，但死亡时会读取最新上限。分裂代数和最初母体血量随游戏存档保存，读档不会重置这些新记录。

v1.1.0 没有保存代数，因此升级时没有谱系记录的已有怪物视为第 0 代；v1.0.0 遗留的禁止分裂标记仍被忽略。无需清理旧存档。

保留原版死亡逻辑与父怪的正常掉落。子怪使用原版工厂创建，复用同一怪物数据和资源；血量按设置计算，其他生命等级、三色伤害等级、体型、碰撞尺寸、移动参数、技能冷却倍率、抗击退、重量、伤害软上限及破防参数保持继承。精英怪保留精英身份和精英技能类型，随后恢复父怪数值，避免重复强化。

“同参数”指上述基础战斗属性。子怪重新开始 AI，满血出现，不继承父怪的死亡血量、燃烧、中毒、冻结、当前攻击阶段、仇恨、弹药回收或预置钥匙/蓝图等掉落列表。其他模组自定义的私有状态不保证复制。

## 安装

1. 解压 `dist/MobDeathSplit-v1.2.1.zip`。
2. 将其中整个 `MobDeathSplit` 文件夹放入游戏的 `coremod/mods/`。
3. 确认路径为 `coremod/mods/MobDeathSplit/modinfo.json`，同目录包含 `MobDeathSplit.dll`。
4. 使用 DCCM 启动游戏。此模组不需要原版脚本模组开关，不需要 `res.pak`。

卸载时移走 `coremod/mods/MobDeathSplit` 文件夹即可。建议先在备用存档测试。

## 文件说明（相对于当前工作目录）

| 路径 | 作用 |
| --- | --- |
| `MobDeathSplit/MobDeathSplit/MobDeathSplit.csproj` | .NET 10 模组项目、MDK 1.0.1 引用、入口和元信息；构建后自动安装 |
| `MobDeathSplit/MobDeathSplit/MobDeathSplitMain.cs` | 死亡 Hook、谱系读取和保存、生成队列、分裂上限与 Boss/友军过滤 |
| `MobDeathSplit/MobDeathSplit/MobSnapshot.cs` | 捕获父怪基础属性，应用到两只满血后代 |
| `MobDeathSplit/MobDeathSplit/SplitMenu.cs` | 参考 ZoomVision 接入 IModMenu，创建三个菜单滑块 |
| `MobDeathSplit/MobDeathSplit/SplitSettings.cs` | Config 的可保存设置模型及默认值 |
| `MobDeathSplit/MobDeathSplit/SplitSave.cs` | SaveData 的后代代数及母体血量模型 |
| `MobDeathSplit/MobDeathSplit/SplitPolicy.cs` | 血量百分比、普通/蝙蝠独立上限和输入范围规则 |
| `MobDeathSplit/tests/SplitPolicyChecks/Program.cs` | 不启动游戏的规则及持久化模型自检 |
| `MobDeathSplit/NuGet.Config` | 只从本机已安装的 DCCM MDK 包源还原依赖 |
| `MobDeathSplit/dist/MobDeathSplit-v1.2.1.zip` | 可安装的编译成品，包含 DLL、自动生成的 modinfo.json 和本说明 |
| `MobDeathSplit/Verification/self-check.json` | 本次构建和包装自检结果 |

原始资源由游戏直接使用：`res/data.cdb` 提供怪物数据，图集依怪物类型自动选择，例如 `res/atlas/zombie.atlas` 和 `res/atlas/zombie0.png`。**模组中没有对应的复制文件或覆盖路径**，两个只读目录均未修改。

## 构建

在 `MobDeathSplit` 目录执行：

```powershell
dotnet restore MobDeathSplit/MobDeathSplit.csproj --configfile NuGet.Config --packages Verification/packages
dotnet build MobDeathSplit/MobDeathSplit.csproj -c Release --no-restore
```

MDK 的输出为 `MobDeathSplit/bin/Release/net10.0/output/MobDeathSplit/`，并自动安装到 `D:/steama/steamapps/common/Dead Cells/coremod/mods/MobDeathSplit/`。文件夹名与元信息 `name` 必须一致。

## 验证状态

环境：Dead Cells v35 对应本地代理，DCCM **35.13.8**，.NET SDK **10.0.101**，MDK **1.0.1**。

- Release 编译：通过，0 警告、0 错误。
- Hook 委托：与本地 MDK `GameProxy.dll` 校验一致。
- 元信息：名称、程序集、入口、版本和目录检查通过。
- 规则自检：23 项通过，包括 Boss 关卡覆盖无限分裂设置、排队任务进入 Boss 场景后被拒绝、返回普通关卡恢复分裂，以及已有的血量、代数及谱系模型检查。
- 资源：没有复制原版游戏资源，没有修改 `GamePseudocode/` 或 `res/`。
- **实机尚未验证**：由用户自行测试；未启动游戏、未操作菜单。按用户后续要求，构建成品安装到指定的 `coremod/mods/MobDeathSplit/`。

### 建议的实机测试

1. 在选项菜单进入“怪物死亡分裂”，将血量设为 50%、普通上限设为 2、自爆蝙蝠上限设为 1。
2. 击杀普通僵尸：第一代与第二代均为最初僵尸最大血量的 50%；第二代死亡不再分裂。
3. 杀死精英怪：检查两个后代的精英技能与血量，不应因二次精英化而增加属性。
4. 在第一代或第二代存活时退出并继续存档：代数上限仍生效，血量基准保持不变。
5. 临时死亡/复活期间不应提前分裂；最终死亡才分裂。
6. Boss、宠物和被控制的友军死亡不应分裂。
7. 自爆蝙蝠和金色变种只分裂一代，不受普通上限 2 影响；将其独立上限改为 0 后应可继续分裂。
8. 眼魔战中击杀触手，或其他 Boss 战中击杀召唤怪：不得产生分裂后代。离开 Boss 关卡后，普通怪恢复分裂。

原版工厂不支持的特殊类型（例如 `AdeleScytheGhost`）不参与分裂。其他创建失败会写入 DCCM 日志，保留原版死亡处理。兼容性问题请查看 `coremod/logs/log_latest.log` 中的 `MobDeathSplit` 记录。

## 开发依据

- [DCCM Hook 系统](https://dead-cells-core-modding.github.io/docs/zh/docs/dev/hooks/)
- [DCCM 事件系统](https://dead-cells-core-modding.github.io/docs/zh/docs/dev/events/)
- [DCCM 配置与存档](https://dead-cells-core-modding.github.io/docs/zh/docs/dev/config-save/)
- [MDK 与模组依赖](https://dead-cells-core-modding.github.io/docs/zh/docs/dev/mdk/)

Hook、事件、Config、SaveData 和 MDK 机制依据上述文档。菜单接口 `IModMenu` 及滑块用法按用户要求参考项目内 `ZoomVision/ZoomVision/ZoomVisionMain.cs`，并核对本地 `ModCore.dll` 接口与 `GameProxy.dll` 控件签名。自爆蝙蝠类型由只读伪代码与 `res/data.cdb` 核实。没有假设存在“克隆怪物”DCCM API。模组由 Codex 协助编写。
