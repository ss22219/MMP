using System.Threading;
using System.Threading.Tasks;

namespace MMP.States
{
    /// <summary>
    /// 状态处理器接口
    /// </summary>
    public interface IStateHandler
    {
        /// <summary>
        /// 执行状态逻辑
        /// </summary>
        /// <param name="context">状态上下文</param>
        /// <param name="ocrResult">OCR 识别结果</param>
        /// <param name="ct">取消令牌</param>
        Task ExecuteAsync(StateContext context, OcrEngine.OcrResult? ocrResult, CancellationToken ct);
        
        /// <summary>
        /// 清理资源（当状态被中断时调用）
        /// </summary>
        /// <param name="context">状态上下文</param>
        void Cleanup(StateContext context);
    }
}
