@echo off
setlocal

if not exist "PAKTool.exe" (
  echo [ERROR] 当前目录没有 PAKTool.exe
  echo 把你的 PAKTool.exe 放到项目根目录。
  pause
  exit /b 1
)

if exist "res.pak" del "res.pak"

echo [V32] 正在打包 textures + atlas...
PAKTool.exe Collapsev1 ".\res_pak_src" ".\res.pak"

if errorlevel 1 (
  echo [ERROR] 打包失败
  pause
  exit /b 1
)

echo [OK] 已生成 res.pak（含环境贴图 atlas）
echo 文件列表：
PAKTool.exe list ".\res.pak" 2>nul || echo (列表功能不可用，手动检查)

echo.
echo 接下来运行 dotnet clean ^&^& dotnet build
pause
