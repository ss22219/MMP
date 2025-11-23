using System;
using System.Collections.Generic;

/// <summary>
/// 清理辅助类 - 处理 Ctrl+C 和程序退出时的资源清理
/// </summary>
public static class CleanupHelper
{
    private static List<IDisposable> disposables = new();
    private static bool isRegistered = false;

    /// <summary>
    /// 注册需要清理的资源
    /// </summary>
    public static void Register(IDisposable disposable)
    {
        if (!isRegistered)
        {
            // 注册 Ctrl+C 处理
            Console.CancelKeyPress += OnCancelKeyPress;
            
            // 注册程序退出处理
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            
            isRegistered = true;
            Console.WriteLine("✓ 已注册清理处理器（Ctrl+C 会自动还原所有 Hook）");
        }

        disposables.Add(disposable);
    }

    /// <summary>
    /// 清理所有资源
    /// </summary>
    public static void CleanupAll()
    {
        if (disposables.Count > 0)
        {
            Console.WriteLine("\n正在清理资源...");
            
            foreach (var disposable in disposables)
            {
                try
                {
                    disposable?.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ 清理时出错: {ex.Message}");
                }
            }
            
            disposables.Clear();
            Console.WriteLine("✓ 资源清理完成");
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Console.WriteLine("\n\n检测到 Ctrl+C，正在安全退出...");
        e.Cancel = true; // 取消默认的终止行为
        
        CleanupAll();
        Environment.Exit(0);
    }

    private static void OnProcessExit(object? sender, EventArgs e)
    {
        CleanupAll();
    }
}
