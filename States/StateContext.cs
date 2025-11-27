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
        public AppConfig Config { get; }

        // OCR 相关
        private readonly Func<OcrEngine.OcrResult?> _getLatestOcrResult;
        private readonly Action<Action<OcrEngine.OcrResult>> _subscribeOcrEvent;
        private readonly Action<Action<OcrEngine.OcrResult>> _unsubscribeOcrEvent;

        public StateContext(
            IntPtr windowHandle,
            KeyboardMouseController? controller,
            BattleEntitiesAPI? battleApi,
            AppConfig config,
            Func<OcrEngine.OcrResult?> getLatestOcrResult,
            Action<Action<OcrEngine.OcrResult>> subscribeOcrEvent,
            Action<Action<OcrEngine.OcrResult>> unsubscribeOcrEvent)
        {
            WindowHandle = windowHandle;
            Controller = controller;
            BattleApi = battleApi;
            Config = config;
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

                await Task.Delay(200, ct);
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

                await Task.Delay(200, ct);
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

                await Task.Delay(200, ct);
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
            const int MAX_MOVE = 200;  // 限制单次移动量，避免过大跳动（与 AutoAimMonster 一致）
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



                if (Math.Abs(yawDiff) < ANGLE_THRESHOLD && Math.Abs(pitchDiff) < ANGLE_THRESHOLD)
                {
                    return moveCount;
                }

                float pixelsPerDegree = 1.0f / MOUSE_SENSITIVITY;
                int mouseX = (int)(yawDiff * pixelsPerDegree);
                int mouseY = (int)(pitchDiff * -pixelsPerDegree);

                mouseX = Math.Max(-MAX_MOVE, Math.Min(MAX_MOVE, mouseX));
                mouseY = Math.Max(-MAX_MOVE, Math.Min(MAX_MOVE, mouseY));

                Controller.SendMouseMove(mouseX, mouseY);
                moveCount++;
                await Task.Delay(10, ct);
            }

            var totalElapsed = (DateTime.Now - startTime).TotalMilliseconds;

            return moveCount;
        }

        /// <summary>
        /// 检查距离并尝试交互
        /// </summary>
        /// <param name="targetPos">目标位置</param>
        /// <param name="context">上下文描述（用于日志）</param>
        /// <returns>如果成功交互返回 true</returns>
        private async Task<bool> TryInteractIfCloseAsync(FVector targetPos, string context, CancellationToken ct = default)
        {
            if (BattleApi == null || Controller == null) return false;

            var currentPos = BattleApi.GetPlayerLocation();
            float currentDistance = CalculateDistance(currentPos, targetPos);

            if (currentDistance <= Config.Movement.InteractDistance)
            {
                Console.WriteLine($"  → {context}距离 ({currentDistance / 100:F1}米)，尝试交互");
                Controller.SendKey("F", 0.1);
                await Task.Delay(100, ct);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 执行二段跳（异步版本）
        /// </summary>
        /// <param name="targetPos">目标位置</param>
        /// <param name="needInteract">是否需要在接近时交互</param>
        /// <param name="ct">取消令牌</param>
        public async Task PerformDoubleJumpAsync(FVector targetPos, bool needInteract = false, CancellationToken ct = default)
        {
            if (BattleApi == null || Controller == null) return;

            // 计算距离，如果距离近就用简单跳跃
            var playerPos = BattleApi.GetCameraLocation();
            float distance = CalculateDistance(playerPos, targetPos);

            if (distance < Config.Movement.SimpleJumpDistance)
            {
                Console.WriteLine($"  → 距离较近 ({distance / 100:F1}米)，使用简单双跳");
                Controller.SendKey("SPACE", 0.1);
                await Task.Delay(300, ct);
                Controller.SendKey("SPACE", 0.1);

                // 如果需要交互，检查距离并交互
                if (needInteract)
                {
                    await Task.Delay(200, ct);
                    await TryInteractIfCloseAsync(targetPos, "简单跳跃后", ct);
                }
                return;
            }

            // 距离远使用完整的二段跳
            Console.WriteLine($"  → 距离较远 ({distance / 100:F1}米)，使用完整二段跳");
            Controller.SendMouseMove(0, -500);
            await Task.Delay(100, ct);
            Controller.SendKey("4", 0.1);
            await Task.Delay(500, ct);

            await AdjustCameraToTargetAsync(targetPos, ct);
            Controller.MouseDown(key: "right");
            Controller.SendKey("SPACE", 0.1);
            await Task.Delay(300, ct);

            // 第一次跳跃后检查距离
            if (needInteract && await TryInteractIfCloseAsync(targetPos, "第一跳后", ct))
            {
                Controller.MouseUp(key: "right");
                return;
            }

            Controller.SendKey("SPACE", 0.1);

            Controller.SendKeyDown("W");
            await Task.Delay(300, ct);

            // 第二次跳跃后检查距离
            if (needInteract && await TryInteractIfCloseAsync(targetPos, "第二跳后", ct))
            {
                Controller.MouseUp(key: "right");
                return;
            }

            Controller.SendKeyDown("LSHIFT");
            await Task.Delay(500, ct);
            await AdjustCameraToTargetAsync(targetPos, ct);

            // 滑翔过程中持续检查距离
            for (int i = 0; i < 3; i++)
            {
                await Task.Delay(500, ct);

                if (needInteract && await TryInteractIfCloseAsync(targetPos, $"滑翔中({i + 1}/3)", ct))
                {
                    Controller.MouseUp(key: "right");
                    return;
                }

                await AdjustCameraToTargetAsync(targetPos, ct);
            }

            Controller.MouseUp(key: "right");
        }

        bool isMoving;
        int interactCount;
        int stuckCheckCount;
        float lastDistance;
        DateTime navStartTime;
        DateTime lastCameraAdjustTime;
        DateTime lastPositionCheckTime;
        DateTime lastJumpTime;
        FVector lastPosition;

        const int NAV_STUCK_CHECK_SECONDS = 30;
        const int POSITION_CHECK_SECONDS = 1;
        /// <summary>
        /// 通用导航到目标位置（增强版）
        /// </summary>
        public async Task<bool> NavigateToTargetAsync(FVector targetPos, int timeoutSeconds, bool needInteract, CancellationToken ct)
        {
            if (BattleApi == null || Controller == null) return false;

            var initialPlayerLoc = BattleApi.GetPlayerLocation();
            float initialDistance = CalculateDistance(initialPlayerLoc, targetPos);


            isMoving = false;
            interactCount = 0;
            stuckCheckCount = 0;
            lastDistance = initialDistance;

            navStartTime = DateTime.Now;
            lastCameraAdjustTime = DateTime.Now;
            lastPositionCheckTime = DateTime.Now;
            lastJumpTime = DateTime.MinValue;
            lastPosition = initialPlayerLoc;
            Console.WriteLine($"  → 开始导航，距离: {initialDistance / 100:F1}米");

            // 交互检查
            if (needInteract && initialDistance < Config.Movement.InteractDistance)
            {
                Controller.SendKey("F", 0.1);
                await Task.Delay(200, ct);
                Controller.SendKey("F", 0.1);
                return true;
            }

            if (!needInteract && initialDistance < Config.Movement.NormalMoveDistance)
                return true;

            // 初始调整视角
            await AdjustCameraToTargetAsync(targetPos, ct);
            await Task.Delay(1000, ct);


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

                    var distance = GetPlayerTargetDistance(targetPos);

                    // 距离过远检查
                    if (distance > Config.Movement.TooFarWarningDistance)
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
                    else if (distance > Config.Movement.SprintDistance)
                    {
                        if (!await CheckHeightDiff(targetPos, needInteract, ct))
                            continue;

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
                    else if (distance > Config.Movement.NormalMoveDistance)
                    {
                        // 检查高度差

                        if (!await CheckHeightDiff(targetPos, needInteract, ct))
                            continue;

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
                        // 检查高度差

                        if (!await CheckHeightDiff(targetPos, needInteract, ct))
                            continue;

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


                    // 每1秒检查位置是否改变
                    if ((DateTime.Now - lastPositionCheckTime).TotalSeconds > POSITION_CHECK_SECONDS)
                    {
                        var currentPlayerLoc = BattleApi.GetPlayerLocation();
                        float positionChange = CalculateDistance(lastPosition, currentPlayerLoc);
                        if (positionChange < 100)
                        {
                            Console.WriteLine($"  ⚠ {POSITION_CHECK_SECONDS}秒位置未改变（变化 {positionChange / 100:F1}米），执行二段跳");
                            await PerformDoubleJumpAsync(targetPos, needInteract, ct);
                            await Task.Delay(500, ct);
                            await AdjustCameraToTargetAsync(targetPos, ct);
                        }
                        lastPosition = currentPlayerLoc;
                        lastPositionCheckTime = DateTime.Now;
                    }

                    await Task.Delay(25, ct);
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

        private async Task<bool> CheckHeightDiff(FVector targetPos, bool needInteract, CancellationToken ct)
        {
            // 检查高度差
            if ((DateTime.Now - lastJumpTime).TotalMilliseconds > 1000)
            {
                var playerLoc = BattleApi.GetPlayerLocation();
                float heightDiff = targetPos.Z - playerLoc.Z;
                if (heightDiff > 50)
                {
                    Console.WriteLine($"  → 二段跳（高度差 {heightDiff / 100:F1}米）");
                    await PerformDoubleJumpAsync(targetPos, needInteract, ct);
                    lastJumpTime = DateTime.Now;
                    isMoving = false;
                    return false;
                }
            }
            return true;
        }

        private float GetPlayerTargetDistance(FVector targetPos)
        {
            var playerLoc = BattleApi.GetPlayerLocation();
            return CalculateDistance(playerLoc, targetPos);
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
            float pitch = (float)(Math.Atan2(dz, horizontalDist) * 180.0 / Math.PI);  // 修复：移除负号

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

