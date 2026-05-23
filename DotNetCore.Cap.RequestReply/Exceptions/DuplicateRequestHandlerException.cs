namespace DotNetCore.Cap.RequestReply.Exceptions;

/// <summary>
/// 同一请求主题注册多个 Handler 时抛出的异常。
/// </summary>
public class DuplicateRequestHandlerException : CapRequestReplyException
{
    /// <summary>
    /// 创建重复 Handler 异常。
    /// </summary>
    /// <param name="topic">重复注册的请求主题。</param>
    public DuplicateRequestHandlerException(string topic)
        : base($"Duplicate CAP request handler registered for topic '{topic}'.")
    {
        Topic = topic;
    }

    /// <summary>
    /// 重复注册的请求主题。
    /// </summary>
    public string Topic { get; }
}
