namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 对 CAP 发布能力的最小适配，便于核心流程测试和替换。
/// </summary>
public interface ICapRequestPublisher
{
    /// <summary>
    /// 发布 CAP 请求消息。
    /// </summary>
    /// <typeparam name="TMessage">消息体类型。</typeparam>
    /// <param name="topic">CAP 主题。</param>
    /// <param name="message">消息体。</param>
    /// <param name="headers">随请求写入的 CAP Header。</param>
    /// <param name="callbackName">CAP callbackName；非 callback 模式为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task PublishAsync<TMessage>(string topic, TMessage message, IReadOnlyDictionary<string, string> headers, string? callbackName,
        CancellationToken cancellationToken = default);
}