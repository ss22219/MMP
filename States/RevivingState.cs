using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 复苏状态处理器
    /// </summary>
    public class RevivingState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (context.Controller == null)
                return;

            Console.WriteLine("[复苏中]");
            context.Controller.SendKeyDown("X");

            try
            {
                // 等待复苏文字消失（可被状态变化中断）
                bool revived = await context.WaitForTextDisappearAsync("复苏", 3000, ct);
                Console.WriteLine(revived ? "  → 复苏完成" : "  → 复苏超时");
            }
            finally
            {
                // 确保释放按键
                context.Controller.SendKeyUp("X");
            }
        }
        
        public void Cleanup(StateContext context)
        {
            // 被中断时释放按键
            if (context.Controller != null)
            {
                Console.WriteLine("  [清理] 释放 X 键");
                context.Controller.SendKeyUp("X");
            }
        }
    }
}
