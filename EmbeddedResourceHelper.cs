using System;
using System.IO;
using System.Reflection;

namespace MMP;

/// <summary>
/// 嵌入资源辅助类，用于从程序集中提取嵌入的模型文件
/// </summary>
public static class EmbeddedResourceHelper
{
    private static readonly string TempModelsPath = Path.Combine(Path.GetTempPath(), "MMP_Models");
    private static bool _extracted = false;

    /// <summary>
    /// 确保模型文件已提取到临时目录
    /// </summary>
    public static string EnsureModelsExtracted()
    {
        if (_extracted && Directory.Exists(TempModelsPath))
        {
            return TempModelsPath;
        }

        Console.WriteLine("[资源提取] 正在提取嵌入的模型文件...");
        
        // 创建临时目录
        if (!Directory.Exists(TempModelsPath))
        {
            Directory.CreateDirectory(TempModelsPath);
        }

        var assembly = Assembly.GetExecutingAssembly();
        var resourceNames = assembly.GetManifestResourceNames();
        
        int extractedCount = 0;
        foreach (var resourceName in resourceNames)
        {
            // 只处理 models 目录下的文件
            if (!resourceName.Contains(".models."))
                continue;

            // 提取文件名（例如：MMP.models.ch_PP-OCRv5_mobile_det.onnx -> ch_PP-OCRv5_mobile_det.onnx）
            var parts = resourceName.Split(".models.");
            if (parts.Length != 2)
                continue;

            var fileName = parts[1];
            var targetPath = Path.Combine(TempModelsPath, fileName);

            // 如果文件已存在且大小正确，跳过
            if (File.Exists(targetPath))
            {
                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream != null)
                {
                    var fileInfo = new FileInfo(targetPath);
                    if (fileInfo.Length == resourceStream.Length)
                    {
                        continue; // 文件已存在且大小正确
                    }
                }
            }

            // 提取文件
            try
            {
                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    Console.WriteLine($"[资源提取] 警告: 无法读取资源 {resourceName}");
                    continue;
                }

                using var fileStream = File.Create(targetPath);
                resourceStream.CopyTo(fileStream);
                extractedCount++;
                
                Console.WriteLine($"[资源提取] ✓ {fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[资源提取] ✗ 提取 {fileName} 失败: {ex.Message}");
            }
        }

        if (extractedCount > 0)
        {
            Console.WriteLine($"[资源提取] 完成，共提取 {extractedCount} 个文件");
        }
        else
        {
            Console.WriteLine($"[资源提取] 所有文件已存在，跳过提取");
        }

        _extracted = true;
        return TempModelsPath;
    }

    /// <summary>
    /// 获取模型文件的完整路径
    /// </summary>
    public static string GetModelPath(string fileName)
    {
        var modelsPath = EnsureModelsExtracted();
        return Path.Combine(modelsPath, fileName);
    }

    /// <summary>
    /// 直接从嵌入资源读取模型文件的字节数组（无需临时文件）
    /// </summary>
    public static byte[]? GetModelBytes(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith($".models.{fileName}"));

        if (resourceName == null)
        {
            Console.WriteLine($"[资源读取] 警告: 找不到嵌入资源 {fileName}");
            return null;
        }

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
                return null;

            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[资源读取] 读取 {fileName} 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 清理临时模型文件
    /// </summary>
    public static void CleanupTempModels()
    {
        try
        {
            if (Directory.Exists(TempModelsPath))
            {
                Directory.Delete(TempModelsPath, true);
                Console.WriteLine("[资源提取] 已清理临时文件");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[资源提取] 清理临时文件失败: {ex.Message}");
        }
    }
}
