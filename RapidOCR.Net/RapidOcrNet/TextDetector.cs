// Apache-2.0 license
// Adapted from RapidAI / RapidOCR
// https://github.com/RapidAI/RapidOCR/blob/92aec2c1234597fa9c3c270efd2600c83feecd8d/dotnet/RapidOcrOnnxCs/OcrLib/DbNet.cs

using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Clipper2Lib;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace RapidOcrNet
{
    public sealed class TextDetector : IDisposable
    {
        private readonly float[] MeanValues = [0.485F * 255F, 0.456F * 255F, 0.406F * 255F];
        private readonly float[] NormValues = [1.0F / 0.229F / 255.0F, 1.0F / 0.224F / 255.0F, 1.0F / 0.225F / 255.0F];

        private InferenceSession _dbNet;
        private string _inputName;

        public void InitModel(string path, int numThread)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Detector model file does not exist: '{path}'.");
            }

            var op = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED,
                InterOpNumThreads = numThread,
                IntraOpNumThreads = numThread,
                LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR // Suppress warnings
            };

            // Try to use DirectML GPU acceleration
            try
            {
                op.AppendExecutionProvider_DML(0);
                System.Diagnostics.Debug.WriteLine("TextDetector: Using DirectML GPU");
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine("TextDetector: DirectML not available, using CPU");
            }

            _dbNet = new InferenceSession(path, op);
            _inputName = _dbNet.InputMetadata.Keys.First();
        }

        public IReadOnlyList<TextBox> GetTextBoxes(Bitmap src, ScaleParam scale, float boxScoreThresh, float boxThresh,
            float unClipRatio)
        {
            Tensor<float> inputTensors;
            using (var srcResize = OcrUtils.ResizeBitmap(src, scale.DstWidth, scale.DstHeight))
            {
                inputTensors = OcrUtils.SubtractMeanNormalize(srcResize, MeanValues, NormValues);
            }

            IReadOnlyCollection<NamedOnnxValue> inputs = new NamedOnnxValue[]
            {
                NamedOnnxValue.CreateFromTensor(_inputName, inputTensors)
            };

            try
            {
                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _dbNet.Run(inputs))
                {
                    return GetTextBoxes(results[0], scale.DstHeight, scale.DstWidth, scale, boxScoreThresh,
                        boxThresh, unClipRatio);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message + ex.StackTrace);
            }

            return null;
        }

        private static PointF[][] FindContours(byte[] array, int rows, int cols)
        {
            Span<int> v = Array.ConvertAll(array, c => (int)c);
            var contours = PContour.FindContours(v, cols, rows);
            return contours
                .Where(c => !c.isHole)
                .Select(c => PContour.ApproxPolyDP(c.GetSpan(), 1).ToArray())
                .ToArray();
        }

        private static byte[] DilateBitmap(byte[] data, int width, int height)
        {
            byte[] result = new byte[data.Length];
            Array.Copy(data, result, data.Length);
            
            // Optimized dilate for binary images (0 or 1)
            // If any neighbor is non-zero, set current pixel to non-zero
            for (int y = 1; y < height - 1; y++)
            {
                for (int x = 1; x < width - 1; x++)
                {
                    int idx = y * width + x;
                    
                    if (result[idx] == 0)
                    {
                        // Check if any neighbor is non-zero
                        if (data[idx - width - 1] > 0 || data[idx - width] > 0 || data[idx - width + 1] > 0 ||
                            data[idx - 1] > 0 || data[idx + 1] > 0 ||
                            data[idx + width - 1] > 0 || data[idx + width] > 0 || data[idx + width + 1] > 0)
                        {
                            result[idx] = 1;
                        }
                    }
                }
            }
            
            return result;
        }

        private static IReadOnlyList<TextBox> GetTextBoxes(DisposableNamedOnnxValue outputTensor, int rows, int cols, ScaleParam s, float boxScoreThresh, float boxThresh, float unClipRatio)
        {
            const float maxSideThresh = 3.0f;
            var rsBoxes = new List<TextBox>();

            ReadOnlySpan<float> predData = outputTensor.AsEnumerable<float>().ToArray();

            Span<byte> thresholdMat = new byte[predData.Length];
            Span<byte> cbufMat = new byte[predData.Length];

            int nonZeroCount = 0;
            for (int i = 0; i < predData.Length; i++)
            {
                var f = predData[i];
                cbufMat[i] = Convert.ToByte(f * 255);
                thresholdMat[i] = f > boxThresh ? (byte)1 : (byte)0;
                if (thresholdMat[i] > 0) nonZeroCount++;
            }

            // Dilate
            byte[] dilated = DilateBitmap(thresholdMat.ToArray(), cols, rows);
            PointF[][] contours = FindContours(dilated, rows, cols);

            // Create bitmap for scoring
            using (Bitmap predBitmap = new Bitmap(cols, rows, PixelFormat.Format8bppIndexed))
            {
                // Set grayscale palette
                ColorPalette palette = predBitmap.Palette;
                for (int i = 0; i < 256; i++)
                {
                    palette.Entries[i] = Color.FromArgb(i, i, i);
                }
                predBitmap.Palette = palette;

                BitmapData bmpData = predBitmap.LockBits(new Rectangle(0, 0, cols, rows),
                    ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                
                try
                {
                    // Handle stride properly
                    if (bmpData.Stride == cols)
                    {
                        Marshal.Copy(cbufMat.ToArray(), 0, bmpData.Scan0, cbufMat.Length);
                    }
                    else
                    {
                        byte[] cbufArray = cbufMat.ToArray();
                        unsafe
                        {
                            byte* ptr = (byte*)bmpData.Scan0;
                            for (int y = 0; y < rows; y++)
                            {
                                Marshal.Copy(cbufArray, y * cols, (IntPtr)(ptr + y * bmpData.Stride), cols);
                            }
                        }
                    }
                }
                finally
                {
                    predBitmap.UnlockBits(bmpData);
                }

                for (int i = 0; i < contours.Length; i++)
                {
                    var contour = contours[i];
                    if (contour.Length <= 2)
                    {
                        continue;
                    }

                    PointF[] minBox = GetMiniBox(contour, out float maxSide);
                    if (maxSide < maxSideThresh)
                    {
                        continue;
                    }

                    double score = GetScore(contour, predBitmap);
                    if (score < boxScoreThresh)
                    {
                        continue;
                    }

                    PointF[]? clipBox = Unclip(minBox, unClipRatio);
                    if (clipBox is null)
                    {
                        continue;
                    }

                    ReadOnlySpan<PointF> clipMinBox = GetMiniBox(clipBox, out maxSide);
                    if (maxSide < maxSideThresh + 2)
                    {
                        continue;
                    }

                    var finalPoints = new PointI[clipMinBox.Length];
                    for (int j = 0; j < clipMinBox.Length; j++)
                    {
                        var item = clipMinBox[j];
                        int x = (int)(item.X / s.ScaleWidth);
                        int ptx = Math.Min(Math.Max(x, 0), s.SrcWidth);

                        int y = (int)(item.Y / s.ScaleHeight);
                        int pty = Math.Min(Math.Max(y, 0), s.SrcHeight);

                        finalPoints[j] = new PointI(ptx, pty);
                    }

                    var textBox = new TextBox
                    {
                        Score = (float)score,
                        Points = finalPoints
                    };
                    rsBoxes.Add(textBox);
                }
            }

            return rsBoxes;
        }

        private static PointF[] GetMiniBox(PointF[] contours, out float minEdgeSize)
        {
            PointF[] points = GeometryExtensions.MinimumAreaRectangle(contours);

            GeometryExtensions.GetSize(points, out float width, out float height);
            minEdgeSize = MathF.Min(width, height);

            Array.Sort(points, CompareByX);

            int index1 = 0;
            int index2 = 1;
            int index3 = 2;
            int index4 = 3;

            if (points[1].Y > points[0].Y)
            {
                index1 = 0;
                index4 = 1;
            }
            else
            {
                index1 = 1;
                index4 = 0;
            }

            if (points[3].Y > points[2].Y)
            {
                index2 = 2;
                index3 = 3;
            }
            else
            {
                index2 = 3;
                index3 = 2;
            }

            return new PointF[] { points[index1], points[index2], points[index3], points[index4] };
        }

        public static int CompareByX(PointF left, PointF right)
        {
            if (left.X > right.X)
            {
                return 1;
            }

            if (left.X == right.X)
            {
                return 0;
            }

            return -1;
        }

        private static double GetScore(PointF[] contours, Bitmap fMapMat)
        {
            short xmin = 9999;
            short xmax = 0;
            short ymin = 9999;
            short ymax = 0;

            try
            {
                foreach (PointF point in contours)
                {
                    if (point.X < xmin) xmin = (short)point.X;
                    if (point.X > xmax) xmax = (short)point.X;
                    if (point.Y < ymin) ymin = (short)point.Y;
                    if (point.Y > ymax) ymax = (short)point.Y;
                }

                int roiWidth = xmax - xmin + 1;
                int roiHeight = ymax - ymin + 1;
                
                // Extract ROI - fix the rectangle calculation
                int rectX = Math.Max(0, xmin - 1);
                int rectY = Math.Max(0, ymin - 1);
                int rectWidth = Math.Min(roiWidth + 2, fMapMat.Width - rectX);
                int rectHeight = Math.Min(roiHeight + 2, fMapMat.Height - rectY);
                
                Rectangle roiRect = new Rectangle(rectX, rectY, rectWidth, rectHeight);
                
                using (Bitmap roiBitmap = fMapMat.Clone(roiRect, fMapMat.PixelFormat))
                {
                    BitmapData roiData = roiBitmap.LockBits(new Rectangle(0, 0, roiBitmap.Width, roiBitmap.Height),
                        ImageLockMode.ReadOnly, roiBitmap.PixelFormat);
                    
                    byte[] roiBitmapBytes = new byte[roiData.Stride * roiData.Height];
                    Marshal.Copy(roiData.Scan0, roiBitmapBytes, 0, roiBitmapBytes.Length);
                    roiBitmap.UnlockBits(roiData);

                    // Create mask - use 32bpp for drawing, then convert to 8bpp
                    byte[] maskBytes;
                    
                    // Draw on 32bpp bitmap first
                    using (Bitmap tempMask = new Bitmap(roiWidth, roiHeight, PixelFormat.Format32bppArgb))
                    {
                        using (Graphics g = Graphics.FromImage(tempMask))
                        {
                            g.Clear(Color.Black);
                            var adjustedPoints = contours.Select(p => new PointF(p.X - xmin, p.Y - ymin)).ToArray();
                            g.FillPolygon(Brushes.White, adjustedPoints);
                        }
                        
                        // Convert to grayscale bytes
                        BitmapData tempData = tempMask.LockBits(new Rectangle(0, 0, roiWidth, roiHeight),
                            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                        
                        maskBytes = new byte[roiWidth * roiHeight];
                        unsafe
                        {
                            byte* ptr = (byte*)tempData.Scan0;
                            for (int y = 0; y < roiHeight; y++)
                            {
                                for (int x = 0; x < roiWidth; x++)
                                {
                                    int srcIdx = y * tempData.Stride + x * 4;
                                    int dstIdx = y * roiWidth + x;
                                    // Take the blue channel (they're all the same for black/white)
                                    maskBytes[dstIdx] = ptr[srcIdx];
                                }
                            }
                        }
                        tempMask.UnlockBits(tempData);
                    }

                    double sum = 0;
                    int count = 0;

                    // Now compare mask with ROI bitmap
                    // maskBytes is roiWidth * roiHeight (no stride)
                    // roiBitmapBytes has stride
                    for (int y = 0; y < roiHeight && y < roiBitmap.Height; y++)
                    {
                        for (int x = 0; x < roiWidth && x < roiBitmap.Width; x++)
                        {
                            int maskIdx = y * roiWidth + x;
                            int roiIdx = y * roiData.Stride + x;
                            
                            if (maskIdx < maskBytes.Length && roiIdx < roiBitmapBytes.Length)
                            {
                                if (maskBytes[maskIdx] == 0) continue;
                                sum += roiBitmapBytes[roiIdx];
                                count++;
                            }
                        }
                    }

                    
                    if (count == 0) return 0;
                    double score = sum / count / byte.MaxValue;
                    return score;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.Message + ex.StackTrace);
            }

            return 0;
        }

        private static PointF[]? Unclip(PointF[] box, float unclipRatio)
        {
            PointF[] points = GeometryExtensions.MinimumAreaRectangle(box);
            GeometryExtensions.GetSize(points, out float width, out float height);

            if (height < 1.001 && width < 1.001)
            {
                return null;
            }

            var theClipperPts = new Path64(box.Select(pt => new Point64(pt.X, pt.Y)));

            float area = MathF.Abs(SignedPolygonArea(box));
            double length = LengthOfPoints(box);
            double distance = area * unclipRatio / length;

            var co = new ClipperOffset();
            co.AddPath(theClipperPts, JoinType.Round, EndType.Polygon);
            var solution = new Paths64();
            co.Execute(distance, solution);
            if (solution.Count == 0)
            {
                return null;
            }

            var unclipped = solution[0];

            var retPts = new PointF[unclipped.Count];
            for (int i = 0; i < unclipped.Count; ++i)
            {
                var ip = unclipped[i];
                retPts[i] = new PointF((int)ip.X, (int)ip.Y);
            }

            return retPts;
        }

        private static float SignedPolygonArea(PointF[] points)
        {
            float area = 0;
            for (int i = 0; i < points.Length - 1; i++)
            {
                area += (points[i + 1].X - points[i].X) * (points[i + 1].Y + points[i].Y) / 2;
            }

            area += (points[0].X - points[points.Length - 1].X) * (points[0].Y + points[points.Length - 1].Y) / 2;

            return area;
        }

        private static double LengthOfPoints(PointF[] box)
        {
            double length = 0;

            PointF pt = box[0];
            double x0 = pt.X;
            double y0 = pt.Y;

            for (int idx = 1; idx < box.Length; idx++)
            {
                PointF pts = box[idx];
                double x1 = pts.X;
                double y1 = pts.Y;
                double dx = x1 - x0;
                double dy = y1 - y0;

                length += Math.Sqrt(dx * dx + dy * dy);

                x0 = x1;
                y0 = y1;
            }

            var dxL = pt.X - x0;
            var dyL = pt.Y - y0;
            length += Math.Sqrt(dxL * dxL + dyL * dyL);

            return length;
        }

        public void Dispose()
        {
            _dbNet.Dispose();
        }
    }
}
