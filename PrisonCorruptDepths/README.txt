V25 EARLY CDB BEFORE LEVELLOGOS

目的：
继续研究真正新增 ID，不走原版关卡壳。

之前已经确认：
- V24 替换 Ossuary 可以进入自定义 LevelStruct。
- 所以 buildMainRooms / createNode / createExit 是能用的。
- 新 ID 崩点是 valid levels / LevelLogos / CDB 初始化顺序。

V25 改动：
- 使用 full data.cdb。
- data.cdb 内含 PrisonCorruptDepths 和 T_PrisonCorruptDepths。
- PrisonCorrupt.nextLevels 挂入 T_PrisonCorruptDepths。
- 不使用 CDBManager。
- 不使用 res.pak。
- 不手动 loadPak。
- 不碰 LevelLogos。
- 在 Initialize 阶段提前 Data.Class.loadJson。
- OnGameEndInit 只检测，不再次加载 CDB。
- P 键 goto(T_PrisonCorruptDepths)。

判断：
1. 如果启动直接崩：
   说明 Initialize 阶段 Data.Class.loadJson 太早，Data 尚未安全初始化。
2. 如果启动不崩，OnGameEndInit ready=True：
   说明提前注册成功。
3. 如果按 P 后不再出现 not part of valid levels：
   说明 LevelLogos 崩点解决。
4. 如果按 P 后捕获 T_PrisonCorruptDepths：
   说明新 ID 终于进入 LevelStruct.get。

安装前清旧目录：
rmdir /s /q "B:\SteamLibrary\steamapps\common\Dead Cells\coremod\mods\TestCorruptPlusLevel"

项目根目录：
rmdir /s /q bin
rmdir /s /q obj
dotnet clean
dotnet build

最终 mods\TestCorruptPlusLevel 里应该有：
TestCorruptPlusLevel.dll
TestCorruptPlusLevel.deps.json
TestCorruptPlusLevel.runtimeconfig.json
modinfo.json
data.cdb

不要有：
res.pak
res 文件夹

日志：
xdt_v25_log.txt


V26 关键变化：
V25 日志显示：
- EarlyCDB loadJson 完成
- PrisonCorruptDepths=True
- T_PrisonCorruptDepths=True
- ready=True
- P pressed
- goto(T_PrisonCorruptDepths) transNull=False
- 但没有捕获 T_PrisonCorruptDepths
外部主日志显示：
Fatal Error: Null access .group

LevelTransition.loadNewLevel() 里有：
if mainId != null -> Game.loadMainLevel(mainId)
之后 get_isADlcPLevel 会读取 Data.Class.level.byId.get(this.mainId).group

所以 V26 不再调用：
LevelTransition.Class.goto(T_PrisonCorruptDepths)

改成：
bool noLoadingData = false;
LevelTransition trans = new LevelTransition("PrisonCorruptDepths", null, null, null, ref noLoadingData);
trans.loadNewLevel();

也就是绕开自定义 transition id，直接测试自定义 main id。

另外 V26 提供 res_pak_src/textures/loadingScreens：
- PrisonCorruptDepths.png
- T_PrisonCorruptDepths.png

建议打包 res.pak，避免 LevelLogos 后面又找不到 loading screen。

步骤：
1. 把 PAKTool.exe 放到项目根目录
2. 运行 pack_res_pak_TEXTURES_ONLY.bat
3. 删除旧 mods\TestCorruptPlusLevel
4. rmdir /s /q bin
5. rmdir /s /q obj
6. dotnet clean
7. dotnet build

最终 mods\TestCorruptPlusLevel 建议有：
TestCorruptPlusLevel.dll
TestCorruptPlusLevel.deps.json
TestCorruptPlusLevel.runtimeconfig.json
modinfo.json
data.cdb
res.pak

日志：
xdt_v26_log.txt

判断：
如果能看到：
Hook__LevelStruct.get 捕获：PrisonCorruptDepths
命中自定义主关卡
[PrisonCorruptDepths] V25 主关卡 buildMainRooms 开始

说明新 ID 主关卡已经打通，问题只剩 transition/route。


V26.1 编译修复：
你的实际 GameProxy.dll 里 LevelTransition 构造函数第 5 个参数识别为普通 bool，不是 ref bool。
所以 V26 的：
new LevelTransition(..., ref noLoadingData)
会报：
CS1615 参数 5 不可与关键字 ref 一起传递

V26.1 改成：
new LevelTransition(..., noLoadingData)

日志文件：
xdt_v26_1_log.txt


V26.2 防复制错版本：
这版 TestCorruptPlusLevel.cs 中不允许出现：
ref noLoadingData

正确代码必须是：
LevelTransition trans = new LevelTransition(ToHLString(MainLevelId), null, null, null, noLoadingData);

如果你仍然报：
CS1615 参数 5 不可与关键字 ref 一起传递
说明你编译的不是这个文件，而是旧文件。

检查命令：
findstr /n "ref noLoadingData" TestCorruptPlusLevel.cs

如果有输出，说明还没替换成功。
如果没有输出，才是 V26.2。

日志：
xdt_v26_2_log.txt


V26.3 编译修复：
V26.2 报：
参数 5: 无法从 bool 转换为 HaxeProxy.Runtime.Ref<bool>

所以实际正确写法不是 ref bool，也不是 bool，而是：
Ref<bool> noLoadingData = default;
LevelTransition trans = new LevelTransition(ToHLString(MainLevelId), null, null, null, noLoadingData);

注意：
这里不要写 ref noLoadingData。
这里也不要写 bool noLoadingData = false。

检查命令：
findstr /n "Ref<bool> noLoadingData" TestCorruptPlusLevel.cs
findstr /n "ref noLoadingData" TestCorruptPlusLevel.cs
findstr /n "bool noLoadingData = false" TestCorruptPlusLevel.cs

正确结果：
第一条有输出。
第二条没有输出。
第三条没有输出。

日志：
xdt_v26_3_log.txt


V26.4 编译修复：
V26.3 已经把 LevelTransition 构造函数第 5 个参数改成 Ref<bool>，但日志里还写了：
" / noLoadingData=" + noLoadingData

Ref<bool> 不能直接和 string 相加，所以报：
CS0019 运算符 + 无法应用于 string 和 Ref<bool>

V26.4 去掉这段日志拼接，只保留：
Log("已创建 LevelTransition(" + MainLevelId + ")，transNull=" + (trans == null));

核心构造仍然是：
Ref<bool> noLoadingData = default;
LevelTransition trans = new LevelTransition(ToHLString(MainLevelId), null, null, null, noLoadingData);

日志：
xdt_v26_4_log.txt


V27 诊断版：
V26.4 已经证明：
- EarlyCDB 成功
- ready=True
- new LevelTransition(PrisonCorruptDepths) 成功
- trans.loadNewLevel() 崩在 Game.loadMainLevel(pr/Game.hx:84)
- 错误是 Null access .group

V27 不换路线，只在 trans.loadNewLevel() 前打印：

DumpLevelRow[ToHLString(MainLevelId)]
DumpLevelRow[trans.mainId]

重点看：
1. exists=True 但 rawNull=True
   说明 Data.Class.level.byId.exists 和 get 不一致，CDB 注入方式仍不够完整。

2. exists=True 且 rawNull=False 且 group=0
   说明 PrisonCorruptDepths 这行本身没问题，崩的是 Game.loadMainLevel 里另一个 .group。
   下一步要 Hook_Game.loadMainLevel 或继续找 Game.cs。

3. trans.mainId 不是 PrisonCorruptDepths
   说明 LevelTransition 构造后 mainId 被改了。

日志：
xdt_v27_log.txt


V28 关键修复：
V27 证明：
- Initialize 早期 loadJson 后，PrisonCorruptDepths=True
- OnGameEndInit 检测也是 ready=True
- 但进入 PrisonStart 后再按 P，DumpLevelRow 显示：
  exists=False / rawNull=True
- 所以新 CDB 数据在进入第一张地图后被游戏覆盖/重载掉了。

V28 方案：
1. 保留 Initialize 的 EarlyCDB：
   目的是让 LevelLogos / WorldMap 初始化时能看到新 ID。

2. 按 P 前再执行 RuntimeCDB reload：
   目的是让 Game.loadMainLevel 真正调用前，Data.Class.level.byId.get(PrisonCorruptDepths) 能读到 row.group。

3. RuntimeCDB 后重新 Dump：
   如果 AfterRuntimeReload/MainLevelId 变成 exists=True / rawNull=False / group=0，
   再调用 trans.loadNewLevel()。

日志：
xdt_v28_log.txt

重点看：
BeforeRuntimeReload/MainLevelId 是否 exists=False
AfterRuntimeReload/MainLevelId 是否 exists=True / rawNull=False
是否进入 Hook__LevelStruct.get 捕获：PrisonCorruptDepths


V29 关键修复：
V28 已经证明：
- RuntimeCDB 后 PrisonCorruptDepths=True
- trans.loadNewLevel() 进入了 Hook__LevelStruct.get
- PrisonCorruptDepths 的 buildMainRooms 完整跑完
- 新 ID 主关卡已经打通
- 现在崩在：
  ui.LevelCard.getLevelCardObject
  ui.WorldMap.drawLevel
  ui.hud.MiniMap.renderWorldMap

原因：
LevelLogos.initLogoTexture 在游戏早期初始化时没有给 PrisonCorruptDepths 建立 textureCoordinateByLevelKind 坐标。
所以 WorldMap 创建 LevelCard 时拿到空 logo，LevelCard.getLevelCardObject 后续空指针。

V29 方案：
在按 P 前 RuntimeCDB reload 后，立即执行：
PatchRuntimeLevelLogoFallback("BeforeTransition")

它会把：
PrisonCorruptDepths
T_PrisonCorruptDepths

的 LevelLogo 坐标指向原版：
Ossuary / PrisonCorrupt / PrisonStart
中存在的一个。

这样 LevelCard.getLevelCardObject 不会再因为 logo null 崩。

日志：
xdt_v29_log.txt

重点看：
LevelLogoFallback[BeforeTransition] fallback=... / main before=False after=True
Hook__LevelStruct.get 捕获：PrisonCorruptDepths
是否还会在 LevelCard.getLevelCardObject 崩。


V30 关键修复：
V29 证明：
- RuntimeCDB reload 后新 ID 存在
- Hook__LevelStruct.get 捕获 PrisonCorruptDepths
- buildMainRooms 完整跑完
- 崩点只剩 LevelCard / WorldMap / MiniMap
- 但 V29 的 LevelLogoFallback 失败：
  InvalidCastException: HashlinkVirtual -> virtual_x_y_<int,int>

V30 修复：
不再从 textureCoordinateByLevelKind.get(fallback) 读取并强转坐标。
直接创建：
virtual_x_y_<int, int> coord = default;
coord.x = 0;
coord.y = 0;

然后：
textureCoordinateByLevelKind.set(PrisonCorruptDepths, coord)
textureCoordinateByLevelKind.set(T_PrisonCorruptDepths, coord)

目的：
让 LevelLogos.getLevelLogo(PrisonCorruptDepths) 的 exists=True，并返回 levelLogoTexture 的第一个卡片区域。
先保证不崩，图标好不好看后面再说。

日志：
xdt_v30_log.txt

重点看：
LevelLogoFallback[BeforeTransition] 已直接写入 coord=(0,0) / main before=False after=True
然后看是否还卡 LevelCard.getLevelCardObject。


V31 关键修复：
V30 证明：
- 新 ID 仍然成功进入 LevelStruct
- buildMainRooms 完整跑完
- 崩点仍是 LevelCard / WorldMap / MiniMap
- V30 的 LevelLogoFallback 失败原因是：
  virtual_x_y_<int,int> coord = default;
  coord.x = 0;
  这里 coord 实际是 null。

V31 方案：
不自己创建 virtual_x_y_<int,int>。
不强转 HashlinkVirtual。
而是：
object rawCoord = textureCoordinateByLevelKind.get(Ossuary / PrisonCorrupt / PrisonStart);
textureCoordinateByLevelKind.set(PrisonCorruptDepths, rawCoord);
textureCoordinateByLevelKind.set(T_PrisonCorruptDepths, rawCoord);

也就是直接复用原版地图已经存在的 HashlinkVirtual 坐标对象。

日志：
xdt_v31_log.txt

重点看：
LevelLogoFallback[BeforeTransition] 已复制 rawCoord / fallback=... / rawType=Hashlink.Proxy.Objects.HashlinkVirtual / main before=False after=True

如果 after=True 后还崩 LevelCard，那下一步就 Hook LevelCard 或 WorldMap，直接跳过新 ID 的地图卡片绘制。
