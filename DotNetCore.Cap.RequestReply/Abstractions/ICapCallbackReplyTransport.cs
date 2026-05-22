namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 标识基于 CAP callbackName 的 ReplyTransport。
/// </summary>
public interface ICapCallbackReplyTransport : IReplyTransport
{
    /// <summary>
    /// CAP 回调主题名称，RequestBus 发布请求时会作为 callbackName 传给 CAP。
    /// </summary>
    string CallbackTopic { get; }
}
