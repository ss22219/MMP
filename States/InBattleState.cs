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
                context.RequestReturnToMainMenu("战斗结束，检测到主界面");
                return;
            }

            // 检查是否需要复苏
            if (ocrResult != null && ocrResult.Regions.Any(r =>
                r.Text.Contains("复苏") || r.Text.Contains("重生")))
            {
                Console.WriteLine("  ⚠ 检测到复苏界面");
                context.RequestReviving("角色死亡，需要复苏");
                return;
            }

            try
            {
                // 获取战斗实体
                var entities = context.BattleApi.GetBattleEntities();
                var cameraLoc = context.BattleApi.GetCameraLocation();

                // 过滤有效怪物（使用统一的检测范围）
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

                    // 检测范围内（使用统一的怪物检测范围）
                    float distance = CalculateDistance(cameraLoc, e.Position);
                    if (distance > context.Config.Battle.MonsterDetectionRange)
                        return false;

                    // 必须存活
                    if (e.AlreadyDead)
                        return false;

                    return true;
                }).ToList();

                if (monsters.Count == 0)
                {
                    // 优先使用 BattlePoints 中的 Boss 战斗点
                    var battlePoints = context.BattleApi.GetBattlePoints();
                    var bossPoints = battlePoints.Where(bp => bp.Name.Contains("Boss") && !bp.Name.Contains("Skill")).ToList();
                    
                    var playerPos = context.BattleApi.GetPlayerLocation();
                    FVector? targetPosition = null;
                    string targetSource = "";

                    if (bossPoints.Count > 0)
                    {
                        // 使用最近的 Boss 战斗点
                        var nearestBossPoint = bossPoints
                            .Select(bp => new { Point = bp, Distance = CalculateDistance(playerPos, bp.Position) })
                            .Where(x => x.Distance <= 30000) // 300米范围内
                            .OrderBy(x => x.Distance)
                            .FirstOrDefault();

                        if (nearestBossPoint != null)
                        {
                            targetPosition = nearestBossPoint.Point.Position;
                            targetSource = $"BattlePoint ({nearestBossPoint.Point.Name}, 距离 {nearestBossPoint.Distance / 100:F1}米)";
                        }
                    }

                    if (targetPosition != null)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到战斗文字但无怪物，调整视角朝向{targetSource}");
                        await context.AdjustCameraToTargetAsync(targetPosition.Value, ct);
                    }
                    else
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到战斗文字但无怪物，向前移动触发");
                    }

                    context.Controller.SendKeyDown("W");
                    context.Controller.SendKeyDown("LSHIFT");
                    
                    // 最多移动3秒，每1000ms检查一次
                    for (int i = 0; i < 3; i++)
                    {
                        await context.DelayAsync(1000, ct);
                        
                        // 每次检查是否有怪物出现（Boss 和普通怪物使用不同范围）
                        var checkEntities = context.BattleApi.GetBattleEntities();
                        var checkMonsters = checkEntities.Where(e =>
                        {
                            if (!e.IsActor || !(e.ClassName.StartsWith("BP_Mon_") || e.ClassName.StartsWith("BP_Boss_")))
                                return false;
                            if (!e.ParentClasses.Any(c => c.Contains("MonsterCharacter")))
                                return false;
                            if (string.IsNullOrEmpty(e.Name) || e.Name == "None")
                                return false;
                            if (e.AlreadyDead)
                                return false;
                            
                            float distance = CalculateDistance(cameraLoc, e.Position);
                            // 使用统一的怪物检测范围
                            return distance <= context.Config.Battle.MonsterDetectionRange;
                        }).ToList();
                        
                        if (checkMonsters.Count > 0)
                        {
                            Console.WriteLine($"  ✓ 检测到怪物，停止移动");
                            break;
                        }
                    }
                    
                    context.Controller.SendKeyUp("LSHIFT");
                    context.Controller.SendKeyUp("W");
                    await context.DelayAsync(500, ct);
                    return;
                }

                // 选择最近的怪物
                var nearestMonster = monsters.OrderBy(m => CalculateDistance(cameraLoc, m.Position)).First();
                float targetDistance = CalculateDistance(cameraLoc, nearestMonster.Position);
                
                // 判断是否为 Boss 并使用对应的靠近距离
                bool isBoss = nearestMonster.ClassName.StartsWith("BP_Boss_") || 
                             nearestMonster.Name.Contains("Boss") || 
                             nearestMonster.ParentClasses.Any(c => c.Contains("Boss"));
                
                string targetType = isBoss ? "Boss" : "怪物";
                float approachDistance = isBoss ? context.Config.Battle.BossApproachDistance : context.Config.Battle.ApproachDistance;
                
                Console.WriteLine($"  → 目标: {nearestMonster.Name} ({targetType}) 距离: {targetDistance / 100:F1}米 [靠近距离: {approachDistance / 100:F0}米]");

                // 如果怪物距离太远，先移动靠近
                if (targetDistance > approachDistance)
                {
                    string monsterKey = $"{nearestMonster.Name}_{nearestMonster.EntityId}";

                    if (!_monsterStuckCount.ContainsKey(monsterKey))
                        _monsterStuckCount[monsterKey] = 0;

                    _monsterStuckCount[monsterKey]++;

                    if (_monsterStuckCount[monsterKey] >= 3)
                    {
                        _monsterStuckCount.Clear();
                        Console.WriteLine($"  ⚠ 怪物被卡住3次");
                        context.RequestForceExit("怪物被卡住3次");
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
                context.BattleApi.RefreshEntityPosition(nearestMonster);
                if(nearestMonster.AlreadyDead)
                    return;
                await context.AdjustCameraToTargetAsync(nearestMonster.Position, ct);

                // 技能使用逻辑：Q 后按间隔释放 E 技能
                var config = context.Config.Battle;
                var skillInterval = _skillECount == 0 ? config.QSkillInterval : config.ESkillInterval;
                
                if ((DateTime.Now - _lastSkillTime).TotalMilliseconds > skillInterval)
                {
                    if (_skillECount == 0)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] → 使用技能 Q");
                        context.Controller.SendKey("Q", 0.1);
                        await Task.Delay(100, ct);
                        _lastSkillTime = DateTime.Now;
                        _skillECount++;
                    }
                    else if (_skillECount < config.ESkillCount)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] → 使用技能 E ({_skillECount}/{config.ESkillCount})");
                        context.Controller.SendKey("E", 0.1);
                        await Task.Delay(100, ct);
                        _lastSkillTime = DateTime.Now;
                        _skillECount++;

                        if (_skillECount >= config.ESkillCount)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] ✓ 技能循环完成，重置计数");
                            _skillECount = 0;
                        }
                    }
                }

                // 攻击
                for (int i = 0; i < config.AttackCount; i++)
                {
                    context.BattleApi.RefreshEntityPosition(nearestMonster);
                    if(nearestMonster.AlreadyDead)
                        return;
                    await context.AdjustCameraToTargetAsync(nearestMonster.Position, ct);
                    context.Controller.MouseDown(-1, -1, "right");
                    await Task.Delay(config.AttackInterval, ct);
                    context.Controller.MouseUp(-1, -1, "right");
                    await Task.Delay(config.AttackRecoveryDelay, ct);
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
