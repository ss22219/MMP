# MMP Auto Start Script (PowerShell)
# Set console encoding to UTF-8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

# ========================================
# Function Definitions
# ========================================

function Find-ProjectRoot {
    $currentDir = Get-Location
    
    if (Test-Path "MMP.csproj") {
        Write-Host "找到项目根目录: $currentDir" -ForegroundColor Green
        return $true
    }
    
    Set-Location ..
    if (Test-Path "MMP.csproj") {
        Write-Host "找到项目根目录: $(Get-Location)" -ForegroundColor Green
        return $true
    }
    
    Set-Location ..
    if (Test-Path "MMP.csproj") {
        Write-Host "找到项目根目录: $(Get-Location)" -ForegroundColor Green
        return $true
    }
    
    Write-Host "✗ 错误: 无法找到项目根目录 (MMP.csproj)" -ForegroundColor Red
    Write-Host "  请确保脚本在项目目录或其子目录中运行" -ForegroundColor Red
    return $false
}

function Compare-Versions {
    param(
        [string]$version1,
        [string]$version2
    )
    
    $v1 = $version1 -replace "^v", ""
    $v2 = $version2 -replace "^v", ""
    
    $v1Parts = $v1.Split(".")
    $v2Parts = $v2.Split(".")
    
    for ($i = 0; $i -lt [Math]::Max($v1Parts.Length, $v2Parts.Length); $i++) {
        $v1Part = if ($i -lt $v1Parts.Length) { [int]$v1Parts[$i] } else { 0 }
        $v2Part = if ($i -lt $v2Parts.Length) { [int]$v2Parts[$i] } else { 0 }
        
        if ($v1Part -gt $v2Part) { return 1 }
        if ($v1Part -lt $v2Part) { return -1 }
    }
    
    return 0
}

function Check-Update {
    param([string]$currentVersion)
    
    try {
        $apiUrl = "https://gitee.com/api/v5/repos/gool/MMP/tags"
        $tags = Invoke-RestMethod -Uri $apiUrl -UseBasicParsing -TimeoutSec 5
        
        if ($tags.Count -gt 0) {
            # 找到真正的最新版本（按版本号排序）
            $latestTag = $tags | Sort-Object { 
                $v = $_.name -replace "^v", ""
                $parts = $v.Split(".")
                [int]$parts[0] * 10000 + [int]$parts[1] * 100 + [int]$parts[2]
            } -Descending | Select-Object -First 1
            
            $latestVersion = $latestTag.name
            Write-Host "最新版本: $latestVersion" -ForegroundColor Cyan
            
            if ($currentVersion -eq $latestVersion) {
                Write-Host "✓ 已是最新版本" -ForegroundColor Green
                return
            }
            
            $comparison = Compare-Versions $currentVersion $latestVersion
            if ($comparison -gt 0) {
                Write-Host "✓ 当前版本更新 ($currentVersion > $latestVersion)" -ForegroundColor Green
                return
            }
            
            Write-Host ""
            Write-Host "========================================" -ForegroundColor Green
            Write-Host "   发现新版本！" -ForegroundColor Green
            Write-Host "========================================" -ForegroundColor Green
            Write-Host "当前版本: $currentVersion" -ForegroundColor Yellow
            Write-Host "最新版本: $latestVersion" -ForegroundColor Green
            Write-Host ""
            Write-Host "是否更新？" -ForegroundColor Yellow
            Write-Host "  1. 是 (推荐)" -ForegroundColor White
            Write-Host "  2. 否 (跳过更新)" -ForegroundColor White
            Write-Host ""
            
            $choice = Read-Host "请输入选项 (1/2)"
            
            if ($choice -eq "1") {
                Do-Update
            }
            else {
                Write-Host "跳过更新" -ForegroundColor Yellow
            }
        }
        else {
            Write-Host "⚠ 未找到版本信息" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "⚠ 无法检查更新: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

function Do-Update {
    Write-Host ""
    Write-Host "正在更新..." -ForegroundColor Yellow
    Write-Host ""
    
    if (Get-Command git -ErrorAction SilentlyContinue) {
        Write-Host "正在保存当前更改..." -ForegroundColor Yellow
        git stash 2>$null | Out-Null
        
        Write-Host "正在拉取最新代码..." -ForegroundColor Yellow
        git pull https://gitee.com/gool/MMP
        
        if ($LASTEXITCODE -eq 0) {
            git stash pop 2>$null | Out-Null
            Write-Host ""
            Write-Host "✓ 更新完成！" -ForegroundColor Green
            Write-Host ""
            Write-Host "更新内容:" -ForegroundColor Cyan
            git log --oneline -5
            Write-Host ""
            Write-Host "请重新运行此脚本以使用新版本" -ForegroundColor Yellow
            Read-Host "按任意键退出"
            exit 0
        }
        else {
            Write-Host "✗ 更新失败" -ForegroundColor Red
            Write-Host "请尝试手动更新: git pull https://gitee.com/gool/MMP" -ForegroundColor Yellow
        }
    }
    else {
        Write-Host "✗ 未安装 Git" -ForegroundColor Red
        Write-Host ""
        Write-Host "请选择更新方式:" -ForegroundColor Yellow
        Write-Host "  1. 手动下载更新 (打开 Gitee 页面)" -ForegroundColor White
        Write-Host "  2. 跳过更新" -ForegroundColor White
        Write-Host ""
        
        $gitChoice = Read-Host "请输入选项 (1/2)"
        
        if ($gitChoice -eq "1") {
            Start-Process "https://gitee.com/gool/MMP/releases"
            Write-Host "请手动下载最新版本并解压替换" -ForegroundColor Yellow
            Read-Host "按任意键继续"
        }
    }
}

# ========================================
# Main Execution
# ========================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   MMP 自动启动脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "正在查找项目根目录..." -ForegroundColor Yellow

if (-not (Find-ProjectRoot)) {
    Read-Host "按任意键退出"
    exit 1
}

Write-Host ""

$currentVersion = "unknown"
if (Test-Path "version.txt") {
    $currentVersion = (Get-Content "version.txt" -Raw).Trim()
}
Write-Host "当前版本: $currentVersion" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/4] 检查更新..." -ForegroundColor Yellow
Check-Update $currentVersion
Write-Host ""

Write-Host "[2/4] 检查 .NET SDK..." -ForegroundColor Yellow

try {
    $dotnetVersion = dotnet --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        $majorVersion = $dotnetVersion.Split(".")[0]
        
        if ($majorVersion -eq "10") {
            # 检查是否为预览版
            if ($dotnetVersion -match "preview|rc|alpha|beta") {
                Write-Host "⚠ 检测到 .NET 10 预览版 (版本: $dotnetVersion)" -ForegroundColor Yellow
                Write-Host ""
                Write-Host "预览版可能不稳定，建议安装正式版" -ForegroundColor Yellow
                Write-Host "请从以下地址下载并安装 .NET 10 正式版:" -ForegroundColor Yellow
                Write-Host "  https://dotnet.microsoft.com/download/dotnet/10.0" -ForegroundColor White
                Write-Host ""
                $choice = Read-Host "是否继续使用预览版? (y/n)"
                if ($choice -ne "y") {
                    exit 1
                }
            }
            else {
                Write-Host "✓ 已安装 .NET 10 SDK (版本: $dotnetVersion)" -ForegroundColor Green
            }
        }
        else {
            Write-Host "⚠ 当前 .NET SDK 版本不是 10.x" -ForegroundColor Yellow
            Write-Host "  当前版本: $dotnetVersion" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "请从以下地址安装 .NET 10 SDK:" -ForegroundColor Yellow
            Write-Host "  https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-10.0.100-win-x64.exe" -ForegroundColor White
            Write-Host ""
            $choice = Read-Host "是否继续? (y/n)"
            if ($choice -ne "y") {
                exit 1
            }
        }
    }
}
catch {
    Write-Host "✗ 未检测到 .NET SDK" -ForegroundColor Red
    Write-Host ""
    Write-Host "请从以下地址安装 .NET 10 SDK:" -ForegroundColor Yellow
    Write-Host "  https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100/dotnet-sdk-10.0.100-win-x64.exe" -ForegroundColor White
    Write-Host ""
    $choice = Read-Host "是否继续? (y/n)"
    if ($choice -ne "y") {
        exit 1
    }
}

Write-Host ""

Write-Host "[4/4] 编译并运行 MMP..." -ForegroundColor Yellow
Write-Host ""

if (-not (Test-Path "MMP.csproj")) {
    Write-Host "✗ 未找到 MMP.csproj 文件" -ForegroundColor Red
    Write-Host "  请确保在项目根目录运行此脚本" -ForegroundColor Red
    Read-Host "按任意键退出"
    exit 1
}

Write-Host "正在编译项目..." -ForegroundColor Yellow
dotnet build MMP.csproj -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "✗ 编译失败" -ForegroundColor Red
    Write-Host ""
    Write-Host "💡 提示: 如果之前运行过 CUDA 版本，请尝试删除 bin 文件夹后重试" -ForegroundColor Yellow
    Write-Host ""
    Read-Host "按任意键退出"
    exit 1
}

Write-Host ""
Write-Host "✓ 编译成功" -ForegroundColor Green
Write-Host ""

# 显示可执行文件路径
$exePath = Join-Path $PWD "bin\Release\net10.0-windows\MMP.exe"
if (Test-Path $exePath) {
    Write-Host "可执行文件位置:" -ForegroundColor Cyan
    Write-Host "  $exePath" -ForegroundColor White
    Write-Host "  (可以直接运行此文件)" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   启动 MMP" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "提示:" -ForegroundColor Yellow
Write-Host "  - 按 F10 启动/停止程序" -ForegroundColor Yellow
Write-Host "  - 按 F12 强制退出深渊" -ForegroundColor Yellow
Write-Host ""
Write-Host "⚠ 注意: 如果切换到 CUDA 版本，请先删除 bin 文件夹" -ForegroundColor Yellow
Write-Host ""

dotnet run --project MMP.csproj -c Release --no-build

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   程序已退出" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Read-Host "按任意键退出"
