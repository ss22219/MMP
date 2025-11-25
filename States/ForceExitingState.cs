using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 强制退出深渊状态处理器
    /// </summary>
    public class ForceExitingState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (context.Controller == null)
                return;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [强制退出深渊]");

            try
            {
                var (windowWidth, windowHeight) = WindowHelper.GetWindowSize(context.WindowHandle);

                // 按ESC打开菜单
                Console.WriteLine("  → 按 ESC 打开菜单");
                context.Controller.SendKey("ESCAPE", 0.1);
                await context.DelayAsync(1000, ct);

                // 点击"放弃"按钮（窗口中心偏下）
                Console.WriteLine("  → 点击 [退出]");
                if(!await context.WaitAndClickAsync("退出", 10000, ct)) return;
                await context.DelayAsync(500, ct);

                // 点击确认
                Console.WriteLine("  → 点击 [确定]");
                if(!await context.WaitAndClickAsync("确定", 10000, ct)) return;
                await context.DelayAsync(2000, ct);

                Console.WriteLine("  ✓ 强制退出完成");
                
                // 等待返回主菜单
                await context.DelayAsync(3000, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ 强制退出失败: {ex.Message}");
            }
        }

        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}
