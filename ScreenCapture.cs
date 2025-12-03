using System;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace MMP
{
    /// <summary>
    /// Provides screen capture functionality for game window using SkiaSharp
    /// </summary>
    public partial class ScreenCapture
    {
        private static bool _dpiAwareSet = false;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSource, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int SRCCOPY = 0x00CC0020;

        /// <summary>
        /// Enable DPI awareness to get true pixel dimensions (call once at startup)
        /// </summary>
        public static void EnableDpiAwareness()
        {
            if (!_dpiAwareSet)
            {
                SetProcessDPIAware();
                _dpiAwareSet = true;
            }
        }

        /// <summary>
        /// Captures the entire primary screen
        /// </summary>
        public static SKBitmap CaptureScreen()
        {
            // 获取主屏幕尺寸
            var screenDC = GetDC(IntPtr.Zero);
            int screenWidth = GetDeviceCaps(screenDC, 8);  // HORZRES
            int screenHeight = GetDeviceCaps(screenDC, 10); // VERTRES
            ReleaseDC(IntPtr.Zero, screenDC);

            return CaptureScreen(0, 0, screenWidth, screenHeight);
        }

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        /// <summary>
        /// Captures a specific region of the screen
        /// </summary>
        public static SKBitmap CaptureScreen(int x, int y, int width, int height)
        {
            IntPtr screenDC = GetDC(IntPtr.Zero);
            IntPtr memDC = CreateCompatibleDC(screenDC);
            IntPtr hBitmap = CreateCompatibleBitmap(screenDC, width, height);
            IntPtr oldBitmap = SelectObject(memDC, hBitmap);

            BitBlt(memDC, 0, 0, width, height, screenDC, x, y, SRCCOPY);

            SelectObject(memDC, oldBitmap);
            DeleteDC(memDC);
            ReleaseDC(IntPtr.Zero, screenDC);

            // 转换为 SKBitmap
            var bitmap = ConvertHBitmapToSKBitmap(hBitmap, width, height);
            DeleteObject(hBitmap);

            return bitmap;
        }

        /// <summary>
        /// Captures a window by its handle (client area only, better for games)
        /// Uses PrintWindow method which works reliably for DirectX games
        /// </summary>
        public static SKBitmap? CaptureWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return null;

            // Ensure DPI awareness for accurate dimensions
            EnableDpiAwareness();

            // Get window and client area dimensions
            if (!GetWindowRect(hWnd, out RECT windowRect))
                return null;
            
            if (!GetClientRect(hWnd, out RECT clientRect))
                return null;

            int windowWidth = windowRect.Right - windowRect.Left;
            int windowHeight = windowRect.Bottom - windowRect.Top;
            int clientWidth = clientRect.Right - clientRect.Left;
            int clientHeight = clientRect.Bottom - clientRect.Top;

            if (clientWidth <= 0 || clientHeight <= 0)
                return null;

            // Calculate border sizes
            int borderLeft = (windowWidth - clientWidth) / 2;
            int borderTop = windowHeight - clientHeight - borderLeft;

            // Capture the entire window using PrintWindow
            IntPtr windowDC = GetWindowDC(hWnd);
            IntPtr memDC = CreateCompatibleDC(windowDC);
            IntPtr hBitmap = CreateCompatibleBitmap(windowDC, windowWidth, windowHeight);
            IntPtr oldBitmap = SelectObject(memDC, hBitmap);

            // PrintWindow with PW_RENDERFULLCONTENT flag (0x00000002)
            PrintWindow(hWnd, memDC, 0x00000002);

            SelectObject(memDC, oldBitmap);
            DeleteDC(memDC);
            ReleaseDC(hWnd, windowDC);

            // Convert full window to SKBitmap
            var fullBitmap = ConvertHBitmapToSKBitmap(hBitmap, windowWidth, windowHeight);
            DeleteObject(hBitmap);

            if (fullBitmap == null)
                return null;

            // Extract client area from full window capture
            try
            {
                var clientBitmap = new SKBitmap(clientWidth, clientHeight);
                using (var canvas = new SKCanvas(clientBitmap))
                using (var paint = new SKPaint())
                {
                    paint.IsAntialias = false;

                    var srcRect = new SKRect(borderLeft, borderTop, borderLeft + clientWidth, borderTop + clientHeight);
                    var dstRect = new SKRect(0, 0, clientWidth, clientHeight);
                    canvas.DrawBitmap(fullBitmap, srcRect, dstRect, paint);
                }

                fullBitmap.Dispose();
                return clientBitmap;
            }
            catch
            {
                fullBitmap?.Dispose();
                return null;
            }
        }

        /// <summary>
        /// Captures the client area of a window
        /// </summary>
        public static SKBitmap? CaptureWindowClient(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return null;

            if (!GetClientRect(hWnd, out RECT clientRect))
                return null;

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;

            if (width <= 0 || height <= 0)
                return null;

            // 获取客户区在屏幕上的位置
            POINT point = new POINT { X = 0, Y = 0 };
            ClientToScreen(hWnd, ref point);

            return CaptureScreen(point.X, point.Y, width, height);
        }

        /// <summary>
        /// 将 Windows HBITMAP 转换为 SKBitmap
        /// </summary>
        private static SKBitmap ConvertHBitmapToSKBitmap(IntPtr hBitmap, int width, int height)
        {
            // 获取位图数据
            BITMAP bm = new BITMAP();
            GetObject(hBitmap, Marshal.SizeOf(bm), ref bm);

            // 创建 SKBitmap
            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new SKBitmap(info);

            // 获取位图数据
            BITMAPINFO bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bmi.bmiHeader.biWidth = width;
            bmi.bmiHeader.biHeight = -height; // 负值表示从上到下
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;
            bmi.bmiHeader.biCompression = 0; // BI_RGB

            IntPtr screenDC = GetDC(IntPtr.Zero);
            
            // 直接将数据复制到 SKBitmap
            IntPtr pixels = bitmap.GetPixels();
            GetDIBits(screenDC, hBitmap, 0, (uint)height, pixels, ref bmi, 0);
            
            ReleaseDC(IntPtr.Zero, screenDC);

            return bitmap;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFO
        {
            public BITMAPINFOHEADER bmiHeader;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public uint[] bmiColors;
        }

        [DllImport("gdi32.dll")]
        private static extern int GetObject(IntPtr hObject, int nCount, ref BITMAP lpObject);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines, IntPtr lpvBits, ref BITMAPINFO lpbi, uint uUsage);

        /// <summary>
        /// 保存 SKBitmap 到文件
        /// </summary>
        public static void SaveToFile(SKBitmap bitmap, string filePath)
        {
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(filePath);
            data.SaveTo(stream);
        }

    }
}
