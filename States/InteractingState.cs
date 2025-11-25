using System;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 交互状态处理器
    /// </summary>
    public class InteractingState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (context.Controller == null)
                return;

            Console.WriteLine("[交互中]");
            context.Controller.SendKey("F", 0.1);
            await context.DelayAsync(500, ct);
        }
        
        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}
