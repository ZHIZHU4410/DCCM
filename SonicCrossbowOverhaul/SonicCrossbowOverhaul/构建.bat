@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  SonicCrossbowOverhaul 构建脚本
rem  - 依赖：.NET SDK 10（本机 dotnet --version 应 >= 10.0）
rem  - 产物：coremod/mods/SonicCrossbowOverhaul/
rem ============================================================

rem 游戏根目录：MDK 的 build.props 用 DEAD_CELLS_GAME_PATH 定位 coremod/
set "DEAD_CELLS_GAME_PATH=D:\steama\steamapps\common\Dead Cells"

cd /d "%~dp0"

echo [1/2] dotnet build ...
dotnet build SonicCrossbowOverhaul.csproj -c Debug -v m
if errorlevel 1 (
    echo.
    echo [FAILED] 构建失败，请检查上面的错误。
    pause
    exit /b 1
)

echo.
echo [2/2] 已安装到:
echo   %DEAD_CELLS_GAME_PATH%\coremod\mods\SonicCrossbowOverhaul\
dir /b "%DEAD_CELLS_GAME_PATH%\coremod\mods\SonicCrossbowOverhaul" 2>nul

echo.
echo 完成。
pause
