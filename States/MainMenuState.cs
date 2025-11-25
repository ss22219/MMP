using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 主菜单状态处理器
    /// </summary>
    public class MainMenuState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (ocrResult == null || context.Controller == null)
                return;

            // 点击"坠入深渊"或"开始探索"
            var targetBtn = ocrResult.Regions.FirstOrDefault(r =>
                r.Text.Contains("坠入深渊"));

            if (targetBtn != null)
            {
                Console.WriteLine($"[主菜单] 点击 {targetBtn.Text}");
                context.Controller.Click((int)targetBtn.Center.X, (int)targetBtn.Center.Y + 5);
                if (!await context.WaitAndClickAsync("开始探索", 5000, ct)) return;
                // 等待状态改变（可被 ct 中断）
                await context.DelayAsync(2000, ct);
            }
            else
            {
                targetBtn = ocrResult.Regions.FirstOrDefault(r =>
                    r.Text.Contains("开始探索"));
                if (targetBtn != null)
                    context.Controller.Click((int)targetBtn.Center.X, (int)targetBtn.Center.Y + 5);
                else
                    // 没有可点击的，短暂等待
                    await context.DelayAsync(500, ct);
            }
        }

        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}