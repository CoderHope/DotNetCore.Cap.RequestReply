namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// 非泛型请求信封视图，用于订阅过滤器自动写回响应。
/// </summary>
public interface IRequestEnvelope
{
    /// <summary>
    /// 请求唯一 ID。
    /// </summary>
    string RequestId { get; }

    /// <summary>
    /// 关联链路 ID。
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// 响应写回地址。
    /// </summary>
    string ReplyTo { get; }

    /// <summary>
    /// 请求过期时间。
    /// </summary>
    DateTimeOffset ExpiresAt { get; }

    /// <summary>
    /// 请求数据。
    /// </summary>
    object? Data { get; }
}

/// <summary>
/// 请求方发布到 CAP 的请求。
/// </summary>
/// <typeparam name="T">请求数据类型。</typeparam>
/// <param name="RequestId">请求唯一 ID。</param>
/// <param name="CorrelationId">关联链路 ID。</param>
/// <param name="ReplyTo">响应写回地址。</param>
/// <param name="ExpiresAt">请求过期时间。</param>
/// <param name="Data">请求数据。</param>
public sealed record RequestEnvelope<T>(string RequestId, string CorrelationId, string ReplyTo, DateTimeOffset ExpiresAt, T Data) : IRequestEnvelope
{
    object? IRequestEnvelope.Data => Data;
}
