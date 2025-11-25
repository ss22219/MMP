using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 簧火机关交互状态处理器
    /// 检测并导航到簧火机关（BP_OpenUIMechanism_Rouge_C）
    /// </summary>
    public class InteractingFireMechanismState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (context.BattleApi == null || context.Controller == null)
                return;

            Console.WriteLine("[簧火机关交互]");

            var tab = ocrResult.Regions.FirstOrDefault(r => r.Text.Contains("确定"));
            if (tab != null)
            {
                Console.WriteLine($"  → 点击 [确定]");
                context.Controller.Click((int)tab.Center.X, (int)tab.Center.Y);
                await context.DelayAsync(500, ct);
                return;
            }
            try
            {
                var entities = context.BattleApi.GetBattleEntities();
                var cameraLoc = context.BattleApi.GetCameraLocation();

                // 查找100米内的簧火机关
                var fireMechanism = entities
                    .Where(e => e.Name == "BP_OpenUIMechanism_Rouge_C")
                    .Where(e => e.CanOpen && !e.OpenState) // 可以打开且未打开
                    .Where(e => StateContext.CalculateDistance(cameraLoc, e.Position) <= 10000) // 100米
                    .OrderBy(e => StateContext.CalculateDistance(cameraLoc, e.Position))
                    .FirstOrDefault();

                // 如果没有找到簧火机关，检查炮台
                if (fireMechanism == null)
                {
                    var gameMechanism = entities
                        .Where(e => e.IsActor && e.Name == "BP_Paotai_Rouge01_C")
                        .Where(e => StateContext.CalculateDistance(cameraLoc, e.Position) <= 10000)
                        .OrderBy(e => StateContext.CalculateDistance(cameraLoc, e.Position))
                        .FirstOrDefault();

                    if (gameMechanism != null)
                    {
                        Console.WriteLine("  → 检测到炮台，查找附近传送点");

                        // 查找所有传送点
                        var allDeliveries = entities
                            .Where(e => e.IsActor && e.ClassName.Contains("RougeLikeDelivery"))
                            .Select(e => new
                            {
                                Entity = e,
                                Distance = StateContext.CalculateDistance(cameraLoc, e.Position)
                            })
                            .OrderBy(x => x.Distance)
                            .ToList();

                        if (allDeliveries.Count > 0)
                        {
                            Console.WriteLine($"  → 发现 {allDeliveries.Count} 个传送点");
                            foreach (var d in allDeliveries.Take(3))
                            {
                                Console.WriteLine($"    - {d.Entity.ClassName} 距离: {d.Distance / 100:F1}米");
                            }
                        }

                        // 按优先级查找传送点
                        string[] targetPriority = new[]
                        {
                            "BP_RougeLikeDelivery_Shop_C",
                            "BP_RougeLikeDelivery_Event_C",
                            "BP_RougeLikeDelivery_Battle_2_C",
                            "BP_RougeLikeDelivery_Battle_C",
                            "BP_RougeLikeDelivery_EliteBattle_C",
                            "BP_RougeLikeDelivery_Boss_C",
                        };

                        fireMechanism = gameMechanism; // 默认使用炮台

                        foreach (var priority in targetPriority)
                        {
                            var targetDelivery = entities
                                .Where(e => e.IsActor && e.ClassName == priority)
                                .Where(e => StateContext.CalculateDistance(cameraLoc, e.Position) <= 30000) // 300米
                                .FirstOrDefault();

                            if (targetDelivery != null)
                            {
                                fireMechanism = targetDelivery;
                                Console.WriteLine($"  → 优先导航到传送点: {priority}");
                                break;
                            }
                        }
                    }
                }

                if (fireMechanism == null)
                {
                    Console.WriteLine("  ✗ 未找到簧火机关");
                    return;
                }

                float distance = StateContext.CalculateDistance(cameraLoc, fireMechanism.Position);
                Console.WriteLine($"  → 簧火机关距离: {distance / 100:F1}米");

                // 导航到簧火机关
                bool navSuccess = await context.NavigateToTargetAsync(fireMechanism.Position, 20,true, ct);

                if (navSuccess)
                {
                    Console.WriteLine("  ✓ 簧火交互完成");
                }
                else
                {
                    Console.WriteLine("  ✗ 簧火导航失败");
                }

                await context.DelayAsync(2000, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ 簧火机关检测失败: {ex.Message}");
            }
        }

        public void Cleanup(StateContext context)
        {
            // 停止移动
            if (context.Controller != null)
            {
                Console.WriteLine("  [清理] 停止移动");
                context.Controller.SendKeyUp("W");
                context.Controller.SendKeyUp("LSHIFT");
            }
        }

    }
}
