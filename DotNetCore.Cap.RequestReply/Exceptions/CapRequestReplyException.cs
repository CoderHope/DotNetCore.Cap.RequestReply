namespace DotNetCore.Cap.RequestReply.Exceptions;

/// <summary>
/// CAP Request/Reply 扩展的基础异常类型。
/// </summary>
public class CapRequestReplyException : Exception
{
    /// <summary>
    /// 创建基础异常。
    /// </summary>
    public CapRequestReplyException()
    {
    }

    /// <summary>
    /// 使用指定消息创建基础异常。
    /// </summary>
    /// <param name="message">异常消息。</param>
    public CapRequestReplyException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 使用指定消息和内部异常创建基础异常。
    /// </summary>
    /// <param name="message">异常消息。</param>
    /// <param name="innerException">内部异常。</param>
    public CapRequestReplyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
