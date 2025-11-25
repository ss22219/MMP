using System.IO;
using System.Text.Json;

namespace MMP
{
    /// <summary>
    /// 应用程序配置
    /// </summary>
    public class AppConfig
    {
        private static readonly string ConfigPath = "config.json";

        /// <summary>
        /// 热键配置
        /// </summary>
        public HotkeyConfig Hotkeys { get; set; } = new();

        /// <summary>
        /// 超时配置（秒）
        /// </summary>
        public TimeoutConfig Timeouts { get; set; } = new();

        /// <summary>
        /// 战斗配置
        /// </summary>
        public MMP.BattleConfig Battle { get; set; } = new();

        /// <summary>
        /// OCR 识别配置
        /// </summary>
        public OcrConfig Ocr { get; set; } = new();

        /// <summary>
        /// 加载配置
        /// </summary>
        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"加载配置失败: {ex.Message}");
            }

            return new AppConfig();
        }

        /// <summary>
        /// 保存配置
        /// </summary>
        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(ConfigPath, json);
                System.Console.WriteLine("配置已保存");
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"保存配置失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 热键配置
    /// </summary>
    public class HotkeyConfig
    {
        /// <summary>
        /// 停止运行热键（默认 F10）
        /// </summary>
        public string ForceExit { get; set; } = "F10";

        /// <summary>
        /// 强制退出深渊热键（默认 F12）
        /// </summary>
        public string ForceExitAbyss { get; set; } = "F12";
    }

    /// <summary>
    /// 超时配置（秒）
    /// </summary>
    public class TimeoutConfig
    {
        /// <summary>
        /// 状态超时时间（默认 300 秒 = 5 分钟）
        /// </summary>
        public int StateTimeout { get; set; } = 300;
    }

    public class BattleConfig
    {
        public float MonsterDetectionRange { get; set; } = 30000;
        public float ApproachDistance { get; set; } = 3000;
        
        // Q 技能配置
        public int QSkillInterval { get; set; } = 1000;
        
        // E 技能配置
        public int ESkillCount { get; set; } = 4;
        public int ESkillInterval { get; set; } = 1000;
        
        // 普通攻击配置
        public int AttackInterval { get; set; } = 350;
        public int AttackCount { get; set; } = 3;
        public int AttackRecoveryDelay { get; set; } = 50;
    }

    /// <summary>
    /// OCR 识别配置
    /// </summary>
    public class OcrConfig
    {
        /// <summary>
        /// OCR 识别间隔（毫秒，默认 500ms）
        /// </summary>
        public int OcrInterval { get; set; } = 500;

        /// <summary>
        /// OCR 识别置信度阈值（0-1，默认 0.5）
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.5f;

        /// <summary>
        /// 是否显示识别结果（默认 false）
        /// </summary>
        public bool ShowRecognitionResults { get; set; } = false;

        /// <summary>
        /// 最小文本长度（默认 1）
        /// </summary>
        public int MinTextLength { get; set; } = 1;
    }
}
