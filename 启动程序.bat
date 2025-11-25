@echo off
chcp 65001 >nul

:: MMP 启动脚本 - 调用 PowerShell 版本
:: 这个 bat 文件只是一个简单的包装器

:: 获取脚本所在目录
set "SCRIPT_DIR=%~dp0"

:: 调用 PowerShell 脚本
powershell -ExecutionPolicy Bypass -File "%SCRIPT_DIR%启动程序.ps1"

:: 如果 PowerShell 脚本执行失败，暂停以便查看错误
if errorlevel 1 (
    echo.
    echo PowerShell 脚本执行失败
    pause
)
