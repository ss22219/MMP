using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using OpenCvSharp;
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
        }

        /// <summary>
        /// OCR 文本区域
        /// </summary>
        public class OcrTextRegion
        {
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
            public PointF Center { get; set; }

            /// <summary>
            /// 区域大小
            /// </summary>
            public SizeF Size { get; set; }

            /// <summary>
            /// 旋转角度
            /// </summary>
            public float Angle { get; set; }

            /// <summary>
            /// 边界框的四个顶点
            /// </summary>
            public PointF[] BoundingBox { get; set; } = Array.Empty<PointF>();
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
                AllowRotateDetection = true,
                Enable180Classification = false
            };
        }

        /// <summary>
        /// 识别 Bitmap 图像中的文字
        /// </summary>
        public OcrResult Recognize(Bitmap bitmap)
        {
            if (_ocr == null)
                throw new InvalidOperationException("OCR 引擎未初始化，请先调用 Initialize()");

            // 将 Bitmap 转换为 Mat
            using Mat mat = BitmapToMat(bitmap);

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
                    Center = new PointF(region.Rect.Center.X, region.Rect.Center.Y),
                    Size = new SizeF(region.Rect.Size.Width, region.Rect.Size.Height),
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
        public List<OcrTextRegion> FindText(Bitmap bitmap, string searchText, bool ignoreCase = true)
        {
            var result = Recognize(bitmap);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return result.Regions.Where(r => r.Text.Contains(searchText, comparison)).ToList();
        }

        /// <summary>
        /// 检查图像中是否包含指定文本
        /// </summary>
        public bool ContainsText(Bitmap bitmap, string searchText, bool ignoreCase = true)
        {
            return FindText(bitmap, searchText, ignoreCase).Count > 0;
        }

        /// <summary>
        /// 识别指定区域的文字
        /// </summary>
        public OcrResult RecognizeRegion(Bitmap bitmap, Rectangle region)
        {
            using var croppedBitmap = bitmap.Clone(region, bitmap.PixelFormat);
            return Recognize(croppedBitmap);
        }

        /// <summary>
        /// 将识别结果可视化到图像上
        /// </summary>
        public Bitmap VisualizeResult(Bitmap originalBitmap, OcrResult result)
        {
            var visualized = new Bitmap(originalBitmap);
            using var graphics = Graphics.FromImage(visualized);
            using var pen = new Pen(Color.Red, 2);
            using var font = new Font("Microsoft YaHei", 12);
            using var textBrush = new SolidBrush(Color.Yellow);
            using var bgBrush = new SolidBrush(Color.FromArgb(128, 0, 0, 0));

            foreach (var region in result.Regions)
            {
                // 绘制边界框
                if (region.BoundingBox.Length == 4)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        var p1 = region.BoundingBox[i];
                        var p2 = region.BoundingBox[(i + 1) % 4];
                        graphics.DrawLine(pen, p1, p2);
                    }
                }

                // 绘制文本
                var textSize = graphics.MeasureString(region.Text, font);
                float textX = region.Center.X - textSize.Width / 2;
                float textY = region.Center.Y - region.Size.Height / 2 - textSize.Height - 2;

                graphics.FillRectangle(bgBrush, textX, textY, textSize.Width, textSize.Height);
                graphics.DrawString(region.Text, font, textBrush, textX, textY);
            }

            return visualized;
        }

        /// <summary>
        /// 将 Bitmap 转换为 OpenCV Mat
        /// </summary>
        private Mat BitmapToMat(Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            byte[] imageBytes = ms.ToArray();
            return Cv2.ImDecode(imageBytes, ImreadModes.Color);
        }

        /// <summary>
        /// 获取旋转矩形的四个顶点
        /// </summary>
        private PointF[] GetBoundingBox(OpenCvSharp.RotatedRect rect)
        {
            var points = Cv2.BoxPoints(rect);
            return points.Select(p => new PointF(p.X, p.Y)).ToArray();
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
