using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 默认空诊断实现。
/// </summary>
public sealed class NoopRequestReplyDiagnostics : IRequestReplyDiagnostics
{
    /// <inheritdoc />
    public IDisposable StartRequestActivity(RequestContext context)
    {
        return NullDisposable.Instance;
    }

    /// <inheritdoc />
    public void MarkReplyReceived(RequestContext context)
    {
    }

    /// <inheritdoc />
    public void MarkTimeout(RequestContext context)
    {
    }

    /// <inheritdoc />
    public void MarkFailed(RequestContext context, Exception exception)
    {
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
