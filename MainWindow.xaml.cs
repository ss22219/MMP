using System;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace MMP;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private AutoAbyssStateMachine? _stateMachine;
    private CancellationTokenSource? _runningCts;
    private CancellationToken? _stopingToken;

    // Windows API for global keyboard hook
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _hookCallback;
    private IntPtr _hookId = IntPtr.Zero;

    // UI Controls
    private Button StartButton = null!;
    private Button StopButton = null!;
    private TextBlock StatusText = null!;
    private TextBlock InfoText = null!;
    private TextBox LogTextBox = null!;
    private TextBox ForceExitHotkeyTextBox = null!;
    private TextBox StateTimeoutTextBox = null!;
    private TextBox MonsterDetectionRangeTextBox = null!;
    private TextBox ApproachDistanceTextBox = null!;
    private TextBox QSkillIntervalTextBox = null!;
    private TextBox ESkillCountTextBox = null!;
    private TextBox ESkillIntervalTextBox = null!;
    private TextBox AttackIntervalTextBox = null!;
    private TextBox AttackCountTextBox = null!;
    private TextBox AttackRecoveryDelayTextBox = null!;
    private TextBox OcrIntervalTextBox = null!;
    private TextBox OcrConfidenceTextBox = null!;
    private TextBox OcrMinTextLengthTextBox = null!;
    private CheckBox ShowOcrResultsCheckBox = null!;
    private TextBox SimpleJumpDistanceTextBox = null!;
    private TextBox InteractDistanceTextBox = null!;
    private TextBox NormalMoveDistanceTextBox = null!;
    private TextBox SprintDistanceTextBox = null!;
    private TextBox TooFarDistanceTextBox = null!;
    private TextBox HeightDiffJumpThresholdTextBox = null!;

    // 防检测配置控件
    private CheckBox EnableRandomKeysCheckBox = null!;
    private TextBox RandomKeyMinIntervalTextBox = null!;
    private TextBox RandomKeyMaxIntervalTextBox = null!;
    private TextBox MouseMoveMinIntervalTextBox = null!;
    private TextBox MouseMoveMaxIntervalTextBox = null!;
    private TextBox MouseMoveMinPixelsTextBox = null!;
    private TextBox MouseMoveMaxPixelsTextBox = null!;

    public MainWindow()
    {
        InitializeComponent();
        _config = AppConfig.Load();

        // 初始化控件引用
        InitializeControls();

        // 加载配置
        LoadConfig();

        // 重定向控制台输出到日志窗口
        Console.SetOut(new TextBoxWriter(LogTextBox));

        // 启动热键监听（延迟到窗口加载完成后）
        this.Opened += (s, e) => StartHotkeyMonitor();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void InitializeControls()
    {
        StartButton = this.FindControl<Button>("StartButton")!;
        StopButton = this.FindControl<Button>("StopButton")!;
        StatusText = this.FindControl<TextBlock>("StatusText")!;
        InfoText = this.FindControl<TextBlock>("InfoText")!;
        LogTextBox = this.FindControl<TextBox>("LogTextBox")!;
        ForceExitHotkeyTextBox = this.FindControl<TextBox>("ForceExitHotkeyTextBox")!;
        StateTimeoutTextBox = this.FindControl<TextBox>("StateTimeoutTextBox")!;
        MonsterDetectionRangeTextBox = this.FindControl<TextBox>("MonsterDetectionRangeTextBox")!;
        ApproachDistanceTextBox = this.FindControl<TextBox>("ApproachDistanceTextBox")!;
        QSkillIntervalTextBox = this.FindControl<TextBox>("QSkillIntervalTextBox")!;
        ESkillCountTextBox = this.FindControl<TextBox>("ESkillCountTextBox")!;
        ESkillIntervalTextBox = this.FindControl<TextBox>("ESkillIntervalTextBox")!;
        AttackIntervalTextBox = this.FindControl<TextBox>("AttackIntervalTextBox")!;
        AttackCountTextBox = this.FindControl<TextBox>("AttackCountTextBox")!;
        AttackRecoveryDelayTextBox = this.FindControl<TextBox>("AttackRecoveryDelayTextBox")!;
        OcrIntervalTextBox = this.FindControl<TextBox>("OcrIntervalTextBox")!;
        OcrConfidenceTextBox = this.FindControl<TextBox>("OcrConfidenceTextBox")!;
        OcrMinTextLengthTextBox = this.FindControl<TextBox>("OcrMinTextLengthTextBox")!;
        ShowOcrResultsCheckBox = this.FindControl<CheckBox>("ShowOcrResultsCheckBox")!;
        SimpleJumpDistanceTextBox = this.FindControl<TextBox>("SimpleJumpDistanceTextBox")!;
        InteractDistanceTextBox = this.FindControl<TextBox>("InteractDistanceTextBox")!;
        NormalMoveDistanceTextBox = this.FindControl<TextBox>("NormalMoveDistanceTextBox")!;
        SprintDistanceTextBox = this.FindControl<TextBox>("SprintDistanceTextBox")!;
        TooFarDistanceTextBox = this.FindControl<TextBox>("TooFarDistanceTextBox")!;
        HeightDiffJumpThresholdTextBox = this.FindControl<TextBox>("HeightDiffJumpThresholdTextBox")!;

        // 防检测配置控件
        EnableRandomKeysCheckBox = this.FindControl<CheckBox>("EnableRandomKeysCheckBox")!;
        RandomKeyMinIntervalTextBox = this.FindControl<TextBox>("RandomKeyMinIntervalTextBox")!;
        RandomKeyMaxIntervalTextBox = this.FindControl<TextBox>("RandomKeyMaxIntervalTextBox")!;
        MouseMoveMinIntervalTextBox = this.FindControl<TextBox>("MouseMoveMinIntervalTextBox")!;
        MouseMoveMaxIntervalTextBox = this.FindControl<TextBox>("MouseMoveMaxIntervalTextBox")!;
        MouseMoveMinPixelsTextBox = this.FindControl<TextBox>("MouseMoveMinPixelsTextBox")!;
        MouseMoveMaxPixelsTextBox = this.FindControl<TextBox>("MouseMoveMaxPixelsTextBox")!;

        var saveConfigButton = this.FindControl<Button>("SaveConfigButton")!;
        var clearLogButton = this.FindControl<Button>("ClearLogButton")!;

        // 连接事件处理器
        StartButton.Click += async (s, e) => await StartButton_Click(s, e);
        StopButton.Click += StopButton_Click;
        saveConfigButton.Click += SaveConfigButton_Click;
        clearLogButton.Click += ClearLogButton_Click;
    }

    private void StartHotkeyMonitor()
    {
        try
        {
            Console.WriteLine($"[全局热键] 正在启动 - {_config.Hotkeys.ForceExit}: 启动/停止");

            _hookCallback = HookCallback;
            var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            var curModule = curProcess.MainModule;
            if (curModule != null)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, GetModuleHandle(curModule.ModuleName), 0);
                if (_hookId == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    Console.WriteLine($"[全局热键] 警告: 启用失败 (错误代码: {error})");
                }
                else
                {
                    Console.WriteLine("[全局热键] ✓ 启用成功");
                }
            }
            else
            {
                Console.WriteLine("[全局热键] 警告: 无法获取主模块");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[全局热键] 错误: {ex.Message}");
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            int stopKey = GetVirtualKeyCode(_config.Hotkeys.ForceExit);

            // 启动/停止切换
            if (vkCode == stopKey)
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    if (_stateMachine != null)
                    {
                        StopButton_Click(null, null);
                    }
                    else
                    {
                        await StartButton_Click(null, null);
                    }
                });
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private static int GetVirtualKeyCode(string keyName)
    {
        // 将按键名称转换为虚拟键码
        return keyName.ToUpper() switch
        {
            "F1" => 0x70,
            "F2" => 0x71,
            "F3" => 0x72,
            "F4" => 0x73,
            "F5" => 0x74,
            "F6" => 0x75,
            "F7" => 0x76,
            "F8" => 0x77,
            "F9" => 0x78,
            "F10" => 0x79,
            "F11" => 0x7A,
            "F12" => 0x7B,
            _ => 0x79 // 默认 F10
        };
    }

    private void LoadConfig()
    {
        // 加载热键配置
        ForceExitHotkeyTextBox.Text = _config.Hotkeys.ForceExit;

        // 加载超时配置
        StateTimeoutTextBox.Text = _config.Timeouts.StateTimeout.ToString();

        // 加载战斗配置
        var battle = _config.Battle;
        MonsterDetectionRangeTextBox.Text = battle.MonsterDetectionRange.ToString();
        ApproachDistanceTextBox.Text = battle.ApproachDistance.ToString();
        QSkillIntervalTextBox.Text = battle.QSkillInterval.ToString();
        ESkillCountTextBox.Text = battle.ESkillCount.ToString();
        ESkillIntervalTextBox.Text = battle.ESkillInterval.ToString();
        AttackIntervalTextBox.Text = battle.AttackInterval.ToString();
        AttackCountTextBox.Text = battle.AttackCount.ToString();
        AttackRecoveryDelayTextBox.Text = battle.AttackRecoveryDelay.ToString();

        // 加载 OCR 配置
        OcrIntervalTextBox.Text = _config.Ocr.OcrInterval.ToString();
        OcrConfidenceTextBox.Text = _config.Ocr.ConfidenceThreshold.ToString();
        OcrMinTextLengthTextBox.Text = _config.Ocr.MinTextLength.ToString();
        ShowOcrResultsCheckBox.IsChecked = _config.Ocr.ShowRecognitionResults;

        // 加载导航配置
        SimpleJumpDistanceTextBox.Text = _config.Movement.SimpleJumpDistance.ToString();
        InteractDistanceTextBox.Text = _config.Movement.InteractDistance.ToString();
        NormalMoveDistanceTextBox.Text = _config.Movement.NormalMoveDistance.ToString();
        SprintDistanceTextBox.Text = _config.Movement.SprintDistance.ToString();
        TooFarDistanceTextBox.Text = _config.Movement.TooFarWarningDistance.ToString();
        HeightDiffJumpThresholdTextBox.Text = _config.Movement.HeightDiffJumpThreshold.ToString();

        // 加载防检测配置
        EnableRandomKeysCheckBox.IsChecked = _config.AntiDetection.EnableRandomKeys;
        RandomKeyMinIntervalTextBox.Text = _config.AntiDetection.RandomKeyMinInterval.ToString();
        RandomKeyMaxIntervalTextBox.Text = _config.AntiDetection.RandomKeyMaxInterval.ToString();
        MouseMoveMinIntervalTextBox.Text = _config.AntiDetection.MouseMoveMinInterval.ToString();
        MouseMoveMaxIntervalTextBox.Text = _config.AntiDetection.MouseMoveMaxInterval.ToString();
        MouseMoveMinPixelsTextBox.Text = _config.AntiDetection.MouseMoveMinPixels.ToString();
        MouseMoveMaxPixelsTextBox.Text = _config.AntiDetection.MouseMoveMaxPixels.ToString();

        // 设置热键输入框的键盘事件
        ForceExitHotkeyTextBox.KeyDown += HotkeyTextBox_KeyDown;
    }

    private void HotkeyTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (sender is TextBox textBox)
        {
            textBox.Text = e.Key.ToString();
        }
    }

    private void SaveConfigButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            // 保存热键配置
            _config.Hotkeys.ForceExit = ForceExitHotkeyTextBox.Text ?? "F10";

            // 保存超时配置
            _config.Timeouts.StateTimeout = int.Parse(StateTimeoutTextBox.Text ?? "60");

            // 保存战斗配置
            _config.Battle.MonsterDetectionRange = float.Parse(MonsterDetectionRangeTextBox.Text ?? "30000");
            _config.Battle.ApproachDistance = float.Parse(ApproachDistanceTextBox.Text ?? "3000");
            _config.Battle.QSkillInterval = int.Parse(QSkillIntervalTextBox.Text ?? "5000");
            _config.Battle.ESkillCount = int.Parse(ESkillCountTextBox.Text ?? "3");
            _config.Battle.ESkillInterval = int.Parse(ESkillIntervalTextBox.Text ?? "200");
            _config.Battle.AttackInterval = int.Parse(AttackIntervalTextBox.Text ?? "100");
            _config.Battle.AttackCount = int.Parse(AttackCountTextBox.Text ?? "5");
            _config.Battle.AttackRecoveryDelay = int.Parse(AttackRecoveryDelayTextBox.Text ?? "500");

            // 保存 OCR 配置
            _config.Ocr.OcrInterval = int.Parse(OcrIntervalTextBox.Text ?? "500");
            _config.Ocr.ConfidenceThreshold = float.Parse(OcrConfidenceTextBox.Text ?? "0.5");
            _config.Ocr.MinTextLength = int.Parse(OcrMinTextLengthTextBox.Text ?? "2");
            _config.Ocr.ShowRecognitionResults = ShowOcrResultsCheckBox.IsChecked ?? false;

            // 保存导航配置
            _config.Movement.SimpleJumpDistance = float.Parse(SimpleJumpDistanceTextBox.Text ?? "500");
            _config.Movement.InteractDistance = float.Parse(InteractDistanceTextBox.Text ?? "350");
            _config.Movement.NormalMoveDistance = float.Parse(NormalMoveDistanceTextBox.Text ?? "600");
            _config.Movement.SprintDistance = float.Parse(SprintDistanceTextBox.Text ?? "2000");
            _config.Movement.TooFarWarningDistance = float.Parse(TooFarDistanceTextBox.Text ?? "20000");
            _config.Movement.HeightDiffJumpThreshold = float.Parse(HeightDiffJumpThresholdTextBox.Text ?? "50");

            // 保存防检测配置
            _config.AntiDetection.EnableRandomKeys = EnableRandomKeysCheckBox.IsChecked ?? true;
            _config.AntiDetection.RandomKeyMinInterval = int.Parse(RandomKeyMinIntervalTextBox.Text ?? "3");
            _config.AntiDetection.RandomKeyMaxInterval = int.Parse(RandomKeyMaxIntervalTextBox.Text ?? "6");
            _config.AntiDetection.MouseMoveMinInterval = int.Parse(MouseMoveMinIntervalTextBox.Text ?? "3");
            _config.AntiDetection.MouseMoveMaxInterval = int.Parse(MouseMoveMaxIntervalTextBox.Text ?? "7");
            _config.AntiDetection.MouseMoveMinPixels = int.Parse(MouseMoveMinPixelsTextBox.Text ?? "1");
            _config.AntiDetection.MouseMoveMaxPixels = int.Parse(MouseMoveMaxPixelsTextBox.Text ?? "5");

            _config.Save();
            InfoText.Text = "配置已保存";

            var messageBox = new Window
            {
                Title = "提示",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = "配置保存成功！", Margin = new Thickness(0, 20, 0, 20) },
                        new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };

            var button = ((StackPanel)messageBox.Content).Children[1] as Button;
            if (button != null)
            {
                button.Click += (s, args) => messageBox.Close();
            }

            messageBox.ShowDialog(this);
        }
        catch (Exception ex)
        {
            var errorBox = new Window
            {
                Title = "错误",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = $"保存配置失败: {ex.Message}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 20) },
                        new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };

            var button = ((StackPanel)errorBox.Content).Children[1] as Button;
            if (button != null)
            {
                button.Click += (s, args) => errorBox.Close();
            }

            errorBox.ShowDialog(this);
        }
    }

    private async Task StartButton_Click(object? sender, RoutedEventArgs? e)
    {
        if (_stateMachine != null)
        {
            var warningBox = new Window
            {
                Title = "提示",
                Width = 300,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = "程序已在运行中", Margin = new Thickness(0, 20, 0, 20) },
                        new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };

            var button = ((StackPanel)warningBox.Content).Children[1] as Button;
            if (button != null)
            {
                button.Click += (s, args) => warningBox.Close();
            }

            await warningBox.ShowDialog(this);
            return;
        }

        try
        {
            _stateMachine = new AutoAbyssStateMachine();
            _runningCts = new CancellationTokenSource();
            var stopingCts = new CancellationTokenSource();
            _stopingToken = stopingCts.Token;

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            StatusText.Text = "运行中";
            InfoText.Text = "程序已启动";
            await _stateMachine.RunAsync(_runningCts.Token);
            stopingCts.Cancel();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"运行错误: {ex.Message}");

            var errorBox = new Window
            {
                Title = "错误",
                Width = 400,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Margin = new Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = $"运行错误: {ex.Message}", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 20) },
                        new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };

            var button = ((StackPanel)errorBox.Content).Children[1] as Button;
            if (button != null)
            {
                button.Click += (s, args) => errorBox.Close();
            }

            await errorBox.ShowDialog(this);
        }
        finally
        {
            _stateMachine = null;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "已停止";
            InfoText.Text = "程序已停止";
        }
    }

    private async void StopButton_Click(object? sender, RoutedEventArgs? e)
    {
        StopButton.IsEnabled = false;
        Console.WriteLine("正在停止程序...");
        InfoText.Text = "正在停止...";
        _stateMachine?.Stop();
        if (_runningCts != null)
            await _runningCts.CancelAsync();
        if (_stopingToken != null)
            try
            {
                await Task.Delay(10000, _stopingToken.Value);
            }
            catch { }
        _stateMachine = null;
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        StatusText.Text = "已停止";
        InfoText.Text = "程序已停止";
        Console.WriteLine("程序已停止");
    }

    private void ClearLogButton_Click(object? sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        // 卸载全局Hook
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            Console.WriteLine("[全局热键] 已卸载");
        }

        _stateMachine?.Stop();
        base.OnClosed(e);
    }
}

/// <summary>
/// 将控制台输出重定向到 TextBox（高性能版本，防止界面卡顿）
/// </summary>
public class TextBoxWriter : System.IO.TextWriter
{
    private readonly TextBox _textBox;
    private readonly System.Text.StringBuilder _buffer = new();
    private readonly System.Threading.Timer _flushTimer;
    private readonly object _lock = new();
    private const int MAX_LOG_LENGTH = 300; // 降低最大长度
    private const int MAX_LINES = 100; // 最大行数
    private DateTime _lastFlushTime = DateTime.MinValue;
    private int _skipCount = 0; // 跳过的日志计数

    public TextBoxWriter(TextBox textBox)
    {
        _textBox = textBox;
        // 每 500ms 刷新一次缓冲区（降低频率）
        _flushTimer = new System.Threading.Timer(_ => Flush(), null, 500, 500);
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_lock)
        {
            _buffer.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value != null)
        {
            lock (_lock)
            {
                _buffer.Append(value);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        Write(value + Environment.NewLine);
    }

    public override void Flush()
    {
        string text;
        lock (_lock)
        {
            if (_buffer.Length == 0) return;
            text = _buffer.ToString();
            _buffer.Clear();
        }

        // 限制刷新频率，避免过于频繁的UI更新
        var now = DateTime.Now;
        if ((now - _lastFlushTime).TotalMilliseconds < 200)
            return;
        _lastFlushTime = now;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var currentText = _textBox.Text ?? "";
                var currentLines = currentText.Split('\n');
                var newLines = text.Split('\n');
                
                // 合并行
                var allLines = currentLines.Concat(newLines).ToList();
                
                // 如果行数过多，只保留最新的行
                if (allLines.Count > MAX_LINES)
                {
                    _skipCount += allLines.Count - MAX_LINES;
                    allLines = allLines.TakeLast(MAX_LINES).ToList();
                    
                    // 添加跳过提示
                    if (_skipCount > 0)
                    {
                        allLines.Insert(0, $"[已跳过 {_skipCount} 行日志] ...");
                    }
                }
                
                var newText = string.Join('\n', allLines);
                
                // 最后检查总长度
                if (newText.Length > MAX_LOG_LENGTH)
                {
                    var lines = newText.Split('\n');
                    var keepLines = lines.TakeLast(MAX_LINES / 2).ToArray();
                    newText = $"[日志过长，已裁剪] ...\n{string.Join('\n', keepLines)}";
                }

                _textBox.Text = newText;
                
                // 简化滚动逻辑
                _textBox.CaretIndex = _textBox.Text.Length;
            }
            catch (Exception ex)
            {
                // 如果更新失败，清空日志重新开始
                System.Diagnostics.Debug.WriteLine($"日志更新错误: {ex.Message}");
                try
                {
                    _textBox.Text = $"[日志重置] {DateTime.Now:HH:mm:ss}\n{text}";
                    _skipCount = 0;
                }
                catch
                {
                    // 完全失败时什么都不做
                }
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _flushTimer?.Dispose();
            Flush();
        }
        base.Dispose(disposing);
    }
}
