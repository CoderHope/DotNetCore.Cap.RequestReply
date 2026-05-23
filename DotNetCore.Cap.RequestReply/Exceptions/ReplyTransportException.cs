namespace DotNetCore.Cap.RequestReply.Exceptions;

/// <summary>
/// ReplyTransport 发送、等待或解析响应失败时抛出的异常。
/// </summary>
public class ReplyTransportException : CapRequestReplyException
{
    /// <summary>
    /// 使用指定消息创建传输异常。
    /// </summary>
    /// <param name="message">异常消息。</param>
    public ReplyTransportException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用指定消息和内部异常创建传输异常。
    /// </summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public ReplyTransportException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
