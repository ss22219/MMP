using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 选择遗物状态处理器
    /// </summary>
    public class SelectingRelicState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (ocrResult == null || context.Controller == null)
                return;

            Console.WriteLine("[选择遗物] → 放弃");

            var giveUpBtn = ocrResult.Regions.FirstOrDefault(r => r.Text.Contains("放弃"));
            if (giveUpBtn != null)
            {
                context.Controller.Click((int)giveUpBtn.Center.X, (int)giveUpBtn.Center.Y);
                await context.DelayAsync(1000, ct);
            }

            // 等待并点击确定
            if (await context.WaitAndClickAsync("确定", 2000, ct))
            {
                await context.DelayAsync(2000, ct);
            }
        }
        
        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}
