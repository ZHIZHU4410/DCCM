
# 死亡细胞核心模组开发仓库

基于 [dead-cells-core-modding/core](https://github.com/dead-cells-core-modding/core) 框架开发的《死亡细胞》游戏模组。

本仓库收集并开发基于 `dead-cells-core-modding/core` 框架的多个《死亡细胞》模组项目。仓库包含若干独立模组，每个模组通常为一个独立的 `.csproj`（示例：`Outside_Clock/Outside_Clock.csproj`、`LibraryMod/LibraryMod.csproj` 等）。

更多 upstream 说明与文档请参考：https://github.com/dead-cells-core-modding/core

---
# Mod Installation (Quick)

This section explains how to install a prepared mod into the game using the Dead Cells Core Modding loader.

Key point: each mod is a folder placed under `coremod/mods/`. The folder name must exactly match the `name` field in its `modinfo.json`.

Installation steps (quick):

1. Ensure a `coremod/mods` directory exists inside the game root (the folder containing `deadcells.exe`):

```powershell
# Example (Windows PowerShell)
cd <DeadCellsGameRoot>
if (-not (Test-Path -Path .\coremod\mods)) { New-Item -ItemType Directory -Path .\coremod\mods }
```

2. Copy or move the prepared mod folder into `coremod/mods/`. Expected structure:

```
mods/
|- <ModName>/
|  |- modinfo.json    # must contain a "name" field equal to the folder name
|  |- <ModName>.dll   # the main mod dll
|  |- assets/         # optional resources (res.pak, data.cdb, etc.)

```

3. Validate `modinfo.json`: the `name` field must exactly match the parent folder `<ModName>`. If they differ, the loader will ignore the mod.

4. Start the game using the Core loader (example):

```powershell
& "<DeadCellsGameRoot>\coremod\core\host\startup\DeadCellsModding.exe"
```

5. Verify the mod loaded by checking the loader logs or the in-game mod list. If not loaded, check `modinfo.json`, DLL dependencies, and folder naming.

Notes:
- Keep DLLs and asset files inside the mod folder. Don't move resource files to other locations unless you know the packaging requirements.


---

# 模组安装（简明）

本节仅说明如何将已编译/准备好的模组安装到游戏（假设已在游戏根目录安装并配置好 `Dead Cells Core Modding`）。

要点：模组以文件夹形式放入 `coremod/mods/`，文件夹名必须与 `modinfo.json` 中的 `name` 字段完全一致。

安装步骤：

1. 在游戏根目录下（含 `deadcells.exe` 的目录）确保存在 `coremod/mods` 文件夹：

```powershell
# 示例（Windows PowerShell）
cd <DeadCellsGameRoot>
if (-not (Test-Path -Path .\coremod\mods)) { New-Item -ItemType Directory -Path .\coremod\mods }
```

2. 将模组文件夹复制或移动到 `coremod/mods/` 下。模组目录示例结构：

```
mods/
|- <ModName>/
|  |- modinfo.json    # 必须包含 name 字段，与文件夹名一致
|  |- <ModName>.dll   # 模组的主 dll
|  |-                 # 可选资源文件夹（res.pak, data.cdb 等）

```

3. 校验 `modinfo.json`：确保 `name` 字段值与父文件夹 `<ModName>` 完全相同（区分大小写/空格）。否则加载器将忽略该模组。

4. 启动通过 Core Loader 的启动器（示例路径）：

```powershell
# 在 Windows 上，使用 Core 提供的启动器运行游戏以加载模组
& "<DeadCellsGameRoot>\coremod\core\host\startup\DeadCellsModding.exe"
```

5. 验证：启动后检查加载日志或游戏内模组列表，确认模组已被识别并加载；若没有加载，请检查 `modinfo.json`、DLL 依赖，以及模组文件夹名称是否一致。

提示与注意事项：
- 模组通常包含二进制 DLL 与资源文件（`res.pak`、`data.cdb` 等），请保持这些文件与模组文件夹同目录。
- 如果模组依赖其他库或版本特定的 MDK，确保与游戏安装的 core/MDK 版本兼容。
- 若需要部署多个模组，分别将它们作为独立文件夹放入 `coremod/mods/`。



