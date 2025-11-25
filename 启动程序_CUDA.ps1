# MMP CUDA 自动启动脚本
# 设置控制台编码为 UTF-8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   MMP CUDA 自动启动脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 自动查找项目根目录
Write-Host "正在查找项目根目录..."
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = $null

# 首先检查脚本所在目录
if (Test-Path (Join-Path $scriptDir "MMP_CUDA.csproj")) {
    $projectRoot = $scriptDir
    Write-Host "找到项目根目录: $projectRoot" -ForegroundColor Green
} else {
    # 向上查找最多 3 层目录
    $currentDir = $scriptDir
    for ($i = 0; $i -lt 3; $i++) {
        $parentDir = Split-Path -Parent $currentDir
        if (-not $parentDir) { break }
        
        if (Test-Path (Join-Path $parentDir "MMP_CUDA.csproj")) {
            $projectRoot = $parentDir
            Write-Host "找到项目根目录: $projectRoot" -ForegroundColor Green
            break
        }
        $currentDir = $parentDir
    }
}

if (-not $projectRoot) {
    Write-Host "错误: 无法找到项目根目录 (MMP_CUDA.csproj)" -ForegroundColor Red
    Write-Host "请确保脚本在项目目录或其子目录中运行"
    pause
    exit 1
}

# 切换到项目根目录
Set-Location $projectRoot
Write-Host "当前工作目录: $PWD"
Write-Host ""

# 读取当前版本
$currentVersion = "unknown"
if (Test-Path "version.txt") {
    $currentVersion = Get-Content "version.txt" -Raw -Encoding UTF8
    $currentVersion = $currentVersion.Trim()
}
Write-Host "当前版本: $currentVersion"
Write-Host ""

# 步骤 1: 检查更新
Write-Host "步骤 1/5: 检查更新..." -ForegroundColor Yellow
try {
    $tags = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/gool/MMP/tags" -UseBasicParsing -TimeoutSec 5
    if ($tags.Count -gt 0) {
        # 找到真正的最新版本（按版本号排序）
        $latestTag = $tags | Sort-Object { 
            $v = $_.name -replace "^v", ""
            $parts = $v.Split(".")
            [int]$parts[0] * 10000 + [int]$parts[1] * 100 + [int]$parts[2]
        } -Descending | Select-Object -First 1
        
        $latestVersion = $latestTag.name
        Write-Host "最新版本: $latestVersion"
        
        # 比较版本
        if ($currentVersion -eq $latestVersion) {
            Write-Host "✓ 已是最新版本" -ForegroundColor Green
        }
        else {
            # 使用版本比较函数
            $comparison = Compare-Versions $currentVersion $latestVersion
            if ($comparison -gt 0) {
                Write-Host "✓ 当前版本更新 ($currentVersion > $latestVersion)" -ForegroundColor Green
            }
            else {
                # 发现新版本
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
                $updateChoice = Read-Host "请输入选项 (1/2)"
                
                if ($updateChoice -eq "1") {
                    if (Get-Command git -ErrorAction SilentlyContinue) {
                        Write-Host ""
                        Write-Host "正在更新..." -ForegroundColor Yellow
                        git stash 2>$null | Out-Null
                        git pull origin main
                        
                        if ($LASTEXITCODE -eq 0) {
                            git stash pop 2>$null | Out-Null
                            Write-Host ""
                            Write-Host "✓ 更新完成！" -ForegroundColor Green
                            Write-Host ""
                            Write-Host "更新内容:" -ForegroundColor Cyan
                            git log --oneline -5
                            Write-Host ""
                            Write-Host "请重新运行此脚本以使用新版本" -ForegroundColor Yellow
                            pause
                            exit 0
                        }
                        else {
                            Write-Host "✗ 更新失败" -ForegroundColor Red
                            Write-Host "请尝试手动更新: git pull origin main" -ForegroundColor Yellow
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
                            pause
                            exit 0
                        }
                    }
                }
                else {
                    Write-Host "跳过更新" -ForegroundColor Yellow
                }
            }
        }
    }
    else {
        Write-Host "⚠ 未找到版本信息" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "⚠ 无法检查更新: $($_.Exception.Message)" -ForegroundColor Yellow
}
Write-Host ""

# Version comparison function
function Compare-Versions {
    param(
        [string]$version1,
        [string]$version2
    )
    
    # Remove v prefix
    $v1 = $version1 -replace "^v", ""
    $v2 = $version2 -replace "^v", ""
    
    # Split version numbers
    $v1Parts = $v1.Split(".")
    $v2Parts = $v2.Split(".")
    
    # Compare each part
    for ($i = 0; $i -lt [Math]::Max($v1Parts.Length, $v2Parts.Length); $i++) {
        $v1Part = if ($i -lt $v1Parts.Length) { [int]$v1Parts[$i] } else { 0 }
        $v2Part = if ($i -lt $v2Parts.Length) { [int]$v2Parts[$i] } else { 0 }
        
        if ($v1Part -gt $v2Part) { return 1 }
        if ($v1Part -lt $v2Part) { return -1 }
    }
    
    return 0
}

# 步骤 2: 检查 CUDA 环境
Write-Host "步骤 2/5: 检查 CUDA 环境..." -ForegroundColor Yellow

# 检查 CUDA 运行库文件
Write-Host "检查 CUDA 运行库..."
$cudaLibsFound = $true
$requiredLibs = @("cublas64_12.dll", "cudnn64_9.dll")
$missingLibs = @()

foreach ($lib in $requiredLibs) {
    # 检查当前目录的 CUDA 文件夹
    $localPath = Join-Path $PWD "CUDA\$lib"
    if (Test-Path $localPath) {
        Write-Host "  找到: CUDA\$lib" -ForegroundColor Green
        continue
    }
    
    # 检查 PATH 环境变量中的路径
    $found = $false
    foreach ($path in $env:PATH.Split(";")) {
        if ($path -and (Test-Path $path)) {
            $fullPath = Join-Path $path $lib
            if (Test-Path $fullPath) {
                Write-Host "  找到: $fullPath" -ForegroundColor Green
                $found = $true
                break
            }
        }
    }
    
    if (-not $found) {
        Write-Host "  未找到: $lib" -ForegroundColor Red
        $missingLibs += $lib
        $cudaLibsFound = $false
    }
}

if (-not $cudaLibsFound) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Red
    Write-Host "   缺少 CUDA 运行库文件" -ForegroundColor Red
    Write-Host "========================================" -ForegroundColor Red
    Write-Host ""
    Write-Host "缺少的文件:"
    foreach ($lib in $missingLibs) {
        Write-Host "  - $lib" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "下载地址:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "CUDA 12.9 Toolkit:" -ForegroundColor Green
    Write-Host "  https://developer.nvidia.com/cuda-12-9-0-download-archive?target_os=Windows&target_arch=x86_64"
    Write-Host ""
    Write-Host "cuDNN 9.10 for CUDA 12.x:" -ForegroundColor Green
    Write-Host "  https://developer.nvidia.com/cudnn-downloads?target_os=Windows&target_arch=x86_64"
    Write-Host "  (需要 NVIDIA 开发者账号)"
    Write-Host ""
    Write-Host "或者从项目 CUDA 文件夹复制:" -ForegroundColor Green
    Write-Host "  将 CUDA 文件夹添加到系统 PATH 环境变量"
    Write-Host "  或确保 CUDA 文件夹在项目根目录"
    Write-Host ""
    Write-Host "是否继续运行？(可能会失败)"
    Write-Host "  1. 是"
    Write-Host "  2. 否 (退出)"
    Write-Host ""
    $cudaChoice = Read-Host "请输入选项 (1/2)"
    
    if ($cudaChoice -ne "1") {
        Write-Host "已取消" -ForegroundColor Yellow
        pause
        exit 1
    }
} else {
    Write-Host "CUDA 运行库检查完成" -ForegroundColor Green
}
Write-Host ""

# 检查 NVIDIA GPU
$nvidiaSmi = Get-Command nvidia-smi -ErrorAction SilentlyContinue
if (-not $nvidiaSmi) {
    Write-Host "警告: 未检测到 NVIDIA GPU 或驱动" -ForegroundColor Yellow
    Write-Host "  CUDA 加速可能无法正常工作"
    Write-Host ""
    Write-Host "是否继续？"
    Write-Host "  1. 是 (继续运行，可能会失败)"
    Write-Host "  2. 否 (退出)"
    Write-Host ""
    $gpuChoice = Read-Host "请输入选项 (1/2)"
    
    if ($gpuChoice -ne "1") {
        Write-Host "已取消" -ForegroundColor Yellow
        pause
        exit 1
    }
} else {
    Write-Host "检测到 NVIDIA GPU" -ForegroundColor Green
    Write-Host ""
    Write-Host "GPU 信息:"
    nvidia-smi --query-gpu=name,driver_version,memory.total --format=csv,noheader
    Write-Host ""
    
    # 获取 GPU Compute Capability (SM 版本)
    Write-Host "正在检测 GPU Compute Capability..."
    try {
        $computeCap = nvidia-smi --query-gpu=compute_cap --format=csv,noheader
        $computeCap = $computeCap.Trim()
        Write-Host "检测到 Compute Capability: $computeCap"
        
        # 将 Compute Capability 转换为 SM 版本 (例如 8.9 -> 89)
        $smVersion = $computeCap -replace "\."
        Write-Host "SM 版本: $smVersion"
        Write-Host ""
        
        # 根据 SM 版本确定 NuGet 包名
        $nugetPackage = switch ($smVersion) {
            { $_ -in @("50", "52", "53") } { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm50" }
            { $_ -in @("60", "61", "62") } { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm60" }
            { $_ -in @("70", "72") } { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm70" }
            "75" { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm75" }
            { $_ -in @("80", "86") } { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm80" }
            "89" { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm89" }
            "90" { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm90" }
            { $_ -in @("100", "120") } { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm120" }
            default { "Sdcb.PaddleInference.runtime.win64.cu129_cudnn910_sm89" }
        }
        
        Write-Host "将使用 NuGet 包: $nugetPackage"
        Write-Host ""
        
        # 检查项目文件中的包引用是否需要更新
        Write-Host "检查项目文件配置..."
        $csprojContent = Get-Content "MMP_CUDA.csproj" -Raw -Encoding UTF8
        
        if ($csprojContent -match "Sdcb\.PaddleInference\.runtime\.win64") {
            if ($csprojContent -match $nugetPackage) {
                Write-Host "项目文件已配置正确的包: $nugetPackage" -ForegroundColor Green
            } else {
                Write-Host ""
                Write-Host "检测到项目文件中的包与当前 GPU 不匹配" -ForegroundColor Yellow
                Write-Host "当前 GPU 需要: $nugetPackage"
                Write-Host ""
                Write-Host "是否自动更新项目文件？"
                Write-Host "  1. 是 (推荐，自动匹配 GPU)"
                Write-Host "  2. 否 (使用现有配置)"
                Write-Host ""
                $updatePkgChoice = Read-Host "请输入选项 (1/2)"
                
                if ($updatePkgChoice -eq "1") {
                    # 备份项目文件
                    Copy-Item "MMP_CUDA.csproj" "MMP_CUDA.csproj.bak" -Force
                    Write-Host "已备份项目文件到 MMP_CUDA.csproj.bak"
                    
                    # 更新项目文件
                    $csprojContent = $csprojContent -replace "Sdcb\.PaddleInference\.runtime\.win64\.cu\d+_cudnn\d+_sm\d+", $nugetPackage
                    [System.IO.File]::WriteAllText("$PWD\MMP_CUDA.csproj", $csprojContent, [System.Text.Encoding]::UTF8)
                    Write-Host "项目文件已更新为: $nugetPackage" -ForegroundColor Green
                } else {
                    Write-Host "保持现有配置"
                }
            }
        } else {
            Write-Host "未找到 PaddleInference 运行时包引用" -ForegroundColor Yellow
            Write-Host "  将在编译时自动安装: $nugetPackage"
        }
    } catch {
        Write-Host "无法检测 GPU Compute Capability" -ForegroundColor Yellow
        Write-Host "  将使用默认配置 (SM 8.9)"
    }
}
Write-Host ""

# 步骤 3: 检查 .NET SDK
Write-Host "步骤 3/5: 检查 .NET SDK..." -ForegroundColor Yellow

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnetCmd) {
    Write-Host "未检测到 .NET SDK" -ForegroundColor Red
    Write-Host ""
    Write-Host "步骤 4/5: 准备安装 .NET 10 SDK..." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "请选择安装方式:"
    Write-Host "  1. 自动下载并安装 (推荐)"
    Write-Host "  2. 打开官方下载页面 (手动安装)"
    Write-Host "  3. 跳过安装，尝试运行"
    Write-Host ""
    $choice = Read-Host "请输入选项 (1/2/3)"
    
    if ($choice -eq "1") {
        Write-Host ""
        Write-Host "正在下载 .NET 10 SDK 安装程序..." -ForegroundColor Yellow
        $installerPath = "$env:TEMP\dotnet-sdk-10-installer.exe"
        try {
            Invoke-WebRequest -Uri "https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100-rc.2.25502.107/dotnet-sdk-10.0.100-rc.2.25502.107-win-x64.exe" -OutFile $installerPath
            Write-Host "下载完成" -ForegroundColor Green
            Write-Host ""
            Write-Host "正在安装 .NET 10 SDK..." -ForegroundColor Yellow
            Start-Process -FilePath $installerPath -ArgumentList "/install", "/quiet", "/norestart" -Wait
            Write-Host "安装完成！请重新运行此脚本" -ForegroundColor Green
            pause
            exit 0
        } catch {
            Write-Host "下载或安装失败" -ForegroundColor Red
            Start-Process "https://dotnet.microsoft.com/download/dotnet/10.0"
            pause
            exit 1
        }
    } elseif ($choice -eq "2") {
        Start-Process "https://dotnet.microsoft.com/download/dotnet/10.0"
        Write-Host "请下载并安装 .NET 10 SDK，然后重新运行此脚本"
        pause
        exit 0
    }
} else {
    $dotnetVersion = dotnet --version
    $dotnetMajor = $dotnetVersion.Split(".")[0]
    
    if ($dotnetMajor -eq "10") {
        Write-Host "已安装 .NET 10 SDK" -ForegroundColor Green
    } else {
        Write-Host "当前 .NET SDK 版本不是 10.x" -ForegroundColor Yellow
        Write-Host "  当前版本: $dotnetVersion"
    }
}
Write-Host ""

# 步骤 5: 编译并运行
Write-Host "步骤 5/5: 编译并运行 MMP (CUDA 版本)..." -ForegroundColor Yellow
Write-Host ""

if (-not (Test-Path "MMP_CUDA.csproj")) {
    Write-Host "未找到 MMP_CUDA.csproj 文件" -ForegroundColor Red
    Write-Host "  请确保在项目根目录运行此脚本"
    pause
    exit 1
}

Write-Host "正在编译 CUDA 版本项目..."
dotnet build MMP_CUDA.csproj -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "编译失败" -ForegroundColor Red
    Write-Host ""
    pause
    exit 1
}

Write-Host ""
Write-Host "编译成功" -ForegroundColor Green
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   启动 MMP (CUDA 加速)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "提示:"
Write-Host "  - 按 F10 强制退出程序"
Write-Host "  - 按 F12 强制退出深渊"
Write-Host "  - 使用 CUDA 加速进行 OCR 识别"
Write-Host ""

# 运行程序
dotnet run --project MMP_CUDA.csproj -c Release

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   程序已退出" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
pause