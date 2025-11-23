using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MMP
{
    /// <summary>
    /// Provides screen capture functionality for game window
    /// </summary>
    public class ScreenCapture
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

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

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

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hDC, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSource, int xSrc, int ySrc, CopyPixelOperation rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// Captures the entire screen
        /// </summary>
        public static Bitmap CaptureScreen()
        {
            return CaptureScreen(0, 0, Screen.PrimaryScreen.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
        }

        /// <summary>
        /// Captures a specific region of the screen
        /// </summary>
        public static Bitmap CaptureScreen(int x, int y, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bitmap;
        }

        /// <summary>
        /// Captures a specific window by handle (client area only, better for games)
        /// Uses PrintWindow method which works reliably for DirectX games
        /// </summary>
        public static Bitmap? CaptureWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return null;

            // Ensure DPI awareness for accurate dimensions
            EnableDpiAwareness();

            // Get window and client area dimensions
            GetWindowRect(hWnd, out RECT windowRect);
            GetClientRect(hWnd, out RECT clientRect);

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
            IntPtr hdcSrc = GetWindowDC(hWnd);
            IntPtr hdcDest = CreateCompatibleDC(hdcSrc);
            IntPtr hBitmap = CreateCompatibleBitmap(hdcSrc, windowWidth, windowHeight);
            IntPtr hOld = SelectObject(hdcDest, hBitmap);

            // PrintWindow with PW_RENDERFULLCONTENT flag (0x00000002)
            PrintWindow(hWnd, hdcDest, 0x00000002);

            // Convert to Bitmap
            Bitmap fullBitmap = Image.FromHbitmap(hBitmap);

            // Cleanup GDI objects
            SelectObject(hdcDest, hOld);
            DeleteObject(hBitmap);
            DeleteDC(hdcDest);
            ReleaseDC(hWnd, hdcSrc);

            // Extract client area from full window capture
            Bitmap clientBitmap = new Bitmap(clientWidth, clientHeight, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(clientBitmap))
            {
                graphics.DrawImage(fullBitmap,
                    new Rectangle(0, 0, clientWidth, clientHeight),
                    new Rectangle(borderLeft, borderTop, clientWidth, clientHeight),
                    GraphicsUnit.Pixel);
            }

            fullBitmap.Dispose();
            return clientBitmap;
        }

        /// <summary>
        /// Captures a specific window by handle using screen capture method
        /// Alternative method that may work better for some games
        /// </summary>
        public static Bitmap? CaptureWindowFromScreen(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
                return null;

            // Ensure DPI awareness for accurate dimensions
            EnableDpiAwareness();

            // Get client area size (game content without borders/titlebar)
            GetClientRect(hWnd, out RECT clientRect);

            int width = clientRect.Right - clientRect.Left;
            int height = clientRect.Bottom - clientRect.Top;

            if (width <= 0 || height <= 0)
                return null;

            // Get client area screen coordinates
            POINT point = new POINT { X = 0, Y = 0 };
            ClientToScreen(hWnd, ref point);

            // Capture from screen (alternative method)
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(point.X, point.Y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }

            return bitmap;
        }

        /// <summary>
        /// Saves a bitmap to file
        /// </summary>
        public static void SaveToFile(Bitmap bitmap, string filePath)
        {
            bitmap.Save(filePath, ImageFormat.Png);
        }
    }

    // Helper class for screen information
    public static class Screen
    {
        public static ScreenInfo PrimaryScreen => new ScreenInfo
        {
            Bounds = new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1))
        };

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
    }

    public class ScreenInfo
    {
        public Rectangle Bounds { get; set; }
    }
}
