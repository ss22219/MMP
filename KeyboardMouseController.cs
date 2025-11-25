using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MMP
{
    /// <summary>
    /// 键盘鼠标控制器 - 基于 Python GenshinInteraction 重写
    /// 使用 PostMessage 实现后台输入控制
    /// </summary>
    public class KeyboardMouseController : IDisposable
    {
        private readonly IntPtr _hwnd;
        private bool _disposed = false;
        private Point _cursorPosition;
        private readonly int _captureWidth;
        private readonly int _captureHeight;
        private bool _backgroundMode = true; // 默认后台模式
        
        /// <summary>
        /// 设置是否使用后台模式
        /// true: 后台模式（使用PostMessage，不需要激活窗口）
        /// false: 前台模式（需要激活窗口并阻塞输入）
        /// </summary>
        public bool BackgroundMode
        {
            get => _backgroundMode;
            set => _backgroundMode = value;
        }

        // ==================== Windows API ====================
        
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref Point lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool BlockInput(bool fBlockIt);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct Point
        {
            public int X;
            public int Y;
        }

        // SendInput 相关结构体
        private const int INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        // Windows 消息常量
        private const uint WM_KEYDOWN = 0x0100;
        private const uint WM_KEYUP = 0x0101;
        private const uint WM_CHAR = 0x0102;
        private const uint WM_LBUTTONDOWN = 0x0201;
        private const uint WM_LBUTTONUP = 0x0202;
        private const uint WM_RBUTTONDOWN = 0x0204;
        private const uint WM_RBUTTONUP = 0x0205;
        private const uint WM_MBUTTONDOWN = 0x0207;
        private const uint WM_MBUTTONUP = 0x0208;
        private const uint WM_MOUSEMOVE = 0x0200;
        private const uint WM_MOUSEWHEEL = 0x020A;

        // 鼠标按钮状态
        private const uint MK_LBUTTON = 0x0001;
        private const uint MK_RBUTTON = 0x0002;
        private const uint MK_MBUTTON = 0x0010;

        // 滚轮增量
        private const int WHEEL_DELTA = 120;

        // 虚拟键码字典
        private static readonly Dictionary<string, int> VkKeyDict = new()
        {
            {"W", 0x57}, {"A", 0x41}, {"S", 0x53}, {"D", 0x44},
            {"E", 0x45}, {"Q", 0x51}, {"R", 0x52}, {"F", 0x46},
            {"SPACE", 0x20}, {"SHIFT", 0x10}, {"CTRL", 0x11}, {"ALT", 0x12},
            {"LSHIFT", 0xA0}, {"RSHIFT", 0xA1}, {"LCTRL", 0xA2}, {"RCTRL", 0xA3},
            {"LALT", 0xA4}, {"RALT", 0xA5},
            {"ESC", 0x1B}, {"ESCAPE", 0x1B}, {"ENTER", 0x0D}, {"TAB", 0x09},
            {"LEFT", 0x25}, {"UP", 0x26}, {"RIGHT", 0x27}, {"DOWN", 0x28},
            {"0", 0x30}, {"1", 0x31}, {"2", 0x32}, {"3", 0x33}, {"4", 0x34},
            {"5", 0x35}, {"6", 0x36}, {"7", 0x37}, {"8", 0x38}, {"9", 0x39}
        };

        // ==================== 构造函数 ====================

        public KeyboardMouseController(IntPtr hwnd, int captureWidth = 1920, int captureHeight = 1080)
        {
            _hwnd = hwnd;
            _captureWidth = captureWidth;
            _captureHeight = captureHeight;
            Debug.WriteLine($"[KeyboardMouseController] 初始化 hwnd={hwnd:X}, size={captureWidth}x{captureHeight}");
        }

        // ==================== 核心方法 ====================

        /// <summary>
        /// 发送 PostMessage
        /// </summary>
        private void Post(uint message, IntPtr wParam, IntPtr lParam)
        {
            PostMessage(_hwnd, message, wParam, lParam);
        }

        /// <summary>
        /// 获取按键的虚拟键码
        /// </summary>
        private int GetKeyByStr(string key)
        {
            string upperKey = key.ToUpper();
            if (VkKeyDict.TryGetValue(upperKey, out int vkCode))
            {
                return vkCode;
            }
            
            // 如果不在字典中，尝试使用 VkKeyScan
            if (key.Length == 1)
            {
                return VkKeyScan(key[0]) & 0xFF;
            }
            
            return 0;
        }

        /// <summary>
        /// 创建鼠标位置参数
        /// </summary>
        private IntPtr MakeMousePosition(int x, int y)
        {
            int posX, posY;
            
            if (x < 0)
            {
                posX = _captureWidth / 2;
                posY = _captureHeight / 2;
            }
            else
            {
                posX = x;
                posY = y;
                
                // 设置光标位置
                Point absPos = new() { X = x, Y = y };
                ClientToScreen(_hwnd, ref absPos);
                SetCursorPos(absPos.X, absPos.Y);
                Thread.Sleep(1);
            }
            
            return (IntPtr)((posY << 16) | (posX & 0xFFFF));
        }

        /// <summary>
        /// 判断窗口是否在前台
        /// </summary>
        private bool IsForeground()
        {
            return GetForegroundWindow() == _hwnd;
        }

        /// <summary>
        /// 激活窗口
        /// </summary>
        public void Activate()
        {
            Debug.WriteLine($"[KeyboardMouseController] Activate {_hwnd:X}");
            SetForegroundWindow(_hwnd);
            Thread.Sleep(50);
        }

        /// <summary>
        /// 取消激活（恢复之前的窗口）
        /// </summary>
        private void Deactivate()
        {
            Debug.WriteLine("[KeyboardMouseController] Deactivate");
            // 这里可以保存并恢复之前的前台窗口
        }

        /// <summary>
        /// 操作包装器 - 处理前后台切换
        /// </summary>
        private T? Operate<T>(Func<T> func, bool block = false)
        {
            // 后台模式：直接执行，不需要激活窗口
            if (_backgroundMode)
            {
                try
                {
                    return func();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[KeyboardMouseController] Operate exception: {ex.Message}");
                    return default;
                }
            }
            
            // 前台模式：需要激活窗口
            bool bg = !IsForeground();
            T? result = default;
            
            if (bg)
            {
                if (block)
                {
                    BlockInput(true);
                }
                GetCursorPos(out _cursorPosition);
                Activate();
            }
            
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[KeyboardMouseController] Operate exception: {ex.Message}");
            }
            
            if (bg)
            {
                Deactivate();
                Thread.Sleep(20);
                SetCursorPos(_cursorPosition.X, _cursorPosition.Y);
                if (block)
                {
                    BlockInput(false);
                }
            }
            
            return result;
        }

        private void Operate(Action action, bool block = false)
        {
            Operate<object?>(() => { action(); return null; }, block);
        }

        // ==================== 键盘输入 ====================

        /// <summary>
        /// 发送按键（按下并释放）
        /// </summary>
        public void SendKey(string key, double downTime = 0.02)
        {
            Debug.WriteLine($"[KeyboardMouseController] SendKey {key} {downTime}");
            Operate(() => DoSendKey(key, downTime));
        }

        private void DoSendKey(string key, double downTime)
        {
            int vkCode = GetKeyByStr(key);
            Post(WM_KEYDOWN, (IntPtr)vkCode, (IntPtr)0x1e0001);
            
            if (downTime > 0.1)
            {
                Thread.Sleep((int)(downTime * 1000));
            }
            else
            {
                Post(WM_CHAR, (IntPtr)vkCode, (IntPtr)0x1e0001);
            }
            
            Post(WM_KEYUP, (IntPtr)vkCode, unchecked((IntPtr)0xc01e0001));
            
            if (downTime <= 0.1)
            {
                Thread.Sleep((int)(downTime * 1000));
            }
            else
            {
                Thread.Sleep(20);
            }
        }

        /// <summary>
        /// 按下按键（不释放）
        /// </summary>
        public void SendKeyDown(string key)
        {
            GetCursorPos(out Point currentPosition);
            Activate();
            DoSendKeyDown(key);
            SetCursorPos(currentPosition.X, currentPosition.Y);
        }

        private void DoSendKeyDown(string key)
        {
            int vkCode = GetKeyByStr(key);
            Post(WM_KEYDOWN, (IntPtr)vkCode, (IntPtr)0x1e0001);
            Post(WM_CHAR, (IntPtr)vkCode, (IntPtr)0x1e0001);
        }

        /// <summary>
        /// 释放按键
        /// </summary>
        public void SendKeyUp(string key)
        {
            Debug.WriteLine($"[KeyboardMouseController] SendKeyUp {key}");
            DoSendKeyUp(key);
            Deactivate();
        }

        private void DoSendKeyUp(string key)
        {
            int vkCode = GetKeyByStr(key);
            Post(WM_KEYUP, (IntPtr)vkCode, unchecked((IntPtr)0xc01e0001));
        }

        // ==================== 鼠标输入 ====================

        /// <summary>
        /// 移动鼠标
        /// </summary>
        public void Move(int x, int y, uint downBtn = 0)
        {
            IntPtr longPos = MakeMousePosition(x, y);
            Post(WM_MOUSEMOVE, (IntPtr)downBtn, longPos);
        }

        /// <summary>
        /// 点击
        /// </summary>
        public void Click(int x = -1, int y = -1, double downTime = 0.02, string key = "left")
        {
            Operate(() => DoClick(x, y, downTime, key), block: true);
        }

        private void DoClick(int x, int y, double downTime, string key)
        {
            Debug.WriteLine($"[KeyboardMouseController] Click {x}, {y} {downTime}");
            
            // 前台模式：先设置鼠标位置
            if (!_backgroundMode && x >= 0 && y >= 0)
            {
                Point absPos = new() { X = x, Y = y };
                ClientToScreen(_hwnd, ref absPos);
                SetCursorPos(absPos.X, absPos.Y);
                Thread.Sleep(50); // 等待鼠标移动到位
            }
            
            IntPtr clickPos = MakeMousePosition(x, y);
            
            uint btnDown, btnMk, btnUp;
            if (key == "left")
            {
                btnDown = WM_LBUTTONDOWN;
                btnMk = MK_LBUTTON;
                btnUp = WM_LBUTTONUP;
            }
            else if (key == "middle")
            {
                btnDown = WM_MBUTTONDOWN;
                btnMk = MK_MBUTTON;
                btnUp = WM_MBUTTONUP;
            }
            else // right
            {
                btnDown = WM_RBUTTONDOWN;
                btnMk = MK_RBUTTON;
                btnUp = WM_RBUTTONUP;
            }
            
            Post(btnDown, (IntPtr)btnMk, clickPos);
            Post(btnUp, IntPtr.Zero, clickPos);
            Thread.Sleep((int)(downTime * 1000));
        }

        /// <summary>
        /// 右键点击
        /// </summary>
        public void RightClick(int x = -1, int y = -1, double downTime = 0.02)
        {
            DoClick(x, y, downTime, "right");
        }

        /// <summary>
        /// 中键点击
        /// </summary>
        public void MiddleClick(int x = -1, int y = -1, double downTime = 0.02)
        {
            Operate(() => DoClick(x, y, downTime, "middle"));
        }

        /// <summary>
        /// 按下鼠标按钮
        /// </summary>
        public void MouseDown(int x = -1, int y = -1, string key = "left")
        {
            Operate(() => DoMouseDown(x, y, key));
        }

        private void DoMouseDown(int x, int y, string key)
        {
            IntPtr clickPos = MakeMousePosition(x, y);
            uint action = key == "left" ? WM_LBUTTONDOWN : WM_RBUTTONDOWN;
            uint btn = key == "left" ? MK_LBUTTON : MK_RBUTTON;
            Post(action, (IntPtr)btn, clickPos);
        }

        /// <summary>
        /// 释放鼠标按钮
        /// </summary>
        public void MouseUp(int x = -1, int y = -1, string key = "left")
        {
            Operate(() => DoMouseUp(x, y, key));
        }

        private void DoMouseUp(int x, int y, string key)
        {
            IntPtr clickPos = MakeMousePosition(x, y);
            Debug.WriteLine($"[KeyboardMouseController] MouseUp {x}, {y}, {clickPos:X}");
            uint action = key == "left" ? WM_LBUTTONUP : WM_RBUTTONUP;
            Post(action, IntPtr.Zero, clickPos);
        }

        /// <summary>
        /// 滚轮滚动
        /// </summary>
        public void Scroll(int x, int y, int scrollAmount)
        {
            Operate(() => DoScroll(x, y, scrollAmount), block: true);
        }

        private void DoScroll(int x, int y, int scrollAmount)
        {
            Debug.WriteLine($"[KeyboardMouseController] Scroll {x}, {y}, {scrollAmount}");
            
            int sign = scrollAmount > 0 ? 1 : (scrollAmount < 0 ? -1 : 0);
            
            // 设置光标位置
            Point absPos = new() { X = x, Y = y };
            ClientToScreen(_hwnd, ref absPos);
            SetCursorPos(absPos.X, absPos.Y);
            Thread.Sleep(100);
            
            // 模拟滚轮
            for (int i = 0; i < Math.Abs(scrollAmount); i++)
            {
                IntPtr longPos = x > 0 && y > 0 ? MakeMousePosition(x, y) : IntPtr.Zero;
                IntPtr wParam = (IntPtr)(WHEEL_DELTA * sign << 16);
                Post(WM_MOUSEWHEEL, wParam, longPos);
                Thread.Sleep(1);
            }
            
            Thread.Sleep(100);
        }

        /// <summary>
        /// 滑动（拖拽）
        /// </summary>
        public void Swipe(int x1, int y1, int x2, int y2, double duration = 3, double settleTime = 0.1)
        {
            Debug.WriteLine($"[KeyboardMouseController] Swipe start {x1}, {y1}, {x2}, {y2}");
            
            // 移动到起点
            Move(x1, y1);
            Thread.Sleep(100);
            
            // 按下鼠标左键
            DoMouseDown(x1, y1, "left");
            
            // 计算步数和步长
            int steps = (int)(duration / 0.01); // 100 steps per second
            double stepDx = (double)(x2 - x1) / steps;
            double stepDy = (double)(y2 - y1) / steps;
            
            // 移动到终点
            for (int i = 0; i < steps; i++)
            {
                int currentX = x1 + (int)(i * stepDx);
                int currentY = y1 + (int)(i * stepDy);
                Move(currentX, currentY, MK_LBUTTON);
                Thread.Sleep(10);
            }
            
            if (settleTime > 0)
            {
                Thread.Sleep((int)(settleTime * 1000));
            }
            
            // 释放鼠标左键
            DoMouseUp(x2, y2, "left");
            Debug.WriteLine($"[KeyboardMouseController] Swipe end {x1}, {y1}, {x2}, {y2}");
        }

        // ==================== SendInput 鼠标移动 ====================

        /// <summary>
        /// 使用 SendInput 发送相对鼠标移动
        /// </summary>
        public void SendMouseMove(int dx, int dy)
        {
            INPUT input = new INPUT
            {
                type = INPUT_MOUSE,
                u = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = 0,
                        dwFlags = MOUSEEVENTF_MOVE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            SendInput(1, new INPUT[] { input }, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>
        /// 平滑移动鼠标（分步执行）
        /// </summary>
        public void SmoothMouseMove(int totalDx, int totalDy, int steps = 20, int delayMs = 5)
        {
            float stepX = totalDx / (float)steps;
            float stepY = totalDy / (float)steps;

            for (int i = 0; i < steps; i++)
            {
                SendMouseMove((int)stepX, (int)stepY);
                Thread.Sleep(delayMs);
            }
        }

        // ==================== 便捷方法 ====================

        public void PressW(int duration = 50) => SendKey("W", duration / 1000.0);
        public void PressA(int duration = 50) => SendKey("A", duration / 1000.0);
        public void PressS(int duration = 50) => SendKey("S", duration / 1000.0);
        public void PressD(int duration = 50) => SendKey("D", duration / 1000.0);
        public void PressE(int duration = 50) => SendKey("E", duration / 1000.0);
        public void PressSpace(int duration = 50) => SendKey("SPACE", duration / 1000.0);
        public void PressShift(int duration = 50) => SendKey("SHIFT", duration / 1000.0);
        public void PressEscape(int duration = 50) => SendKey("ESCAPE", duration / 1000.0);
        public void PressEnter(int duration = 50) => SendKey("ENTER", duration / 1000.0);

        // ==================== 清理 ====================

        public void Dispose()
        {
            if (_disposed)
                return;

            Debug.WriteLine("[KeyboardMouseController] 清理资源");
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
