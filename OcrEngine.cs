using System.Text;
using SkiaSharp;
using RapidOcrNet;

namespace MMP
{
    /// <summary>
    /// OCR 引擎，用于识别游戏画面中的文字
    /// 使用 RapidOCR.Net 库 + DirectML GPU 加速
    /// </summary>
    public class OcrEngine : IDisposable
    {
        private RapidOcr? _ocr;
        private bool _disposed = false;
        private readonly RapidOcrOptions _options;
        private readonly bool _useGpu;

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
            public SKPoint Center { get; set; }

            /// <summary>
            /// 边界框的四个顶点
            /// </summary>
            public SKPoint[] BoundingBox { get; set; } = [];
        }

        /// <summary>
        /// 创建 OCR 引擎实例
        /// </summary>
        /// <param name="useGpu">是否使用 GPU 加速（DirectML）</param>
        /// <param name="options">OCR 选项，如果为 null 则使用默认选项</param>
        public OcrEngine(bool useGpu = true, RapidOcrOptions? options = null)
        {
            _useGpu = useGpu;
            _options = options ?? RapidOcrOptions.Default;
            Console.WriteLine($"[OcrEngine] 使用 RapidOCR.Net + DirectML，GPU 加速: {(_useGpu ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 初始化 OCR 引擎
        /// </summary>
        /// <param name="detPath">检测模型路径（可选，默认使用嵌入的模型）</param>
        /// <param name="clsPath">分类模型路径（可选，默认使用嵌入的模型）</param>
        /// <param name="recPath">识别模型路径（可选，默认使用嵌入的模型）</param>
        /// <param name="keysPath">字典路径（可选，默认使用嵌入的模型）</param>
        /// <param name="numThread">线程数（0 表示自动）</param>
        public void Initialize(string? detPath = null, string? clsPath = null, string? recPath = null, string? keysPath = null, int numThread = 16)
        {
            if (_ocr != null)
                return;

            _ocr = new RapidOcr();
            
            // 尝试从嵌入资源直接加载（无需临时文件）
            var detBytes = EmbeddedResourceHelper.GetModelBytes("ch_PP-OCRv5_mobile_det.onnx");
            var clsBytes = EmbeddedResourceHelper.GetModelBytes("ch_ppocr_mobile_v2.0_cls_infer.onnx");
            var recBytes = EmbeddedResourceHelper.GetModelBytes("ch_PP-OCRv5_rec_mobile_infer.onnx");
            var keysBytes = EmbeddedResourceHelper.GetModelBytes("ppocrv5_dict.txt");
            
            if (detBytes != null && clsBytes != null && recBytes != null && keysBytes != null)
            {
                Console.WriteLine("[OcrEngine] 从嵌入资源加载模型（无临时文件）");
                var keysText = Encoding.UTF8.GetString(keysBytes);
                _ocr.InitModels(detBytes, clsBytes, recBytes, keysText, numThread, _useGpu);
                return;
            }
            
            // 回退：使用文件路径加载
            Console.WriteLine("[OcrEngine] 嵌入资源不可用，使用文件路径加载");
            
            // 提取嵌入的模型文件
            EmbeddedResourceHelper.EnsureModelsExtracted();
            
            // 使用中文 PP-OCRv5 模型（优先使用嵌入的资源）
            detPath ??= EmbeddedResourceHelper.GetModelPath("ch_PP-OCRv5_mobile_det.onnx");
            clsPath ??= EmbeddedResourceHelper.GetModelPath("ch_ppocr_mobile_v2.0_cls_infer.onnx");
            recPath ??= EmbeddedResourceHelper.GetModelPath("ch_PP-OCRv5_rec_mobile_infer.onnx");
            keysPath ??= EmbeddedResourceHelper.GetModelPath("ppocrv5_dict.txt");
            
            // 如果嵌入资源不存在，尝试使用本地 models 目录
            if (!File.Exists(detPath))
                detPath = "models/ch_PP-OCRv5_mobile_det.onnx";
            if (!File.Exists(clsPath))
                clsPath = "models/ch_ppocr_mobile_v2.0_cls_infer.onnx";
            if (!File.Exists(recPath))
                recPath = "models/ch_PP-OCRv5_rec_mobile_infer.onnx";
            if (!File.Exists(keysPath))
                keysPath = "models/ppocrv5_dict.txt";
            
            _ocr.InitModels(detPath, clsPath, recPath, keysPath, numThread, _useGpu);
        }

        /// <summary>
        /// 识别 SKBitmap 图像中的文字
        /// </summary>
        public OcrResult Recognize(SKBitmap bitmap)
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
        public List<OcrTextRegion> FindText(SKBitmap bitmap, string searchText, bool ignoreCase = true)
        {
            var result = Recognize(bitmap);
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return [.. result.Regions.Where(r => r.Text.Contains(searchText, comparison))];
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
        public static SKBitmap VisualizeResult(SKBitmap originalBitmap, OcrResult result)
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
                TextSize = 20,
                IsAntialias = true
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
                var textBounds = new SKRect();
                textPaint.MeasureText(region.Text, ref textBounds);
                float textX = region.Center.X - textBounds.Width / 2;
                float textY = region.Center.Y - textBounds.Height - 2;

                canvas.DrawRect(textX, textY, textBounds.Width, textBounds.Height, bgPaint);
                canvas.DrawText(region.Text, textX, textY + textBounds.Height, textPaint);
            }

            return visualized;
        }

        /// <summary>
        /// 计算边界框的中心点
        /// </summary>
        private static SKPoint CalculateCenter(SKPointI[] points)
        {
            float sumX = 0, sumY = 0;
            foreach (var p in points)
            {
                sumX += p.X;
                sumY += p.Y;
            }
            return new SKPoint(sumX / points.Length, sumY / points.Length);
        }

        /// <summary>
        /// 转换点坐标
        /// </summary>
        private static SKPoint[] ConvertPoints(SKPointI[] points)
        {
            return points.Select(p => new SKPoint(p.X, p.Y)).ToArray();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _ocr?.Dispose();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
