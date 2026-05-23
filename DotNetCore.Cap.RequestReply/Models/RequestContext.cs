namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// 一次 Request/Reply 调用的运行时上下文。
/// </summary>
public sealed class RequestContext
{
    /// <summary>
    /// 请求唯一 ID。
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// 关联链路 ID。
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// CAP 请求主题。
    /// </summary>
    public required string Topic { get; init; }

    /// <summary>
    /// 响应写回地址。
    /// </summary>
    public required string ReplyTo { get; init; }

    /// <summary>
    /// 请求创建时间。
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 请求过期时间。
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// 本次请求等待超时时间。
    /// </summary>
    public required TimeSpan Timeout { get; init; }

    /// <summary>
    /// 当前使用的响应通道名称。
    /// </summary>
    public required string TransportName { get; init; }
}
