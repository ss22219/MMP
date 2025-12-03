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
        private static readonly string[] ExpectedButtons = ["开始探索", "继续探索"];
        
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (ocrResult == null || context.Controller == null)
                return;

            // 显示识别到的文本
            var allText = string.Join(", ", ocrResult.Regions.Select(r => r.Text));
            Console.WriteLine($"[主菜单] 识别到的文本: {allText}");

            // 点击"坠入深渊"或"开始探索"
            var targetBtn = ocrResult.Regions.FirstOrDefault(r =>
                r.Text.Contains("坠入深渊"));

            if (targetBtn != null)
            {
                int maxRetries = 3;
                for (int i = 0; i < maxRetries; i++)
                {
                    context.Controller.Click((int)targetBtn.Center.X, (int)targetBtn.Center.Y + 5);
                    
                    if (await context.WaitAndClickAsync(ExpectedButtons, 20000, ct))
                        break;
                        
                    if (i < maxRetries - 1)
                        await context.DelayAsync(1000, ct);
                }
            }
            else
            {
                targetBtn = ocrResult.Regions.FirstOrDefault(r =>
                    r.Text.Contains("开始探索") || r.Text.Contains("继续探索"));
                    
                if (targetBtn != null)
                    context.Controller.Click((int)targetBtn.Center.X, (int)targetBtn.Center.Y + 5);
                else
                    await context.DelayAsync(500, ct);
            }
        }

        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}