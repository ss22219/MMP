@echo off
chcp 65001 >nul

echo ========================================
echo    MMP 快速启动
echo ========================================
echo.

:: 直接运行（Debug 模式）
dotnet run --project MMP.csproj

if errorlevel 1 (
    echo.
    echo ✗ 运行失败
    pause
    exit /b 1
)

pause
