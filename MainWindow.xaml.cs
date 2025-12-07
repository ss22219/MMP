using System;
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
    private Thread? _hotkeyThread;
    private volatile bool _shouldStopHotkey = false;
    
    // Windows API for hotkey detection
    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    // UI Controls
    private Button StartButton = null!;
    private Button StopButton = null!;
    private TextBlock StatusText = null!;
    private TextBlock InfoText = null!;
    private TextBox LogTextBox = null!;
    private TextBox ForceExitHotkeyTextBox = null!;
    private TextBox ForceExitAbyssHotkeyTextBox = null!;
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
        
        // 启动热键监听
        StartHotkeyMonitor();
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
        ForceExitAbyssHotkeyTextBox = this.FindControl<TextBox>("ForceExitAbyssHotkeyTextBox")!;
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
        _hotkeyThread = new Thread(() =>
        {
            Console.WriteLine($"[热键监听] 已启动 - {_config.Hotkeys.ForceExit}: 启动/停止, {_config.Hotkeys.ForceExitAbyss}: 强制退出深渊");
            
            bool lastStopState = false;
            bool lastForceExitAbyssState = false;
            
            while (!_shouldStopHotkey)
            {
                try
                {
                    // 获取配置的热键对应的虚拟键码
                    int stopKey = GetVirtualKeyCode(_config.Hotkeys.ForceExit);
                    int forceExitAbyssKey = GetVirtualKeyCode(_config.Hotkeys.ForceExitAbyss);
                    
                    bool currentStopState = (GetAsyncKeyState(stopKey) & 0x8000) != 0;
                    bool currentForceExitAbyssState = (GetAsyncKeyState(forceExitAbyssKey) & 0x8000) != 0;
                    
                    // F10: 启动/停止切换
                    if (currentStopState && !lastStopState)
                    {
                        Dispatcher.UIThread.Post(async () =>
                        {
                            if (_stateMachine != null)
                            {
                                // 当前正在运行 -> 停止
                                Console.WriteLine($"\n[{_config.Hotkeys.ForceExit}] 停止运行");
                                StopButton.IsEnabled = false;
                                InfoText.Text = "正在停止...";
                                
                                await Task.Run(() => _stateMachine.Stop());
                                await Task.Delay(1000);
                                
                                _stateMachine = null;
                                StartButton.IsEnabled = true;
                                StopButton.IsEnabled = false;
                                InfoText.Text = "已停止";
                                Console.WriteLine("程序已停止");
                            }
                            else
                            {
                                // 当前已停止 -> 启动
                                Console.WriteLine($"\n[{_config.Hotkeys.ForceExit}] 启动运行");
                                await StartButton_Click(null, null);
                            }
                        });
                    }
                    
                    // 强制退出深渊
                    if (currentForceExitAbyssState && !lastForceExitAbyssState)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            Console.WriteLine($"\n[{_config.Hotkeys.ForceExitAbyss}] 强制退出深渊");
                            if (_stateMachine != null)
                            {
                                // 触发强制退出深渊逻辑
                                Task.Run(() => _stateMachine.Stop());
                            }
                            else
                            {
                                Console.WriteLine("程序未在运行");
                            }
                        });
                    }
                    
                    lastStopState = currentStopState;
                    lastForceExitAbyssState = currentForceExitAbyssState;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[热键监听] 错误: {ex.Message}");
                }
                
                Thread.Sleep(50);
            }
            
            Console.WriteLine("[热键监听] 已停止");
        })
        {
            IsBackground = true,
            Name = "Hotkey Monitor Thread"
        };
        
        _hotkeyThread.Start();
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
        ForceExitAbyssHotkeyTextBox.Text = _config.Hotkeys.ForceExitAbyss;

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
        ForceExitAbyssHotkeyTextBox.KeyDown += HotkeyTextBox_KeyDown;
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
            _config.Hotkeys.ForceExitAbyss = ForceExitAbyssHotkeyTextBox.Text ?? "F11";

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

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "运行中";
        InfoText.Text = "程序已启动";

        try
        {
            _stateMachine = new AutoAbyssStateMachine();
            await _stateMachine.RunAsync();
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

    private async void StopButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_stateMachine != null)
        {
            StopButton.IsEnabled = false;
            Console.WriteLine("正在停止程序...");
            InfoText.Text = "正在停止...";
            
            // 异步停止，避免阻塞 UI
            await Task.Run(() =>
            {
                _stateMachine.Stop();
            });
            
            // 等待状态机完全停止
            await Task.Delay(1000);
            
            _stateMachine = null;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
            StatusText.Text = "已停止";
            InfoText.Text = "程序已停止";
            Console.WriteLine("程序已停止");
        }
    }

    private void ClearLogButton_Click(object? sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        _shouldStopHotkey = true;
        _stateMachine?.Stop();
        _hotkeyThread?.Join(1000);
        base.OnClosed(e);
    }
}

/// <summary>
/// 将控制台输出重定向到 TextBox
/// </summary>
public class TextBoxWriter : System.IO.TextWriter
{
    private readonly TextBox _textBox;
    private readonly System.Text.StringBuilder _buffer = new();
    private readonly System.Threading.Timer _flushTimer;
    private readonly object _lock = new();

    public TextBoxWriter(TextBox textBox)
    {
        _textBox = textBox;
        // 每 100ms 刷新一次缓冲区
        _flushTimer = new System.Threading.Timer(_ => Flush(), null, 100, 100);
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

        Dispatcher.UIThread.Post(() =>
        {
            _textBox.Text += text;
            // Avalonia 中的滚动到底部
            _textBox.CaretIndex = _textBox.Text?.Length ?? 0;
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
