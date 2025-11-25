using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace MMP
{
    public partial class MainWindow : Window
    {
        private AppConfig _config;
        private AutoAbyssStateMachine? _stateMachine;
        private Thread? _hotkeyThread;
        private volatile bool _shouldStopHotkey = false;
        
        // Windows API for hotkey detection
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public MainWindow()
        {
            InitializeComponent();
            _config = AppConfig.Load();
            LoadConfig();
            
            // 重定向控制台输出到日志窗口
            Console.SetOut(new TextBoxWriter(LogTextBox));
            
            // 启动热键监听
            StartHotkeyMonitor();
        }
        
        private void StartHotkeyMonitor()
        {
            _hotkeyThread = new Thread(() =>
            {
                Console.WriteLine($"[热键监听] 已启动 - {_config.Hotkeys.ForceExit}: 停止运行, {_config.Hotkeys.ForceExitAbyss}: 强制退出深渊");
                
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
                        
                        // 停止运行（不关闭程序）
                        if (currentStopState && !lastStopState)
                        {
                            Dispatcher.Invoke(async () =>
                            {
                                Console.WriteLine($"\n[{_config.Hotkeys.ForceExit}] 停止运行");
                                if (_stateMachine != null)
                                {
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
                                    Console.WriteLine("程序未在运行");
                                }
                            });
                        }
                        
                        // 强制退出深渊
                        if (currentForceExitAbyssState && !lastForceExitAbyssState)
                        {
                            Dispatcher.Invoke(() =>
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
        
        private int GetVirtualKeyCode(string keyName)
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

            // 设置热键输入框的键盘事件
            ForceExitHotkeyTextBox.PreviewKeyDown += HotkeyTextBox_PreviewKeyDown;
            ForceExitAbyssHotkeyTextBox.PreviewKeyDown += HotkeyTextBox_PreviewKeyDown;
        }

        private void HotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;
            var textBox = sender as System.Windows.Controls.TextBox;
            if (textBox != null)
            {
                textBox.Text = e.Key.ToString();
            }
        }

        private void SaveConfigButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 保存热键配置
                _config.Hotkeys.ForceExit = ForceExitHotkeyTextBox.Text;
                _config.Hotkeys.ForceExitAbyss = ForceExitAbyssHotkeyTextBox.Text;

                // 保存超时配置
                _config.Timeouts.StateTimeout = int.Parse(StateTimeoutTextBox.Text);

                // 保存战斗配置
                _config.Battle.MonsterDetectionRange = float.Parse(MonsterDetectionRangeTextBox.Text);
                _config.Battle.ApproachDistance = float.Parse(ApproachDistanceTextBox.Text);
                _config.Battle.QSkillInterval = int.Parse(QSkillIntervalTextBox.Text);
                _config.Battle.ESkillCount = int.Parse(ESkillCountTextBox.Text);
                _config.Battle.ESkillInterval = int.Parse(ESkillIntervalTextBox.Text);
                _config.Battle.AttackInterval = int.Parse(AttackIntervalTextBox.Text);
                _config.Battle.AttackCount = int.Parse(AttackCountTextBox.Text);
                _config.Battle.AttackRecoveryDelay = int.Parse(AttackRecoveryDelayTextBox.Text);

                // 保存 OCR 配置
                _config.Ocr.OcrInterval = int.Parse(OcrIntervalTextBox.Text);
                _config.Ocr.ConfidenceThreshold = float.Parse(OcrConfidenceTextBox.Text);
                _config.Ocr.MinTextLength = int.Parse(OcrMinTextLengthTextBox.Text);
                _config.Ocr.ShowRecognitionResults = ShowOcrResultsCheckBox.IsChecked ?? false;

                _config.Save();
                InfoText.Text = "配置已保存";
                MessageBox.Show("配置保存成功！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存配置失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_stateMachine != null)
            {
                MessageBox.Show("程序已在运行中", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                MessageBox.Show($"运行错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private async void StopButton_Click(object sender, RoutedEventArgs e)
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

        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
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
        private readonly System.Windows.Controls.TextBox _textBox;
        private readonly System.Text.StringBuilder _buffer = new();
        private readonly System.Threading.Timer _flushTimer;
        private readonly object _lock = new();

        public TextBoxWriter(System.Windows.Controls.TextBox textBox)
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

            _textBox.Dispatcher.Invoke(() =>
            {
                _textBox.AppendText(text);
                // 强制滚动到底部
                _textBox.CaretIndex = _textBox.Text.Length;
                _textBox.ScrollToEnd();
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
}
