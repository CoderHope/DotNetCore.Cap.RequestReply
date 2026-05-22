using DotNetCore.Cap.RequestReply.Models;

namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// Reply 中转通道抽象，负责创建回复地址、发送回复和等待回复。
/// </summary>
public interface IReplyTransport
{
    /// <summary>
    /// 传输名称，会写入 <c>x-reply-transport</c> Header。
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 为当前请求创建回复地址。
    /// </summary>
    /// <param name="context">请求上下文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可写入 Header 的回复地址。</returns>
    Task<ReplyAddress> CreateReplyAddressAsync(RequestContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将响应写入指定回复地址。
    /// </summary>
    /// <typeparam name="TResponse">响应数据类型。</typeparam>
    /// <param name="address">回复地址。</param>
    /// <param name="reply">响应信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SendAsync<TResponse>(ReplyAddress address, ReplyEnvelope<TResponse> reply, CancellationToken cancellationToken = default);

    /// <summary>
    /// 等待当前请求的响应。
    /// </summary>
    /// <typeparam name="TResponse">响应数据类型。</typeparam>
    /// <param name="context">请求上下文。</param>
    /// <param name="timeout">等待超时时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应信封。</returns>
    Task<ReplyEnvelope<TResponse>> WaitAsync<TResponse>(RequestContext context, TimeSpan timeout, CancellationToken cancellationToken = default);
}