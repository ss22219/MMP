// Apache-2.0 license
// Adapted from RapidAI / RapidOCR

using Microsoft.ML.OnnxRuntime.Tensors;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Drawing2D;

namespace RapidOcrNet
{
    internal static class OcrUtils
    {
        public static Tensor<float> SubtractMeanNormalize(Bitmap src, float[] meanVals, float[] normVals)
        {
            int cols = src.Width;
            int rows = src.Height;
            const int expChannels = 3; // RGB channels

            Tensor<float> inputTensor = new DenseTensor<float>([1, expChannels, rows, cols]);

            BitmapData bmpData = src.LockBits(new Rectangle(0, 0, cols, rows), 
                ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

            try
            {
                unsafe
                {
                    byte* ptr = (byte*)bmpData.Scan0;
                    int stride = bmpData.Stride;

                    for (int r = 0; r < rows; ++r)
                    {
                        byte* row = ptr + (r * stride);
                        for (int c = 0; c < cols; ++c)
                        {
                            int offset = c * 3;
                            // System.Drawing uses BGR format, model expects BGR order
                            byte b = row[offset];
                            byte g = row[offset + 1];
                            byte r_val = row[offset + 2];

                            // Keep BGR order as in original SkiaSharp code
                            inputTensor[0, 0, r, c] = (b - meanVals[0]) * normVals[0];
                            inputTensor[0, 1, r, c] = (g - meanVals[1]) * normVals[1];
                            inputTensor[0, 2, r, c] = (r_val - meanVals[2]) * normVals[2];
                        }
                    }
                }
            }
            finally
            {
                src.UnlockBits(bmpData);
            }

            return inputTensor;
        }

        public static Bitmap MakePadding(Bitmap src, int padding)
        {
            if (padding <= 0)
            {
                return src;
            }

            int newWidth = src.Width + 2 * padding;
            int newHeight = src.Height + 2 * padding;

            Bitmap newBmp = new Bitmap(newWidth, newHeight, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(newBmp))
            {
                g.Clear(Color.White);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, padding, padding, src.Width, src.Height);
            }

            return newBmp;
        }

        public static int GetThickness(Bitmap boxImg)
        {
            int minSize = boxImg.Width > boxImg.Height ? boxImg.Height : boxImg.Width;
            return minSize / 1000 + 2;
        }

        public static IEnumerable<Bitmap> GetPartImages(Bitmap src, IReadOnlyList<TextBox> textBoxes)
        {
            for (int i = 0; i < textBoxes.Count; ++i)
            {
                if (textBoxes[i]?.Points != null && textBoxes[i].Points.Length == 4)
                {
                    yield return GetRotateCropImage(src, textBoxes[i].Points);
                }
            }
        }

        public static Bitmap ResizeBitmap(Bitmap src, int width, int height)
        {
            Bitmap resized = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, width, height);
            }
            return resized;
        }

        public static Bitmap GetRotateCropImage(Bitmap src, PointI[] box)
        {
            System.Diagnostics.Debug.Assert(box.Length == 4);
            Span<PointI> points = stackalloc PointI[] { box[0], box[1], box[2], box[3] };
            
            ReadOnlySpan<int> collectX = stackalloc int[] { box[0].X, box[1].X, box[2].X, box[3].X };
            int left = int.MaxValue;
            int right = int.MinValue;
            foreach (var v in collectX)
            {
                if (v < left) left = v;
                else if (v > right) right = v;
            }

            ReadOnlySpan<int> collectY = stackalloc int[] { box[0].Y, box[1].Y, box[2].Y, box[3].Y };
            int top = int.MaxValue;
            int bottom = int.MinValue;
            foreach (var v in collectY)
            {
                if (v < top) top = v;
                else if (v > bottom) bottom = v;
            }

            Rectangle rect = new Rectangle(left, top, right - left, bottom - top);
            Bitmap imgCrop = src.Clone(rect, src.PixelFormat);

            ref PointI p0 = ref points[0];
            p0.X -= left;
            p0.Y -= top;

            ref PointI p1 = ref points[1];
            p1.X -= left;
            p1.Y -= top;

            ref PointI p2 = ref points[2];
            p2.X -= left;
            p2.Y -= top;

            ref PointI p3 = ref points[3];
            p3.X -= left;
            p3.Y -= top;

            int imgCropWidth = (int)Math.Sqrt((p0.X - p1.X) * (p0.X - p1.X) + (p0.Y - p1.Y) * (p0.Y - p1.Y));
            int imgCropHeight = (int)Math.Sqrt((p0.X - p3.X) * (p0.X - p3.X) + (p0.Y - p3.Y) * (p0.Y - p3.Y));

            var srcPoints = new PointF[] 
            { 
                new PointF(p0.X, p0.Y), 
                new PointF(p1.X, p1.Y), 
                new PointF(p2.X, p2.Y) 
            };
            
            var dstPoints = new PointF[] 
            { 
                new PointF(0, 0), 
                new PointF(imgCropWidth, 0), 
                new PointF(imgCropWidth, imgCropHeight) 
            };

            Bitmap partImg = new Bitmap(imgCropWidth, imgCropHeight, PixelFormat.Format24bppRgb);
            using (Graphics g = Graphics.FromImage(partImg))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Transform = GetPerspectiveTransform(srcPoints, dstPoints, imgCropWidth, imgCropHeight);
                g.DrawImage(imgCrop, 0, 0);
            }

            imgCrop.Dispose();

            if (partImg.Height >= partImg.Width * 1.5)
            {
                return BitmapRotateClockWise90(partImg);
            }

            return partImg;
        }

        private static Matrix GetPerspectiveTransform(PointF[] srcPoints, PointF[] dstPoints, int width, int height)
        {
            // Simplified affine transform for 3 points
            var matrix = new Matrix();
            
            // Calculate affine transformation matrix
            float x1 = srcPoints[0].X, y1 = srcPoints[0].Y;
            float x2 = srcPoints[1].X, y2 = srcPoints[1].Y;
            float x3 = srcPoints[2].X, y3 = srcPoints[2].Y;
            
            float u1 = dstPoints[0].X, v1 = dstPoints[0].Y;
            float u2 = dstPoints[1].X, v2 = dstPoints[1].Y;
            float u3 = dstPoints[2].X, v3 = dstPoints[2].Y;
            
            float denom = (x1 - x2) * (y1 - y3) - (x1 - x3) * (y1 - y2);
            
            if (Math.Abs(denom) < 0.0001f)
            {
                return matrix; // Return identity if degenerate
            }
            
            float a = ((u1 - u2) * (y1 - y3) - (u1 - u3) * (y1 - y2)) / denom;
            float b = ((u1 - u2) * (x1 - x3) - (u1 - u3) * (x1 - x2)) / -denom;
            float c = u1 - a * x1 - b * y1;
            
            float d = ((v1 - v2) * (y1 - y3) - (v1 - v3) * (y1 - y2)) / denom;
            float e = ((v1 - v2) * (x1 - x3) - (v1 - v3) * (x1 - x2)) / -denom;
            float f = v1 - d * x1 - e * y1;
            
            matrix = new Matrix(a, d, b, e, c, f);
            return matrix;
        }

        public static Bitmap BitmapRotateClockWise180(Bitmap src)
        {
            Bitmap rotated = new Bitmap(src.Width, src.Height, src.PixelFormat);
            using (Graphics g = Graphics.FromImage(rotated))
            {
                g.TranslateTransform(src.Width / 2f, src.Height / 2f);
                g.RotateTransform(180);
                g.TranslateTransform(-src.Width / 2f, -src.Height / 2f);
                g.DrawImage(src, new Point(0, 0));
            }
            return rotated;
        }

        public static Bitmap BitmapRotateClockWise90(Bitmap src)
        {
            Bitmap rotated = new Bitmap(src.Height, src.Width, src.PixelFormat);
            using (Graphics g = Graphics.FromImage(rotated))
            {
                g.TranslateTransform(rotated.Width / 2f, rotated.Height / 2f);
                g.RotateTransform(90);
                g.TranslateTransform(-src.Width / 2f, -src.Height / 2f);
                g.DrawImage(src, new Point(0, 0));
            }
            return rotated;
        }
    }
}
