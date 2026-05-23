namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// PendingRequest 生命周期状态。
/// </summary>
public enum PendingRequestStatus
{
    /// <summary>
    /// 请求已创建，正在等待响应。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 请求已收到成功响应。
    /// </summary>
    Completed = 1,

    /// <summary>
    /// 请求已收到失败响应。
    /// </summary>
    Failed = 2,

    /// <summary>
    /// 请求等待超时。
    /// </summary>
    Timeout = 3,

    /// <summary>
    /// 请求被调用方取消。
    /// </summary>
    Canceled = 4,

    /// <summary>
    /// 请求方已超时，但之后又收到响应。
    /// </summary>
    CompletedAfterTimeout = 5
}
