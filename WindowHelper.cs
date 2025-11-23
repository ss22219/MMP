using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MMP
{
    public class WindowHelper
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public static IntPtr FindWindow(string windowTitle)
        {
            IntPtr foundWindow = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                int length = GetWindowTextLength(hWnd);
                if (length == 0) return true;

                StringBuilder builder = new StringBuilder(length + 1);
                GetWindowText(hWnd, builder, builder.Capacity);
                string title = builder.ToString();

                if (title.Contains(windowTitle))
                {
                    foundWindow = hWnd;
                    return false; // Stop enumeration
                }

                return true; // Continue enumeration
            }, IntPtr.Zero);

            return foundWindow;
        }

        /// <summary>
        /// 通过窗口标题查找窗口（FindWindow 的别名）
        /// </summary>
        public static IntPtr FindWindowByTitle(string windowTitle)
        {
            return FindWindow(windowTitle);
        }

        /// <summary>
        /// 通过进程名查找窗口
        /// </summary>
        public static IntPtr FindWindowByProcessName(string processName)
        {
            IntPtr foundWindow = IntPtr.Zero;
            
            // 移除 .exe 后缀（如果有）
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                processName = processName.Substring(0, processName.Length - 4);
            }

            EnumWindows((hWnd, lParam) =>
            {
                // 获取窗口的进程ID
                uint processId;
                GetWindowThreadProcessId(hWnd, out processId);
                
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById((int)processId);
                    if (process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    {
                        // 确保窗口可见
                        if (IsWindowVisible(hWnd))
                        {
                            foundWindow = hWnd;
                            return false; // Stop enumeration
                        }
                    }
                }
                catch
                {
                    // 进程可能已经退出
                }

                return true; // Continue enumeration
            }, IntPtr.Zero);

            return foundWindow;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        /// <summary>
        /// 获取窗口客户区大小
        /// </summary>
        public static (int width, int height) GetWindowSize(IntPtr hWnd)
        {
            if (GetClientRect(hWnd, out RECT rect))
            {
                return (rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
            return (1920, 1080); // 默认值
        }

        public static string GetActiveWindowTitle()
        {
            try
            {
                IntPtr handle = GetForegroundWindow();
                int length = GetWindowTextLength(handle);
                if (length == 0) return "";

                StringBuilder builder = new StringBuilder(length + 1);
                GetWindowText(handle, builder, builder.Capacity);
                return builder.ToString();
            }
            catch
            {
                return "";
            }
        }

        public static bool IsTargetWindow()
        {
            string windowTitle = GetActiveWindowTitle();
            return windowTitle.Contains("二重螺旋") || windowTitle.Contains("Double Helix");
        }
    }
}
