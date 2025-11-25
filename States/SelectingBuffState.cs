using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 选择 Buff 状态处理器
    /// </summary>
    public class SelectingBuffState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (ocrResult == null || context.Controller == null)
                return;

            Console.WriteLine("[选择Buff]");

            // 优先选择"行迹获取量提高"
            var traceBuff = ocrResult.Regions.FirstOrDefault(r =>
                r.Text.Contains("行迹") && r.Text.Contains("获取") && r.Center.X > 640);

            if (traceBuff != null)
            {
                Console.WriteLine("  → 行迹获取量提高");
                context.Controller.Click((int)traceBuff.Center.X, (int)traceBuff.Center.Y);
                await context.DelayAsync(300, ct);
            }
            else
            {
                // 次选"远程武器伤害提高"
                var rangedBuff = ocrResult.Regions.FirstOrDefault(r =>
                    r.Text.Contains("远程武器") && r.Center.X > 640);
                
                if (rangedBuff != null)
                {
                    Console.WriteLine("  → 远程武器伤害提高");
                    context.Controller.Click((int)rangedBuff.Center.X, (int)rangedBuff.Center.Y);
                    await context.DelayAsync(300, ct);
                }
                else
                {
                    // 放弃
                    Console.WriteLine("  → 放弃");
                    await context.WaitAndClickAsync("放弃", 2000, ct);
                }
            }

            // 点击"选择"按钮
            if (await context.WaitAndClickAsync("选择", 2000, ct))
            {
                await context.DelayAsync(300, ct);
            }

            // 点击"确定"按钮
            if (await context.WaitAndClickAsync("确定", 2000, ct))
            {
                // 等待状态改变
                await context.DelayAsync(3000, ct);
            }
        }
        
        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}
