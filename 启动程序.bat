@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ========================================
echo    MMP 自动启动脚本
echo ========================================
echo.

:: 读取当前版本
set "CURRENT_VERSION=unknown"
if exist "version.txt" (
    set /p CURRENT_VERSION=<version.txt
)
echo 当前版本: %CURRENT_VERSION%
echo.

:: 检查更新
echo [1/4] 检查更新...
call :check_update
echo.

:: 检查 .NET SDK 是否已安装
echo [2/4] 检查 .NET SDK...
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ✗ 未检测到 .NET SDK
    goto :install_dotnet
)

:: 检查 .NET 10 SDK 是否已安装
for /f "tokens=1 delims=." %%a in ('dotnet --version 2^>nul') do set DOTNET_MAJOR=%%a
if "%DOTNET_MAJOR%"=="10" (
    echo ✓ 已安装 .NET 10 SDK
    goto :build_and_run
)

echo ⚠ 当前 .NET SDK 版本不是 10.x
echo   当前版本: 
dotnet --version
echo.

:install_dotnet
echo.
echo [3/4] 准备安装 .NET 10 SDK...
echo.
echo 请选择安装方式:
echo   1. 自动下载并安装 (推荐)
echo   2. 打开官方下载页面 (手动安装)
echo   3. 跳过安装，尝试运行
echo.
set /p choice="请输入选项 (1/2/3): "

if "%choice%"=="1" goto :auto_install
if "%choice%"=="2" goto :manual_install
if "%choice%"=="3" goto :build_and_run

echo ✗ 无效选项，退出
pause
exit /b 1

:auto_install
echo.
echo 正在下载 .NET 10 SDK 安装程序...
echo 下载地址: https://dotnet.microsoft.com/download/dotnet/10.0

:: 使用 PowerShell 下载安装程序
powershell -Command "& {[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -Uri 'https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100-rc.2.25502.107/dotnet-sdk-10.0.100-rc.2.25502.107-win-x64.exe' -OutFile '%TEMP%\dotnet-sdk-10-installer.exe'}"

if errorlevel 1 (
    echo ✗ 下载失败，请手动安装
    goto :manual_install
)

echo ✓ 下载完成
echo.
echo 选择安装方式:
echo   1. 静默安装 (自动完成，推荐)
echo   2. 图形界面安装 (手动操作)
echo.
set /p install_choice="请输入选项 (1/2): "

if "%install_choice%"=="1" goto :silent_install
if "%install_choice%"=="2" goto :gui_install

:silent_install
echo.
echo 正在静默安装 .NET 10 SDK...
echo 这可能需要几分钟，请耐心等待...
echo.

:: 静默安装参数: /install /quiet /norestart
%TEMP%\dotnet-sdk-10-installer.exe /install /quiet /norestart

if errorlevel 1 (
    echo ✗ 静默安装失败，尝试图形界面安装
    goto :gui_install
)

echo.
echo ✓ 安装完成！
echo.
echo 正在验证安装...
timeout /t 3 /nobreak >nul

:: 刷新环境变量
call :refresh_env

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ⚠ 安装成功，但需要重启命令行才能生效
    echo   请关闭此窗口并重新运行 start.bat
    pause
    exit /b 0
)

echo ✓ .NET SDK 已就绪
echo.
goto :build_and_run

:gui_install
echo.
echo 正在启动图形界面安装程序...
echo 请按照提示完成安装
echo.
start /wait %TEMP%\dotnet-sdk-10-installer.exe

echo.
echo 安装完成！请关闭此窗口并重新运行 start.bat
pause
exit /b 0

:refresh_env
:: 刷新环境变量（尝试）
for /f "tokens=2*" %%a in ('reg query "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v Path 2^>nul') do set "SysPath=%%b"
for /f "tokens=2*" %%a in ('reg query "HKCU\Environment" /v Path 2^>nul') do set "UserPath=%%b"
set "PATH=%SysPath%;%UserPath%"
exit /b 0

:manual_install
echo.
echo 正在打开 .NET 10 SDK 下载页面...
start https://dotnet.microsoft.com/download/dotnet/10.0
echo.
echo 请下载并安装 .NET 10 SDK，然后重新运行此脚本
pause
exit /b 0

:build_and_run
echo.
echo [4/4] 编译并运行 MMP...
echo.

:: 检查项目文件是否存在
if not exist "MMP.csproj" (
    echo ✗ 未找到 MMP.csproj 文件
    echo   请确保在项目根目录运行此脚本
    pause
    exit /b 1
)

:: 编译项目
echo 正在编译项目...
dotnet build MMP.csproj -c Release

if errorlevel 1 (
    echo.
    echo ✗ 编译失败
    echo.
    pause
    exit /b 1
)

echo.
echo ✓ 编译成功
echo.
echo ========================================
echo    启动 MMP
echo ========================================
echo.
echo 提示:
echo   - 按 F10 强制退出程序
echo   - 按 F12 强制退出深渊
echo.

:: 运行程序
dotnet run --project MMP.csproj -c Release --no-build

echo.
echo ========================================
echo    程序已退出
echo ========================================
pause
exit /b 0

:: ========================================
:: 版本检查和更新函数
:: ========================================

:check_update
:: 从 Gitee API 获取最新版本
set "API_URL=https://gitee.com/api/v5/repos/gool/MMP/tags"
set "TEMP_JSON=%TEMP%\mmp_tags.json"

:: 使用 PowerShell 获取版本信息
powershell -Command "try { $tags = Invoke-RestMethod -Uri '%API_URL%' -UseBasicParsing; if ($tags.Count -gt 0) { $tags[0].name } else { 'unknown' } } catch { 'error' }" > "%TEMP%\mmp_latest_version.txt" 2>nul

if not exist "%TEMP%\mmp_latest_version.txt" (
    echo ⚠ 无法检查更新
    exit /b 0
)

set /p LATEST_VERSION=<"%TEMP%\mmp_latest_version.txt"

if "%LATEST_VERSION%"=="error" (
    echo ⚠ 无法连接到更新服务器
    exit /b 0
)

if "%LATEST_VERSION%"=="unknown" (
    echo ⚠ 未找到版本信息
    exit /b 0
)

echo 最新版本: %LATEST_VERSION%

:: 比较版本
if "%CURRENT_VERSION%"=="%LATEST_VERSION%" (
    echo ✓ 已是最新版本
    exit /b 0
)

echo.
echo ========================================
echo    发现新版本！
echo ========================================
echo 当前版本: %CURRENT_VERSION%
echo 最新版本: %LATEST_VERSION%
echo.
echo 是否更新？
echo   1. 是 (推荐)
echo   2. 否 (跳过更新)
echo.
set /p update_choice="请输入选项 (1/2): "

if not "%update_choice%"=="1" (
    echo 跳过更新
    exit /b 0
)

:: 执行更新
call :do_update
exit /b 0

:do_update
echo.
echo 正在更新...
echo.

:: 检查是否安装了 git
git --version >nul 2>&1
if errorlevel 1 (
    echo ✗ 未安装 Git
    echo.
    echo 请选择更新方式:
    echo   1. 手动下载更新 (打开 Gitee 页面)
    echo   2. 跳过更新
    echo.
    set /p git_choice="请输入选项 (1/2): "
    
    if "%git_choice%"=="1" (
        start https://gitee.com/gool/MMP/releases
        echo 请手动下载最新版本并解压替换
        pause
    )
    exit /b 0
)

:: 保存当前更改
echo 正在保存当前更改...
git stash >nul 2>&1

:: 拉取最新代码
echo 正在拉取最新代码...
git pull origin main

if errorlevel 1 (
    echo ✗ 更新失败
    echo.
    echo 请尝试手动更新:
    echo   git pull origin main
    echo.
    pause
    exit /b 1
)

:: 恢复保存的更改
git stash pop >nul 2>&1

echo.
echo ✓ 更新完成！
echo.
echo 更新内容:
git log --oneline -5

echo.
echo 请重新运行此脚本以使用新版本
pause
exit /b 0
