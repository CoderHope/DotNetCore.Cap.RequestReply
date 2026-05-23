namespace DotNetCore.Cap.RequestReply.Exceptions;

/// <summary>
/// 消费方返回失败响应时抛出的异常。
/// </summary>
public class RequestFailedException : CapRequestReplyException
{
    /// <summary>
    /// 创建请求失败异常。
    /// </summary>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="errorMessage">错误消息。</param>
    public RequestFailedException(string requestId, string? errorCode, string? errorMessage)
        : base($"Request '{requestId}' failed with code '{errorCode ?? "UNKNOWN"}': {errorMessage}")
    {
        RequestId = requestId;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 请求 ID。
    /// </summary>
    public string RequestId { get; }

    /// <summary>
    /// 错误码。
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// 错误消息。
    /// </summary>
    public string? ErrorMessage { get; }
}
