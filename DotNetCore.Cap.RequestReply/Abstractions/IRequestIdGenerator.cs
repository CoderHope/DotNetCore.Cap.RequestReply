namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 创建唯一 RequestId。
/// </summary>
public interface IRequestIdGenerator
{
    /// <summary>
    /// 创建新的 RequestId。
    /// </summary>
    /// <returns>RequestId 字符串。</returns>
    string CreateRequestId();
}
