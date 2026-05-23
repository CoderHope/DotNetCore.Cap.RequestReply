namespace DotNetCore.Cap.RequestReply.Exceptions;

/// <summary>
/// 请求等待响应超时时抛出的异常。
/// </summary>
public class RequestTimeoutException : CapRequestReplyException
{
    /// <summary>
    /// 创建请求超时异常。
    /// </summary>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="timeout">超时时间。</param>
    public RequestTimeoutException(string requestId, TimeSpan timeout)
        : base($"Request '{requestId}' timed out after {timeout.TotalMilliseconds:0} ms.")
    {
        RequestId = requestId;
        Timeout = timeout;
    }

    /// <summary>
    /// 请求 ID。
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// 超时时间。
    /// </summary>
    public TimeSpan Timeout { get; }
}
