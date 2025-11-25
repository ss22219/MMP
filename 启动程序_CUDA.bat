@echo off
chcp 65001 >nul

echo ========================================
echo    MMP CUDA 启动器
echo ========================================
echo.

:: 检查 PowerShell 脚本是否存在
if not exist "启动程序_CUDA.ps1" (
    echo 错误: 未找到 启动程序_CUDA.ps1 文件
    echo 请确保该文件与此批处理文件在同一目录
    pause
    exit /b 1
)

:: 调用 PowerShell 脚本
echo 正在启动 PowerShell 脚本...
echo.

powershell -ExecutionPolicy Bypass -File "启动程序_CUDA.ps1"

:: 检查 PowerShell 脚本的退出代码
if errorlevel 1 (
    echo.
    echo PowerShell 脚本执行出错
    pause
    exit /b 1
)

exit /b 0
