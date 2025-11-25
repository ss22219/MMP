using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
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
            Interacting,               // 交互中
            InteractingFireMechanism,  // 簧火机关交互
            ClosingUI,                 // 关闭UI
            ForceExiting,              // 强制退出深渊
            Error                      // 错误状态
        }

        private GameState _currentState = GameState.MainMenu;
        private readonly object _stateLock = new object();
        
        // OCR 线程相关
        private Thread? _ocrThread;
        private volatile bool _shouldStop = false;
        private OcrEngine.OcrResult? _latestOcrResult = null;
        private readonly object _ocrResultLock = new object();
        
        // OCR 完成事件（只定义一次）
        private event Action<OcrEngine.OcrResult>? OnOcrCompleted;
        
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
        private SingleThreadSyncContext? _syncContext;
        
        public GameState CurrentState
        {
            get { lock (_stateLock) { return _currentState; } }
            private set { lock (_stateLock) { _currentState = value; } }
        }

        public void Run()
        {
            if (!Initialize())
                return;

            StartOcrThread();
            StartExitMonitorThread();
            
            // 如果当前有同步上下文（WPF Dispatcher），直接使用它
            var currentContext = SynchronizationContext.Current;
            if (currentContext != null)
            {
                // 在当前同步上下文（WPF UI 线程）上运行
                currentContext.Post(async _ =>
                {
                    await MainLoopAsync();
                }, null);
            }
            else
            {
                // 没有同步上下文，使用自定义的单线程上下文
                _syncContext = new SingleThreadSyncContext();
                SynchronizationContext.SetSynchronizationContext(_syncContext);

                _syncContext.Post(async _ =>
                {
                    await MainLoopAsync();
                    _syncContext.Complete();
                }, null);

                _syncContext.RunOnCurrentThread();
                Cleanup();
            }
        }
        
        /// <summary>
        /// 异步运行状态机（用于 WPF）
        /// </summary>
        public async Task RunAsync()
        {
            if (!Initialize())
                return;

            StartOcrThread();
            StartExitMonitorThread();
            
            // 在后台线程运行主循环，避免阻塞 UI
            await Task.Run(async () =>
            {
                await MainLoopAsync();
            });
            
            Cleanup();
        }

        /// <summary>
        /// 停止状态机
        /// </summary>
        public void Stop()
        {
            Console.WriteLine("[状态机] 正在停止...");
            _shouldStop = true;
            _currentStateCts?.Cancel();
            _currentWaitCts?.Cancel();
            
            // 完成同步上下文，让 RunOnCurrentThread 退出
            _syncContext?.Complete();
        }

        // 热键监听已移至 UI 层，此方法保留为空以保持兼容性
        private void StartExitMonitorThread()
        {
            // 热键功能现在由 WPF UI 处理
        }

        /// <summary>
        /// 强制退出深渊（切换到 ForceExiting 状态）
        /// </summary>
        private void PerformForceExitAbyss()
        {
            Console.WriteLine("  → 切换到强制退出状态");
            
            // 取消当前状态
            _currentStateCts?.Cancel();
            
            // 切换到强制退出状态
            TransitionTo(GameState.ForceExiting);
            
            // 重置所有状态处理器（清除内部状态）
            ResetAllStateHandlers();
        }

        private bool Initialize()
        {
            Console.WriteLine("=== 深渊自动化状态机 ===");
            Console.WriteLine("按 F10 强制退出");
            Console.WriteLine();

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
                handler => OnOcrCompleted += handler,
                handler => OnOcrCompleted -= handler
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
                        lastOcrTime = DateTime.Now;

                        using var screenshot = ScreenCapture.CaptureWindow(_hwnd);
                        if (screenshot != null && _ocrEngine != null)
                        {
                            ocrCount++;
                            
                            var result = _ocrEngine.Recognize(screenshot);
                            if (result != null && result.Regions != null)
                            {
                                // 更新最新的 OCR 结果（线程安全）
                                lock (_ocrResultLock)
                                {
                                    _latestOcrResult = result;
                                }
                                
                                // 触发 OCR 完成事件
                                OnOcrCompleted?.Invoke(result);
                            }
                        }

                        // 计算本次 OCR 耗时（可选：用于监控性能）
                        var ocrDuration = (DateTime.Now - lastOcrTime).TotalMilliseconds;
                        if (ocrDuration > ocrIntervalMs * 2)
                        {
                            Console.WriteLine($"[OCR线程] 警告: OCR 耗时 {ocrDuration:F0}ms，超过间隔时间");
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

        private async Task MainLoopAsync()
        {
            Console.WriteLine("=== 主循环启动 ===");
            
            while (!_shouldStop)
            {
                try
                {
                    _controller!.Activate();
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
                    
                    // 【临时禁用】订阅 OCR 事件，检测状态变化
                    // 让状态有机会完整执行，避免被立即中断
                    Action<OcrEngine.OcrResult>? stateChangeHandler = null;
                    
                 
                    stateChangeHandler = (ocr) =>
                    {
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
                    OnOcrCompleted += stateChangeHandler;
                    

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
                        // 状态被中断，调用清理
                        if (currentHandler != null && _stateContext != null)
                        {
                            Console.WriteLine($"  [状态中断] 清理 {CurrentState}");
                            currentHandler.Cleanup(_stateContext);
                        }
                        
                        // 转换到新状态
                        if (_nextState != null)
                        {
                            TransitionTo(_nextState.Value);
                        }
                    }
                    finally
                    {
                        // 清理
                        if (stateChangeHandler != null)
                            OnOcrCompleted -= stateChangeHandler;
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

                    Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"主循环错误: {ex.Message}");
                    Console.WriteLine($"堆栈跟踪:\n{ex.StackTrace}");
                    Thread.Sleep(1000);
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
                GameState.Interacting => new InteractingState(),
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
            _ocrEngine?.Dispose();
            _controller?.Dispose();
            Console.WriteLine("✓ 清理完成");
        }

        // Main 方法已移至 App.xaml.cs（WPF 入口点）
        // 如果需要控制台模式，可以取消注释以下代码：
        // static void Main(string[] args)
        // {
        //     var stateMachine = new AutoAbyssStateMachine();
        //     stateMachine.Run();
        // }
    }
    
    public class SingleThreadSyncContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback, object?)> _queue = new();

        public void RunOnCurrentThread()
        {
            foreach (var workItem in _queue.GetConsumingEnumerable())
                workItem.Item1(workItem.Item2);
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Add((d, state));
        }

        public void Complete() => _queue.CompleteAdding();
    }
}
