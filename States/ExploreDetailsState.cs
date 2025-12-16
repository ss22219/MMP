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
        private int _longPressAttempts = 0;
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
                (Keywords: new[] { "结束奇遇", "结束", "炮台轰击", "开始游戏" }, Name: "游戏结束"),
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
            for (int i = 0; i < 3; i++)
            {
                context.Controller.SendKeyDown("SPACE");
                await context.DelayAsync(100, ct);
                context.Controller.SendKeyUp("SPACE");
            }
            await context.DelayAsync(100, ct);
            // 增加长按尝试次数
            _longPressAttempts++;
            
            // 如果已经尝试了4次长按，执行滑动操作
            if (_longPressAttempts >= 4)
            {
                Console.WriteLine($"  → 长按尝试{_longPressAttempts}次未退出，执行滑动操作");
                var (winWidth, winHeight) = WindowHelper.GetWindowSize(context.WindowHandle);
                
                // 从窗口x：1/3，从下往上快速点击到y：1/2处，每次移动20%
                int clickX = winWidth / 3;
                int startY = winHeight - 50;  // 底部留一点边距
                int endY = winHeight / 2;  // 中间
                int totalDistance = startY - endY;
                int steps = 5;  // 5步，每步20%
                int stepDistance = totalDistance / steps;
                
                Console.WriteLine($"  → 快速点击操作：从({clickX}, {startY})向上点击到({clickX}, {endY})");
                
                for (int i = 0; i <= steps; i++)
                {
                    int currentY = startY - (stepDistance * i);
                    Console.WriteLine($"    点击位置: ({clickX}, {currentY})");
                    context.Controller.Click(clickX, currentY);
                    await context.DelayAsync(100, ct);  // 每次点击间隔100ms
                }
                
                await context.DelayAsync(500, ct);
                
                // 重置计数器
                _longPressAttempts = 0;
            }
            else
            {
                // 查找并点击"长按"文字
                var longPressText = ocrResult.Regions.FirstOrDefault(r => r.Text.Contains("长按"));
                if (longPressText != null)
                {
                    Console.WriteLine($"  → 点击并长按 [长按] 文字（2.5秒）- 尝试 {_longPressAttempts}/4");
                    context.Controller.MouseDown((int)longPressText.Center.X, (int)longPressText.Center.Y, "left");
                    await context.DelayAsync(2500, ct);
                    context.Controller.MouseUp((int)longPressText.Center.X, (int)longPressText.Center.Y, "left");
                }
                else
                {
                    Console.WriteLine($"  → 未找到 [长按] 文字，使用空格键关闭 - 尝试 {_longPressAttempts}/4");
                    context.Controller.SendKeyDown("SPACE");
                    await context.DelayAsync(2500, ct);
                    context.Controller.SendKeyUp("SPACE");
                }
            }
        }
        
        public void Cleanup(StateContext context)
        {
            // 重置长按尝试计数器
            if (_longPressAttempts > 0)
            {
                Console.WriteLine($"  [清理] 重置长按尝试计数器 ({_longPressAttempts} → 0)");
                _longPressAttempts = 0;
            }
        }
    }
}
