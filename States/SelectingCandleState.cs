using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 选择烛芯状态处理器
    /// </summary>
    public class SelectingCandleState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (ocrResult == null || context.Controller == null)
                return;

            Console.WriteLine("[选择烛芯]");

            // 优先选择"噬影蝶"
            var eaterCandle = ocrResult.Regions.FirstOrDefault(r =>
                r.Text.Contains("噬影蝶") && r.Center.X > 640);

            if (eaterCandle != null)
            {
                Console.WriteLine("  → 噬影蝶");
                context.Controller.Click((int)eaterCandle.Center.X, (int)eaterCandle.Center.Y);
                await context.DelayAsync(300, ct);
            }
            else
            {
                // 次选"浮海月"
                var moonCandle = ocrResult.Regions.FirstOrDefault(r =>
                    r.Text.Contains("浮海月") && r.Center.X > 640);

                if (moonCandle != null)
                {
                    Console.WriteLine("  → 浮海月");
                    context.Controller.Click((int)moonCandle.Center.X, (int)moonCandle.Center.Y);
                    await context.DelayAsync(300, ct);
                }
                else
                {
                    // 放弃
                    var giveUpBtn = ocrResult.Regions.FirstOrDefault(r =>
                        r.Text.Contains("放弃") && r.Center.X > 640);
                    if (giveUpBtn != null)
                    {
                        Console.WriteLine("  → 放弃");
                        context.Controller.Click((int)giveUpBtn.Center.X, (int)giveUpBtn.Center.Y);
                        await context.DelayAsync(300, ct);
                    }
                }
            }

            // 等待并点击"选择"按钮
            if (await context.WaitAndClickAsync("选择", 2000, ct))
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
