using DotNetCore.Cap.RequestReply.Models;

namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// Request/Reply 诊断扩展点，用于后续接入 OpenTelemetry。
/// </summary>
public interface IRequestReplyDiagnostics
{
    /// <summary>
    /// 开始一次请求诊断活动。
    /// </summary>
    /// <param name="context">请求上下文。</param>
    /// <returns>活动释放句柄。</returns>
    IDisposable StartRequestActivity(RequestContext context);

    /// <summary>
    /// 标记收到响应。
    /// </summary>
    /// <param name="context">请求上下文。</param>
    void MarkReplyReceived(RequestContext context);

    /// <summary>
    /// 标记请求超时。
    /// </summary>
    /// <param name="context">请求上下文。</param>
    void MarkTimeout(RequestContext context);

    /// <summary>
    /// 标记请求失败。
    /// </summary>
    /// <param name="context">请求上下文。</param>
    /// <param name="exception">失败异常。</param>
    void MarkFailed(RequestContext context, Exception exception);
}
