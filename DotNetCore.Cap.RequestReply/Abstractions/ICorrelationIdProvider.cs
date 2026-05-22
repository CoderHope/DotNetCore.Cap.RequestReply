namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 创建请求链路 CorrelationId。
/// </summary>
public interface ICorrelationIdProvider
{
    /// <summary>
    /// 创建新的 CorrelationId。
    /// </summary>
    /// <returns>CorrelationId 字符串。</returns>
    string CreateCorrelationId();
}
