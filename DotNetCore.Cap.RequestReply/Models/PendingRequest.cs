namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// 记录一次请求从创建到完成、失败或超时的生命周期状态。
/// </summary>
public sealed class PendingRequest
{
    /// <summary>
    /// 存储层内部主键。
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 请求唯一 ID。
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// 关联链路 ID。
    /// </summary>
    public required string CorrelationId { get; init; }

    /// <summary>
    /// 请求发布到的 CAP 主题。
    /// </summary>
    public required string RequestTopic { get; init; }

    /// <summary>
    /// 响应写回地址。
    /// </summary>
    public required string ReplyTo { get; init; }

    /// <summary>
    /// 响应通道名称。
    /// </summary>
    public required string ReplyTransport { get; init; }

    /// <summary>
    /// 当前请求状态。
    /// </summary>
    public PendingRequestStatus Status { get; set; } = PendingRequestStatus.Pending;

    /// <summary>
    /// 请求数据类型名称。
    /// </summary>
    public string? RequestType { get; init; }

    /// <summary>
    /// 响应数据类型名称。
    /// </summary>
    public string? ResponseType { get; init; }

    /// <summary>
    /// 序列化后的请求正文。
    /// </summary>
    public string? RequestBody { get; init; }

    /// <summary>
    /// 序列化后的响应正文。
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// 失败错误码。
    /// </summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 失败错误消息。
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 请求过期时间。
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// 请求创建时间。
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// 请求完成、失败或晚到响应写入时间。
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// 创建当前状态的浅拷贝，避免内存存储内部对象被外部修改。
    /// </summary>
    /// <returns>请求状态副本。</returns>
    public PendingRequest Clone()
    {
        return new PendingRequest
        {
            Id = Id,
            RequestId = RequestId,
            CorrelationId = CorrelationId,
            RequestTopic = RequestTopic,
            ReplyTo = ReplyTo,
            ReplyTransport = ReplyTransport,
            Status = Status,
            RequestType = RequestType,
            ResponseType = ResponseType,
            RequestBody = RequestBody,
            ResponseBody = ResponseBody,
            ErrorCode = ErrorCode,
            ErrorMessage = ErrorMessage,
            ExpiresAt = ExpiresAt,
            CreatedAt = CreatedAt,
            CompletedAt = CompletedAt
        };
    }
}
