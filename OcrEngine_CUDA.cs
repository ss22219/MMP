using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OpenCvSharp;
using SkiaSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Local;

namespace MMP
{
    /// <summary>
    /// OCR 引擎，用于识别游戏画面中的文字
    /// 默认使用 V5 中文模型 + Mkldnn 设备
    /// </summary>
    public class OcrEngine : IDisposable
    {
        private PaddleOcrAll? _ocr;
        private bool _disposed = false;
        private readonly bool _useGpu;

        /// <summary>
        /// OCR 设备类型
        /// </summary>
        public enum DeviceType
        {
            /// <summary>
            /// CPU - 自动选择最佳设备（推荐）
            /// </summary>
            Auto,

            /// <summary>
            /// GPU - 需要安装 CUDA（最快）
            /// </summary>
            Gpu
        }

        /// <summary>
        /// OCR 识别结果
        /// </summary>
        public class OcrResult
        {
            /// <summary>
            /// 本次识别的唯一标识符
            /// </summary>
            public Guid Id { get; set; } = Guid.NewGuid();

            /// <summary>
            /// 识别时间戳
            /// </summary>
            public DateTime Timestamp { get; set; } = DateTime.Now;

            /// <summary>
            /// 识别到的文本区域列表
            /// </summary>
            public List<OcrTextRegion> Regions { get; set; } = new();

            /// <summary>
            /// 所有文本拼接结果
            /// </summary>
            public string AllText => string.Join("\n", Regions.Select(r => r.Text));

            /// <summary>
            /// 识别到的文本数量
            /// </summary>
            public int Count => Regions.Count;

            /// <summary>
            /// 获取简短的 ID 用于日志显示（前8位）
            /// </summary>
            public string ShortId => Id.ToString("N")[..8];
        }

        /// <summary>
        /// OCR 文本区域
        /// </summary>
        public class OcrTextRegion
        {
            /// <summary>
            /// 文本区域的唯一标识符
            /// </summary>
            public Guid Id { get; set; } = Guid.NewGuid();

            /// <summary>
            /// 识别的文本
            /// </summary>
            public string Text { get; set; } = string.Empty;

            /// <summary>
            /// 置信度 (0-1)
            /// </summary>
            public float Confidence { get; set; }

            /// <summary>
            /// 中心点坐标
            /// </summary>
            public SKPoint Center { get; set; }

            /// <summary>
            /// 区域大小
            /// </summary>
            public SKSize Size { get; set; }

            /// <summary>
            /// 旋转角度
            /// </summary>
            public float Angle { get; set; }

            /// <summary>
            /// 边界框的四个顶点
            /// </summary>
            public SKPoint[] BoundingBox { get; set; } = Array.Empty<SKPoint>();

            /// <summary>
            /// 获取简短的 ID 用于日志显示（前8位）
            /// </summary>
            public string ShortId => Id.ToString("N")[..8];
        }

        /// <summary>
        /// 创建 OCR 引擎实例
        /// </summary>
        /// <param name="useGpu">是否使用 GPU 加速（需要安装 NVIDIA GPU 驱动和 CUDA 运行时包）</param>
        public OcrEngine(bool useGpu = true)
        {
            _useGpu = useGpu;
            Console.WriteLine($"[OcrEngine] 设备模式: {(_useGpu ? "GPU" : "CPU (Mkldnn)")}");
        }

        /// <summary>
        /// 初始化 OCR 引擎
        /// 使用 V5 中文模型，自动选择最佳设备
        /// </summary>
        public void Initialize()
        {
            if (_ocr != null)
                return;

            // 使用 V5 中文模型（内置，无需下载）
            var model = LocalFullModels.ChineseV5;

            // 配置设备：GPU 或 Mkldnn
            Action<PaddleConfig> deviceConfig = _useGpu 
                ? PaddleDevice.Gpu() 
                : PaddleDevice.Mkldnn();

            // 创建 OCR 实例
            _ocr = new PaddleOcrAll(model, deviceConfig)
            {
                AllowRotateDetection = false,  // 禁用旋转检测以提升速度
                Enable180Classification = false
            };
        }

        /// <summary>
        /// 识别 SKBitmap 图像中的文字
        /// </summary>
        public OcrResult Recognize(SKBitmap skBitmap)
        {
            if (_ocr == null)
                throw new InvalidOperationException("OCR 引擎未初始化，请先调用 Initialize()");

            // 将 SKBitmap 转换为 Mat
            using Mat mat = SKBitmapToMat(skBitmap);

            // 执行 OCR
            var paddleResult = _ocr.Run(mat);

            // 转换结果
            var result = new OcrResult();
            foreach (var region in paddleResult.Regions)
            {
                var textRegion = new OcrTextRegion
                {
                    Text = region.Text,
                    Confidence = region.Score,
                    Center = new SKPoint(region.Rect.Center.X, region.Rect.Center.Y),
                    Size = new SKSize(region.Rect.Size.Width, region.Rect.Size.Height),
                    Angle = region.Rect.Angle,
                    BoundingBox = GetBoundingBox(region.Rect)
                };
                result.Regions.Add(textRegion);
            }

            return result;
        }

        /// <summary>
        /// 在指定区域内查找包含特定文本的区域
        /// </summary>
        public List<OcrTextRegion> FindText(SKBitmap bitmap, string searchText, bool ignoreCase = true)
        {
            var result = Recognize(bitmap);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return result.Regions.Where(r => r.Text.Contains(searchText, comparison)).ToList();
        }

        /// <summary>
        /// 检查图像中是否包含指定文本
        /// </summary>
        public bool ContainsText(SKBitmap bitmap, string searchText, bool ignoreCase = true)
        {
            return FindText(bitmap, searchText, ignoreCase).Count > 0;
        }

        /// <summary>
        /// 识别指定区域的文字
        /// </summary>
        public OcrResult RecognizeRegion(SKBitmap bitmap, SKRectI region)
        {
            var croppedBitmap = new SKBitmap(region.Width, region.Height);
            if (!bitmap.ExtractSubset(croppedBitmap, region))
            {
                throw new Exception("无法提取图像子集");
            }
            
            var result = Recognize(croppedBitmap);
            croppedBitmap.Dispose();
            return result;
        }

        /// <summary>
        /// 将识别结果可视化到图像上
        /// </summary>
        public SKBitmap VisualizeResult(SKBitmap originalBitmap, OcrResult result)
        {
            var visualized = originalBitmap.Copy();
            using var canvas = new SKCanvas(visualized);
            using var paint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = SKColors.Red,
                StrokeWidth = 2,
                IsAntialias = true
            };
            using var textPaint = new SKPaint
            {
                Color = SKColors.Yellow,
                IsAntialias = true
            };
            using var textFont = new SKFont
            {
                Size = 20
            };
            using var bgPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 128),
                Style = SKPaintStyle.Fill
            };

            foreach (var region in result.Regions)
            {
                // 绘制边界框
                if (region.BoundingBox.Length == 4)
                {
                    var path = new SKPath();
                    path.MoveTo(region.BoundingBox[0]);
                    for (int i = 1; i < 4; i++)
                    {
                        path.LineTo(region.BoundingBox[i]);
                    }
                    path.Close();
                    canvas.DrawPath(path, paint);
                }

                // 绘制文本
                float textWidth = textFont.MeasureText(region.Text);
                float textX = region.Center.X - textWidth / 2;
                float textY = region.Center.Y - textFont.Size - 2;

                canvas.DrawRect(textX, textY, textWidth, textFont.Size, bgPaint);
                canvas.DrawText(region.Text, textX, textY + textFont.Size, textFont, textPaint);
            }

            return visualized;
        }

        /// <summary>
        /// 将 SKBitmap 转换为 OpenCV Mat（高性能版本）
        /// </summary>
        private static Mat SKBitmapToMat(SKBitmap skBitmap)
        {
            // 直接从 SKBitmap 的像素数据创建 Mat，避免 PNG 编码/解码
            var info = skBitmap.Info;
            var pixels = skBitmap.GetPixels();
            
            // 尝试直接使用 RGB 格式，避免颜色转换
            if (info.ColorType == SKColorType.Bgra8888)
            {
                // 创建 BGRA Mat
                using var bgraMat = Mat.FromPixelData(info.Height, info.Width, MatType.CV_8UC4, pixels);
                
                // 只转换为 BGR，去掉 Alpha 通道
                var bgrMat = new Mat();
                Cv2.CvtColor(bgraMat, bgrMat, ColorConversionCodes.BGRA2BGR);
                return bgrMat;
            }
            else
            {
                // 其他格式回退到原来的方法
                using var image = SKImage.FromBitmap(skBitmap);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                byte[] imageBytes = data.ToArray();
                return Cv2.ImDecode(imageBytes, ImreadModes.Color);
            }
        }

        /// <summary>
        /// 获取旋转矩形的四个顶点
        /// </summary>
        private static SKPoint[] GetBoundingBox(RotatedRect rect)
        {
            var points = Cv2.BoxPoints(rect);
            return points.Select(p => new SKPoint(p.X, p.Y)).ToArray();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _ocr?.Dispose();
            _ocr = null;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        ~OcrEngine()
        {
            Dispose();
        }
    }
}
