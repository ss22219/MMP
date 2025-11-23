# UE4/UE5 内存偏移查找器

自动从游戏进程中查找关键内存偏移的工具。

## 功能特性

- ✅ 自动查找 GWorld、GNames、GEngine 等全局偏移
- ✅ 支持字节模式搜索（带通配符）
- ✅ 自动计算 RIP 相对寻址地址
- ✅ 字符串引用搜索
- ✅ 生成 C# 代码

## 使用方法

### 1. 编译并运行

```bash
dotnet run TestOffsetFinder.cs
```

### 2. 输入游戏进程名

例如：`Client-Win64-Shipping`（不带 .exe）

### 3. 选择操作

#### 选项 1: 自动查找所有关键偏移
自动查找 GWorld、GNames、GEngine 并生成代码

#### 选项 2-4: 单独查找
- 查找 GWorld
- 查找 GNames  
- 查找 GEngine

#### 选项 5: 自定义模式搜索
输入字节模式，例如：
```
48 8B 1D ?? ?? ?? ??
```
- 使用 `??` 表示通配符（任意字节）
- 支持自动计算 RIP 相对地址

#### 选项 6: 查找字符串引用
搜索特定字符串在内存中的位置

#### 选项 7: 计算 RIP 相对地址
手动计算 RIP 相对寻址的目标地址

## 示例：从 IDA Pro 汇编代码查找偏移

### 步骤 1: 在 IDA Pro 中找到引用

```assembly
.text:00000001408EFDAD 48 8B 1D E4 A2 6D 06    mov rbx, cs:GWorld
```

### 步骤 2: 提取信息

- **指令地址**: `0x1408EFDAD`（去掉模块基址 `0x140000000` = `0x8EFDAD`）
- **字节模式**: `48 8B 1D ?? ?? ?? ??`
- **指令长度**: 7 字节

### 步骤 3: 使用查找器

1. 选择 "5. 自定义模式搜索"
2. 输入模式: `48 8B 1D ?? ?? ?? ??`
3. 选择计算 RIP 相对地址
4. 输入指令长度: `7`
5. 选择匹配的结果

### 步骤 4: 获取偏移

工具会自动计算并显示：
```
✓ 目标偏移: 0x6FCA098
```

## 常见汇编模式

### GWorld
```assembly
48 8B 1D ?? ?? ?? ??    ; mov rbx, cs:GWorld
48 8B 0D ?? ?? ?? ??    ; mov rcx, cs:GWorld
```

### GNames
```assembly
48 8B 05 ?? ?? ?? ?? 48 85 C0    ; mov rax, cs:GNames; test rax, rax
48 8B 0D ?? ?? ?? ?? 48 85 C9    ; mov rcx, cs:GNames; test rcx, rcx
```

### GEngine
```assembly
48 8B 0D ?? ?? ?? ?? 48 85 C9 74    ; mov rcx, cs:GEngine; test rcx, rcx
```

## RIP 相对寻址计算公式

```
目标地址 = 指令地址 + 指令长度 + 偏移值
```

例如：
```
指令地址: 0x8EFDAD
指令长度: 7
偏移值: 0x066DA2E4 (从指令中提取)

下一条指令: 0x8EFDAD + 7 = 0x8EFDB4
目标地址: 0x8EFDB4 + 0x066DA2E4 = 0x6FCA098
```

## 输出示例

```
【查找 GWorld】
搜索模式: 48 8B 1D ?? ?? ?? ??
搜索范围: 0x0 - 0x7000000 (112 MB)
  ✓ 找到 15 个匹配
找到 15 个候选地址:
  指令地址: 0x8EFDAD -> GWorld 偏移: 0x6FCA098
  指令地址: 0x8F0123 -> GWorld 偏移: 0x6FCA098
  ...

生成的 C# 代码:
const long OFFSET_WORLD = 0x6FCA098;
const long GNAMES_OFFSET = 0x6E1FA80;
const long OFFSET_GAMEENGINE = 0x6FC64A0;
```

## 注意事项

1. **需要管理员权限**：读取其他进程内存需要管理员权限
2. **游戏必须运行**：确保游戏进程正在运行
3. **反作弊系统**：某些游戏有反作弊保护，可能无法读取内存
4. **版本差异**：不同游戏版本的偏移可能不同

## 集成到项目

找到偏移后，更新 `GetBattleEntitiesAPI.cs` 中的常量：

```csharp
const long OFFSET_WORLD = 0x6FCA098;      // 从查找器获取
const long GNAMES_OFFSET = 0x6E1FA80;     // 从查找器获取
const long OFFSET_GAMEENGINE = 0x6FC64A0; // 从查找器获取
```

## 高级用法

### 查找类成员偏移

1. 在 IDA Pro 中找到类结构
2. 查看成员变量的偏移
3. 或使用 ReClass.NET 动态分析

### 验证偏移

使用 Cheat Engine：
1. 附加到游戏进程
2. 计算 `模块基址 + 偏移`
3. 查看指针是否有效
4. 验证数据结构

## 故障排除

### 找不到模式
- 检查字节模式是否正确
- 尝试缩短模式或增加通配符
- 确认游戏版本是否匹配

### 计算的偏移不正确
- 确认指令长度是否正确
- 检查是否是 RIP 相对寻址
- 验证模块基址

### 无法读取内存
- 以管理员身份运行
- 检查反作弊系统
- 确认进程名正确
