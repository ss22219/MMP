using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 导航状态处理器
    /// </summary>
    public class NavigatingState : IStateHandler
    {
        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            Console.WriteLine("[导航中]");
            
            if (context.BattleApi == null || context.Controller == null)
                return;

            // 查找目标传送点（按优先级）
            string[] targetPriority = new[]
            {
                "BP_RougeLikeDelivery_Shop_C",        // 商店
                "BP_RougeLikeDelivery_Event_C",       // 事件
                "BP_RougeLikeDelivery_Battle_2_C",    // 战斗2
                "BP_RougeLikeDelivery_Battle_C",      // 战斗
                "BP_RougeLikeDelivery_EliteBattle_C", // 精英战斗
                "BP_RougeLikeDelivery_Boss_C",        // Boss
            };

            var entities = context.BattleApi.GetBattleEntities();
            var cameraLoc = context.BattleApi.GetCameraLocation();

            EntityInfo? targetDelivery = null;
            
            // 按优先级查找300米内的传送点
            foreach (var priority in targetPriority)
            {
                targetDelivery = entities
                    .Where(e => e.IsActor && e.ClassName == priority)
                    .Where(e => StateContext.CalculateDistance(cameraLoc, e.Position) <= 30000) // 300米
                    .FirstOrDefault();

                if (targetDelivery != null)
                {
                    Console.WriteLine($"  → 找到目标: {GetDeliveryTypeName(priority)}");
                    break;
                }
            }

            if (targetDelivery == null)
            {
                Console.WriteLine("  ⚠ 未找到可导航的传送点");
                return;
            }

            // 导航到目标
            bool navSuccess = await context.NavigateToTargetAsync(targetDelivery.Position, 60, true, ct);
            
            if (navSuccess)
            {
                Console.WriteLine("  ✓ 导航成功");
            }
            else
            {
                Console.WriteLine("  ✗ 导航失败");
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

        private static string GetDeliveryTypeName(string className)
        {
            return className switch
            {
                "BP_RougeLikeDelivery_Shop_C" => "商店",
                "BP_RougeLikeDelivery_Event_C" => "事件",
                "BP_RougeLikeDelivery_Battle_2_C" => "战斗2",
                "BP_RougeLikeDelivery_Battle_C" => "战斗",
                "BP_RougeLikeDelivery_EliteBattle_C" => "精英战斗",
                "BP_RougeLikeDelivery_Boss_C" => "Boss",
                _ => className
            };
        }
    }
}
