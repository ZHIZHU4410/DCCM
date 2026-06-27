@echo off
chcp 65001 >nul
echo [V26.3] 检查当前 TestCorruptPlusLevel.cs 的 noLoadingData 写法
echo.

if not exist "TestCorruptPlusLevel.cs" (
  echo [ERROR] 当前目录没有 TestCorruptPlusLevel.cs
  echo 请在项目根目录运行这个 bat。
  pause
  exit /b 1
)

echo [1] 正确写法应该存在：
findstr /n "Ref<bool> noLoadingData" "TestCorruptPlusLevel.cs"

if errorlevel 1 (
  echo [ERROR] 没找到 Ref<bool> noLoadingData，说明不是 V26.3。
  pause
  exit /b 1
)

echo.
echo [2] 错误写法不应该存在：
findstr /n "ref noLoadingData" "TestCorruptPlusLevel.cs"
if not errorlevel 1 (
  echo [ERROR] 发现 ref noLoadingData，这是旧错误。
  pause
  exit /b 1
)

findstr /n "bool noLoadingData = false" "TestCorruptPlusLevel.cs"
if not errorlevel 1 (
  echo [ERROR] 发现 bool noLoadingData = false，这是 V26.2 错误。
  pause
  exit /b 1
)

echo.
echo [OK] noLoadingData 写法正确，可以编译。
echo 正确版本日志应该是 xdt_v26_4_log.txt
pause
