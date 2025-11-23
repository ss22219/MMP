using System;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using MMP;

namespace MMP
{
    /// <summary>
    /// 自动检测"坠入深渊"并点击
    /// </summary>
    class TestAutoClickAbyss
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== 自动检测并点击 ===");
            Console.WriteLine("检测目标: 坠入深渊、开始探索、放弃");
            Console.WriteLine("按 Ctrl+C 停止");
            Console.WriteLine();

            // 获取游戏窗口
            string windowTitle = "二重螺旋";
            IntPtr hwnd = WindowHelper.FindWindow(windowTitle);
            
            if (hwnd == IntPtr.Zero)
            {
                Console.WriteLine($"错误: 找不到窗口 '{windowTitle}'");
                return;
            }

            Console.WriteLine($"找到窗口: {windowTitle} (hwnd={hwnd:X})");

            // 获取窗口大小
            var (windowWidth, windowHeight) = WindowHelper.GetWindowSize(hwnd);
            Console.WriteLine($"窗口大小: {windowWidth}x{windowHeight}");

            // 初始化 OCR 引擎
            using var ocrEngine = new OcrEngine();
            ocrEngine.Initialize();
            Console.WriteLine("OCR 引擎初始化完成");

            // 初始化鼠标控制器
            using var controller = new KeyboardMouseController(hwnd, windowWidth, windowHeight);
            controller.BackgroundMode = false; // 使用前台模式
            Console.WriteLine("鼠标控制器初始化完成（前台模式）");
            Console.WriteLine();

            // F10 强制退出监听
            bool shouldExit = false;
            var exitThread = new Thread(() =>
            {
                bool lastF10State = false;
                while (!shouldExit)
                {
                    bool currentF10State = (GetAsyncKeyState(0x79) & 0x8000) != 0; // 0x79 = F10
                    
                    if (currentF10State && !lastF10State)
                    {
                        Console.WriteLine("\n[F10] 强制退出程序");
                        shouldExit = true;
                        Environment.Exit(0);
                    }
                    
                    lastF10State = currentF10State;
                    Thread.Sleep(50);
                }
            });
            exitThread.IsBackground = true;
            exitThread.Start();
            
            Console.WriteLine("按 F10 强制退出程序");
            Console.WriteLine();

            int checkCount = 0;
            int clickCount = 0;
            
            // 战斗相关
            bool inBattle = false;
            string lastDeliveryType = ""; // 记录最后访问的传送点类型
            bool needMoveBeforeBattle = false; // 是否需要在战斗前移动
            HashSet<int> failedDeliveryIds = new HashSet<int>(); // 记录失败的传送点ID
            DateTime battleStartTime = DateTime.MinValue;
            
            // 状态超时检测
            DateTime stateStartTime = DateTime.Now;
            const int STATE_TIMEOUT_MINUTES = 5; // 状态超时时间（5分钟）
            
            [DllImport("user32.dll")]
            static extern short GetAsyncKeyState(int vKey);
            
            // 初始化 BattleEntitiesAPI
            var navApi = new BattleEntitiesAPI("EM-Win64-Shipping");
            // 主循环
            while (true)
            {
                try
                {
                    checkCount++;
                    
                    // 检查状态超时（5分钟）
                    if ((DateTime.Now - stateStartTime).TotalMinutes > STATE_TIMEOUT_MINUTES)
                    {
                        Console.WriteLine($"\n⚠ 状态超时（{STATE_TIMEOUT_MINUTES}分钟），执行强制退出");
                        PerformForceExit();
                        continue;
                    }
                    
                    // OCR前将鼠标移到窗口左上角
                    controller.Move(0, 0);
                    Thread.Sleep(50);
                    
                    // 截取游戏画面
                    using var screenshot = ScreenCapture.CaptureWindow(hwnd);
                    
                    if (screenshot == null)
                    {
                        Console.WriteLine("截图失败，等待 1 秒后重试...");
                        Thread.Sleep(1000);
                        continue;
                    }

                    // OCR 识别
                    var result = ocrEngine.Recognize(screenshot);
                    
                    // 检测右上角是否有"战斗"或"驱散幽影"文字
                    bool hasBattleText = false;
                    foreach (var region in result.Regions)
                    {
                        // 右上角区域：X > 窗口宽度的75%, Y < 300
                        if (region.Center.X > windowWidth * 0.75f && region.Center.Y < 300)
                        {
                            if (region.Text.Contains("前往下一层"))
                                break;
                            if (region.Text.Contains("战斗") || region.Text.Contains("驱散幽影"))
                            {
                                hasBattleText = true;
                                Console.WriteLine($"[调试] 检测到战斗文字: {region.Text} 位置:({region.Center.X:F0}, {region.Center.Y:F0})");
                                break;
                            }
                        }
                    }
                    
                    // 检测是否有怪物（有怪物就算战斗中）
                    bool hasMonsters = false;
                    if (!inBattle)
                    {
                        try
                        {
                            var checkApi = new BattleEntitiesAPI("EM-Win64-Shipping");
                            var entities = checkApi.GetBattleEntities();
                            var cameraLocation = checkApi.GetCameraLocation();
                            
                            // 查找 300 米内的存活怪物
                            hasMonsters = entities.Any(e => 
                                e.IsActor && 
                                (e.ClassName.StartsWith("BP_Mon_") || e.ClassName.StartsWith("BP_Boss_")) &&
                                e.ParentClasses.Any(c => c.Contains("MonsterCharacter")) &&
                                !e.AlreadyDead &&
                                CalculateDistance(cameraLocation, e.Position) <= 30000);
                            
                            if (hasMonsters)
                            {
                                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到怪物，进入战斗模式");
                            }
                            else if (hasBattleText)
                            {
                                // 有战斗文字但没有怪物，检查是否在特定坐标附近
                                var playerLoc = checkApi.GetPlayerLocation();
                                
                                // 定义两个目标坐标
                                FVector targetCoord1 = new FVector { X = -202583.0469f, Y = 100746.0391f, Z = 379.8486023f };
                                FVector targetCoord2 = new FVector { X = -502585.8125f, Y = 100663.3594f, Z = 379.8485718f };
                                
                                float distanceToTarget1 = CalculateDistance(playerLoc, targetCoord1);
                                float distanceToTarget2 = CalculateDistance(playerLoc, targetCoord2);
                                
                                // 检查是否在任一目标坐标200米内
                                if (distanceToTarget1 <= 20000) // 200米 = 20000单位
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 在目标坐标1的200米内({distanceToTarget1/100:F1}米)，开始导航");
                                    NavigateToTarget(checkApi, targetCoord1, 30, false); // 30秒超时，不需要交互
                                    Thread.Sleep(500);
                                }
                                else if (distanceToTarget2 <= 20000) // 200米 = 20000单位
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 在目标坐标2的200米内({distanceToTarget2/100:F1}米)，开始导航");
                                    NavigateToTarget(checkApi, targetCoord2, 30, false); // 30秒超时，不需要交互
                                    Thread.Sleep(500);
                                }
                                else
                                {
                                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到战斗文字但无怪物，向前移动6秒触发");
                                    controller.SendKeyDown("W");
                                    controller.SendKeyDown("LSHIFT");
                                    Thread.Sleep(6000);
                                    controller.SendKeyUp("LSHIFT");
                                    controller.SendKeyUp("W");
                                    Thread.Sleep(500);
                                }
                                continue; // 重新检测
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"检测怪物失败: {ex.Message}");
                        }
                    }
                    
                    // 如果检测到怪物，进入战斗模式
                    if (hasMonsters && !inBattle)
                    {
                        inBattle = true;
                        battleStartTime = DateTime.Now;
                        stateStartTime = DateTime.Now; // 重置超时计时器
                        Console.WriteLine(">>> 进入战斗模式 <<<");
                        
                        // 初始化 BattleEntitiesAPI
                        var battleApi = new BattleEntitiesAPI("EM-Win64-Shipping");
                        
                        // 战斗循环
                        DateTime lastSkillTime = DateTime.MinValue;
                        int skillECount = 0; // E 技能使用次数
                        DateTime battleLoopStartTime = DateTime.Now; // 战斗循环开始时间
                        DateTime lastBattleOcrTime = DateTime.MinValue; // 上次战斗中OCR检测时间
                        const int BATTLE_TIMEOUT_MINUTES = 5; // 战斗超时时间（分钟）
                        const int BATTLE_OCR_INTERVAL_MS = 3000; // 战斗中OCR检测间隔（3秒）
                        
                        // 卡住检测
                        Dictionary<string, int> monsterStuckCount = new Dictionary<string, int>(); // 记录每个怪物被卡住的次数
                        
                        while (inBattle)
                        {
                            try
                            {
                                // 检查战斗超时（5分钟）
                                if ((DateTime.Now - battleLoopStartTime).TotalMinutes > BATTLE_TIMEOUT_MINUTES)
                                {
                                    Console.WriteLine($"⚠ 战斗超时（{BATTLE_TIMEOUT_MINUTES}分钟），执行强制退出");
                                    PerformForceExit();
                                    break;
                                }
                                
                                // 每 200ms 获取一次战斗实体
                                var entities = battleApi.GetBattleEntities();
                                var cameraLocation = battleApi.GetCameraLocation();
                                
                                // 查找 BP_Mon_ 或 BP_Boss_ 开头的怪物，并应用过滤条件
                                var monsters = entities.Where(e => 
                                {
                                    if (!e.IsActor || !(e.ClassName.StartsWith("BP_Mon_") || e.ClassName.StartsWith("BP_Boss_")))
                                        return false;
                                    
                                    // 【关键过滤0】继承链过滤：必须继承自 MonsterCharacter
                                    if (!e.ParentClasses.Any(c => c.Contains("MonsterCharacter")))
                                    {
                                        return false;
                                    }
                                    
                                    // 【关键过滤1】名称过滤：Name 为空或 "None" 的不要
                                    if (string.IsNullOrEmpty(e.Name) || e.Name == "None")
                                    {
                                        return false;
                                    }
                                    
                                    // 【关键过滤2】距离过滤：300米以外的不要
                                    float distance = CalculateDistance(cameraLocation, e.Position);
                                    if (distance > 30000) // 300米 = 30000单位
                                    {
                                        return false;
                                    }
                                    
                                    // 【关键过滤3】死亡状态过滤：AlreadyDead = true 的不要
                                    if (e.AlreadyDead)
                                    {
                                        return false;
                                    }
                                    
                                    return true;
                                }).ToList();
                                
                                if (monsters.Count == 0)
                                {
                                    Console.WriteLine("未找到有效怪物（300米内且存活），退出战斗模式");
                                    inBattle = false;
                                    break;
                                }
                                
                                // 选择最近的怪物
                                var nearestMonster = monsters
                                    .OrderBy(m => CalculateDistance(cameraLocation, m.Position))
                                    .First();
                                
                                float targetDistance = CalculateDistance(cameraLocation, nearestMonster.Position);
                                Console.WriteLine($"目标怪物: {nearestMonster.Name} 距离: {targetDistance:F0} 单位 ({targetDistance/100:F1}米)");
                                
                                // 如果怪物距离太远（>30米），先移动靠近
                                if (targetDistance > 3000)
                                {
                                    string monsterKey = $"{nearestMonster.Name}_{nearestMonster.EntityId}";
                                    
                                    // 检查这个怪物是否被卡住太多次
                                    if (!monsterStuckCount.ContainsKey(monsterKey))
                                    {
                                        monsterStuckCount[monsterKey] = 0;
                                    }
                                    
                                    monsterStuckCount[monsterKey]++;
                                    
                                    if (monsterStuckCount[monsterKey] > 3)
                                    {
                                        Console.WriteLine($"  ⚠ 怪物 {nearestMonster.Name} 被卡住超过3次，跳过此怪物");
                                        // 从怪物列表中移除这个怪物（通过标记为已死亡）
                                        // 实际上我们无法修改实体数据，所以继续攻击其他怪物
                                        // 如果只剩这一个怪物，会在下次循环中退出战斗
                                        var (winWidth, winHeight) = WindowHelper.GetWindowSize(hwnd);
                                        controller.Click(winWidth - 70, 50);
                                        Thread.Sleep(1000);
                                        continue;
                                    }
                                    
                                    Console.WriteLine($"  → 怪物距离较远，移动靠近 (尝试 {monsterStuckCount[monsterKey]}/3)");
                                    
                                    // 调整视角对准怪物
                                    AdjustCameraToTarget(battleApi, nearestMonster.Position);
                                    
                                    // 向前移动2秒
                                    controller.SendKeyDown("W");
                                    controller.SendKeyDown("LSHIFT");
                                    Thread.Sleep(2000);
                                    controller.SendKeyUp("LSHIFT");
                                    controller.SendKeyUp("W");
                                    
                                    continue; // 重新检测怪物位置
                                }
                                
                                // 如果能攻击到，重置卡住计数
                                string attackMonsterKey = $"{nearestMonster.Name}_{nearestMonster.EntityId}";
                                if (monsterStuckCount.ContainsKey(attackMonsterKey))
                                {
                                    monsterStuckCount[attackMonsterKey] = 0;
                                }
                                
                                // 调整视角对准怪物
                                AdjustCameraToTarget(battleApi, nearestMonster.Position);
                                
                                // 技能使用逻辑：按 R 后每 2 秒按一次 E，连续 4 次
                                if (skillECount < 6 && (DateTime.Now - lastSkillTime).TotalMilliseconds > 1000)
                                {
                                    if (skillECount == 0)
                                    {
                                        // 第一次：按 R
                                        Console.WriteLine("  → 使用技能 R");
                                        controller.SendKey("Q", 0.1);
                                        lastSkillTime = DateTime.Now;
                                        skillECount++;
                                    }
                                    else
                                    {
                                        // 后续：按 E
                                        Console.WriteLine($"  → 使用技能 E ({skillECount}/6)");
                                        controller.SendKey("E", 0.1);
                                        lastSkillTime = DateTime.Now;
                                        skillECount++;
                                        
                                        // 4 次 E 用完后重置计数
                                        if (skillECount >= 6)
                                        {
                                            skillECount = 0;
                                        }
                                    }
                                }
                                
                                // 按住鼠标右键攻击 0.4秒
                                controller.MouseDown(-1, -1, "right");
                                Thread.Sleep(400);
                                controller.MouseUp(-1, -1, "right");
                                
                                Thread.Sleep(100);
                                
                                // 每3秒进行一次OCR检测（检测各种界面状态）
                                if ((DateTime.Now - lastBattleOcrTime).TotalMilliseconds > BATTLE_OCR_INTERVAL_MS)
                                {
                                    using var battleCheck = ScreenCapture.CaptureWindow(hwnd);
                                    if (battleCheck != null)
                                    {
                                        var battleResult = ocrEngine.Recognize(battleCheck);
                                        
                                        // 检测"复苏"
                                        bool needRevive = battleResult.Regions.Any(r => 
                                            r.Text.Contains("复苏") && !r.Text.Contains("获得遗物"));
                                        
                                        if (needRevive)
                                        {
                                            Console.WriteLine("  ⚠ 检测到复苏提示，按住 X 键");
                                            controller.SendKeyDown("X");
                                            Thread.Sleep(2000);
                                            controller.SendKeyUp("X");
                                            Console.WriteLine("  → 复苏完成");
                                            lastBattleOcrTime = DateTime.Now;
                                            continue;
                                        }
                                        
                                        // 检测"激活套装"或"获得烛芯"
                                        var activateSetup = battleResult.Regions.FirstOrDefault(r => 
                                            r.Text.Contains("激活套装") || r.Text.Contains("获得烛芯"));
                                        
                                        if (activateSetup != null)
                                        {
                                            int clickX = (int)activateSetup.Center.X;
                                            int clickY = (int)activateSetup.Center.Y + 50;
                                            Console.WriteLine($"  → 战斗中检测到{activateSetup.Text}，点击下方按钮");
                                            controller.Click(clickX, clickY);
                                            Thread.Sleep(1000);
                                            lastBattleOcrTime = DateTime.Now;
                                            continue;
                                        }
                                        
                                        // 检测"点击空白处继续"
                                        var clickToContinue = battleResult.Regions.FirstOrDefault(r => 
                                            r.Text.Contains("点击空白"));
                                        
                                        if (clickToContinue != null)
                                        {
                                            Console.WriteLine("  → 战斗中检测到点击空白处继续");
                                            controller.Click((int)clickToContinue.Center.X, (int)clickToContinue.Center.Y);
                                            Thread.Sleep(1000);
                                            lastBattleOcrTime = DateTime.Now;
                                            continue;
                                        }
                                        
                                        // 检测烛芯选择界面
                                        bool battleCandleSelection = battleResult.Regions.Any(r => 
                                            r.Text.Contains("选择") && r.Text.Contains("烛芯"));
                                        
                                        if (battleCandleSelection)
                                        {
                                            Console.WriteLine("  → 战斗中检测到烛芯选择，暂停战斗处理界面");
                                            inBattle = false; // 退出战斗循环，让主循环处理
                                            break;
                                        }
                                        
                                        // 检测Buff选择界面
                                        bool battleBuffSelection = battleResult.Regions.Any(r => 
                                            r.Text.Contains("上次探索") || r.Text.Contains("探索过深渊"));
                                        
                                        if (battleBuffSelection)
                                        {
                                            Console.WriteLine("  → 战斗中检测到Buff选择，暂停战斗处理界面");
                                            inBattle = false;
                                            break;
                                        }
                                        
                                        // 检测遗物选择界面
                                        bool battleRelicSelection = battleResult.Regions.Any(r => 
                                            r.Text.Contains("上次探索过深渊") && r.Text.Contains("层"));
                                        
                                        if (battleRelicSelection)
                                        {
                                            Console.WriteLine("  → 战斗中检测到遗物选择，暂停战斗处理界面");
                                            inBattle = false;
                                            break;
                                        }
                                    }
                                    
                                    lastBattleOcrTime = DateTime.Now;
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"战斗循环错误: {ex.Message}");
                                inBattle = false;
                            }
                        }
                        
                        Console.WriteLine("<<< 退出战斗模式 >>>");
                        Thread.Sleep(2000);
                        continue;
                    }
                    
                    // 检测"探索详情"
                    bool hasExploreDetails = result.Regions.Any(r => r.Text.Contains("探索详情"));
                    
                    if (hasExploreDetails)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到探索详情界面");
                        
                        // 查找并点击标签
                        var emberTab = result.Regions.FirstOrDefault(r => r.Text.Contains("[余烬]") || r.Text.Contains("余烬"));
                        var candleTab = result.Regions.FirstOrDefault(r => r.Text.Contains("[烛芯]") || r.Text.Contains("烛芯"));
                        var relicTab = result.Regions.FirstOrDefault(r => r.Text.Contains("[遗物]") || r.Text.Contains("遗物"));
                        var gameTab = result.Regions.FirstOrDefault(r => r.Text.Contains("炮台轰击") || r.Text.Contains("开始游戏"));
                        if (candleTab != null)
                        {
                            Console.WriteLine("  → 点击 [烛芯]");
                            controller.Click((int)candleTab.Center.X, (int)candleTab.Center.Y);
                            Thread.Sleep(500);
                        }                        
                        if (emberTab != null)
                        {
                            Console.WriteLine("  → 点击 [余烬]");
                            controller.Click((int)emberTab.Center.X, (int)emberTab.Center.Y);
                            Thread.Sleep(500);
                        }
                        
                        
                        if (relicTab != null)
                        {
                            Console.WriteLine("  → 点击 [遗物]");
                            controller.Click((int)relicTab.Center.X, (int)relicTab.Center.Y);
                            Thread.Sleep(500);
                        }
                                             
                        if (gameTab != null)
                        {
                            Console.WriteLine("  → 点击 [开始游戏]");
                            controller.Click((int)gameTab.Center.X, (int)gameTab.Center.Y);
                            Thread.Sleep(500);
                        }
                        
                        // 最后按空格键关闭（长按1秒）
                        Console.WriteLine("  → 按空格键关闭（长按1秒）");
                        controller.SendKey("SPACE", 1.2);
                        Thread.Sleep(100);
                        continue;
                    }
                    
                    // 检测100米内是否有簧火机关
                    try
                    {
                        var entities = navApi.GetBattleEntities();
                        var cameraLoc = navApi.GetCameraLocation();

                        // 查找100米内的 BP_OpenUIMechanism_Rouge_C（过滤CanOpen和OpenState）
                        var fireMechanism = entities
                            .Where(e => e.Name == "BP_OpenUIMechanism_Rouge_C")
                            .Where(e => e.CanOpen && !e.OpenState) // 过滤CanOpen和OpenState
                            .Where(e => CalculateDistance(cameraLoc, e.Position) <= 10000) // 100米 = 10000单位
                            .OrderBy(e => CalculateDistance(cameraLoc, e.Position))
                            .FirstOrDefault();
                            
                            
                        var gameMechanism = entities
                            .Where(e => e.IsActor && e.Name == "BP_Paotai_Rouge01_C")
                            //.Where(e => e.IsActive) // 过滤CanOpen和OpenState
                            .Where(e => CalculateDistance(cameraLoc, e.Position) <= 10000) // 100米 = 10000单位
                            .OrderBy(e => CalculateDistance(cameraLoc, e.Position))
                            .FirstOrDefault();
                        if (gameMechanism != null) {
                        var allDeliveries = entities
                            .Where(e => e.IsActor && e.ClassName.Contains("RougeLikeDelivery"))
                            .Select(e => new { 
                                Entity = e, 
                                Distance = CalculateDistance(cameraLoc, e.Position) 
                            })
                            .OrderBy(x => x.Distance)
                            .ToList();
                                                // 定义目标优先级
                        string[] targetPriority = new[]
                        {
                            "BP_RougeLikeDelivery_Shop_C",
                            "BP_RougeLikeDelivery_Event_C",
                            "BP_RougeLikeDelivery_Battle_2_C",
                            "BP_RougeLikeDelivery_Battle_C",
                            "BP_RougeLikeDelivery_EliteBattle_C",
                            "BP_RougeLikeDelivery_Boss_C",
                        };
                        if (allDeliveries.Count > 0)
                        {
                            Console.WriteLine($"  发现 {allDeliveries.Count} 个传送点:");
                            foreach (var d in allDeliveries.Take(5))
                            {
                                Console.WriteLine($"    - {d.Entity.ClassName} 距离: {d.Distance/100:F1}米");
                            }
                        }
                        
                            fireMechanism = gameMechanism;
                        foreach (var priority in targetPriority)
                        {
                            // 查找符合类型且距离在300米内的目标，排除失败的传送点
                            var targetDelivery = entities
                                .Where(e => e.IsActor && e.ClassName == priority)
                                .Where(e => CalculateDistance(cameraLoc, e.Position) <= 30000) // 300米 = 30000单位
                                .Where(e => !failedDeliveryIds.Contains(e.EntityId)) // 排除失败的传送点
                                .FirstOrDefault();
                            
                            if (targetDelivery != null)
                            {
                                
                                fireMechanism = targetDelivery;
                                break;
                            }
                        }
                        
                        }

                        if (fireMechanism != null)
                        {
                            float distance = CalculateDistance(cameraLoc, fireMechanism.Position);
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到100米内有簧火机关，距离: {distance/100:F1}米");
                            Console.WriteLine(">>> 开始导航到簧火机关 <<<");
                            
                            // 使用通用导航函数（20秒超时，需要交互）
                            bool fireNavSuccess = NavigateToTarget(navApi, fireMechanism.Position, 20, true);
                            
                            if (fireNavSuccess)
                            {
                                Console.WriteLine("<<< 完成簧火交互 >>>");
                            }
                            else
                            {
                                Console.WriteLine("  ⚠ 簧火导航失败");
                            }
                            
                            Thread.Sleep(2000);
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"检测簧火机关失败: {ex.Message}");
                    }
                    
                    // 检测"前往下一层深渊"或"休整"
                    bool hasNextFloorPrompt = false;
                    foreach (var region in result.Regions)
                    {
                        string text = region.Text.Trim();
                        
                        // 排除标题文字和按钮文字
                        if (text.Contains("深渊卷") || text.Contains("之国") || 
                            text.Contains("坠入") || text == "深渊")
                        {
                            continue;
                        }
                        
                        // 只匹配导航相关的文字
                        if (text.Contains("前往") || 
                            text.Contains("下一层") || 
                            text.Contains("休整"))
                        {
                            hasNextFloorPrompt = true;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到导航提示: {text}");
                            break;
                        }
                    }
                    
                    // 每 20 次检测输出一次所有识别到的文字（用于调试）
                    if (checkCount % 20 == 0 && result.Regions.Count > 0)
                    {
                        Console.WriteLine($"[调试] OCR 识别到 {result.Regions.Count} 个文字区域:");
                        foreach (var region in result.Regions.Take(10))
                        {
                            Console.WriteLine($"  - \"{region.Text}\" 位置:({region.Center.X:F0}, {region.Center.Y:F0})");
                        }
                    }
                    
                    // 检测确认对话框（"是否放弃"）
                    bool hasConfirmDialog = result.Regions.Any(r => 
                        r.Text.Contains("是否放弃") || r.Text.Contains("放弃本次选择"));
                    
                    if (hasConfirmDialog)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到确认对话框");
                        
                        // 查找"确定"按钮
                        var confirmBtn = result.Regions.FirstOrDefault(r => 
                            r.Text.Trim() == "确定" || r.Text.Trim().Contains("确定"));
                        
                        if (confirmBtn != null)
                        {
                            Console.WriteLine("  → 点击确定");
                            controller.Click((int)confirmBtn.Center.X, (int)confirmBtn.Center.Y);
                            Thread.Sleep(1000);
                            continue;
                        }
                    }
                    
                    // 检测遗物选择界面（"上次探索过深渊X层"）
                    bool hasRelicSelection = result.Regions.Any(r => 
                        r.Text.Contains("上次探索过深渊") && r.Text.Contains("层"));
                    
                    if (hasRelicSelection)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到遗物选择界面");
                        
                        // 查找"放弃"或"确定"按钮（不受左边1/3限制）
                        var giveUpBtn = result.Regions.FirstOrDefault(r => 
                            r.Text.Trim() == "放弃" || r.Text.Trim().Contains("放弃"));
                        var confirmBtn = result.Regions.FirstOrDefault(r => 
                            r.Text.Trim() == "确定" || r.Text.Trim().Contains("确定"));
                        
                        if (giveUpBtn != null)
                        {
                            Console.WriteLine("  → 点击放弃");
                            stateStartTime = DateTime.Now; // 重置超时计时器
                            controller.Click((int)giveUpBtn.Center.X, (int)giveUpBtn.Center.Y);
                            Thread.Sleep(1000);
                            continue;
                        }
                        else if (confirmBtn != null)
                        {
                            Console.WriteLine("  → 点击确定");
                            stateStartTime = DateTime.Now; // 重置超时计时器
                            controller.Click((int)confirmBtn.Center.X, (int)confirmBtn.Center.Y);
                            Thread.Sleep(1000);
                            continue;
                        }
                        else
                        {
                            Console.WriteLine("  ⚠ 未找到放弃或确定按钮，尝试按ESC关闭");
                            controller.SendKey("ESC", 0.1);
                            Thread.Sleep(1000);
                            continue;
                        }
                    }
                    
                    if (hasNextFloorPrompt)
                    {
                        Console.WriteLine(">>> 开始前往下一层深渊 <<<");
                        
                        
                        // 定义目标优先级
                        string[] targetPriority = new[]
                        {
                            "BP_RougeLikeDelivery_Shop_C",
                            "BP_RougeLikeDelivery_Event_C",
                            "BP_RougeLikeDelivery_Battle_2_C",
                            "BP_RougeLikeDelivery_Battle_C",
                            "BP_RougeLikeDelivery_EliteBattle_C",
                            "BP_RougeLikeDelivery_Boss_C",
                        };
                        
                        // 查找目标
                        var entities = navApi.GetBattleEntities();
                        var cameraLoc = navApi.GetCameraLocation();
                        EntityInfo? targetDelivery = null;
                        
                        // 先列出所有传送点及其距离（调试用）
                        var allDeliveries = entities
                            .Where(e => e.IsActor && e.ClassName.Contains("RougeLikeDelivery"))
                            .Select(e => new { 
                                Entity = e, 
                                Distance = CalculateDistance(cameraLoc, e.Position) 
                            })
                            .OrderBy(x => x.Distance)
                            .ToList();
                        
                        if (allDeliveries.Count > 0)
                        {
                            Console.WriteLine($"  发现 {allDeliveries.Count} 个传送点:");
                            foreach (var d in allDeliveries.Take(5))
                            {
                                Console.WriteLine($"    - {d.Entity.ClassName} 距离: {d.Distance/100:F1}米");
                            }
                        }
                        
                        foreach (var priority in targetPriority)
                        {
                            // 查找符合类型且距离在300米内的目标，排除失败的传送点
                            targetDelivery = entities
                                .Where(e => e.IsActor && e.ClassName == priority)
                                .Where(e => CalculateDistance(cameraLoc, e.Position) <= 30000) // 300米 = 30000单位
                                .Where(e => !failedDeliveryIds.Contains(e.EntityId)) // 排除失败的传送点
                                .FirstOrDefault();
                            
                            if (targetDelivery != null)
                            {
                                float distance = CalculateDistance(cameraLoc, targetDelivery.Position);
                                Console.WriteLine($"  ✓ 找到目标: {targetDelivery.ClassName} 距离: {distance/100:F1}米 (ID: {targetDelivery.EntityId})");
                                lastDeliveryType = targetDelivery.ClassName; // 记录传送点类型
                                
                                // 如果是精英或Boss，标记需要移动
                                if (targetDelivery.ClassName == "BP_RougeLikeDelivery_EliteBattle_C" || 
                                    targetDelivery.ClassName == "BP_RougeLikeDelivery_Boss_C")
                                {
                                    needMoveBeforeBattle = true;
                                }
                                
                                break;
                            }
                        }
                        
                        if (targetDelivery == null)
                        {
                            Console.WriteLine("  ✗ 未找到300米内的优先传送点");
                            failedDeliveryIds.Clear(); // 清空黑名单
                            Console.WriteLine("  → 已清空黑名单");
                            Thread.Sleep(2000);
                            continue;
                        }
                        
                        Console.WriteLine($"  目标位置: {targetDelivery.Position}");
                        
                        // 使用通用导航函数
                        bool navSuccess = NavigateToTarget(navApi, targetDelivery.Position, 60, true);
                        
                        // 处理导航结果
                        if (!navSuccess)
                        {
                            // 导航失败，加入黑名单
                            failedDeliveryIds.Add(targetDelivery.EntityId);
                            Console.WriteLine($"  → 已将传送点 ID {targetDelivery.EntityId} 加入黑名单");
                        }
                        else
                        {
                            // 导航成功，清空黑名单
                            failedDeliveryIds.Clear();
                            Console.WriteLine("  → 导航成功，清空黑名单");
                        }
                        
                        Console.WriteLine("<<< 退出导航模式 >>>");
                        
                        // 根据传送点类型决定是否需要移动
                        if (lastDeliveryType == "BP_RougeLikeDelivery_Shop_C")
                        {
                            // 商店：移动6秒
                            Console.WriteLine($"  → 检测到商店，向前移动6秒...");
                            controller.SendKeyDown("W");
                            controller.SendKeyDown("LSHIFT"); // 加速跑
                            Thread.Sleep(6000);
                            controller.SendKeyUp("LSHIFT");
                            controller.SendKeyUp("W");
                            Thread.Sleep(500);
                            Console.WriteLine("  → 移动完成");
                        }
                        
                        Thread.Sleep(500);
                        continue;
                    }
                    
                    // 检测"点击空白处继续"
                    bool hasClickToContinue = false;
                    OcrEngine.OcrTextRegion? clickToContinueRegion = null;
                    
                    foreach (var region in result.Regions)
                    {
                        string text = region.Text.Trim();
                        if (text.Contains("点击空白"))
                        {
                            hasClickToContinue = true;
                            clickToContinueRegion = region;
                            break;
                        }
                    }
                    
                    if (hasClickToContinue && clickToContinueRegion != null)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到: {clickToContinueRegion.Text}");
                        int clickX = (int)clickToContinueRegion.Center.X;
                        int clickY = (int)clickToContinueRegion.Center.Y;
                        controller.Click(clickX, clickY);
                        Console.WriteLine($"  → 已点击文字位置 ({clickX}, {clickY})");
                        Thread.Sleep(1000);
                        continue; // 处理完后继续下一轮循环
                    }
                    
                    // 查找目标文字（按优先级匹配）
                    bool found = false;
                    OcrEngine.OcrTextRegion? targetRegion = null;
                    string matchedType = "";
                    
                    // 检测烛芯选择界面
                    bool hasCandleSelection = false;
                    OcrEngine.OcrTextRegion? candleEaterRegion = null; // 噬影蝶
                    OcrEngine.OcrTextRegion? candleMoonRegion = null;  // 浮海月
                    OcrEngine.OcrTextRegion? candleGiveUpRegion = null; // 放弃
                    OcrEngine.OcrTextRegion? candleConfirmRegion = null; // 确定
                    
                    foreach (var region in result.Regions)
                    {
                        string text = region.Text.Trim();
                        
                        // 【过滤】左边 1/3 的区域不要点击（1920 / 3 = 640）
                        if (region.Center.X < 640)
                        {
                            continue;
                        }
                        
                        if (text.Contains("选择") && text.Contains("烛芯"))
                        {
                            hasCandleSelection = true;
                        }
                        if (text.Contains("激活套装") || text.Contains("获得烛芯"))
                        {
                            // 检测到"激活套装"或"获得烛芯"，点击下方50像素
                            int clickX = (int)region.Center.X;
                            int clickY = (int)region.Center.Y + 50;
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到{text}，点击下方按钮 ({clickX}, {clickY})");
                            controller.Click(clickX, clickY);
                            Thread.Sleep(1000);
                            continue; // 处理完后继续下一轮循环
                        }
                        if (text.Contains("噬影蝶"))
                        {
                            candleEaterRegion = region;
                        }
                        if (text.Contains("浮海月"))
                        {
                            candleMoonRegion = region;
                        }
                        if (text == "放弃" || text.Contains("放弃"))
                        {
                            candleGiveUpRegion = region;
                        }
                        if (text == "确定" || text.Contains("确定"))
                        {
                            candleConfirmRegion = region;
                        }
                    }
                    
                    // 处理烛芯选择
                    if (hasCandleSelection)
                    {
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到烛芯选择界面");
                        
                        if (candleEaterRegion != null)
                        {
                            Console.WriteLine("  → 发现噬影蝶，点击选择");
                            controller.Click((int)candleEaterRegion.Center.X, (int)candleEaterRegion.Center.Y);
                            Thread.Sleep(500);
                            
                            // 点击选择按钮
                            var selectBtn = result.Regions.FirstOrDefault(r => r.Text.Contains("选择") && !r.Text.Contains("烛芯"));
                            if (selectBtn != null)
                            {
                                Console.WriteLine("  → 点击选择按钮");
                                controller.Click((int)selectBtn.Center.X, (int)selectBtn.Center.Y);
                                Thread.Sleep(500);
                            }
                        }
                        else if (candleMoonRegion != null)
                        {
                            Console.WriteLine("  → 发现浮海月，点击选择");
                            controller.Click((int)candleMoonRegion.Center.X, (int)candleMoonRegion.Center.Y);
                            Thread.Sleep(500);
                            
                            // 点击选择按钮
                            var selectBtn = result.Regions.FirstOrDefault(r => r.Text.Contains("选择") && !r.Text.Contains("烛芯"));
                            if (selectBtn != null)
                            {
                                Console.WriteLine("  → 点击选择按钮");
                                controller.Click((int)selectBtn.Center.X, (int)selectBtn.Center.Y);
                                Thread.Sleep(500);
                            }
                        }
                        else if (candleGiveUpRegion != null)
                        {
                            Console.WriteLine("  → 没有目标烛芯，点击放弃");
                            controller.Click((int)candleGiveUpRegion.Center.X, (int)candleGiveUpRegion.Center.Y);
                            Thread.Sleep(500);
                            
                            // 重新截图识别确定按钮
                            using var confirmCheck = ScreenCapture.CaptureWindow(hwnd);
                            if (confirmCheck != null)
                            {
                                var confirmResult = ocrEngine.Recognize(confirmCheck);
                                candleConfirmRegion = confirmResult.Regions.FirstOrDefault(r => 
                                    r.Text.Trim() == "确定" || r.Text.Trim().Contains("确定"));
                            }
                        }
                        
                        // 最后点击确定
                        if (candleConfirmRegion != null)
                        {
                            Console.WriteLine("  → 点击确定");
                            stateStartTime = DateTime.Now; // 重置超时计时器
                            controller.Click((int)candleConfirmRegion.Center.X, (int)candleConfirmRegion.Center.Y);
                            Thread.Sleep(2000);
                        }
                        else
                        {
                            Console.WriteLine("  ⚠ 未找到确定按钮");
                        }
                        
                        continue; // 处理完烛芯选择后继续下一轮循环
                    }
                    
                    // Buff选择相关
                    OcrEngine.OcrTextRegion? traceBuffRegion = null;  // 行迹获取量提高
                    OcrEngine.OcrTextRegion? rangedBuffRegion = null; // 远程武器伤害提高
                    OcrEngine.OcrTextRegion? giveUpRegion = null;     // 放弃按钮
                    OcrEngine.OcrTextRegion? selectButtonRegion = null; // 选择按钮
                    bool hasAbyssPrompt = false; // 是否有"上次探索过深渊"提示
                    
                    foreach (var region in result.Regions)
                    {
                        string text = region.Text.Trim();
                        
                        // 【过滤】左边 1/3 的区域不要点击（1920 / 3 = 640）
                        bool isLeftThird = region.Center.X < 640;
                        
                        // 检测"上次探索过深渊"提示（不受位置限制）
                        if (text.Contains("上次探索") || text.Contains("探索过深渊"))
                        {
                            hasAbyssPrompt = true;
                        }
                        
                        // 以下按钮只在右边 2/3 区域检测
                        if (isLeftThird)
                        {
                            continue;
                        }
                        
                        // 检测"行迹获取量提高"（最优先）
                        if (text.Contains("行迹") && text.Contains("获取") && text.Contains("提高"))
                        {
                            traceBuffRegion = region;
                        }
                        
                        // 检测"远程武器伤害提高"（次优先）
                        if (text.Contains("远程武器") && text.Contains("伤害") && text.Contains("提高"))
                        {
                            rangedBuffRegion = region;
                        }
                        
                        // 记录"放弃"按钮位置
                        if (text == "放弃" || text.Contains("放弃"))
                        {
                            giveUpRegion = region;
                        }
                        
                        // 记录"选择"按钮位置
                        if (text == "选择" || text.Contains("选择"))
                        {
                            selectButtonRegion = region;
                        }
                        
                        // 优先级1: 匹配"坠入深渊"（排除标题）
                        if (!found && !text.Contains("深渊卷") && !text.Contains("之国") &&
                            (text == "坠入深渊" || 
                            text.StartsWith("坠入深渊") ||
                            (text.Contains("坠入") && text.Contains("深渊") && text.Length <= 6)))
                        {
                            found = true;
                            targetRegion = region;
                            matchedType = "坠入深渊";
                        }
                        
                        // 优先级2: 匹配"开始探索"
                        if (!found && (text == "开始探索" || text.Contains("开始探索")))
                        {
                            found = true;
                            targetRegion = region;
                            matchedType = "开始探索";
                        }
                    }
                    
                    // Buff选择优先级（只在没有其他操作时处理）
                    if (!found)
                    {
                        if (traceBuffRegion != null)
                        {
                            // 最优先：行迹获取量提高
                            found = true;
                            targetRegion = traceBuffRegion;
                            matchedType = "行迹获取量提高";
                        }
                        else if (rangedBuffRegion != null)
                        {
                            // 次优先：远程武器伤害提高
                            found = true;
                            targetRegion = rangedBuffRegion;
                            matchedType = "远程武器伤害提高";
                        }
                        else if (giveUpRegion != null && hasAbyssPrompt)
                        {
                            // 最低优先级：放弃（只在检测到"上次探索过深渊"时点击）
                            found = true;
                            targetRegion = giveUpRegion;
                            matchedType = "放弃(上次探索过深渊)";
                        }
                    }

                    if (found && targetRegion != null)
                    {
                        clickCount++;
                        stateStartTime = DateTime.Now; // 重置超时计时器
                        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测到 [{matchedType}]: {targetRegion.Text}");
                        Console.WriteLine($"  位置: ({targetRegion.Center.X:F0}, {targetRegion.Center.Y:F0})");
                        Console.WriteLine($"  置信度: {targetRegion.Confidence:F2}");
                        
                        // 点击文字中心位置
                        int clickX = (int)targetRegion.Center.X;
                        int clickY = (int)targetRegion.Center.Y;
                        
                        controller.Click(clickX, clickY);
                        Console.WriteLine($"  已点击 ({clickX}, {clickY})");
                        
                        // 如果是选择buff，需要再点击"选择"按钮确认
                        bool isBuffSelection = matchedType == "行迹获取量提高" || 
                                              matchedType == "远程武器伤害提高" || 
                                              matchedType == "放弃(上次探索过深渊)";
                        
                        if (isBuffSelection)
                        {
                            Thread.Sleep(500); // 等待选中效果
                            
                            // 重新截图识别"选择"按钮
                            if (selectButtonRegion == null)
                            {
                                using var selectCheck = ScreenCapture.CaptureWindow(hwnd);
                                if (selectCheck != null)
                                {
                                    var selectResult = ocrEngine.Recognize(selectCheck);
                                    selectButtonRegion = selectResult.Regions.FirstOrDefault(r => 
                                        r.Text.Trim() == "选择" || r.Text.Trim().Contains("选择"));
                                }
                            }
                            
                            if (selectButtonRegion != null)
                            {
                                int selectX = (int)selectButtonRegion.Center.X;
                                int selectY = (int)selectButtonRegion.Center.Y;
                                
                                controller.Click(selectX, selectY);
                                Console.WriteLine($"  已点击选择按钮 ({selectX}, {selectY})");
                                
                                // 等待界面响应，然后查找并点击"确定"按钮
                                Thread.Sleep(500);
                                using var confirmCheck = ScreenCapture.CaptureWindow(hwnd);
                                if (confirmCheck != null)
                                {
                                    var confirmResult = ocrEngine.Recognize(confirmCheck);
                                    var confirmButton = confirmResult.Regions.FirstOrDefault(r => 
                                        r.Text.Trim() == "确定" || r.Text.Trim().Contains("确定"));
                                    
                                    if (confirmButton != null)
                                    {
                                        int confirmX = (int)confirmButton.Center.X;
                                        int confirmY = (int)confirmButton.Center.Y;
                                        controller.Click(confirmX, confirmY);
                                        Console.WriteLine($"  已点击确定按钮 ({confirmX}, {confirmY})");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"  ⚠ 未找到确定按钮");
                                    }
                                }
                            }
                            else
                            {
                                Console.WriteLine($"  ⚠ 未找到选择按钮");
                            }
                        }
                        
                        Console.WriteLine($"  累计点击: {clickCount} 次");
                        Console.WriteLine();
                        
                        // 点击后等待 2 秒
                        Thread.Sleep(2000);
                    }
                    else
                    {
                        // 每 10 次检测输出一次状态
                        if (checkCount % 10 == 0)
                        {
                            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] 检测中... (已检测 {checkCount} 次, 点击 {clickCount} 次)");
                        }
                    }

                    // 每 500ms 检测一次
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"错误: {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        
        
            
            // 辅助函数：执行强制退出
            void PerformForceExit()
            {
                Console.WriteLine("  执行强制退出...");
                
                // 按ESC打开菜单
                controller.SendKey("ESCAPE", 0.1);
                Thread.Sleep(1000);
                
                // 点击"放弃"按钮（窗口中心偏下）
                controller.Click(windowWidth / 2, windowHeight / 2 + 150);
                Thread.Sleep(500);
                
                // 点击确认
                controller.Click(windowWidth / 2 + 100, windowHeight / 2 + 100);
                Thread.Sleep(2000);
                
                Console.WriteLine("  → 已执行强制退出，重置状态");
                
                // 重置所有状态
                inBattle = false;
                lastDeliveryType = "";
                needMoveBeforeBattle = false;
                failedDeliveryIds.Clear();
                battleStartTime = DateTime.MinValue;
                stateStartTime = DateTime.Now; // 重置超时计时器
                
                Thread.Sleep(3000);
            }
            
            // 辅助函数：调整视角对准目标（循环调整直到对准）
            int AdjustCameraToTarget(BattleEntitiesAPI api, FVector targetPos)
            {
                const float MOUSE_SENSITIVITY = 0.2f;
                const int MAX_MOVE = 200; // 增大单次移动量
                const float ANGLE_THRESHOLD = 0.5f; // 角度误差阈值
                
                int moveCount = 0;
                var startTime = DateTime.Now;
                
                // 获取窗口大小
                var (winWidth, winHeight) = WindowHelper.GetWindowSize(hwnd);
                
                // 先把鼠标移到窗口中间
                controller.Move(winWidth / 2, winHeight / 2);
                Thread.Sleep(50); // 等待鼠标移动完成
                
                Console.WriteLine($"  [视角调整] 开始瞄准目标 {targetPos}");
                
                var initialCamRot = api.GetCameraRotation();
                
                for (int i = 0; i < 20; i++)
                {
                    var camLoc = api.GetCameraLocation();
                    var camRot = api.GetCameraRotation();
                    var tarRot = CalculateRotationToTarget(camLoc, targetPos);
                    
                    // 检测摄像机是否卡住（移动3次后角度没有变化）
                    if (i == 3)
                    {
                        float yawChange = Math.Abs(camRot.Yaw - initialCamRot.Yaw);
                        float pitchChange = Math.Abs(camRot.Pitch - initialCamRot.Pitch);
                        
                        if (yawChange < 0.1f && pitchChange < 0.1f)
                        {
                            Console.WriteLine($"  [视角调整] 摄像机未响应，尝试关闭UI");
                            
                            // 截图OCR检测是否有确认按钮
                            using var uiCheck = ScreenCapture.CaptureWindow(hwnd);
                            if (uiCheck != null)
                            {
                                var uiResult = ocrEngine.Recognize(uiCheck);
                                var confirmButton = uiResult.Regions.FirstOrDefault(r => 
                                    r.Text.Trim() == "确定" || r.Text.Trim().Contains("确定"));
                                
                                if (confirmButton != null)
                                {
                                    Console.WriteLine($"  → 检测到确认按钮，点击确定");
                                    controller.Click((int)confirmButton.Center.X, (int)confirmButton.Center.Y);
                                    Thread.Sleep(500);
                                }
                                else
                                {
                                    Console.WriteLine($"  → 未检测到确认按钮，点击右上角关闭");
                                    // 点击右上角（窗口宽度 - 70, 50）
                                    controller.Click(winWidth - 70, 50);
                                    Thread.Sleep(500);
                                }
                            }
                            
                            initialCamRot = api.GetCameraRotation(); // 重新获取初始角度
                        }
                    }
                    
                    // 标准化角度
                    float currentYaw = NormalizeAngle(camRot.Yaw);
                    float currentPitch = NormalizeAngle(camRot.Pitch);
                    float targetYaw = NormalizeAngle(tarRot.Yaw);
                    float targetPitch = NormalizeAngle(tarRot.Pitch);
                    
                    float yawDiff = NormalizeAngle(targetYaw - currentYaw);
                    float pitchDiff = targetPitch - currentPitch;
                    
                    // 如果已经对准，退出
                    if (Math.Abs(yawDiff) < ANGLE_THRESHOLD && Math.Abs(pitchDiff) < ANGLE_THRESHOLD)
                    {
                        var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                        Console.WriteLine($"  [视角调整] 完成！移动 {moveCount} 次，耗时 {elapsed:F0}ms");
                        return moveCount;
                    }
                    
                    // 计算鼠标移动量
                    float pixelsPerDegree = 1.0f / MOUSE_SENSITIVITY;
                    int mouseX = (int)(yawDiff * pixelsPerDegree);
                    int mouseY = (int)(-pitchDiff * pixelsPerDegree);
                    
                    // 限制单次移动量
                    mouseX = Math.Max(-MAX_MOVE, Math.Min(MAX_MOVE, mouseX));
                    mouseY = Math.Max(-MAX_MOVE, Math.Min(MAX_MOVE, mouseY));
                    
                    controller.SendMouseMove(mouseX, mouseY);
                    moveCount++;
                    Thread.Sleep(25); // 等待鼠标移动生效
                }
                return moveCount;
            }
            
            // 辅助函数：执行二段跳
            void PerformDoubleJump(BattleEntitiesAPI navApi, FVector targetPos)
            {
                // 先对着天上按4（视角向上看）
                controller.SendMouseMove(0, -500); // 向上移动视角
                Thread.Sleep(100);
                controller.SendKey("4", 0.1);
                Thread.Sleep(500);
                AdjustCameraToTarget(navApi, targetPos);
                // 执行二段跳
                controller.SendKey("SPACE", 0.1);
                Thread.Sleep(300);
                controller.SendKey("SPACE", 0.1);
                // 执行二段跳
                controller.SendKeyDown("W");
                Thread.Sleep(300);
                controller.SendKeyDown("LSHIFT");
            }
            
            // 辅助函数：通用导航到目标位置
            bool NavigateToTarget(BattleEntitiesAPI navApi, FVector targetPos, int timeoutSeconds = 60, bool needInteract = true)
            {
                Console.WriteLine($"  开始导航到目标位置: {targetPos}");
                
                bool reachedTarget = false;
                bool isMoving = false;
                DateTime lastScanTime = DateTime.MinValue;
                DateTime lastOcrCheckTime = DateTime.MinValue;
                DateTime lastFPressTime = DateTime.MinValue;
                DateTime lastJumpTime = DateTime.MinValue;
                DateTime lastCameraAdjustTime = DateTime.MinValue;
                float lastDistance = 0;
                float initialDistance = 0;
                int stuckCheckCount = 0;
                
                // 获取初始玩家位置
                var initialPlayerLoc = navApi.GetPlayerLocation();
                Console.WriteLine($"  玩家位置: {initialPlayerLoc}");
                
                // 初始调整视角对准目标
                AdjustCameraToTarget(navApi, targetPos);
                lastCameraAdjustTime = DateTime.Now;
                
                // 记录初始距离
                initialDistance = CalculateDistance(initialPlayerLoc, targetPos);
                lastDistance = initialDistance;
                
                DateTime navStartTime = DateTime.Now;
                const int NAV_STUCK_CHECK_SECONDS = 30;
                Thread.Sleep(1000);
                while (!reachedTarget)
                {
                    try
                    {
                        // 检查超时
                        if ((DateTime.Now - navStartTime).TotalSeconds > timeoutSeconds)
                        {
                            Console.WriteLine($"  ⚠ 导航超时（{timeoutSeconds}秒），退出导航");
                            if (isMoving)
                            {
                                controller.SendKeyUp("W");
                                controller.SendKeyUp("LSHIFT");
                            }
                            return false;
                        }
                        
                        // 检查是否卡住
                        if ((DateTime.Now - navStartTime).TotalSeconds > NAV_STUCK_CHECK_SECONDS)
                        {
                            var currentPlayerLoc = navApi.GetPlayerLocation();
                            float currentDistance = CalculateDistance(currentPlayerLoc, targetPos);
                            
                            if (currentDistance > initialDistance * 0.7f)
                            {
                                Console.WriteLine($"  ⚠ 导航{NAV_STUCK_CHECK_SECONDS}秒后距离仍为 {currentDistance/100:F1}米（初始 {initialDistance/100:F1}米），放弃");
                                if (isMoving)
                                {
                                    controller.SendKeyUp("W");
                                    controller.SendKeyUp("LSHIFT");
                                }
                                return false;
                            }
                        }
                        
                        // 每8秒重新调整一次视角
                        if ((DateTime.Now - lastCameraAdjustTime).TotalMilliseconds > 8000)
                        {
                            Console.WriteLine("  → 重新调整视角");
                            
                            if (isMoving)
                            {
                                controller.SendKeyUp("W");
                                controller.SendKeyUp("LSHIFT");
                                isMoving = false;
                            }
                            
                            AdjustCameraToTarget(navApi, targetPos);
                            lastCameraAdjustTime = DateTime.Now;
                            Thread.Sleep(200);
                        }
                        
                        // 每25ms更新一次距离和移动状态
                        if ((DateTime.Now - lastScanTime).TotalMilliseconds > 25)
                        {
                            var playerLocation = navApi.GetPlayerLocation();
                            float distance = CalculateDistance(playerLocation, targetPos);
                            if (distance > 20000)
                            {
                                Console.WriteLine($"距离: {distance/100:F1}米");
                                return false;
                            }

                            // 检测位置重置
                            if (lastDistance > 0 && distance > lastDistance + 1000)
                            {
                                stuckCheckCount++;
                                Console.WriteLine($"  ⚠ 检测到位置被重置（{stuckCheckCount}/3）");
                                
                                if (stuckCheckCount >= 3)
                                {
                                    Console.WriteLine("  ⚠ 多次被重置位置，放弃");
                                    if (isMoving)
                                    {
                                        controller.SendKeyUp("W");
                                        controller.SendKeyUp("LSHIFT");
                                    }
                                    return false;
                                }
                            }
                            
                            // 根据距离控制移动
                            if (distance > 2000) // 10米以上：冲刺
                            {
                                // 实时判断高度差，决定是否跳跃
                                if ((DateTime.Now - lastJumpTime).TotalMilliseconds > 1000)
                                {
                                    var currentPlayerLoc = navApi.GetPlayerLocation();
                                    float currentHeightDiff = targetPos.Z - currentPlayerLoc.Z;
                                    
                                    if (currentHeightDiff > 50) // 高度差超过0.5米才跳
                                    {
                                        Console.WriteLine($"  → 二段跳（高度差 {currentHeightDiff/100:F1}米）");
                                        PerformDoubleJump(navApi, targetPos);
                                        lastJumpTime = DateTime.Now;
                                        isMoving = false;
                                    }
                                }

                                if (!isMoving)
                                {
                                    AdjustCameraToTarget(navApi, targetPos);
                                    Console.WriteLine("  → 开始移动（冲刺）");
                                    controller.SendKeyDown("W");
                                    Thread.Sleep(300);
                                    controller.SendKeyDown("LSHIFT");
                                    isMoving = true;
                                }
                            }
                            else if (distance > 600) // 4-10米：普通移动
                            {
                                // 实时判断高度差，决定是否跳跃
                                if ((DateTime.Now - lastJumpTime).TotalMilliseconds > 1000)
                                {
                                    var currentPlayerLoc = navApi.GetPlayerLocation();
                                    float currentHeightDiff = targetPos.Z - currentPlayerLoc.Z;
                                    
                                    if (currentHeightDiff > 50) // 高度差超过0.5米才跳
                                    {
                                        Console.WriteLine($"  → 二段跳（高度差 {currentHeightDiff/100:F1}米）");
                                        PerformDoubleJump(navApi, targetPos);
                                        lastJumpTime = DateTime.Now;
                                        isMoving = false;
                                    }
                                }
                                if (isMoving)
                                {
                                    AdjustCameraToTarget(navApi, targetPos);
                                    Console.WriteLine("  → 取消冲刺，普通移动");
                                    controller.SendKeyUp("LSHIFT");
                                    controller.SendKeyUp("W");
                                    isMoving = false;
                                }
                                controller.SendKeyDown("W");
                            }
                            else if (distance > 350) // 4-10米：普通移动
                            {
                                // 实时判断高度差，决定是否跳跃
                                if ((DateTime.Now - lastJumpTime).TotalMilliseconds > 1000)
                                {
                                    var currentPlayerLoc = navApi.GetPlayerLocation();
                                    float currentHeightDiff = targetPos.Z - currentPlayerLoc.Z;
                                    
                                    if (currentHeightDiff > 50) // 高度差超过0.5米才跳
                                    {
                                        Console.WriteLine($"  → 二段跳（高度差 {currentHeightDiff/100:F1}米）");
                                        PerformDoubleJump(navApi, targetPos);
                                        lastJumpTime = DateTime.Now;
                                        isMoving = false;
                                    }
                                }
                                if (isMoving)
                                {
                                    AdjustCameraToTarget(navApi, targetPos);
                                    Console.WriteLine("  → 取消冲刺，普通移动");
                                    controller.SendKeyUp("LSHIFT");
                                    controller.SendKeyUp("W");
                                    isMoving = false;
                                }
                                controller.SendKeyDown("W");
                            }
                            else // 4米以内：停止移动
                            {
                                if (isMoving)
                                {
                                    AdjustCameraToTarget(navApi, targetPos);
                                    Console.WriteLine("  → 停止移动（进入交互范围）");
                                    controller.SendKeyUp("W");
                                    controller.SendKeyUp("LSHIFT");
                                    isMoving = false;
                                }
                                else
                                {
                                    controller.SendKeyUp("W");
                                }
                                
                                // 如果需要交互，按F键
                                if (needInteract && (DateTime.Now - lastFPressTime).TotalMilliseconds > 300)
                                {
                                    controller.SendKey("F", 0.1);
                                    Console.WriteLine("  → 按 F 键交互");
                                    lastFPressTime = DateTime.Now;
                                }
                            }
                            
                            if (Math.Abs(distance - lastDistance) > 100)
                            {
                                AdjustCameraToTarget(navApi, targetPos);
                                Console.WriteLine($"  距离目标: {distance:F0} 单位 ({distance/100:F1}米)");
                                lastDistance = distance;
                            }
                            
                            lastScanTime = DateTime.Now;
                        }
                        
                        Thread.Sleep(10);
                        
                        // 每500ms检查一次OCR（仅在需要交互时）
                        if (needInteract && (DateTime.Now - lastOcrCheckTime).TotalMilliseconds > 1000)
                        {
                            using var navCheck = ScreenCapture.CaptureWindow(hwnd);
                            if (navCheck != null)
                            {
                                var navResult = ocrEngine.Recognize(navCheck);
                                if(navResult.Regions.Any(r => 
                                    r.Text.Contains("探索详情"))){
                                    Console.WriteLine("  ✓ 导航成功");
                                    return true;
                                }

                                bool stillHasPrompt = navResult.Regions.Any(r => 
                                    r.Text.Contains("前往") || 
                                    r.Text.Contains("下一层") || 
                                    r.Text.Contains("深渊") ||
                                    r.Text.Contains("查看火光") ||
                                    r.Text.Contains("休整"));
                                
                                if (!stillHasPrompt)
                                {
                                    Console.WriteLine("  ✓ 提示消失，导航成功");
                                    if (isMoving)
                                    {
                                        controller.SendKeyUp("W");
                                        controller.SendKeyUp("LSHIFT");
                                    }
                                    return true;
                                }
                            }
                            
                            lastOcrCheckTime = DateTime.Now;
                        }
                        
                        // 如果不需要交互，距离小于5米就算成功
                        if (!needInteract)
                        {
                            var playerLocation = navApi.GetPlayerLocation();
                            float distance = CalculateDistance(playerLocation, targetPos);
                            if (distance < 500)
                            {
                                Console.WriteLine($"  ✓ 到达目标位置（距离 {distance/100:F1}米）");
                                if (isMoving)
                                {
                                    controller.SendKeyUp("W");
                                    controller.SendKeyUp("LSHIFT");
                                }
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  导航错误: {ex.Message}");
                        if (isMoving)
                        {
                            controller.SendKeyUp("W");
                            controller.SendKeyUp("LSHIFT");
                        }
                        return false;
                    }
                }
                
                return false;
            }

        }
        
        // ==================== 辅助函数 ====================
        
        static float CalculateDistance(FVector pos1, FVector pos2)
        {
            float dx = pos2.X - pos1.X;
            float dy = pos2.Y - pos1.Y;
            float dz = pos2.Z - pos1.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }
        
        static FRotator CalculateRotationToTarget(FVector from, FVector to)
        {
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float dz = to.Z - from.Z;

            float horizontalDistance = (float)Math.Sqrt(dx * dx + dy * dy);
            float yaw = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            float pitch = (float)(Math.Atan2(dz, horizontalDistance) * 180.0 / Math.PI);

            return new FRotator { Pitch = pitch, Yaw = yaw, Roll = 0 };
        }
        
        static float NormalizeAngle(float angle)
        {
            while (angle > 180) angle -= 360;
            while (angle < -180) angle += 360;
            return angle;
        }
    }
}
