using DotNetCore.Cap.RequestReply.Models;
using CapHeaders = DotNetCore.CAP.Messages.Headers;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// CAP Request/Reply Header 名称和创建逻辑。
/// </summary>
public static class RequestReplyHeaders
{
    /// <summary>
    /// 请求唯一 ID Header。
    /// </summary>
    public const string RequestId = "cap-request-reply-request-id";

    /// <summary>
    /// 关联链路 ID Header。
    /// </summary>
    public const string CorrelationId = "cap-request-reply-correlation-id";

    /// <summary>
    /// 响应写回地址 Header。
    /// </summary>
    public const string ReplyTo = "cap-request-reply-reply-to";

    /// <summary>
    /// 响应通道名称 Header。
    /// </summary>
    public const string ReplyTransport = "cap-request-reply-transport";

    /// <summary>
    /// 请求类型 Header。
    /// </summary>
    public const string RequestType = "cap-request-reply-request-type";

    /// <summary>
    /// 响应类型 Header。
    /// </summary>
    public const string ResponseType = "cap-request-reply-response-type";

    /// <summary>
    /// 过期时间 Header。
    /// </summary>
    public const string ExpiresAt = "cap-request-reply-expires-at";

    /// <summary>
    /// 超时毫秒数 Header。
    /// </summary>
    public const string TimeoutMs = "cap-request-reply-timeout-ms";

    /// <summary>
    /// 信封版本 Header。
    /// </summary>
    public const string EnvelopeVersion = "cap-request-reply-envelope-version";

    /// <summary>
    /// 当前支持的信封版本。
    /// </summary>
    public const string CurrentEnvelopeVersion = "1";

    /// <summary>
    /// 基于请求上下文创建 CAP Header。
    /// </summary>
    /// <typeparam name="TRequest">请求数据类型。</typeparam>
    /// <typeparam name="TResponse">响应数据类型。</typeparam>
    /// <param name="context">请求上下文。</param>
    /// <returns>Header 字典。</returns>
    public static IReadOnlyDictionary<string, string> Create<TRequest, TResponse>(RequestContext context)
    {
        return new Dictionary<string, string>
        {
            [RequestId] = context.RequestId,
            [CorrelationId] = context.CorrelationId,
            [CapHeaders.CorrelationId] = context.CorrelationId,
            [ReplyTo] = context.ReplyTo,
            [ReplyTransport] = context.TransportName,
            [RequestType] = FormatTypeName(typeof(TRequest)),
            [ResponseType] = FormatTypeName(typeof(TResponse)),
            [ExpiresAt] = context.ExpiresAt.UtcDateTime.ToString("O"),
            [TimeoutMs] = ((long)context.Timeout.TotalMilliseconds).ToString(),
            [EnvelopeVersion] = CurrentEnvelopeVersion
        };
    }

    /// <summary>
    /// 格式化类型名称，优先使用完整类型名。
    /// </summary>
    /// <param name="type">类型。</param>
    /// <returns>类型名称。</returns>
    public static string FormatTypeName(Type type)
    {
        return type.FullName ?? type.Name;
    }
}
