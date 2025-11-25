using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 状态上下文 - 提供状态处理器需要的所有资源
    /// </summary>
    public class StateContext
    {
        public IntPtr WindowHandle { get; }
        public KeyboardMouseController? Controller { get; }
        public BattleEntitiesAPI? BattleApi { get; }
        
        // OCR 相关
        private readonly Func<OcrEngine.OcrResult?> _getLatestOcrResult;
        private readonly Action<Action<OcrEngine.OcrResult>> _subscribeOcrEvent;
        private readonly Action<Action<OcrEngine.OcrResult>> _unsubscribeOcrEvent;
        
        public StateContext(
            IntPtr windowHandle, 
            KeyboardMouseController? controller, 
            BattleEntitiesAPI? battleApi,
            Func<OcrEngine.OcrResult?> getLatestOcrResult,
            Action<Action<OcrEngine.OcrResult>> subscribeOcrEvent,
            Action<Action<OcrEngine.OcrResult>> unsubscribeOcrEvent)
        {
            WindowHandle = windowHandle;
            Controller = controller;
            BattleApi = battleApi;
            _getLatestOcrResult = getLatestOcrResult;
            _subscribeOcrEvent = subscribeOcrEvent;
            _unsubscribeOcrEvent = unsubscribeOcrEvent;
        }
        
        /// <summary>
        /// 延时等待（可被 CancellationToken 中断）
        /// </summary>
        public async Task DelayAsync(int milliseconds, CancellationToken ct)
        {
            try
            {
                await Task.Delay(milliseconds, ct).ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                // 被取消是正常的，不需要处理
            }
        }
        
        /// <summary>
        /// 等待直到检测到特定文本
        /// </summary>
        public async Task<bool> WaitForTextAsync(string text, int timeoutMs, CancellationToken ct)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddMilliseconds(timeoutMs);

            while (DateTime.Now < endTime && !ct.IsCancellationRequested)
            {
                var ocrResult = _getLatestOcrResult();
                if (ocrResult?.Regions != null)
                {
                    bool found = ocrResult.Regions.Any(r => r.Text.Contains(text));
                    if (found)
                        return true;
                }

                await Task.Delay(50, ct);
            }

            return false;
        }
        
        /// <summary>
        /// 等待直到文本消失
        /// </summary>
        public async Task<bool> WaitForTextDisappearAsync(string text, int timeoutMs, CancellationToken ct)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddMilliseconds(timeoutMs);

            while (DateTime.Now < endTime && !ct.IsCancellationRequested)
            {
                var ocrResult = _getLatestOcrResult();
                if (ocrResult == null || ocrResult.Regions == null)
                    return true;

                bool disappeared = !ocrResult.Regions.Any(r => r.Text.Contains(text));
                if (disappeared)
                    return true;

                await Task.Delay(50, ct);
            }

            return false;
        }
        
        /// <summary>
        /// 等待并点击出现的按钮（单个文本）
        /// </summary>
        public async Task<bool> WaitAndClickAsync(string buttonText, int timeoutMs, CancellationToken ct)
        {
            return await WaitAndClickAsync(new[] { buttonText }, timeoutMs, ct);
        }

        /// <summary>
        /// 等待并点击出现的按钮（多个文本，找到任意一个就点击）
        /// </summary>
        public async Task<bool> WaitAndClickAsync(string[] buttonTexts, int timeoutMs, CancellationToken ct)
        {
            var startTime = DateTime.Now;
            var endTime = startTime.AddMilliseconds(timeoutMs);

            while (DateTime.Now < endTime && !ct.IsCancellationRequested)
            {
                var ocrResult = _getLatestOcrResult();
                if (ocrResult != null && ocrResult.Regions != null)
                {
                    // 遍历所有候选文本
                    foreach (var buttonText in buttonTexts)
                    {
                        var button = ocrResult.Regions.FirstOrDefault(r => r.Text.Contains(buttonText));
                        if (button != null && Controller != null)
                        {
                            Console.WriteLine($"  [等待点击] 找到 {buttonText}，点击");
                            Controller.Click((int)button.Center.X, (int)button.Center.Y);
                            return true;
                        }
                    }
                }

                await Task.Delay(50, ct);
            }

            Console.WriteLine($"  [等待点击] 超时，未找到 [{string.Join(", ", buttonTexts)}]");
            return false;
        }

        /// <summary>
        /// 调整视角对准目标（异步版本）
        /// </summary>
        public async Task<int> AdjustCameraToTargetAsync(FVector targetPos, CancellationToken ct = default)
        {
            if (BattleApi == null || Controller == null) return 0;

            const float MOUSE_SENSITIVITY = 0.2f;
            const int MAX_MOVE = 200;
            const float ANGLE_THRESHOLD = 0.5f;

            var startTime = DateTime.Now;
            int moveCount = 0;
            var (winWidth, winHeight) = WindowHelper.GetWindowSize(WindowHandle);

            Controller.Move(winWidth / 2, winHeight / 2);
            await Task.Delay(50, ct);

            for (int i = 0; i < 20; i++)
            {
                var camLoc = BattleApi.GetCameraLocation();
                var camRot = BattleApi.GetCameraRotation();
                var tarRot = CalculateRotationToTarget(camLoc, targetPos);

                float currentYaw = NormalizeAngle(camRot.Yaw);
                float currentPitch = NormalizeAngle(camRot.Pitch);
                float targetYaw = NormalizeAngle(tarRot.Yaw);
                float targetPitch = NormalizeAngle(tarRot.Pitch);

                float yawDiff = NormalizeAngle(targetYaw - currentYaw);
                float pitchDiff = targetPitch - currentPitch;

                if (Math.Abs(yawDiff) < ANGLE_THRESHOLD && Math.Abs(pitchDiff) < ANGLE_THRESHOLD)                    // 如果已经对准，退出
                {
                    var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    Console.WriteLine($"  [视角调整] 完成！移动 {moveCount} 次，耗时 {elapsed:F0}ms");
                    return moveCount;
                }

                float pixelsPerDegree = 1.0f / MOUSE_SENSITIVITY;
                int mouseX = (int)(yawDiff * pixelsPerDegree);
                int mouseY = (int)(-pitchDiff * pixelsPerDegree);

                mouseX = Math.Max(-MAX_MOVE, Math.Min(MAX_MOVE, mouseX));
                mouseY = Math.Max(-MAX_MOVE, Math.Min(MAX_MOVE, mouseY));

                Controller.SendMouseMove(mouseX, mouseY);
                moveCount++;
                await Task.Delay(25, ct);
            }                    
            Console.WriteLine($"  [视角调整] 完成！移动 {moveCount} 次，耗时 {(DateTime.Now - startTime).TotalMilliseconds:F0}ms");


            return moveCount;
        }

        /// <summary>
        /// 执行二段跳（异步版本）
        /// </summary>
        public async Task PerformDoubleJumpAsync(FVector targetPos, CancellationToken ct = default)
        {
            if (BattleApi == null || Controller == null) return;

            Controller.SendMouseMove(0, -500);
            await Task.Delay(100, ct);
            Controller.SendKey("4", 0.1);
            await Task.Delay(500, ct);
            
            await AdjustCameraToTargetAsync(targetPos, ct);
            Controller.MouseDown(key: "right");
            Controller.SendKey("SPACE", 0.1);
            await Task.Delay(300, ct);
            Controller.SendKey("SPACE", 0.1);
            
            Controller.SendKeyDown("W");
            await Task.Delay(300, ct);
            Controller.SendKeyDown("LSHIFT");
        }

        /// <summary>
        /// 通用导航到目标位置（增强版）
        /// </summary>
        public async Task<bool> NavigateToTargetAsync(FVector targetPos, int timeoutSeconds, bool needInteract, CancellationToken ct)
        {
            if (BattleApi == null || Controller == null) return false;

            var initialPlayerLoc = BattleApi.GetPlayerLocation();
            float initialDistance = CalculateDistance(initialPlayerLoc, targetPos);
            
            Console.WriteLine($"  → 开始导航，距离: {initialDistance / 100:F1}米");

            // 初始距离检查
            if (needInteract && initialDistance < 350)
            {
                Controller.SendKey("F", 0.1);
                await Task.Delay(200, ct);
                Controller.SendKey("F", 0.1);
                return true;
            }

            if (!needInteract && initialDistance < 500)
                return true;

            // 初始调整视角
            await AdjustCameraToTargetAsync(targetPos, ct);
            await Task.Delay(1000, ct);

            bool isMoving = false;
            int interactCount = 0;
            int stuckCheckCount = 0;
            float lastDistance = initialDistance;
            
            DateTime navStartTime = DateTime.Now;
            DateTime lastCameraAdjustTime = DateTime.Now;
            DateTime lastPositionCheckTime = DateTime.Now;
            DateTime lastJumpTime = DateTime.MinValue;
            DateTime lastScanTime = DateTime.MinValue;
            FVector lastPosition = initialPlayerLoc;

            const int NAV_STUCK_CHECK_SECONDS = 30;
            const int POSITION_CHECK_SECONDS = 5;

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // 检查普通超时
                    if ((DateTime.Now - navStartTime).TotalSeconds > timeoutSeconds)
                    {
                        Console.WriteLine($"  ⚠ 导航超时（{timeoutSeconds}秒）");
                        return false;
                    }

                    // 检查卡住超时
                    if ((DateTime.Now - navStartTime).TotalSeconds > NAV_STUCK_CHECK_SECONDS)
                    {
                        var currentPlayerLoc = BattleApi.GetPlayerLocation();
                        float currentDistance = CalculateDistance(currentPlayerLoc, targetPos);
                        if (currentDistance > initialDistance * 0.7f)
                        {
                            Console.WriteLine($"  ⚠ 导航{NAV_STUCK_CHECK_SECONDS}秒后距离仍为 {currentDistance / 100:F1}米（初始 {initialDistance / 100:F1}米），放弃");
                            return false;
                        }
                    }

                    // 每25ms更新一次距离和移动状态
                    if ((DateTime.Now - lastScanTime).TotalMilliseconds > 25)
                    {
                        var playerLoc = BattleApi.GetPlayerLocation();
                        float distance = CalculateDistance(playerLoc, targetPos);

                        // 距离过远检查
                        if (distance > 20000)
                        {
                            Console.WriteLine($"  ⚠ 距离过远: {distance / 100:F1}米");
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
                                return false;
                            }
                        }

                        // 到达目标检查
                        if (distance <= 350)
                        {
                            if (isMoving)
                            {
                                Controller.SendKeyUp("W");
                                Controller.SendKeyUp("LSHIFT");
                                isMoving = false;
                            }

                            if (needInteract)
                            {
                                interactCount++;
                                Controller.SendKey("F", 0.1);
                                Console.WriteLine("  → 按 F 键交互");
                                await Task.Delay(300, ct);
                                
                                if (interactCount > 10)
                                    return true;
                            }
                            else
                            {
                                Console.WriteLine($"  ✓ 到达目标位置（距离 {distance / 100:F1}米）");
                                return true;
                            }
                        }
                        else if (distance > 2000) // 20米以上：冲刺
                        {
                            // 检查高度差，决定是否跳跃
                            if ((DateTime.Now - lastJumpTime).TotalMilliseconds > 1000)
                            {
                                float heightDiff = targetPos.Z - playerLoc.Z;
                                if (heightDiff > 50)
                                {
                                    Console.WriteLine($"  → 二段跳（高度差 {heightDiff / 100:F1}米）");
                                    await PerformDoubleJumpAsync(targetPos, ct);
                                    lastJumpTime = DateTime.Now;
                                    isMoving = false;
                                }
                            }

                            if (!isMoving)
                            {
                                await AdjustCameraToTargetAsync(targetPos, ct);
                                Console.WriteLine("  → 开始移动（冲刺）");
                                Controller.SendKey("4", 0.1);
                                await Task.Delay(300, ct);
                                Controller.SendKeyDown("W");
                                await Task.Delay(300, ct);
                                Controller.SendKeyDown("LSHIFT");
                                isMoving = true;
                            }
                        }
                        else if (distance > 600) // 6-20米：普通移动
                        {
                            // 检查高度差
                            if ((DateTime.Now - lastJumpTime).TotalMilliseconds > 1000)
                            {
                                float heightDiff = targetPos.Z - playerLoc.Z;
                                if (heightDiff > 50)
                                {
                                    Console.WriteLine($"  → 二段跳（高度差 {heightDiff / 100:F1}米）");
                                    await PerformDoubleJumpAsync(targetPos, ct);
                                    lastJumpTime = DateTime.Now;
                                    isMoving = false;
                                }
                            }

                            if (isMoving)
                            {
                                await AdjustCameraToTargetAsync(targetPos, ct);
                                Console.WriteLine("  → 取消冲刺，普通移动");
                                Controller.SendKeyUp("LSHIFT");
                                Controller.SendKeyUp("W");
                                isMoving = false;
                            }
                            Controller.SendKeyDown("W");
                        }
                        else // 3.5-6米：慢速接近
                        {
                            Controller.SendKey("SPACE", 0.1);
                            
                            // 检查高度差
                            if ((DateTime.Now - lastJumpTime).TotalMilliseconds > 1000)
                            {
                                float heightDiff = targetPos.Z - playerLoc.Z;
                                if (heightDiff > 50)
                                {
                                    Console.WriteLine($"  → 二段跳（高度差 {heightDiff / 100:F1}米）");
                                    await PerformDoubleJumpAsync(targetPos, ct);
                                    lastJumpTime = DateTime.Now;
                                    isMoving = false;
                                }
                            }

                            if (isMoving)
                            {
                                await AdjustCameraToTargetAsync(targetPos, ct);
                                Console.WriteLine("  → 取消冲刺，普通移动");
                                Controller.SendKeyUp("LSHIFT");
                                Controller.SendKeyUp("W");
                                Controller.MouseUp(key: "right");
                                isMoving = false;
                            }
                            Controller.SendKeyDown("W");
                        }

                        // 距离变化日志
                        if (Math.Abs(distance - lastDistance) > 100)
                        {
                            await AdjustCameraToTargetAsync(targetPos, ct);
                            Console.WriteLine($"  距离目标: {distance:F0} 单位 ({distance / 100:F1}米)");
                            lastDistance = distance;
                        }

                        lastScanTime = DateTime.Now;
                    }

                    // 每5秒检查位置是否改变
                    if ((DateTime.Now - lastPositionCheckTime).TotalSeconds > POSITION_CHECK_SECONDS)
                    {
                        var currentPlayerLoc = BattleApi.GetPlayerLocation();
                        float positionChange = CalculateDistance(lastPosition, currentPlayerLoc);
                        if (positionChange < 100)
                        {
                            Console.WriteLine($"  ⚠ {POSITION_CHECK_SECONDS}秒位置未改变（变化 {positionChange / 100:F1}米），执行二段跳");
                            await PerformDoubleJumpAsync(targetPos, ct);
                            await Task.Delay(500, ct);
                            await AdjustCameraToTargetAsync(targetPos, ct);
                        }
                        lastPosition = currentPlayerLoc;
                        lastPositionCheckTime = DateTime.Now;
                    }

                    // 每8秒重新调整视角
                    if ((DateTime.Now - lastCameraAdjustTime).TotalSeconds > 8)
                    {
                        Console.WriteLine("  → 重新调整视角");
                        if (isMoving)
                        {
                            Controller.SendKeyUp("W");
                            Controller.SendKeyUp("LSHIFT");
                            Controller.MouseUp(key: "right");
                            isMoving = false;
                        }
                        await AdjustCameraToTargetAsync(targetPos, ct);
                        lastCameraAdjustTime = DateTime.Now;
                        await Task.Delay(200, ct);
                    }

                    await Task.Delay(10, ct);
                }
            }
            finally
            {
                Controller.SendKeyUp("W");
                Controller.SendKeyUp("LSHIFT");
                Controller.MouseUp(key: "right");
            }

            return false;
        }

        public static float CalculateDistance(FVector pos1, FVector pos2)
        {
            float dx = pos2.X - pos1.X;
            float dy = pos2.Y - pos1.Y;
            float dz = pos2.Z - pos1.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static FRotator CalculateRotationToTarget(FVector fromPos, FVector toPos)
        {
            float dx = toPos.X - fromPos.X;
            float dy = toPos.Y - fromPos.Y;
            float dz = toPos.Z - fromPos.Z;

            float yaw = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);
            float horizontalDist = (float)Math.Sqrt(dx * dx + dy * dy);
            float pitch = (float)(Math.Atan2(-dz, horizontalDist) * 180.0 / Math.PI);

            return new FRotator { Pitch = pitch, Yaw = yaw, Roll = 0 };
        }

        public static float NormalizeAngle(float angle)
        {
            while (angle > 180) angle -= 360;
            while (angle < -180) angle += 360;
            return angle;
        }
    }
}

