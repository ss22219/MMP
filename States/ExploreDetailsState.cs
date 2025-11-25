using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 探索详情状态处理器
    /// </summary>
    public class ExploreDetailsState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (ocrResult == null || context.Controller == null)
                return;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [探索详情]");

            // 按优先级查找标签
            var tabPriority = new[]
            {
                (Keywords: new[] { "烛芯", "[烛芯]" }, Name: "烛芯"),
                (Keywords: new[] { "余烬", "[余烬]" }, Name: "余烬"),
                (Keywords: new[] { "遗物", "[遗物]" }, Name: "遗物"),
                (Keywords: new[] { "结束", "炮台轰击", "开始游戏" }, Name: "游戏结束"),
                (Keywords: new[] { "获得" }, Name: "获得")
            };

            // 查找并点击第一个匹配的标签
            foreach (var (keywords, name) in tabPriority)
            {
                var tab = ocrResult.Regions.FirstOrDefault(r => keywords.Any(k => r.Text.Contains(k)));
                if (tab != null)
                {
                    Console.WriteLine($"  → 点击 [{name}]");
                    context.Controller.Click((int)tab.Center.X, (int)tab.Center.Y);
                    await context.DelayAsync(500, ct);
                    break;
                }
            }

            // 按空格键关闭
            Console.WriteLine("  → 按空格关闭（长按1.2秒）");
            context.Controller.SendKey("SPACE", 1.2);
            await context.DelayAsync(100, ct);
        }
        
        public void Cleanup(StateContext context)
        {
            // 无需清理
        }
    }
}
