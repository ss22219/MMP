using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 关闭 UI 状态处理器
    /// 处理各种弹窗和提示界面
    /// </summary>
    public class ClosingUIState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (ocrResult == null || context.Controller == null)
                return;

            Console.WriteLine("[关闭UI]");

            // 检测需要关闭的 UI 文字
            var uiTexts = new[]
            {
                "点击空白",
                "探索完成",
                "探索成功",
                "激活套装",
                "获得烛芯",
                "获得遗物",
                "点击任意"
            };

            var detectedUI = ocrResult.Regions
                .Where(r => uiTexts.Any(text => r.Text.Contains(text)))
                .ToList();

            if (detectedUI.Any())
            {
                var uiText = string.Join(", ", detectedUI.Select(r => r.Text).Take(3));
                Console.WriteLine($"  → 检测到: {uiText}");

                // 点击右上角关闭
                var (winWidth, winHeight) = WindowHelper.GetWindowSize(context.WindowHandle);
                context.Controller.Click(winWidth - 70, 50);
                
                await context.DelayAsync(500, ct);
                
                Console.WriteLine("  ✓ 已点击右上角");
            }
            else
            {
                // 没有检测到需要关闭的 UI，短暂等待
                await context.DelayAsync(200, ct);
            }
        }

        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}
