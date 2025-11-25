using System;
using System.Linq;
using System.Threading;

namespace MMP
{
    /// <summary>
    /// 辅助函数 - 智能等待和工具方法
    /// </summary>
    public partial class AutoAbyssStateMachine
    {
        // 当前等待的取消令牌源
        private CancellationTokenSource? _currentWaitCts;
        /// <summary>
        /// 延时等待 - 使用事件驱动，OCR 完成后自动检查是否需要中断
        /// </summary>
        /// <param name="milliseconds">最大等待时长（毫秒）</param>
        /// <param name="allowStateChange">是否允许状态改变时中断（默认 true）</param>
        /// <returns>检测到的新状态，如果超时或不允许状态改变则返回 null</returns>
        private async Task<GameState?> DelayAsync(int milliseconds, bool allowStateChange = true)
        {
            var startTime = DateTime.Now;
            var currentState = CurrentState;
            GameState? detectedState = null;

            // 创建取消令牌
            _currentWaitCts = new CancellationTokenSource();
            var ct = _currentWaitCts.Token;

            // 如果允许状态改变，订阅 OCR 完成事件
            Action<OcrEngine.OcrResult>? ocrHandler = null;
            if (allowStateChange)
            {
                ocrHandler = (ocrResult) =>
                {
                    // OCR 完成后，调用状态决策器
                    var newState = StateDecider(ocrResult, currentState);

                    // 如果检测到状态变化，取消等待
                    if (newState != null && newState != currentState)
                    {
                        var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                        Console.WriteLine($"  [延时中断] OCR 检测到 {currentState} → {newState}（已等待 {elapsed:F0}ms）");
                        detectedState = newState;
                        _currentWaitCts?.Cancel();
                    }
                };

                OnOcrCompleted += ocrHandler;
            }

            try
            {
                // 直接 await Task.Delay，简洁！
                await Task.Delay(milliseconds, ct);
                return detectedState;
            }
            catch (TaskCanceledException)
            {
                // 被取消，返回检测到的状态
                return detectedState;
            }
            finally
            {
                // 清理
                if (ocrHandler != null)
                {
                    OnOcrCompleted -= ocrHandler;
                }
                _currentWaitCts?.Dispose();
                _currentWaitCts = null;
            }
        }

        /// <summary>
        /// 同步版本的 Delay（用于不支持 async 的地方）
        /// </summary>
        private GameState? Delay(int milliseconds, bool allowStateChange = true)
        {
            return DelayAsync(milliseconds, allowStateChange).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 状态决策器 - 根据 OCR 结果和游戏数据，决定应该转换到哪个状态
        /// </summary>
        /// <param name="ocrResult">OCR 识别结果</param>
        /// <param name="currentState">当前状态</param>
        /// <returns>应该转换到的新状态，如果不需要转换则返回 null</returns>
        private GameState? StateDecider(OcrEngine.OcrResult? ocrResult, GameState currentState)
        {
            if (ocrResult == null || ocrResult.Regions == null)
                return null;
            // 优先级从高到低检测各种状态特征
            // 检测主菜单特征
            bool hasMainMenu = ocrResult.Regions.Any(r =>
                r.Text.Contains("坠入深渊") ||
                r.Text.Contains("乐土之国"));
            if (hasMainMenu)
                return GameState.MainMenu;

            // 【最高优先级】关闭 UI
            // 检测需要关闭的弹窗和提示
            bool hasUIToClose = ocrResult.Regions.Any(r =>
                r.Text.Contains("点击空白") ||
                r.Text.Contains("探索完成") ||
                r.Text.Contains("探索成功") ||
                r.Text.Contains("激活套装") ||
                r.Text.Contains("获得烛芯") ||
                r.Text.Contains("获得遗物") ||
                r.Text.Contains("点击任意"));
            if (hasUIToClose)
                return GameState.ClosingUI;

            // 【最高优先级】ForceExiting 状态执行完成后，检测是否回到主菜单
            if (currentState == GameState.ForceExiting)
            {
                // 还在退出过程中，不转换状态
                return null;
            }

            // 【高优先级】复苏
            if (ocrResult.Regions.Any(r => r.Text.Contains("复苏") && !r.Text.Contains("获得遗物")))
                return GameState.Reviving;

            // 【高优先级】探索详情
            if (ocrResult.Regions.Any(r => r.Text.Contains("探索详情")))
            {
                return GameState.ExploreDetails;
            }


            if (ocrResult.Regions.Any(r => r.Text.Contains("上次探索过深渊")))
            {
                return GameState.SelectingBuff;
            }

            if (ocrResult.Regions.Any(r => r.Text.Contains("选择烛芯")))
            {
                return GameState.SelectingCandle;
            }


            // 【中高优先级】簧火机关交互检测（使用 BattleAPI）
            // 簧火机关优先级高于导航，确保及时交互
            if (_battleApi != null)
            {
                try
                {
                    var entities = _battleApi.GetBattleEntities();
                    var cameraLoc = _battleApi.GetCameraLocation();

                    // 检测100米内的簧火机关
                    bool hasFireMechanism = entities.Any(e =>
                        ((e.Name == "BP_OpenUIMechanism_Rouge_C" && e.CanOpen && !e.OpenState) ||
                         (e.IsActor && e.Name == "BP_Paotai_Rouge01_C")) &&
                        CalculateDistance(cameraLoc, e.Position) <= _config.Battle.ApproachDistance * 3);

                    if (hasFireMechanism)
                    {
                        return GameState.InteractingFireMechanism;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [StateDecider] 检测簧火机关失败: {ex.Message}");
                }
            }

            // 【中高优先级】导航状态检测（使用 BattleAPI + OCR）
            // 导航优先级高于战斗，因为可能在战斗区域附近有传送点
            var navigationRegions = ocrResult.Regions.Where(r => r.Text.Contains("前往") || r.Text.Contains("下一层")).ToList();
            bool hasNavigationText = navigationRegions.Any();

            // 如果检测到导航文字，检查是否有可导航的传送点
            if (hasNavigationText)
            {
                if (_battleApi != null)
                {
                    try
                    {
                        var entities = _battleApi.GetBattleEntities();
                        var cameraLoc = _battleApi.GetCameraLocation();

                        // 检查是否有300米内的传送点
                        bool hasDeliveryPoint = entities.Any(e =>
                            e.IsActor &&
                            e.ClassName.Contains("RougeLikeDelivery") &&
                            CalculateDistance(cameraLoc, e.Position) <= _config.Battle.MonsterDetectionRange);

                        if (hasDeliveryPoint)
                        {
                            var navText = string.Join(", ", navigationRegions.Select(r => r.Text));
                            Console.WriteLine($"  [StateDecider] 检测到导航文字和传送点: {navText}");
                            return GameState.Navigating;
                        }
                        else
                        {
                            Console.WriteLine($"  [StateDecider] 检测到导航文字但无传送点，忽略");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  [StateDecider] 检测传送点失败: {ex.Message}");
                    }
                }
                else
                {
                    // 如果没有 BattleAPI，仅依赖 OCR
                    var navText = string.Join(", ", navigationRegions.Select(r => r.Text));
                    Console.WriteLine($"  [StateDecider] 检测到导航文字: {navText}");
                    return GameState.Navigating;
                }
            }

            // 如果已经在导航状态，保持不变
            if (hasNavigationText && currentState == GameState.Navigating)
            {
                return null;
            }

            // 【低优先级】战斗状态检测（使用 BattleAPI）
            // 战斗优先级低于导航，避免在传送点附近误判为战斗
            if (_battleApi != null && currentState != GameState.InBattle)
            {
                try
                {
                    if (ocrResult.Regions.Any(r => r.Text.Contains("战斗") || r.Text.Contains("驱散幽影")))
                        return GameState.InBattle;
                    var entities = _battleApi.GetBattleEntities();
                    var cameraLoc = _battleApi.GetCameraLocation();

                    // 检测300米内是否有存活的怪物
                    bool hasMonsters = entities.Any(e =>
                        e.IsActor &&
                        (e.ClassName.StartsWith("BP_Mon_") || e.ClassName.StartsWith("BP_Boss_")) &&
                        e.ParentClasses.Any(c => c.Contains("MonsterCharacter")) &&
                        !e.AlreadyDead &&
                        CalculateDistance(cameraLoc, e.Position) <= _config.Battle.MonsterDetectionRange);

                    if (hasMonsters)
                    {
                        // 确认不在主界面或其他非战斗界面
                        bool isInMainMenu = ocrResult.Regions.Any(r =>
                            r.Text.Contains("坠入深渊") ||
                            r.Text.Contains("开始探索") ||
                            r.Text.Contains("探索详情") ||
                            r.Text.Contains("选择"));

                        // 确认不在导航界面（导航优先）
                        bool isInNavigating = hasNavigationText;

                        if (!isInMainMenu && !isInNavigating)
                        {
                            Console.WriteLine($"  [StateDecider] 检测到怪物，进入战斗");
                            return GameState.InBattle;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [StateDecider] 检测怪物失败: {ex.Message}");
                }
            }

            // 【最低优先级】主菜单
            // 只有在没有导航文字时，才检测主菜单
            // 获取窗口尺寸用于区域判断
            bool hasMainMenuText = ocrResult.Regions.Any(r =>
                r.Text.Contains("坠入深渊") ||
                r.Text.Contains("开始探索") ||
                r.Text.Contains("乐土之国"));

            // "继续探索" 必须在右下角 1/4 区域
            bool hasContinueExplore = false;
            if (ocrResult.Regions.Any(r =>
                 r.Text.Contains("继续探索"))){

                var (winWidth, winHeight) = WindowHelper.GetWindowSize(_hwnd);
                float rightQuarterX = winWidth * 0.75f;  // 右侧 1/4 区域的起始 X 坐标
                float bottomQuarterY = winHeight * 0.75f; // 底部 1/4 区域的起始 Y 坐标

                hasContinueExplore = ocrResult.Regions.Any(r =>
                 r.Text.Contains("继续探索") &&
                 r.Center.X >= rightQuarterX &&
                 r.Center.Y >= bottomQuarterY);
            }
            if ((hasMainMenuText || hasContinueExplore) && !hasNavigationText)
            {
                if (currentState != GameState.MainMenu)
                    return GameState.MainMenu;
            }

            // 没有检测到状态变化
            return null;
        }



        /// <summary>
        /// 等待直到检测到特定文本（不允许状态改变中断）
        /// </summary>
        private bool WaitForText(string text, int timeoutMs = 5000, bool contains = true)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddMilliseconds(timeoutMs);

            while (DateTime.Now < endTime)
            {
                if (_shouldStop)
                    return false;

                var ocrResult = GetLatestOcrResult();
                if (ocrResult?.Regions != null)
                {
                    bool found = ocrResult.Regions.Any(r =>
                        contains ? r.Text.Contains(text) : r.Text == text);

                    if (found)
                        return true;
                }

                Thread.Sleep(50);
            }

            return false;
        }

        /// <summary>
        /// 等待直到文本消失（不允许状态改变中断）
        /// </summary>
        private bool WaitForTextDisappear(string text, int timeoutMs = 5000)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddMilliseconds(timeoutMs);

            while (DateTime.Now < endTime)
            {
                if (_shouldStop)
                    return false;

                var ocrResult = GetLatestOcrResult();
                if (ocrResult == null || ocrResult.Regions == null)
                    return true; // 没有 OCR 结果也算消失

                bool disappeared = !ocrResult.Regions.Any(r => r.Text.Contains(text));
                if (disappeared)
                    return true;

                Thread.Sleep(50);
            }

            return false;
        }

        /// <summary>
        /// 点击并等待界面响应
        /// </summary>
        private bool ClickAndWait(int x, int y, string expectedText, int timeoutMs = 2000)
        {
            if (_controller == null)
                return false;

            _controller.Click(x, y);
            return WaitForText(expectedText, timeoutMs);
        }

        /// <summary>
        /// 点击按钮并等待其消失
        /// </summary>
        private bool ClickAndWaitDisappear(int x, int y, string buttonText, int timeoutMs = 2000)
        {
            if (_controller == null)
                return false;

            _controller.Click(x, y);
            return WaitForTextDisappear(buttonText, timeoutMs);
        }

        /// <summary>
        /// 按键并等待响应
        /// </summary>
        private bool SendKeyAndWait(string key, string expectedText, int timeoutMs = 2000)
        {
            if (_controller == null)
                return false;

            _controller.SendKey(key, 0.1);
            return WaitForText(expectedText, timeoutMs);
        }

        /// <summary>
        /// 等待并点击出现的按钮
        /// </summary>
        private bool WaitAndClick(string buttonText, int timeoutMs = 5000)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddMilliseconds(timeoutMs);

            while (DateTime.Now < endTime)
            {
                if (_shouldStop)
                    return false;

                var ocrResult = GetLatestOcrResult();
                if (ocrResult != null && ocrResult.Regions != null)
                {
                    var button = ocrResult.Regions.FirstOrDefault(r => r.Text.Contains(buttonText));
                    if (button != null && _controller != null)
                    {
                        Console.WriteLine($"  [等待点击] 找到 {buttonText}，点击");
                        _controller.Click((int)button.Center.X, (int)button.Center.Y);
                        return true;
                    }
                }

                Thread.Sleep(50);
            }

            Console.WriteLine($"  [等待点击] 超时，未找到 {buttonText}");
            return false;
        }

        // 辅助函数
        private static float CalculateDistance(FVector pos1, FVector pos2)
        {
            float dx = pos2.X - pos1.X;
            float dy = pos2.Y - pos1.Y;
            float dz = pos2.Z - pos1.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }



    }
}
