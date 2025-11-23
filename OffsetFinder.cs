using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace MMP
{
    /// <summary>
    /// 内存偏移查找器 - 通过汇编特征模式自动查找游戏偏移
    /// </summary>
    public class OffsetFinder
    {
        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);
        
        [DllImport("kernel32.dll")]
        static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, int dwSize, out int lpNumberOfBytesRead);
        
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr hObject);

        const int PROCESS_VM_READ = 0x0010;
        const int PROCESS_QUERY_INFORMATION = 0x0400;

        private IntPtr processHandle;
        private IntPtr moduleBase;
        private long moduleSize;
        private string processName;

        public OffsetFinder(string processName)
        {
            this.processName = processName;
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
            moduleSize = targetProcess.MainModule.ModuleMemorySize;

            Console.WriteLine($"✓ 已连接: {targetProcess.ProcessName} (PID: {targetProcess.Id})");
            Console.WriteLine($"  模块基址: 0x{moduleBase.ToInt64():X}");
            Console.WriteLine($"  模块大小: 0x{moduleSize:X} ({moduleSize / 1024 / 1024} MB)");
        }

        ~OffsetFinder()
        {
            if (processHandle != IntPtr.Zero)
                CloseHandle(processHandle);
        }

        /// <summary>
        /// 读取内存区域
        /// </summary>
        private byte[] ReadMemoryRegion(IntPtr address, int size)
        {
            byte[] buffer = new byte[size];
            if (!ReadProcessMemory(processHandle, address, buffer, size, out int bytesRead))
                return Array.Empty<byte>();
            
            if (bytesRead != size)
            {
                Array.Resize(ref buffer, bytesRead);
            }
            
            return buffer;
        }

        /// <summary>
        /// 在内存中搜索字节模式（支持通配符）
        /// </summary>
        /// <param name="pattern">字节模式，例如 "48 8B 1D ?? ?? ?? ??"</param>
        /// <param name="startOffset">搜索起始偏移</param>
        /// <param name="searchSize">搜索大小（0表示搜索整个模块）</param>
        /// <returns>找到的所有地址列表</returns>
        public List<long> SearchPattern(string pattern, long startOffset = 0, long searchSize = 0)
        {
            List<long> results = new List<long>();
            
            // 解析模式
            var patternBytes = ParsePattern(pattern, out var mask);
            if (patternBytes.Length == 0)
            {
                Console.WriteLine("⚠ 无效的模式");
                return results;
            }

            // 确定搜索范围
            if (searchSize == 0)
                searchSize = moduleSize - startOffset;

            long searchEnd = Math.Min(startOffset + searchSize, moduleSize);
            
            Console.WriteLine($"搜索模式: {pattern}");
            Console.WriteLine($"搜索范围: 0x{startOffset:X} - 0x{searchEnd:X} ({(searchEnd - startOffset) / 1024 / 1024} MB)");

            // 分块读取和搜索（每次读取 1MB）
            const int chunkSize = 1024 * 1024;
            int overlap = patternBytes.Length - 1;

            for (long offset = startOffset; offset < searchEnd; offset += chunkSize - overlap)
            {
                int readSize = (int)Math.Min(chunkSize, searchEnd - offset);
                IntPtr readAddr = new IntPtr(moduleBase.ToInt64() + offset);
                
                byte[] buffer = ReadMemoryRegion(readAddr, readSize);
                if (buffer.Length == 0)
                    continue;

                // 在缓冲区中搜索模式
                for (int i = 0; i <= buffer.Length - patternBytes.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < patternBytes.Length; j++)
                    {
                        if (mask[j] && buffer[i + j] != patternBytes[j])
                        {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                    {
                        long foundOffset = offset + i;
                        results.Add(foundOffset);
                    }
                }

                // 显示进度
                if ((offset / chunkSize) % 100 == 0)
                {
                    double progress = (double)(offset - startOffset) / (searchEnd - startOffset) * 100;
                    Console.Write($"\r  进度: {progress:F1}%");
                }
            }

            Console.WriteLine($"\r  ✓ 找到 {results.Count} 个匹配");
            return results;
        }

        /// <summary>
        /// 解析字节模式字符串
        /// </summary>
        private byte[] ParsePattern(string pattern, out bool[] mask)
        {
            var parts = pattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>();
            var maskList = new List<bool>();

            foreach (var part in parts)
            {
                if (part == "??" || part == "?")
                {
                    bytes.Add(0);
                    maskList.Add(false); // 通配符
                }
                else
                {
                    if (byte.TryParse(part, System.Globalization.NumberStyles.HexNumber, null, out byte b))
                    {
                        bytes.Add(b);
                        maskList.Add(true); // 精确匹配
                    }
                }
            }

            mask = maskList.ToArray();
            return bytes.ToArray();
        }

        /// <summary>
        /// 从 RIP 相对寻址指令中提取目标地址
        /// </summary>
        /// <param name="instructionOffset">指令在模块中的偏移</param>
        /// <param name="instructionLength">指令总长度</param>
        /// <param name="ripOffsetPosition">RIP偏移值在指令中的位置（通常是指令长度-4）</param>
        /// <returns>目标地址相对于模块基址的偏移</returns>
        public long CalculateRipRelativeOffset(long instructionOffset, int instructionLength, int ripOffsetPosition = -1)
        {
            if (ripOffsetPosition == -1)
                ripOffsetPosition = instructionLength - 4;

            // 读取指令
            IntPtr instructionAddr = new IntPtr(moduleBase.ToInt64() + instructionOffset);
            byte[] instruction = ReadMemoryRegion(instructionAddr, instructionLength);
            
            if (instruction.Length < instructionLength)
            {
                Console.WriteLine("⚠ 无法读取指令");
                return -1;
            }

            // 提取 RIP 偏移值（小端序）
            int ripOffset = BitConverter.ToInt32(instruction, ripOffsetPosition);
            
            // 计算目标地址
            long nextInstructionOffset = instructionOffset + instructionLength;
            long targetOffset = nextInstructionOffset + ripOffset;

            return targetOffset;
        }

        /// <summary>
        /// 查找 GWorld 偏移
        /// 使用超长精确模式，确保只有 1-5 个匹配
        /// </summary>
        public long FindGWorld()
        {
            Console.WriteLine("\n【查找 GWorld】");
            
            // 使用超长、超精确的模式（已验证有效）
            // 按精确度从高到低排序
            string[] patterns = {
                // 最精确：完整的 FEngineLoop::Tick 序列（20字节）
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 41 B0 01 33 D2 48 8B CB E8",     // mov rbx, GWorld; test rbx, rbx; jz; mov r8b,1; xor edx,edx; mov rcx,rbx; call
                
                // 次精确：去掉 call（18字节）
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 41 B0 01 33 D2 48 8B CB",        // mov rbx, GWorld; test rbx, rbx; jz; mov r8b,1; xor edx,edx; mov rcx,rbx
                
                // 中等精确（15字节）
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 41 B0 01 33 D2",                 // mov rbx, GWorld; test rbx, rbx; jz; mov r8b,1; xor edx,edx
                
                // 较短但仍精确（13字节）
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 41 B0 01",                       // mov rbx, GWorld; test rbx, rbx; jz; mov r8b,1
                
                // mov rcx 版本
                "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 41 B0 01 33 D2 48 8B D1",        // mov rcx 版本
                "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 41 B0 01 33 D2",                 // mov rcx 版本（短）
                
                // jz long 版本
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 0F 84 ?? ?? ?? ?? 41 B0 01",           // jz long 版本
            };

            Dictionary<long, int> targetCounts = new Dictionary<long, int>();
            string? successPattern = null;

            foreach (var pattern in patterns)
            {
                var results = SearchPattern(pattern);
                
                // 理想情况：1-5 个匹配
                if (results.Count > 0 && results.Count <= 5)
                {
                    Console.WriteLine($"✓ 找到精确模式！匹配 {results.Count} 次");
                    successPattern = pattern;
                    
                    foreach (var offset in results)
                    {
                        long targetOffset = CalculateRipRelativeOffset(offset, 7, 3);
                        if (!targetCounts.ContainsKey(targetOffset))
                            targetCounts[targetOffset] = 0;
                        targetCounts[targetOffset]++;
                    }
                    break; // 找到精确模式就停止
                }
                else if (results.Count > 5 && results.Count <= 20)
                {
                    // 可接受的范围，继续收集
                    Console.WriteLine($"模式匹配 {results.Count} 次（可接受）");
                    
                    foreach (var offset in results)
                    {
                        long targetOffset = CalculateRipRelativeOffset(offset, 7, 3);
                        if (!targetCounts.ContainsKey(targetOffset))
                            targetCounts[targetOffset] = 0;
                        targetCounts[targetOffset]++;
                    }
                }
            }

            if (targetCounts.Count == 0)
            {
                Console.WriteLine("⚠ 未找到 GWorld 引用");
                return -1;
            }

            // 如果只有一个唯一的目标地址，直接返回
            if (targetCounts.Count == 1)
            {
                var uniqueTarget = targetCounts.First();
                Console.WriteLine($"\n✅ 找到唯一目标: 0x{uniqueTarget.Key:X}");
                return uniqueTarget.Key;
            }

            // 多个候选地址，按引用次数排序
            var sortedTargets = targetCounts.OrderByDescending(kvp => kvp.Value).ToList();
            
            Console.WriteLine($"\n候选 GWorld 地址:");
            foreach (var kvp in sortedTargets.Take(10))
            {
                Console.WriteLine($"  偏移: 0x{kvp.Key:X} (被引用 {kvp.Value} 次)");
            }

            long bestCandidate = sortedTargets[0].Key;
            Console.WriteLine($"\n✓ 推荐使用: 0x{bestCandidate:X}");
            return bestCandidate;
        }

        /// <summary>
        /// 查找 GNames 偏移
        /// GNames 通常在 FName 解析代码中被引用
        /// </summary>
        public long FindGNames()
        {
            Console.WriteLine("\n【查找 GNames】");
            
            // 使用长模式，特征是 FName 解析代码
            string[] patterns = {
                // FName 解析的典型模式（包含位移操作）
                "48 8B 05 ?? ?? ?? ?? 48 63 ?? 48 C1 ?? ?? 48 8D",                    // mov rax, GNames; movsxd; shr; lea
                "48 8B 05 ?? ?? ?? ?? 48 63 ?? 48 C1 ?? ?? 48 03",                    // mov rax, GNames; movsxd; shr; add
                "48 8B 05 ?? ?? ?? ?? 48 63 ?? 48 C1 ?? ?? 4C 8B",                    // mov rax, GNames; movsxd; shr; mov r
                
                // 带检查的版本
                "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 63 ?? 48 C1",                 // mov rax, GNames; test rax, rax; jz; movsxd; shr
                "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 48 63 ?? 48 C1",                 // mov rcx 版本
                
                // 较短的模式
                "48 8B 05 ?? ?? ?? ?? 48 63 ?? 48 C1",                                // mov rax, GNames; movsxd; shr
                "48 8B 0D ?? ?? ?? ?? 48 63 ?? 48 C1",                                // mov rcx 版本
            };

            Dictionary<long, int> targetCounts = new Dictionary<long, int>();

            foreach (var pattern in patterns)
            {
                var results = SearchPattern(pattern);
                
                if (results.Count > 0 && results.Count <= 100)
                {
                    Console.WriteLine($"模式匹配 {results.Count} 次");
                    
                    foreach (var offset in results)
                    {
                        long targetOffset = CalculateRipRelativeOffset(offset, 7, 3);
                        if (!targetCounts.ContainsKey(targetOffset))
                            targetCounts[targetOffset] = 0;
                        targetCounts[targetOffset]++;
                    }
                    
                    // 如果找到少量匹配，可能已经足够精确
                    if (results.Count <= 20)
                        break;
                }
            }

            if (targetCounts.Count == 0)
            {
                Console.WriteLine("⚠ 未找到 GNames 引用");
                return -1;
            }

            // GNames 通常被引用很多次，选择最常见的
            var sortedTargets = targetCounts.OrderByDescending(kvp => kvp.Value).ToList();
            
            Console.WriteLine($"\n候选 GNames 地址:");
            foreach (var kvp in sortedTargets.Take(5))
            {
                Console.WriteLine($"  偏移: 0x{kvp.Key:X} (被引用 {kvp.Value} 次)");
            }

            long bestCandidate = sortedTargets[0].Key;
            Console.WriteLine($"\n✓ 推荐使用: 0x{bestCandidate:X} (被引用 {sortedTargets[0].Value} 次)");
            return bestCandidate;
        }

        /// <summary>
        /// 查找 GEngine 偏移
        /// </summary>
        public long FindGEngine()
        {
            Console.WriteLine("\n【查找 GEngine】");
            
            string[] patterns = {
                "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 48 8B 01",        // mov rcx, GEngine; test rcx, rcx; jz; mov rax, [rcx]
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 48 8B 03",        // mov rbx, GEngine; test rbx, rbx; jz; mov rax, [rbx]
                "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 48 8B 81",        // mov rcx, GEngine; test rcx, rcx; jz; mov rax, [rcx+offset]
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 48 8B 83",        // mov rbx, GEngine; test rbx, rbx; jz; mov rax, [rbx+offset]
            };

            Dictionary<long, int> targetCounts = new Dictionary<long, int>();

            foreach (var pattern in patterns)
            {
                var results = SearchPattern(pattern);
                if (results.Count > 0 && results.Count < 100)
                {
                    Console.WriteLine($"模式匹配 {results.Count} 次");
                    
                    foreach (var offset in results)
                    {
                        long targetOffset = CalculateRipRelativeOffset(offset, 7, 3);
                        if (!targetCounts.ContainsKey(targetOffset))
                            targetCounts[targetOffset] = 0;
                        targetCounts[targetOffset]++;
                    }
                }
            }

            if (targetCounts.Count == 0)
            {
                Console.WriteLine("⚠ 未找到 GEngine 引用");
                return -1;
            }

            // 过滤：GEngine 通常被引用 2-20 次
            var validTargets = targetCounts.Where(kvp => kvp.Value >= 2 && kvp.Value <= 20)
                                          .OrderByDescending(kvp => kvp.Value)
                                          .ToList();

            if (validTargets.Count == 0)
            {
                validTargets = targetCounts.OrderByDescending(kvp => kvp.Value).ToList();
            }

            Console.WriteLine($"\n候选 GEngine 地址:");
            foreach (var kvp in validTargets.Take(5))
            {
                Console.WriteLine($"  偏移: 0x{kvp.Key:X} (被引用 {kvp.Value} 次)");
            }

            long bestCandidate = validTargets[0].Key;
            Console.WriteLine($"\n✓ 推荐使用: 0x{bestCandidate:X}");
            return bestCandidate;
        }

        /// <summary>
        /// 查找字符串引用
        /// </summary>
        public List<long> FindStringReference(string searchString)
        {
            Console.WriteLine($"\n【查找字符串引用: \"{searchString}\"】");
            
            byte[] stringBytes = Encoding.ASCII.GetBytes(searchString);
            List<long> results = new List<long>();

            // 搜索字符串
            const int chunkSize = 1024 * 1024;
            for (long offset = 0; offset < moduleSize; offset += chunkSize)
            {
                int readSize = (int)Math.Min(chunkSize, moduleSize - offset);
                IntPtr readAddr = new IntPtr(moduleBase.ToInt64() + offset);
                
                byte[] buffer = ReadMemoryRegion(readAddr, readSize);
                if (buffer.Length == 0)
                    continue;

                for (int i = 0; i <= buffer.Length - stringBytes.Length; i++)
                {
                    bool found = true;
                    for (int j = 0; j < stringBytes.Length; j++)
                    {
                        if (buffer[i + j] != stringBytes[j])
                        {
                            found = false;
                            break;
                        }
                    }

                    if (found)
                    {
                        results.Add(offset + i);
                    }
                }
            }

            Console.WriteLine($"找到 {results.Count} 个字符串实例");
            return results;
        }

        /// <summary>
        /// 验证偏移是否有效
        /// </summary>
        public bool ValidateOffset(long offset, string name)
        {
            Console.WriteLine($"\n【验证 {name}】");
            
            IntPtr address = new IntPtr(moduleBase.ToInt64() + offset);
            byte[] buffer = ReadMemoryRegion(address, 8);
            
            if (buffer.Length < 8)
            {
                Console.WriteLine("⚠ 无法读取地址");
                return false;
            }

            long pointerValue = BitConverter.ToInt64(buffer, 0);
            Console.WriteLine($"  地址: 0x{address.ToInt64():X}");
            Console.WriteLine($"  指针值: 0x{pointerValue:X}");

            // 检查指针是否在合理范围内
            if (pointerValue > 0x10000 && pointerValue < 0x7FFFFFFFFFFF)
            {
                Console.WriteLine("  ✓ 指针值看起来有效");
                
                // 尝试读取指针指向的内容
                IntPtr targetAddr = new IntPtr(pointerValue);
                byte[] targetBuffer = ReadMemoryRegion(targetAddr, 16);
                
                if (targetBuffer.Length >= 16)
                {
                    Console.WriteLine($"  ✓ 可以读取目标地址的内容");
                    return true;
                }
            }
            else
            {
                Console.WriteLine("  ⚠ 指针值不在有效范围");
            }

            return false;
        }

        /// <summary>
        /// 生成 C# 代码
        /// </summary>
        public void GenerateCode(Dictionary<string, long> offsets)
        {
            Console.WriteLine("\n╔════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║  生成的 C# 代码");
            Console.WriteLine("╚════════════════════════════════════════════════════════════════╝\n");

            foreach (var kvp in offsets)
            {
                if (kvp.Value >= 0)
                {
                    Console.WriteLine($"const long {kvp.Key} = 0x{kvp.Value:X};");
                }
            }
        }

        /// <summary>
        /// 智能查找 GWorld（带验证）
        /// </summary>
        public long FindGWorldSmart()
        {
            Console.WriteLine("\n【智能查找 GWorld】");
            
            // 第一步：使用精确模式搜索
            string[] patterns = {
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 74",        // mov rbx, cs:GWorld; test rbx, rbx; jz
                "48 8B 1D ?? ?? ?? ?? 48 85 DB 0F 84",     // mov rbx, cs:GWorld; test rbx, rbx; jz (long)
            };

            Dictionary<long, int> candidates = new Dictionary<long, int>();

            foreach (var pattern in patterns)
            {
                var results = SearchPattern(pattern);
                foreach (var offset in results)
                {
                    long targetOffset = CalculateRipRelativeOffset(offset, 7, 3);
                    if (!candidates.ContainsKey(targetOffset))
                        candidates[targetOffset] = 0;
                    candidates[targetOffset]++;
                }
            }

            if (candidates.Count == 0)
            {
                Console.WriteLine("⚠ 未找到候选地址");
                return -1;
            }

            // 第二步：验证候选地址
            Console.WriteLine($"\n找到 {candidates.Count} 个候选地址，开始验证...");
            
            var validCandidates = new List<(long offset, int count)>();
            
            foreach (var kvp in candidates.OrderByDescending(x => x.Value).Take(20))
            {
                if (ValidateOffset(kvp.Key, $"候选 0x{kvp.Key:X}"))
                {
                    validCandidates.Add((kvp.Key, kvp.Value));
                }
            }

            if (validCandidates.Count == 0)
            {
                Console.WriteLine("⚠ 没有通过验证的候选地址");
                return -1;
            }

            // 返回引用次数最多的有效地址
            var best = validCandidates.OrderByDescending(x => x.count).First();
            Console.WriteLine($"\n✓ 最佳候选: 0x{best.offset:X} (被引用 {best.count} 次)");
            return best.offset;
        }

        /// <summary>
        /// 自动查找所有关键偏移
        /// </summary>
        public Dictionary<string, long> FindAllOffsets()
        {
            var offsets = new Dictionary<string, long>();

            offsets["OFFSET_WORLD"] = FindGWorldSmart();
            offsets["GNAMES_OFFSET"] = FindGNames();
            offsets["OFFSET_GAMEENGINE"] = FindGEngine();

            return offsets;
        }
    }
}
