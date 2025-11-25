using System.Drawing;
using RapidOcrNet;

namespace MMP
{
    /// <summary>
    /// OCR 引擎，用于识别游戏画面中的文字
    /// 使用 RapidOCR.Net 库
    /// </summary>
    public class OcrEngine : IDisposable
    {
        private RapidOcr? _ocr;
        private bool _disposed = false;
        private readonly RapidOcrOptions _options;

        /// <summary>
        /// OCR 识别结果
        /// </summary>
        public class OcrResult
        {
            /// <summary>
            /// 识别到的文本区域列表
            /// </summary>
            public List<OcrTextRegion> Regions { get; set; } = [];

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
            /// 边界框的四个顶点
            /// </summary>
            public PointF[] BoundingBox { get; set; } = [];
        }

        /// <summary>
        /// 创建 OCR 引擎实例
        /// </summary>
        /// <param name="options">OCR 选项，如果为 null 则使用默认选项</param>
        public OcrEngine(RapidOcrOptions? options = null)
        {
            _options = options ?? RapidOcrOptions.Default;
            Console.WriteLine("[OcrEngine] 使用 RapidOCR.Net");
        }

        /// <summary>
        /// 初始化 OCR 引擎
        /// </summary>
        /// <param name="detPath">检测模型路径（可选，默认使用 models/ch_PP-OCRv5_mobile_det.onnx）</param>
        /// <param name="clsPath">分类模型路径（可选，默认使用 models/ch_ppocr_mobile_v2.0_cls_infer.onnx）</param>
        /// <param name="recPath">识别模型路径（可选，默认使用 models/ch_PP-OCRv5_rec_mobile_infer.onnx）</param>
        /// <param name="keysPath">字典路径（可选，默认使用 models/ppocrv5_dict.txt）</param>
        /// <param name="numThread">线程数（0 表示自动）</param>
        public void Initialize(string? detPath = null, string? clsPath = null, string? recPath = null, string? keysPath = null, int numThread = 16)
        {
            if (_ocr != null)
                return;

            _ocr = new RapidOcr();
            
            // 使用中文 PP-OCRv5 模型
            detPath ??= "models/ch_PP-OCRv5_mobile_det.onnx";
            clsPath ??= "models/ch_ppocr_mobile_v2.0_cls_infer.onnx";
            recPath ??= "models/ch_PP-OCRv5_rec_mobile_infer.onnx";
            keysPath ??= "models/ppocrv5_dict.txt";
            
            _ocr.InitModels(detPath, clsPath, recPath, keysPath, numThread);
        }

        /// <summary>
        /// 识别 Bitmap 图像中的文字
        /// </summary>
        public OcrResult Recognize(Bitmap bitmap)
        {
            if (_ocr == null)
                throw new InvalidOperationException("OCR 引擎未初始化，请先调用 Initialize()");

            if (bitmap == null)
            {
                Console.WriteLine("[OcrEngine] 警告: bitmap 为 null，返回空结果");
                return new OcrResult();
            }

            // 验证 bitmap 的有效性
            if (bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                Console.WriteLine($"[OcrEngine] 警告: bitmap 尺寸无效 ({bitmap.Width}x{bitmap.Height})，返回空结果");
                return new OcrResult();
            }

            try
            {
                // 尝试访问 bitmap 数据以确保它是有效的
                var pixelFormat = bitmap.PixelFormat;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OcrEngine] 警告: bitmap 数据无效: {ex.Message}");
                return new OcrResult();
            }

            try
            {
                // 执行 OCR
                var rapidResult = _ocr.Detect(bitmap, _options);

                if (rapidResult == null || rapidResult.TextBlocks == null)
                {
                    Console.WriteLine("[OcrEngine] 警告: OCR 返回结果为 null");
                    return new OcrResult();
                }

                // 转换结果
                var result = new OcrResult();
                foreach (var block in rapidResult.TextBlocks)
                {
                    if (block == null || block.BoxPoints == null || block.BoxPoints.Length == 0)
                    {
                        Console.WriteLine("[OcrEngine] 警告: 跳过无效的文本块");
                        continue;
                    }

                    try
                    {
                        var textRegion = new OcrTextRegion
                        {
                            Text = block.GetText() ?? string.Empty,
                            Confidence = block.BoxScore,
                            Center = CalculateCenter(block.BoxPoints),
                            BoundingBox = ConvertPoints(block.BoxPoints)
                        };
                        result.Regions.Add(textRegion);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OcrEngine] 警告: 处理文本块时出错: {ex.Message}");
                        continue;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[OcrEngine] OCR 识别失败: {ex.Message}");
                Console.WriteLine($"[OcrEngine] 堆栈跟踪:\n{ex.StackTrace}");
                return new OcrResult();
            }
        }

        /// <summary>
        /// 在指定区域内查找包含特定文本的区域
        /// </summary>
        public List<OcrTextRegion> FindText(Bitmap bitmap, string searchText, bool ignoreCase = true)
        {
            var result = Recognize(bitmap);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return [.. result.Regions.Where(r => r.Text.Contains(searchText, comparison))];
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
        public static Bitmap VisualizeResult(Bitmap originalBitmap, OcrResult result)
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
                float textY = region.Center.Y - textSize.Height - 2;

                graphics.FillRectangle(bgBrush, textX, textY, textSize.Width, textSize.Height);
                graphics.DrawString(region.Text, font, textBrush, textX, textY);
            }

            return visualized;
        }

        /// <summary>
        /// 计算边界框的中心点
        /// </summary>
        private static PointF CalculateCenter(PointI[] points)
        {
            float sumX = 0, sumY = 0;
            foreach (var p in points)
            {
                sumX += p.X;
                sumY += p.Y;
            }
            return new PointF(sumX / points.Length, sumY / points.Length);
        }

        /// <summary>
        /// 转换 PointI 数组为 PointF 数组
        /// </summary>
        private static PointF[] ConvertPoints(PointI[] points)
        {
            var result = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                result[i] = new PointF(points[i].X, points[i].Y);
            }
            return result;
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
