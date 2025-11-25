using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using MMP;

// ============================================================================
// Battle Entities API - UE4/UE5 游戏实体读取
// ============================================================================
// 
// 偏移更新方法：
// 1. 游戏更新后，运行偏移查找工具：
//    dotnet run --features:FileBasedProgram --file .\test\QuickFindAllOffsets.cs
// 
// 2. 将输出的偏移值更新到下面的常量中
// 
// 3. 如需查找其他偏移：
//    - 在 IDA Pro 中找到汇编引用
//    - 使用 test/TestAssemblyPattern.cs 测试字节模式
//    - 找到精确模式后添加到代码中
// 
// ============================================================================

public struct FVector
{
    public float X;
    public float Y;
    public float Z;

    public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
}

// FRotator 结构
[StructLayout(LayoutKind.Sequential)]
public struct FRotator
{
    public float Pitch;
    public float Yaw;
    public float Roll;

    public override string ToString() => $"(Pitch:{Pitch:F2}, Yaw:{Yaw:F2}, Roll:{Roll:F2})";
}

// TMap 结构
[StructLayout(LayoutKind.Sequential)]
public struct TMapData
{
    public IntPtr Data;
    public int ArrayNum;
    public int ArrayMax;
    public IntPtr AllocatorData;
    public int NumBits;
    public int MaxBits;
    public int FirstFreeIndex;
    public int NumFreeIndices;
}

// TSet 元素结构
[StructLayout(LayoutKind.Sequential)]
public struct TSetElement
{
    public int Key;
    public int Padding;
    public IntPtr Value;
    public int HashNextId;
    public int HashIndex;
}

// TArray 结构
[StructLayout(LayoutKind.Sequential)]
public struct TArray
{
    public IntPtr Data;
    public int Num;
    public int Max;
}

// ENpcPetState 枚举
public enum ENpcPetState : byte
{
    None = 0,
    Active = 1,                  // 可交互状态
    InteractiveSuccess = 2,
    InteractiveFail = 3
}

public class EntityInfo
{
    public int EntityId;
    public IntPtr EntityPtr;
    public string Name = "";
    public string ClassName = "";
    public bool IsActor;
    public FVector Position;
    public List<string> ParentClasses = new List<string>();
    public List<ComponentInfo> Components = new List<ComponentInfo>();
    public ENpcPetState InteractiveState = ENpcPetState.None; // 宠物交互状态
    public bool AlreadyDead = false; // 是否已死亡
    public bool IsActive = true; // 是否激活（用于ACombatItemBase）
    public bool CanOpen = true; // 是否可以打开（用于AMechanismBase）
    public bool OpenState = true; // 开启状态（用于AMechanismBase）

    public override string ToString()
    {
        string actorType = IsActor ? "[Actor]" : "[Entity]";
        string parents = ParentClasses.Count > 0 ? $" 继承: {string.Join(" -> ", ParentClasses)}" : "";
        string state = InteractiveState != ENpcPetState.None ? $" 状态:{InteractiveState}" : "";
        return $"{actorType} {Name} ({ClassName}) ID:{EntityId} 位置:{Position} 组件:{Components.Count}个{state}{parents}";
    }
}

public class ComponentInfo
{
    public string Name = "";
    public string ClassName = "";
    public IntPtr ComponentPtr;

    public override string ToString() => $"{Name} ({ClassName})";
}

public class BattleEntitiesAPI
{
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
    [DllImport("kernel32.dll")]
    static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr hObject);

    const int PROCESS_VM_READ = 0x0010;
    const int PROCESS_QUERY_INFORMATION = 0x0400;

    // ============================================================================
    // 全局偏移（运行时自动查找，所有实例共享）
    // 如果自动查找失败，可以使用工具手动查找：
    // - test/QuickFindAllOffsets.cs - 一键查找所有偏移
    // - test/DebugOffsets.cs - 验证偏移链是否正确
    // ============================================================================
    private static long OFFSET_WORLD = 0;              // GWorld 全局指针（运行时查找）
    private static long GNAMES_OFFSET = 0;             // GNames/GFNamePool 全局指针（运行时查找）
    private static long OFFSET_GAMEENGINE = 0;         // GEngine 全局指针（运行时查找）
    private static bool offsetsInitialized = false;    // 偏移是否已初始化
    private static readonly object offsetLock = new object();  // 线程锁

    // ============================================================================
    // UObject 基础偏移
    // ============================================================================
    const int UOBJECT_NAME = 0x18;              // UObject->Name (FName)
    const int UOBJECT_CLASS = 0x10;             // UObject->Class (UClass*)

    // ============================================================================
    // UWorld 相关偏移
    // ============================================================================
    const int OFFSET_GAMESTATE = 0x130;         // UWorld->GameState

    // ============================================================================
    // GameState 相关偏移
    // ============================================================================
    const int OFFSET_BATTLE = 0xE90;            // GameState->Battle
    const int OFFSET_GAMESTATE_NPCMAP = 0x6B8;  // GameState->NpcMap

    // ============================================================================
    // Battle 相关偏移
    // ============================================================================
    const int OFFSET_BATTLE_ENTITIES = 0x380;   // Battle->Entities (TMap)

    // ============================================================================
    // 游戏特定状态偏移
    // ============================================================================
    const int OFFSET_PET_INTERACTIVESTATE = 0x2224;     // APetNpcCharacter->InteractiveState
    const int OFFSET_ALREADYDEAD = 0x122B;              // 怪物死亡状态 (bool AlreadyDead)
    const int OFFSET_COMBATITEM_ISACTIVE = 0x8C1;       // ACombatItemBase->IsActive
    const int OFFSET_MECHANISM_CANOPEN = 0x9D8;         // AMechanismBase->CanOpen
    const int OFFSET_MECHANISM_OPENSTATE = 0x9D9;       // AMechanismBase->OpenState

    // ============================================================================
    // 相机/玩家相关偏移
    // ============================================================================
    const int OFFSET_GAMEINSTANCE = 0xE18;              // GameEngine->GameInstance
    const int OFFSET_LOCALPLAYERS = 0x38;               // GameInstance->LocalPlayers
    const int OFFSET_PLAYERCONTROLLER = 0x30;           // LocalPlayer->PlayerController
    const int OFFSET_ACKNOWLEDGEDPAWN = 0x320;          // PlayerController->AcknowledgedPawn
    const int OFFSET_PLAYERCAMERAMANAGER = 0x338;       // PlayerController->PlayerCameraManager
    const int OFFSET_CAMERACACHEPRIVATE = 0x1C70;       // PlayerCameraManager->CameraCachePrivate
    const int OFFSET_POV = 0x10;                        // CameraCachePrivate.POV

    // ============================================================================
    // AActor 相关偏移
    // ============================================================================
    const int ACTOR_ROOTCOMPONENT = 0x160;              // AActor->RootComponent
    const int ACTOR_OWNEDCOMPONENTS = 0x168;            // AActor->OwnedComponents (TArray)

    // ============================================================================
    // SceneComponent 相关偏移
    // ============================================================================
    const int SCENECOMPONENT_COMPONENTTOWORLD = 0x1C0;  // USceneComponent->ComponentToWorld

    private IntPtr processHandle;
    private IntPtr moduleBase;
    private IntPtr gNamesAddress;
    private OffsetFinder? offsetFinder;
    
    // 实体缓存（100ms）
    private List<EntityInfo>? _cachedEntities = null;
    private DateTime _lastCacheTime = DateTime.MinValue;
    private const int CACHE_DURATION_MS = 100;

    public BattleEntitiesAPI(string processName)
    {
        Process[] processes = Process.GetProcessesByName(processName);
        if (processes.Length == 0)
            throw new Exception($"未找到进程: {processName}");

        Process targetProcess = processes[0];
        processHandle = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, false, targetProcess.Id);
        if (processHandle == IntPtr.Zero)
            throw new Exception("无法打开进程");

        if (targetProcess.MainModule == null)
            throw new Exception("无法获取主模块");

        moduleBase = targetProcess.MainModule.BaseAddress;

        Console.WriteLine($"✓ 已连接: {targetProcess.ProcessName} (PID: {targetProcess.Id})");
        Console.WriteLine($"  模块基址: 0x{moduleBase.ToInt64():X}");

        // 初始化偏移（只在第一个实例时执行）
        InitializeOffsets(processName);
        
        gNamesAddress = new IntPtr(moduleBase.ToInt64() + GNAMES_OFFSET);
    }

    /// <summary>
    /// 初始化偏移（线程安全，只执行一次）
    /// </summary>
    private void InitializeOffsets(string processName)
    {
        if (offsetsInitialized)
        {
            Console.WriteLine("✓ 使用已加载的偏移");
            return;
        }

        lock (offsetLock)
        {
            // 双重检查
            if (offsetsInitialized)
            {
                Console.WriteLine("✓ 使用已加载的偏移");
                return;
            }

            Console.WriteLine("\n正在查找游戏偏移...");
            FindOffsets(processName);
            offsetsInitialized = true;
        }
    }

    /// <summary>
    /// 自动查找游戏偏移
    /// </summary>
    private void FindOffsets(string processName)
    {
        try
        {
            offsetFinder = new OffsetFinder(processName);

            // 查找 GWorld
            Console.Write("  查找 GWorld... ");
            OFFSET_WORLD = offsetFinder.FindGWorld();
            if (OFFSET_WORLD > 0)
            {
                Console.WriteLine($"✓ 0x{OFFSET_WORLD:X}");
            }
            else
            {
                Console.WriteLine("✗ 失败");
                throw new Exception("无法找到 GWorld 偏移");
            }

            // 查找 GNames
            Console.Write("  查找 GNames... ");
            GNAMES_OFFSET = FindGNamesOffset();
            if (GNAMES_OFFSET > 0)
            {
                Console.WriteLine($"✓ 0x{GNAMES_OFFSET:X}");
                gNamesAddress = new IntPtr(moduleBase.ToInt64() + GNAMES_OFFSET);
            }
            else
            {
                Console.WriteLine("✗ 失败");
                throw new Exception("无法找到 GNames 偏移");
            }

            // 查找 GEngine
            Console.Write("  查找 GEngine... ");
            OFFSET_GAMEENGINE = FindGEngineOffset();
            if (OFFSET_GAMEENGINE > 0)
            {
                Console.WriteLine($"✓ 0x{OFFSET_GAMEENGINE:X}");
            }
            else
            {
                Console.WriteLine("⚠ 未找到（非必需）");
            }

            Console.WriteLine("✓ 偏移查找完成\n");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠ 偏移查找失败: {ex.Message}");
            Console.WriteLine("请使用工具手动查找偏移: test/QuickFindAllOffsets.cs");
            throw;
        }
    }

    /// <summary>
    /// 查找 GNames 偏移
    /// </summary>
    private long FindGNamesOffset()
    {
        if (offsetFinder == null) return -1;

        string pattern = "4C 8D 05 ?? ?? ?? ?? EB ?? 48 8D 0D ?? ?? ?? ?? E8";
        var results = offsetFinder.SearchPattern(pattern);

        if (results.Count > 0)
        {
            var targetCounts = new Dictionary<long, int>();
            foreach (var result in results)
            {
                long offset = offsetFinder.CalculateRipRelativeOffset(result, 7);
                if (!targetCounts.ContainsKey(offset))
                    targetCounts[offset] = 0;
                targetCounts[offset]++;
            }

            long bestOffset = -1;
            int maxCount = 0;
            foreach (var kvp in targetCounts)
            {
                if (kvp.Value > maxCount)
                {
                    maxCount = kvp.Value;
                    bestOffset = kvp.Key;
                }
            }

            return bestOffset;
        }

        return -1;
    }

    /// <summary>
    /// 查找 GEngine 偏移
    /// </summary>
    private long FindGEngineOffset()
    {
        if (offsetFinder == null) return -1;

        string pattern = "48 89 74 24 20 E8 ?? ?? ?? ?? 48 8B 4C 24 ?? 48 89 05 ?? ?? ?? ?? 48 85 C9 74 05 E8";
        var results = offsetFinder.SearchPattern(pattern);

        if (results.Count > 0)
        {
            string beforeMov = pattern.Substring(0, pattern.IndexOf("48 89 05"));
            int movOffset = beforeMov.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return offsetFinder.CalculateRipRelativeOffset(results[0] + movOffset, 7);
        }

        return -1;
    }

    ~BattleEntitiesAPI()
    {
        if (processHandle != IntPtr.Zero)
            CloseHandle(processHandle);
    }

    private IntPtr ReadPointer(IntPtr address)
    {
        byte[] buffer = new byte[8];
        if (!ReadProcessMemory(processHandle, address, buffer, 8, out _))
            return IntPtr.Zero;
        return new IntPtr(BitConverter.ToInt64(buffer, 0));
    }

    private int ReadInt32(IntPtr address)
    {
        byte[] buffer = new byte[4];
        if (!ReadProcessMemory(processHandle, address, buffer, 4, out _))
            return 0;
        return BitConverter.ToInt32(buffer, 0);
    }

    private short ReadShort(IntPtr address)
    {
        byte[] buffer = new byte[2];
        if (!ReadProcessMemory(processHandle, address, buffer, 2, out _))
            return 0;
        return BitConverter.ToInt16(buffer, 0);
    }

    private byte ReadByte(IntPtr address)
    {
        byte[] buffer = new byte[1];
        if (!ReadProcessMemory(processHandle, address, buffer, 1, out _))
            return 0;
        return buffer[0];
    }

    private T ReadStruct<T>(IntPtr address) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] buffer = new byte[size];
        if (!ReadProcessMemory(processHandle, address, buffer, size, out _))
            return default(T);

        GCHandle handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            T? result = Marshal.PtrToStructure<T>(handle.AddrOfPinnedObject());
            return result ?? default(T);
        }
        finally
        {
            handle.Free();
        }
    }

    private bool IsValidPointer(IntPtr ptr)
    {
        long addr = ptr.ToInt64();
        return addr > 0x10000 && addr < 0x7FFFFFFFFFFF;
    }

    private string ReadFName(IntPtr objectPtr)
    {
        try
        {
            int nameIndex = ReadInt32(new IntPtr(objectPtr.ToInt64() + UOBJECT_NAME));
            int chunkOffset = nameIndex >> 16;
            int nameOffset = nameIndex & 0xFFFF;

            IntPtr chunkPtr = ReadPointer(new IntPtr(gNamesAddress.ToInt64() + 8 * (chunkOffset + 2)));
            if (!IsValidPointer(chunkPtr))
                return "Invalid";

            IntPtr namePoolChunk = new IntPtr(chunkPtr.ToInt64() + 2 * nameOffset);
            short header = ReadShort(namePoolChunk);
            int nameLength = header >> 6;

            if (nameLength <= 0 || nameLength > 1024)
                return "BadLength";

            byte[] buffer = new byte[nameLength];
            ReadProcessMemory(processHandle, new IntPtr(namePoolChunk.ToInt64() + 2), buffer, nameLength, out int bytesRead);

            return Encoding.ASCII.GetString(buffer, 0, bytesRead).TrimEnd('\0');
        }
        catch
        {
            return "Error";
        }
    }

    private IntPtr GetGameState()
    {
        try
        {
            IntPtr worldAddr = new IntPtr(moduleBase.ToInt64() + OFFSET_WORLD);
            IntPtr world = ReadPointer(worldAddr);
            if (world == IntPtr.Zero)
                return IntPtr.Zero;

            return ReadPointer(new IntPtr(world.ToInt64() + OFFSET_GAMESTATE));
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private IntPtr GetBattle()
    {
        IntPtr gameState = GetGameState();
        if (gameState == IntPtr.Zero)
            return IntPtr.Zero;

        return ReadPointer(new IntPtr(gameState.ToInt64() + OFFSET_BATTLE));
    }

    private IntPtr GetPlayerController()
    {
        try
        {
            IntPtr gameEngineAddr = new IntPtr(moduleBase.ToInt64() + OFFSET_GAMEENGINE);
            IntPtr gameEngine = ReadPointer(gameEngineAddr);
            if (gameEngine == IntPtr.Zero) return IntPtr.Zero;

            IntPtr gameInstance = ReadPointer(new IntPtr(gameEngine.ToInt64() + OFFSET_GAMEINSTANCE));
            if (gameInstance == IntPtr.Zero) return IntPtr.Zero;

            IntPtr localPlayersArrayAddr = new IntPtr(gameInstance.ToInt64() + OFFSET_LOCALPLAYERS);
            IntPtr localPlayersArray = ReadPointer(localPlayersArrayAddr);
            if (localPlayersArray == IntPtr.Zero) return IntPtr.Zero;

            IntPtr localPlayer = ReadPointer(localPlayersArray);
            if (localPlayer == IntPtr.Zero) return IntPtr.Zero;

            return ReadPointer(new IntPtr(localPlayer.ToInt64() + OFFSET_PLAYERCONTROLLER));
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public FVector GetCameraLocation()
    {
        IntPtr playerController = GetPlayerController();
        if (playerController == IntPtr.Zero)
            return default;

        IntPtr cameraManager = ReadPointer(new IntPtr(playerController.ToInt64() + OFFSET_PLAYERCAMERAMANAGER));
        if (cameraManager == IntPtr.Zero)
            return default;

        // 读取 CameraCachePrivate.POV.Location
        IntPtr locationAddr = new IntPtr(cameraManager.ToInt64() + OFFSET_CAMERACACHEPRIVATE + OFFSET_POV);
        return ReadStruct<FVector>(locationAddr);
    }

    public FVector GetPlayerLocation()
    {
        IntPtr playerController = GetPlayerController();
        if (playerController == IntPtr.Zero)
            return default;

        IntPtr pawn = ReadPointer(new IntPtr(playerController.ToInt64() + OFFSET_ACKNOWLEDGEDPAWN));
        if (pawn == IntPtr.Zero)
            return default;

        IntPtr rootComponent = ReadPointer(new IntPtr(pawn.ToInt64() + ACTOR_ROOTCOMPONENT));
        if (rootComponent == IntPtr.Zero)
            return default;

        IntPtr transformAddr = new IntPtr(rootComponent.ToInt64() + SCENECOMPONENT_COMPONENTTOWORLD);
        return ReadStruct<FVector>(new IntPtr(transformAddr.ToInt64() + 0x10));
    }

    public FRotator GetCameraRotation()
    {
        IntPtr playerController = GetPlayerController();
        if (playerController == IntPtr.Zero)
            return default;

        IntPtr cameraManager = ReadPointer(new IntPtr(playerController.ToInt64() + OFFSET_PLAYERCAMERAMANAGER));
        if (cameraManager == IntPtr.Zero)
            return default;

        // 读取 CameraCachePrivate.POV.Rotation
        IntPtr rotationAddr = new IntPtr(cameraManager.ToInt64() + OFFSET_CAMERACACHEPRIVATE + OFFSET_POV + 0xC);
        return ReadStruct<FRotator>(rotationAddr);
    }

    // 获取类的继承链
    private List<string> GetClassHierarchy(IntPtr classPtr)
    {
        List<string> hierarchy = new List<string>();
        IntPtr currentClass = classPtr;

        while (IsValidPointer(currentClass))
        {
            string className = ReadFName(currentClass);
            if (string.IsNullOrEmpty(className) || className == "Invalid" || className == "Error")
                break;

            hierarchy.Add(className);

            // 读取父类 (UStruct::SuperStruct at offset 0x40)
            IntPtr superClass = ReadPointer(new IntPtr(currentClass.ToInt64() + 0x40));
            if (superClass == currentClass || !IsValidPointer(superClass))
                break;

            currentClass = superClass;
        }

        return hierarchy;
    }

    // 判断是否继承自 AActor
    private bool IsActorClass(List<string> hierarchy)
    {
        return hierarchy.Any(c => c == "Actor" || c == "AActor");
    }

    // 获取 Actor 的位置
    private FVector GetActorPosition(IntPtr actorPtr)
    {
        try
        {
            IntPtr rootComponent = ReadPointer(new IntPtr(actorPtr.ToInt64() + ACTOR_ROOTCOMPONENT));
            if (!IsValidPointer(rootComponent))
                return new FVector { X = 0, Y = 0, Z = 0 };

            // 从 ComponentToWorld 变换矩阵读取位置 (Translation at offset +0x10)
            IntPtr transformAddr = new IntPtr(rootComponent.ToInt64() + SCENECOMPONENT_COMPONENTTOWORLD);
            return ReadStruct<FVector>(new IntPtr(transformAddr.ToInt64() + 0x10));
        }
        catch
        {
            return new FVector { X = 0, Y = 0, Z = 0 };
        }
    }

    // 获取 Actor 的组件列表
    private List<ComponentInfo> GetActorComponents(IntPtr actorPtr)
    {
        List<ComponentInfo> components = new List<ComponentInfo>();

        try
        {
            // 读取 OwnedComponents TArray
            IntPtr componentsArrayAddr = new IntPtr(actorPtr.ToInt64() + ACTOR_OWNEDCOMPONENTS);
            TArray componentsArray = ReadStruct<TArray>(componentsArrayAddr);

            if (componentsArray.Num <= 0 || !IsValidPointer(componentsArray.Data))
                return components;

            // 遍历组件数组
            for (int i = 0; i < Math.Min(componentsArray.Num, 100); i++)
            {
                IntPtr componentPtrAddr = new IntPtr(componentsArray.Data.ToInt64() + i * 8);
                IntPtr componentPtr = ReadPointer(componentPtrAddr);

                if (!IsValidPointer(componentPtr))
                    continue;

                string componentName = ReadFName(componentPtr);
                IntPtr componentClassPtr = ReadPointer(new IntPtr(componentPtr.ToInt64() + UOBJECT_CLASS));
                string componentClassName = IsValidPointer(componentClassPtr) ? ReadFName(componentClassPtr) : "Unknown";

                components.Add(new ComponentInfo
                {
                    Name = componentName,
                    ClassName = componentClassName,
                    ComponentPtr = componentPtr
                });
            }
        }
        catch { }

        return components;
    }

    /// <summary>
    /// 清除实体缓存，强制下次调用重新读取
    /// </summary>
    public void ClearEntitiesCache()
    {
        _cachedEntities = null;
        _lastCacheTime = DateTime.MinValue;
    }

    // 主API：获取 Battle.Entities（带100ms缓存）
    public List<EntityInfo> GetBattleEntities()
    {
        // 检查缓存是否有效
        if (_cachedEntities != null && (DateTime.Now - _lastCacheTime).TotalMilliseconds < CACHE_DURATION_MS)
        {
            return _cachedEntities;
        }

        List<EntityInfo> entities = new List<EntityInfo>();

        try
        {
            IntPtr battle = GetBattle();
            if (battle == IntPtr.Zero)
            {
                Console.WriteLine("⚠ 无法获取 Battle 对象");
                return entities;
            }

            Console.WriteLine($"✓ Battle 地址: 0x{battle.ToInt64():X}");

            // 读取 Battle.Entities TMap
            IntPtr entitiesMapAddr = new IntPtr(battle.ToInt64() + OFFSET_BATTLE_ENTITIES);
            TMapData mapData = ReadStruct<TMapData>(entitiesMapAddr);

            Console.WriteLine($"  Entities 数量: {mapData.ArrayNum}");

            if (mapData.ArrayNum <= 0 || !IsValidPointer(mapData.Data))
            {
                Console.WriteLine("⚠ Entities 为空");
                return entities;
            }

            int elementSize = 24; // TSetElement size

            for (int i = 0; i < mapData.ArrayNum; i++)
            {
                IntPtr elementAddr = new IntPtr(mapData.Data.ToInt64() + i * elementSize);

                try
                {
                    // 检查 HashIndex 判断槽位是否有效
                    int hashIndex = ReadInt32(new IntPtr(elementAddr.ToInt64() + 20));
                    if (hashIndex == -1)
                        continue;

                    int key = ReadInt32(elementAddr);
                    IntPtr value = ReadPointer(new IntPtr(elementAddr.ToInt64() + 8));

                    if (!IsValidPointer(value))
                        continue;

                    // 读取对象信息
                    string objectName = ReadFName(value);
                    IntPtr classPtr = ReadPointer(new IntPtr(value.ToInt64() + UOBJECT_CLASS));
                    string className = IsValidPointer(classPtr) ? ReadFName(classPtr) : "Unknown";

                    // 获取类继承链
                    List<string> hierarchy = GetClassHierarchy(classPtr);
                    bool isActor = IsActorClass(hierarchy);

                    // 创建实体信息
                    EntityInfo entity = new EntityInfo
                    {
                        EntityId = key,
                        EntityPtr = value,
                        Name = objectName,
                        ClassName = className,
                        IsActor = isActor,
                        ParentClasses = hierarchy,
                        Position = new FVector { X = 0, Y = 0, Z = 0 },
                        Components = new List<ComponentInfo>()
                    };

                    // 如果是 Actor，获取位置和组件
                    if (isActor)
                    {
                        entity.Position = GetActorPosition(value);
                        entity.Components = GetActorComponents(value);
                        
                        // 如果是怪物或Boss，读取 AlreadyDead 状态
                        if (className.StartsWith("BP_Mon_") || className.StartsWith("BP_Boss_"))
                        {
                            try
                            {
                                byte deadState = ReadByte(new IntPtr(value.ToInt64() + OFFSET_ALREADYDEAD));
                                entity.AlreadyDead = deadState != 0;
                            }
                            catch
                            {
                                entity.AlreadyDead = false;
                            }
                        }
                        // 如果继承自 ACombatItemBase，读取 IsActive 状态
                        if (entity.ParentClasses.Any(c => c.Contains("CombatItemBase")))
                        {
                            try
                            {
                                byte activeState = ReadByte(new IntPtr(value.ToInt64() + OFFSET_COMBATITEM_ISACTIVE));
                                entity.IsActive = activeState != 0;
                            }
                            catch
                            {
                                entity.IsActive = true; // 默认为激活状态
                            }
                        }
                        
                        // 如果继承自 AMechanismBase，读取 CanOpen 和 OpenState 状态
                        if (entity.ParentClasses.Any(c => c.Contains("MechanismBase")))
                        {
                            try
                            {
                                byte canOpenState = ReadByte(new IntPtr(value.ToInt64() + OFFSET_MECHANISM_CANOPEN));
                                entity.CanOpen = canOpenState != 0;
                            }
                            catch
                            {
                                entity.CanOpen = true; // 默认为可打开状态
                            }
                            
                            try
                            {
                                byte openState = ReadByte(new IntPtr(value.ToInt64() + OFFSET_MECHANISM_OPENSTATE));
                                entity.OpenState = openState != 0;
                            }
                            catch
                            {
                                entity.OpenState = true; // 默认为开启状态
                            }
                        }
                    }

                    entities.Add(entity);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ 读取实体 {i} 失败: {ex.Message}");
                }
            }

            Console.WriteLine($"✓ 成功读取 {entities.Count} 个实体");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 错误: {ex.Message}");
        }

        // 更新缓存
        _cachedEntities = entities;
        _lastCacheTime = DateTime.Now;

        return entities;
    }

    // 主API：获取 GameState.NpcMap
    public List<EntityInfo> GetNpcMap()
    {
        List<EntityInfo> npcs = new List<EntityInfo>();

        try
        {
            IntPtr gameState = GetGameState();
            if (gameState == IntPtr.Zero)
            {
                Console.WriteLine("⚠ 无法获取 GameState 对象");
                return npcs;
            }

            Console.WriteLine($"✓ GameState 地址: 0x{gameState.ToInt64():X}");

            // 读取 GameState.NpcMap TMap
            IntPtr npcMapAddr = new IntPtr(gameState.ToInt64() + OFFSET_GAMESTATE_NPCMAP);
            TMapData mapData = ReadStruct<TMapData>(npcMapAddr);

            Console.WriteLine($"  NpcMap 数量: {mapData.ArrayNum}");

            if (mapData.ArrayNum <= 0 || !IsValidPointer(mapData.Data))
            {
                Console.WriteLine("⚠ NpcMap 为空");
                return npcs;
            }

            int elementSize = 24; // TSetElement size

            for (int i = 0; i < mapData.ArrayNum; i++)
            {
                IntPtr elementAddr = new IntPtr(mapData.Data.ToInt64() + i * elementSize);

                try
                {
                    // 检查 HashIndex 判断槽位是否有效
                    int hashIndex = ReadInt32(new IntPtr(elementAddr.ToInt64() + 20));
                    if (hashIndex == -1)
                        continue;

                    int key = ReadInt32(elementAddr);
                    IntPtr value = ReadPointer(new IntPtr(elementAddr.ToInt64() + 8));

                    if (!IsValidPointer(value))
                        continue;

                    // 读取对象信息
                    string objectName = ReadFName(value);
                    IntPtr classPtr = ReadPointer(new IntPtr(value.ToInt64() + UOBJECT_CLASS));
                    string className = IsValidPointer(classPtr) ? ReadFName(classPtr) : "Unknown";

                    // 获取类继承链
                    List<string> hierarchy = GetClassHierarchy(classPtr);
                    bool isActor = IsActorClass(hierarchy);

                    // 创建实体信息
                    EntityInfo entity = new EntityInfo
                    {
                        EntityId = key,
                        EntityPtr = value,
                        Name = objectName,
                        ClassName = className,
                        IsActor = isActor,
                        ParentClasses = hierarchy,
                        Position = new FVector { X = 0, Y = 0, Z = 0 },
                        Components = new List<ComponentInfo>()
                    };

                    // 如果是 Actor，获取位置和组件
                    if (isActor)
                    {
                        entity.Position = GetActorPosition(value);
                        entity.Components = GetActorComponents(value);

                        // 如果是宠物 NPC，读取 InteractiveState
                        if (className.Contains("PetNPC") || className.Contains("BP_PetNPC_Common"))
                        {
                            try
                            {
                                byte state = ReadByte(new IntPtr(value.ToInt64() + OFFSET_PET_INTERACTIVESTATE));
                                entity.InteractiveState = (ENpcPetState)state;
                            }
                            catch
                            {
                                entity.InteractiveState = ENpcPetState.None;
                            }
                        }
                    }

                    npcs.Add(entity);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠ 读取 NPC {i} 失败: {ex.Message}");
                }
            }

            Console.WriteLine($"✓ 成功读取 {npcs.Count} 个 NPC");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 错误: {ex.Message}");
        }

        return npcs;
    }

    // 打印实体信息
    public void PrintEntities(List<EntityInfo> entities)
    {
        Console.WriteLine("\n╔════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║  Battle.Entities ({entities.Count} 个实体)");
        Console.WriteLine("╚════════════════════════════════════════════════════════════════╝\n");

        var actors = entities.Where(e => e.IsActor).ToList();
        var nonActors = entities.Where(e => !e.IsActor).ToList();

        Console.WriteLine($"【Actor 实体】 ({actors.Count} 个)");
        Console.WriteLine(new string('-', 80));

        foreach (var entity in actors)
        {
            Console.WriteLine($"\n[{entity.EntityId}] {entity.Name} ({entity.ClassName})");
            Console.WriteLine($"  地址: 0x{entity.EntityPtr.ToInt64():X}");
            Console.WriteLine($"  位置: {entity.Position}");
            Console.WriteLine($"  继承链: {string.Join(" -> ", entity.ParentClasses)}");

            if (entity.Components.Count > 0)
            {
                Console.WriteLine($"  组件 ({entity.Components.Count} 个):");
                foreach (var comp in entity.Components)
                {
                    Console.WriteLine($"    - {comp.Name} ({comp.ClassName})");
                }
            }
        }

        if (nonActors.Count > 0)
        {
            Console.WriteLine($"\n\n【非 Actor 实体】 ({nonActors.Count} 个)");
            Console.WriteLine(new string('-', 80));

            foreach (var entity in nonActors)
            {
                Console.WriteLine($"\n[{entity.EntityId}] {entity.Name} ({entity.ClassName})");
                Console.WriteLine($"  地址: 0x{entity.EntityPtr.ToInt64():X}");
                Console.WriteLine($"  继承链: {string.Join(" -> ", entity.ParentClasses)}");
            }
        }
    }
}