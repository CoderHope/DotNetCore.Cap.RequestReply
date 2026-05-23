using System.Diagnostics;
using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 基于 <see cref="ActivitySource"/> 的 Request/Reply 诊断实现。
/// </summary>
public sealed class OpenTelemetryRequestReplyDiagnostics : IRequestReplyDiagnostics
{
    private const string ActivitySourceName = "DotNetCore.Cap.RequestReply";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    /// <inheritdoc />
    public IDisposable StartRequestActivity(RequestContext context)
    {
        var activity = ActivitySource.StartActivity("cap.request_reply", ActivityKind.Client);
        if (activity is null)
        {
            return NullDisposable.Instance;
        }

        activity.SetTag("request.id", context.RequestId);
        activity.SetTag("correlation.id", context.CorrelationId);
        activity.SetTag("messaging.destination.name", context.Topic);
        activity.SetTag("request_reply.transport", context.TransportName);
        activity.SetTag("request_reply.reply_to", context.ReplyTo);
        return activity;
    }

    /// <inheritdoc />
    public void MarkReplyReceived(RequestContext context)
    {
        Activity.Current?.AddEvent(new ActivityEvent("reply_received"));
        Activity.Current?.SetStatus(ActivityStatusCode.Ok);
    }

    /// <inheritdoc />
    public void MarkTimeout(RequestContext context)
    {
        Activity.Current?.AddEvent(new ActivityEvent("timeout"));
        Activity.Current?.SetStatus(ActivityStatusCode.Error, "Request timed out.");
    }

    /// <inheritdoc />
    public void MarkCanceled(RequestContext context)
    {
        Activity.Current?.AddEvent(new ActivityEvent("canceled"));
        Activity.Current?.SetStatus(ActivityStatusCode.Ok, "Request canceled.");
    }

    /// <inheritdoc />
    public void MarkFailed(RequestContext context, Exception exception)
    {
        Activity.Current?.AddEvent(new ActivityEvent("failed"));
        Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    private sealed class NullDisposable : IDisposable
    {
        public static readonly NullDisposable Instance = new();

        public void Dispose()
        {
        }
    }
}
