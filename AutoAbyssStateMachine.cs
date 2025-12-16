using System.Collections.Concurrent;
using Avalonia.Threading;
using MMP.States;

namespace MMP
{
    /// <summary>
    /// 深渊自动化状态机 - 主程序
    /// </summary>
    public partial class AutoAbyssStateMachine
    {
        // 游戏状态枚举
        public enum GameState
        {
            Initializing,              // 初始化
            MainMenu,                  // 主菜单
            SelectingBuff,             // 选择Buff
            SelectingCandle,           // 选择烛芯
            SelectingRelic,            // 选择遗物
            Navigating,                // 导航中
            InBattle,                  // 战斗中
            Reviving,                  // 复苏中
            ExploreDetails,            // 探索详情
            InteractingFireMechanism,  // 簧火机关交互
            ClosingUI,                 // 关闭UI
            ForceExiting,              // 强制退出深渊
            Error                      // 错误状态
        }

        private volatile GameState _currentState = GameState.MainMenu;

        // OCR 线程相关
        private Thread? _ocrThread;
        private volatile bool _shouldStop = false;
        private OcrEngine.OcrResult? _latestOcrResult = null;
        private readonly object _ocrResultLock = new object();

        // OCR 完成回调
        private Action<OcrEngine.OcrResult>? _ocrCompletedCallback;

        // 防AFK线程相关
        private Thread? _antiAfkThread;
        private bool _hasLoggedNonBattleState = false;

        // 组件
        private IntPtr _hwnd;
        private OcrEngine? _ocrEngine;
        private KeyboardMouseController? _controller;
        private BattleEntitiesAPI? _battleApi;
        private AppConfig _config = new();

        // 状态数据
        private DateTime _stateStartTime = DateTime.Now;

        // 状态处理器缓存（保持实例状态）
        private readonly Dictionary<GameState, IStateHandler> _stateHandlers = new();

        // 状态上下文
        private StateContext? _stateContext;

        // 同步上下文（用于停止）

        public GameState CurrentState
        {
            get { return _currentState; }
            private set { _currentState = value; }
        }

        /// <summary>
        /// 异步运行状态机（用于 WPF）
        /// </summary>
        public async Task RunAsync(CancellationToken ct)
        {
            if (!Initialize())
                return;

            StartOcrThread();
            StartAntiAfkThread();
            try
            {
                await MainLoopAsync(ct);
            }
            catch
            {
                Cleanup();
            }
        }

        /// <summary>
        /// 停止状态机
        /// </summary>
        public void Stop()
        {
            Console.WriteLine("[状态机] 正在停止...");
            _shouldStop = true;
            _currentStateCts?.Cancel();
        }


        private bool Initialize()
        {
            // 加载配置
            _config = AppConfig.Load();
            Console.WriteLine($"配置已加载:");
            Console.WriteLine($"  - OCR 间隔: {_config.Ocr.OcrInterval}ms");
            Console.WriteLine($"  - 状态超时: {_config.Timeouts.StateTimeout}秒");
            Console.WriteLine($"  - 怪物检测距离: {_config.Battle.MonsterDetectionRange / 100}米");
            Console.WriteLine();

            // 查找游戏进程
            var processes = System.Diagnostics.Process.GetProcessesByName("EM-Win64-Shipping");
            if (processes.Length == 0)
            {
                Console.WriteLine("错误: 找不到游戏进程");
                return false;
            }

            _hwnd = processes[0].MainWindowHandle;
            if (_hwnd == IntPtr.Zero)
            {
                Console.WriteLine("错误: 游戏窗口无效");
                return false;
            }

            Console.WriteLine($"找到游戏窗口 (PID={processes[0].Id})");

            // 初始化 OCR
            _ocrEngine = new OcrEngine();
            try
            {
                _ocrEngine.Initialize();
                Console.WriteLine("✓ OCR 引擎初始化完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ OCR 初始化失败: {ex.Message}");
                return false;
            }

            // 初始化控制器
            var (width, height) = WindowHelper.GetWindowSize(_hwnd);
            _controller = new KeyboardMouseController(_hwnd, width, height);
            _controller.BackgroundMode = false;
            Console.WriteLine("✓ 控制器初始化完成");

            // 初始化战斗 API
            _battleApi = new BattleEntitiesAPI("EM-Win64-Shipping");
            Console.WriteLine("✓ 战斗 API 初始化完成");

            // 初始化状态上下文
            _stateContext = new StateContext(
                _hwnd,
                _controller,
                _battleApi,
                _config,
                GetLatestOcrResult,
                RequestStateTransition
            );
            Console.WriteLine("✓ 状态上下文初始化完成");
            Console.WriteLine();

            return true;
        }

        private void StartOcrThread()
        {
            _ocrThread = new Thread(() =>
            {
                Console.WriteLine("[OCR线程] 启动");
                int ocrIntervalMs = _config.Ocr.OcrInterval;
                DateTime lastOcrTime = DateTime.MinValue;
                int ocrCount = 0; // OCR 计数器

                while (!_shouldStop)
                {
                    try
                    {
                        // 计算距离上次 OCR 的时间
                        var elapsed = (DateTime.Now - lastOcrTime).TotalMilliseconds;

                        // 如果还没到间隔时间，等待
                        if (elapsed < ocrIntervalMs)
                        {
                            Thread.Sleep(50); // 短暂休眠，避免 CPU 空转
                            continue;
                        }

                        // 记录本次 OCR 开始时间
                        var ocrStartTime = DateTime.Now;
                        lastOcrTime = ocrStartTime;

                        // 使用 CaptureWindow 捕获游戏画面（已自动裁剪客户区）
                        var captureStart = DateTime.Now;
                        using var screenshot = ScreenCapture.CaptureWindow(_hwnd);
                        var captureTime = (DateTime.Now - captureStart).TotalMilliseconds;
                        
                        if (screenshot != null && _ocrEngine != null)
                        {
                            ocrCount++;

                            var recognizeStart = DateTime.Now;
                            var result = _ocrEngine.Recognize(screenshot);
                            var recognizeTime = (DateTime.Now - recognizeStart).TotalMilliseconds;
                            
                            if (result != null && result.Regions != null)
                            {
                                // 更新最新的 OCR 结果（线程安全）
                                var lockStart = DateTime.Now;
                                lock (_ocrResultLock)
                                {
                                    _latestOcrResult = result;
                                }
                                var lockTime = (DateTime.Now - lockStart).TotalMilliseconds;

                                // 触发 OCR 完成回调（在锁外调用，避免死锁）
                                var callbackStart = DateTime.Now;
                                try
                                {
                                    _ocrCompletedCallback?.Invoke(result);
                                }
                                catch (Exception callbackEx)
                                {
                                    Console.WriteLine($"[OCR线程] 回调错误: {callbackEx.Message}");
                                }
                                var callbackTime = (DateTime.Now - callbackStart).TotalMilliseconds;
                                
                                // 详细性能分析
                                var totalDuration = (DateTime.Now - ocrStartTime).TotalMilliseconds;
                                if (totalDuration > ocrIntervalMs * 2)
                                {
                                    Console.WriteLine($"[OCR线程] 性能警告: 总耗时 {totalDuration:F0}ms > {ocrIntervalMs * 2}ms");
                                    Console.WriteLine($"  - 截图: {captureTime:F1}ms");
                                    Console.WriteLine($"  - 识别: {recognizeTime:F1}ms");
                                    Console.WriteLine($"  - 锁定: {lockTime:F1}ms");
                                    Console.WriteLine($"  - 回调: {callbackTime:F1}ms");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[OCR线程] 错误: {ex.Message}");
                        Thread.Sleep(100); // 出错后短暂休眠
                    }

                    // 固定间隔，避免因 OCR 处理慢而累积延迟
                    Thread.Sleep(50); // 短暂休眠，避免 CPU 空转
                }
                Console.WriteLine("[OCR线程] 停止");
            })
            {
                IsBackground = true,
                Name = "OCR Thread"
            };

            _ocrThread.Start();
        }

        /// <summary>
        /// 启动防AFK线程（鼠标移动 + 随机按键，绕过脚本检测）
        /// </summary>
        private void StartAntiAfkThread()
        {
            _antiAfkThread = new Thread(() =>
            {
                var antiDetectConfig = _config.AntiDetection;
                Console.WriteLine($"[防AFK线程] 启动 - 鼠标移动 + 随机按键 (启用: {antiDetectConfig.EnableRandomKeys})");
                var random = new Random();

                // 获取初始鼠标位置
                POINT lastPos = new POINT();
                GetCursorPos(out lastPos);
                int idleSeconds = 0;

                // 随机按键池（Z, 1, 2, 3, 5）
                string[] randomKeys = { "Z", "1", "2", "3", "5" };

                // 下次按键时间（从配置读取）
                int nextKeyPressTime = random.Next(antiDetectConfig.RandomKeyMinInterval, antiDetectConfig.RandomKeyMaxInterval + 1);
                int keyPressTimer = 0;

                while (!_shouldStop)
                {
                    try
                    {
                        // ========== 状态检查 ==========
                        // 只在战斗状态时处理 AFK
                        if (CurrentState != GameState.InBattle)
                        {
                            // 只在第一次进入非战斗状态时输出日志，避免日志刷屏
                            if (!_hasLoggedNonBattleState)
                            {
                                Console.WriteLine($"[防AFK] 当前状态 {CurrentState}，暂停 AFK 处理（仅战斗状态启用）");
                                _hasLoggedNonBattleState = true;
                            }
                            Thread.Sleep(1000); // 等待1秒后重新检查
                            continue;
                        }
                        else
                        {
                            // 重置日志标志
                            _hasLoggedNonBattleState = false;
                        }

                        // ========== 鼠标移动检测 ==========
                        POINT currentPos = new POINT();
                        GetCursorPos(out currentPos);

                        // 检查鼠标是否移动
                        if (currentPos.X == lastPos.X && currentPos.Y == lastPos.Y)
                        {
                            idleSeconds++;

                            // 从配置读取移动阈值（随机间隔）
                            int moveThreshold = random.Next(antiDetectConfig.MouseMoveMinInterval, antiDetectConfig.MouseMoveMaxInterval + 1);
                            if (idleSeconds >= moveThreshold)
                            {
                                // 从配置读取移动像素范围
                                int maxPixels = antiDetectConfig.MouseMoveMaxPixels;
                                int deltaX = random.Next(-maxPixels, maxPixels + 1);
                                int deltaY = random.Next(-maxPixels, maxPixels + 1);

                                // 确保至少移动1像素
                                if (deltaX == 0 && deltaY == 0)
                                    deltaX = random.Next(0, 2) == 0 ? -1 : 1;

                                int newX = currentPos.X + deltaX;
                                int newY = currentPos.Y + deltaY;

                                // 限制在屏幕范围内
                                newX = Math.Max(0, Math.Min(GetSystemMetrics(0) - 1, newX));
                                newY = Math.Max(0, Math.Min(GetSystemMetrics(1) - 1, newY));

                                // 移动鼠标
                                SetCursorPos(newX, newY);

                                // 验证移动
                                Thread.Sleep(50);
                                POINT verifyPos = new POINT();
                                GetCursorPos(out verifyPos);

                                // 重置计数
                                idleSeconds = 0;
                                lastPos = verifyPos;
                            }
                        }
                        else
                        {
                            // 鼠标移动了，重置计数
                            idleSeconds = 0;
                            lastPos = currentPos;
                        }

                        // ========== 随机按键发送（绕过键盘指纹检测）==========
                        if (antiDetectConfig.EnableRandomKeys)
                        {
                            keyPressTimer++;

                            if (keyPressTimer >= nextKeyPressTime && _controller != null)
                            {
                                // 只在战斗状态时发送随机按键
                                if (CurrentState != GameState.InBattle)
                                {
                                    // 重置计时器但不发送按键
                                    keyPressTimer = 0;
                                    nextKeyPressTime = random.Next(antiDetectConfig.RandomKeyMinInterval, antiDetectConfig.RandomKeyMaxInterval + 1);
                                    continue;
                                }
                                // 随机选择 1-3 个按键
                                int keyCount = random.Next(1, 4);
                                var selectedKeys = new List<string>();

                                for (int i = 0; i < keyCount; i++)
                                {
                                    string key = randomKeys[random.Next(randomKeys.Length)];
                                    selectedKeys.Add(key);
                                }

                                // 发送按键序列（随机间隔）
                                foreach (var key in selectedKeys)
                                {
                                    // 随机按键持续时间 (50-150ms)
                                    double holdTime = 0.05 + random.NextDouble() * 0.1;
                                    _controller.SendKey(key, holdTime);

                                    // 随机间隔 (100-500ms)
                                    int interval = random.Next(100, 501);
                                    Thread.Sleep(interval);
                                }

                                Console.WriteLine($"[防AFK] 发送随机按键: {string.Join(", ", selectedKeys)}");

                                // 重置计时器，从配置读取下次间隔
                                keyPressTimer = 0;
                                nextKeyPressTime = random.Next(antiDetectConfig.RandomKeyMinInterval, antiDetectConfig.RandomKeyMaxInterval + 1);
                            }
                        }

                        // 每秒检查一次
                        Thread.Sleep(1000);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[防AFK线程] 错误: {ex.Message}");
                        Thread.Sleep(1000);
                    }
                }

                Console.WriteLine("[防AFK线程] 停止");
            })
            {
                IsBackground = true,
                Name = "Anti-AFK Thread"
            };

            _antiAfkThread.Start();
        }

        // Windows API for mouse position
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        /// <summary>
        /// 获取最新的 OCR 结果（线程安全）
        /// </summary>
        private OcrEngine.OcrResult? GetLatestOcrResult()
        {
            lock (_ocrResultLock)
            {
                return _latestOcrResult;
            }
        }

        // 当前状态的取消令牌
        private CancellationTokenSource? _currentStateCts;
        private GameState? _nextState;

        /// <summary>
        /// 状态转换请求回调（供 StateContext 使用）
        /// </summary>
        private void RequestStateTransition(GameState newState)
        {
            if (_nextState == null) // 避免重复设置
            {
                _nextState = newState;
                _currentStateCts?.Cancel(); // 中断当前状态执行
            }
        }

        private async Task MainLoopAsync(CancellationToken ct)
        {
            Console.WriteLine("=== 主循环启动 ===");

            // 设置 OCR 完成回调
            _ocrCompletedCallback = (ocr) =>
            {
                // 检查状态超时（ForceExiting 状态不检查超时）
                if (CurrentState != GameState.ForceExiting &&
                    (DateTime.Now - _stateStartTime).TotalSeconds > _config.Timeouts.StateTimeout)
                {
                    Console.WriteLine($"⚠ 状态超时 ({CurrentState})，强制退出深渊");
                    _nextState = GameState.ForceExiting;
                    _currentStateCts?.Cancel();
                    return;
                }

                var newState = StateDecider(ocr, CurrentState);
                if (newState != null && newState != CurrentState)
                {
                    var allText = string.Join(", ", ocr.Regions.Select(r => r.Text).Take(5));
                    Console.WriteLine($"  [状态中断] OCR 检测到 {CurrentState} → {newState}");
                    Console.WriteLine($"  [OCR文字] {allText}...");
                    _nextState = newState;
                    _currentStateCts?.Cancel();
                }
            };
            while (!_shouldStop)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // 激活窗口（仅在前台模式需要）
                    if (_controller != null && !_controller.BackgroundMode)
                        _controller.Activate();

                    // 检查状态超时（ForceExiting 状态不检查超时）
                    if (CurrentState != GameState.ForceExiting &&
                        (DateTime.Now - _stateStartTime).TotalSeconds > _config.Timeouts.StateTimeout)
                    {
                        Console.WriteLine($"⚠ 状态超时 ({CurrentState})，强制退出深渊");
                        TransitionTo(GameState.ForceExiting);
                    }

                    // 获取最新 OCR 结果
                    var ocrResult = GetLatestOcrResult();

                    // 为当前状态创建取消令牌
                    _currentStateCts = new CancellationTokenSource();
                    _nextState = null;

                    // 根据当前状态执行对应逻辑
                    IStateHandler? currentHandler = null;
                    try
                    {
                        currentHandler = GetStateHandler(CurrentState);
                        if (currentHandler != null && _stateContext != null)
                        {
                            await currentHandler.ExecuteAsync(_stateContext, ocrResult, _currentStateCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        if(ct.IsCancellationRequested)
                            return;
                        // 状态被中断，调用清理
                        if (currentHandler != null && _stateContext != null)
                            currentHandler.Cleanup(_stateContext);

                        // 转换到新状态
                        if (_nextState != null)
                            TransitionTo(_nextState.Value);
                    }
                    finally
                    {
                        _currentStateCts?.Dispose();
                        _currentStateCts = null;
                    }

                    // 状态执行完成后，检查是否需要转换状态
                    var finalOcrResult = GetLatestOcrResult();
                    if (finalOcrResult != null)
                    {
                        var newState = StateDecider(finalOcrResult, CurrentState);
                        if (newState != null && newState != CurrentState)
                        {
                            Console.WriteLine($"[状态转换] {CurrentState} → {newState}");
                            TransitionTo(newState.Value);
                        }
                    }
                    await Task.Delay(100, ct);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"主循环错误: {ex.Message}");
                    Console.WriteLine($"堆栈跟踪:\n{ex.StackTrace}");
                    await Task.Delay(1000, ct);
                }
            }
        }

        private IStateHandler? GetStateHandler(GameState state)
        {
            // 使用缓存的状态处理器，保持实例状态
            if (_stateHandlers.TryGetValue(state, out var handler))
            {
                return handler;
            }

            // 创建新的状态处理器并缓存
            handler = state switch
            {
                GameState.MainMenu => new MainMenuState(),
                GameState.SelectingBuff => new SelectingBuffState(),
                GameState.SelectingCandle => new SelectingCandleState(),
                GameState.SelectingRelic => new SelectingRelicState(),
                GameState.Navigating => new NavigatingState(),
                GameState.InBattle => new InBattleState(),
                GameState.Reviving => new RevivingState(),
                GameState.ExploreDetails => new ExploreDetailsState(),
                GameState.InteractingFireMechanism => new InteractingFireMechanismState(),
                GameState.ClosingUI => new ClosingUIState(),
                GameState.ForceExiting => new ForceExitingState(),
                _ => null
            };

            if (handler != null)
            {
                _stateHandlers[state] = handler;
            }

            return handler;
        }

        /// <summary>
        /// 重置指定状态的处理器（清除其内部状态）
        /// </summary>
        private void ResetStateHandler(GameState state)
        {
            _stateHandlers.Remove(state);
        }

        /// <summary>
        /// 重置所有状态处理器
        /// </summary>
        private void ResetAllStateHandlers()
        {
            _stateHandlers.Clear();
        }

        private void TransitionTo(GameState newState)
        {
            if (CurrentState != newState)
            {
                Console.WriteLine($"[状态转换] {CurrentState} → {newState}");
                CurrentState = newState;
                _stateStartTime = DateTime.Now;
            }
        }

        private void Cleanup()
        {
            Console.WriteLine("=== 清理资源 ===");
            _shouldStop = true;
            _ocrThread?.Join(2000);
            _antiAfkThread?.Join(2000);
            _ocrEngine?.Dispose();
            _controller?.Dispose();
            Console.WriteLine("✓ 清理完成");
        }
    }
}
