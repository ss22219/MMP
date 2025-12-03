# v1.1.0 更新日志

发布日期: 2024-12-03

## 🎉 重大更新

### 1. 迁移到 Avalonia UI 框架
- 从 WPF 迁移到跨平台的 Avalonia UI 11.2.3
- 提升了 UI 性能和现代化体验
- 为未来跨平台支持奠定基础

### 2. 完全采用 SkiaSharp 图形库
- **MMP_CUDA 版本**：完全重构为使用 SkiaSharp.SKBitmap
- 移除了 System.Drawing 依赖，提升性能和兼容性
- 统一了两个版本的图形处理接口

### 3. 屏幕截图优化
- 修复了窗口截图问题
- 改进了客户区裁剪算法
- 使用最新的 SkiaSharp 3.116.1 API（移除过时警告）

### 4. 防 AFK 功能增强
- 添加了智能鼠标抖动功能
- 每 5 秒自动微小移动鼠标（-2 到 +2 像素）
- 使用 Windows API 实现，更加可靠

## 🔧 技术改进

### OCR 引擎优化
- **OcrEngine.cs**：优化了 RapidOCR.Net 集成
- **OcrEngine_CUDA.cs**：完全重构为 SKBitmap 接口
  - 添加 SKBitmapToMat 转换方法
  - 使用 SKFont 替代过时的 SKPaint.TextSize
  - 优化文本可视化渲染

### 项目结构
- 添加 `EmbeddedResourceHelper.cs` 用于嵌入式资源管理
- 改进了模型文件的加载机制
- 优化了编译配置和条件编译

### 依赖更新
- Avalonia: 11.2.2 → 11.2.3
- SkiaSharp: 3.116.1（新增）
- 移除 System.Drawing.Common 依赖

## 🐛 Bug 修复

1. 修复了截图时客户区边框计算错误
2. 修复了 CUDA 版本的类型转换问题
3. 修复了"开始探索"状态的等待时间（增加到 20 秒）
4. 移除了所有条件编译警告

## 📦 构建改进

- 清理了条件编译指令（移除 `#if USE_PADDLE_OCR`）
- 统一了两个项目的依赖管理
- 改进了 .csproj 文件结构
- 添加了 app.manifest 用于管理员权限控制

## 🎯 性能提升

- OCR 处理性能优化
- 内存管理改进（正确的 Dispose 模式）
- 减少了不必要的类型转换

## 📝 代码质量

- 修复了所有编译警告
- 改进了代码注释和文档
- 统一了代码风格
- 增强了错误处理

## ⚠️ 破坏性变更

- CUDA 版本的 `OcrEngine.Recognize()` 现在接受 `SKBitmap` 而不是 `System.Drawing.Bitmap`
- 移除了 `ScreenCapture.CaptureWindowAsBitmap()` 方法

## 🔄 迁移指南

如果你有自定义代码使用了旧的 API：

```csharp
// 旧代码
using var bitmap = ScreenCapture.CaptureWindowAsBitmap(hwnd);
var result = ocrEngine.Recognize(bitmap);

// 新代码
using var bitmap = ScreenCapture.CaptureWindow(hwnd);
var result = ocrEngine.Recognize(bitmap);
```

## 📋 完整提交记录

- b3d8ff8: 修复截图问题，添加了鼠标抖动
- 87de75c: 迁移到 Avalonia UI
- c4151bf: 开始探索等待加到 20s

---

**下载地址**: [GitHub Releases](https://github.com/your-repo/releases/tag/v1.1.0)

**系统要求**:
- Windows 10/11 (x64)
- .NET 10.0 Runtime
- CUDA 版本需要 NVIDIA GPU + CUDA 12.9 + cuDNN 9.1
