@echo off
chcp 65001 >nul

echo ========================================
echo    MMP CUDA Launcher
echo ========================================
echo.

:: Get batch file directory
set "SCRIPT_DIR=%~dp0"
cd /d "%SCRIPT_DIR%"

:: Find project root directory
echo Finding project root directory...

:: Check current directory
if exist "MMP_CUDA.csproj" (
    echo Found project root: %CD%
    goto :found_project
)

:: Check parent directory
cd ..
if exist "MMP_CUDA.csproj" (
    echo Found project root: %CD%
    goto :found_project
)

:: Check parent parent directory
cd ..
if exist "MMP_CUDA.csproj" (
    echo Found project root: %CD
    goto :found_project
)

:: 未找到项目
echo 错误: 无法找到项目根目录 (MMP_CUDA.csproj)
echo 请确保脚本在项目目录或其子目录中运行
pause
exit /b 1

:found_project
echo.

:: 检查 PowerShell 脚本是否存在
set "PS_SCRIPT=%SCRIPT_DIR%启动程序_CUDA.ps1"
if not exist "%PS_SCRIPT%" (
    echo 错误: 未找到 启动程序_CUDA.ps1 文件
    echo 查找路径: %PS_SCRIPT%
    pause
    exit /b 1
)

:: 调用 PowerShell 脚本
echo 正在启动 PowerShell 脚本...
echo.

powershell -ExecutionPolicy Bypass -File "%PS_SCRIPT%"

:: 检查 PowerShell 脚本的退出代码
if errorlevel 1 (
    echo.
    echo PowerShell 脚本执行出错
    pause
    exit /b 1
)

exit /b 0
