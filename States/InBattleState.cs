using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 战斗状态处理器
    /// </summary>
    public class InBattleState : IStateHandler
    {
        private DateTime _lastSkillTime = DateTime.MinValue;
        private int _skillECount = 0;
        private Dictionary<string, int> _monsterStuckCount = new Dictionary<string, int>();

        public async Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct)
        {
            if (context.BattleApi == null || context.Controller == null)
                return;

            Console.WriteLine("[战斗中]");

            // 检查是否回到主界面
            if (ocrResult != null && ocrResult.Regions.Any(r =>
                r.Text.Contains("坠入深渊") || r.Text.Contains("探索详情")))
            {
                Console.WriteLine("  ⚠ 检测到主界面，退出战斗");
                return;
            }

            try
            {
                // 获取战斗实体
                var entities = context.BattleApi.GetBattleEntities();
                var cameraLoc = context.BattleApi.GetCameraLocation();

                // 过滤有效怪物
                var monsters = entities.Where(e =>
                {
                    if (!e.IsActor || !(e.ClassName.StartsWith("BP_Mon_") || e.ClassName.StartsWith("BP_Boss_")))
                        return false;

                    // 必须继承自 MonsterCharacter
                    if (!e.ParentClasses.Any(c => c.Contains("MonsterCharacter")))
                        return false;

                    // 名称不能为空或 "None"
                    if (string.IsNullOrEmpty(e.Name) || e.Name == "None")
                        return false;

                    // 300米以内
                    float distance = CalculateDistance(cameraLoc, e.Position);
                    if (distance > 30000)
                        return false;

                    // 必须存活
                    if (e.AlreadyDead)
                        return false;

                    return true;
                }).ToList();

                if (monsters.Count == 0)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到战斗文字但无怪物，向前移动3秒触发");
                    context.Controller.SendKeyDown("W");
                    context.Controller.SendKeyDown("LSHIFT");
                    await context.DelayAsync(3000, ct);
                    context.Controller.SendKeyUp("LSHIFT");
                    context.Controller.SendKeyUp("W");
                    await context.DelayAsync(500, ct);
                    return;
                }

                // 选择最近的怪物
                var nearestMonster = monsters.OrderBy(m => CalculateDistance(cameraLoc, m.Position)).First();
                float targetDistance = CalculateDistance(cameraLoc, nearestMonster.Position);
                
                Console.WriteLine($"  → 目标: {nearestMonster.Name} 距离: {targetDistance / 100:F1}米");

                // 如果怪物距离太远（>30米），先移动靠近
                if (targetDistance > 3000)
                {
                    string monsterKey = $"{nearestMonster.Name}_{nearestMonster.EntityId}";

                    if (!_monsterStuckCount.ContainsKey(monsterKey))
                        _monsterStuckCount[monsterKey] = 0;

                    _monsterStuckCount[monsterKey]++;

                    if (_monsterStuckCount[monsterKey] >= 3)
                    {
                        Console.WriteLine($"  ⚠ 怪物被卡住3次，点击右上角");
                        var (winWidth, winHeight) = WindowHelper.GetWindowSize(context.WindowHandle);
                        context.Controller.Click(winWidth - 70, 50);
                        return;
                    }

                    Console.WriteLine($"  → 移动靠近 (尝试 {_monsterStuckCount[monsterKey]}/3)");

                    // 调整视角对准怪物
                    await context.AdjustCameraToTargetAsync(nearestMonster.Position, ct);

                    // 向前移动2秒
                    context.Controller.SendKeyDown("W");
                    context.Controller.SendKeyDown("LSHIFT");
                    await context.DelayAsync(2000, ct);
                    context.Controller.SendKeyUp("LSHIFT");
                    context.Controller.SendKeyUp("W");

                    return;
                }

                // 如果能攻击到，重置卡住计数
                string attackMonsterKey = $"{nearestMonster.Name}_{nearestMonster.EntityId}";
                if (_monsterStuckCount.ContainsKey(attackMonsterKey))
                    _monsterStuckCount[attackMonsterKey] = 0;

                // 按 Z 和 F（可能是技能或交互）
                context.Controller.SendKey("Z", 0.1);
                await Task.Delay(50, ct);
                context.Controller.SendKey("F", 0.1);
                await Task.Delay(50, ct);

                // 调整视角对准怪物
                await context.AdjustCameraToTargetAsync(nearestMonster.Position, ct);

                // 技能使用逻辑：Q 后每秒按一次 E，连续 4 次
                if ((DateTime.Now - _lastSkillTime).TotalMilliseconds > 1000)
                {
                    if (_skillECount == 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] → 使用技能 Q");
                        context.Controller.SendKey("Q", 0.1);
                        await Task.Delay(100, ct);
                        _lastSkillTime = DateTime.Now;
                        _skillECount++;
                    }
                    else if (_skillECount < 4)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] → 使用技能 E ({_skillECount}/4)");
                        context.Controller.SendKey("E", 0.1);
                        await Task.Delay(100, ct);
                        _lastSkillTime = DateTime.Now;
                        _skillECount++;

                        if (_skillECount >= 4)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ 技能循环完成，重置计数");
                            _skillECount = 0;
                        }
                    }
                }

                // 攻击3次
                for (int i = 0; i < 3; i++)
                {
                    context.Controller.MouseDown(-1, -1, "right");
                    await Task.Delay(350, ct);
                    context.Controller.MouseUp(-1, -1, "right");
                    await Task.Delay(50, ct);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  ✗ 战斗错误: {ex.Message}");
            }
        }

        public void Cleanup(StateContext context)
        {
            // 停止移动和攻击
            if (context.Controller != null)
            {
                Console.WriteLine("  [清理] 停止战斗动作");
                context.Controller.SendKeyUp("W");
                context.Controller.SendKeyUp("LSHIFT");
                context.Controller.MouseUp(-1, -1, "right");
            }
        }

        private static float CalculateDistance(FVector pos1, FVector pos2)
        {
            float dx = pos2.X - pos1.X;
            float dy = pos2.Y - pos1.Y;
            float dz = pos2.Z - pos1.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
    }
}
