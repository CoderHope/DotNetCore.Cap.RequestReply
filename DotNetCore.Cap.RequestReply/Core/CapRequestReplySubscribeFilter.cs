using System.Reflection;
using DotNetCore.CAP.Filter;
using DotNetCore.CAP.Messages;
using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Logging;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 将 CAP 订阅方法的普通返回值自动包装为 Request/Reply 响应并写回 ReplyTransport。
/// </summary>
public sealed class CapRequestReplySubscribeFilter : SubscribeFilter
{
    private static readonly string HandlerExceptionCode = "HANDLER_EXCEPTION";
    private readonly IReplyTransport _replyTransport;
    private readonly IRequestStore _requestStore;
    private readonly ILogger<CapRequestReplySubscribeFilter> _logger;

    /// <summary>
    /// 创建自动响应过滤器。
    /// </summary>
    public CapRequestReplySubscribeFilter(
        IReplyTransport replyTransport,
        IRequestStore requestStore,
        ILogger<CapRequestReplySubscribeFilter> logger)
    {
        _replyTransport = replyTransport;
        _requestStore = requestStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task OnSubscribeExecutedAsync(ExecutedContext context)
    {
        if (!IsCapRequestReplyHandler(context))
        {
            return;
        }

        var request = TryGetRequestEnvelope(context.DeliverMessage);
        if (request is null)
        {
            return;
        }

        var responseType = GetResponseType(context.ConsumerDescriptor.MethodInfo.ReturnType, context.Result);
        var reply = IsReplyEnvelope(context.Result)
            ? context.Result!
            : CreateReplyEnvelope(responseType, request, true, context.Result, null, null);

        context.Result = reply;
        await SendReplyIfNeededAsync(responseType, request, reply, context.DeliverMessage.Headers).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async Task OnSubscribeExceptionAsync(ExceptionContext context)
    {
        if (!IsCapRequestReplyHandler(context))
        {
            return;
        }

        var request = TryGetRequestEnvelope(context.DeliverMessage);
        if (request is null)
        {
            return;
        }

        var responseType = GetResponseType(context.ConsumerDescriptor.MethodInfo.ReturnType, null);
        var reply = CreateReplyEnvelope(
            responseType,
            request,
            false,
            GetDefaultValue(responseType),
            HandlerExceptionCode,
            context.Exception.Message);

        context.Result = reply;
        context.ExceptionHandled = true;
        await SendReplyIfNeededAsync(responseType, request, reply, context.DeliverMessage.Headers).ConfigureAwait(false);
    }

    private static bool IsCapRequestReplyHandler(FilterContext context)
    {
        return context.ConsumerDescriptor.MethodInfo.GetCustomAttribute<CapRequestReplyAttribute>(inherit: true) is not null;
    }

    private IRequestEnvelope? TryGetRequestEnvelope(Message message)
    {
        return message.Value is IRequestEnvelope request && HasRequestReplyHeaders(message.Headers, request)
            ? request
            : null;
    }

    private bool HasRequestReplyHeaders(IDictionary<string, string?> headers, IRequestEnvelope request)
    {
        if (!HeaderEquals(headers, RequestReplyHeaders.RequestId, request.RequestId) ||
            !HeaderEquals(headers, RequestReplyHeaders.CorrelationId, request.CorrelationId) ||
            !HeaderEquals(headers, RequestReplyHeaders.ReplyTo, request.ReplyTo) ||
            !headers.TryGetValue(RequestReplyHeaders.ReplyTransport, out _) ||
            !headers.TryGetValue(RequestReplyHeaders.RequestType, out _) ||
            !headers.TryGetValue(RequestReplyHeaders.ResponseType, out _))
        {
            return false;
        }

        if (!headers.TryGetValue(RequestReplyHeaders.EnvelopeVersion, out var version) ||
            !string.Equals(version, RequestReplyHeaders.CurrentEnvelopeVersion, StringComparison.Ordinal))
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Request/Reply headers rejected: envelope version missing or unsupported. requestId={RequestId} version={Version}",
                    request.RequestId,
                    version);
            }

            return false;
        }

        return true;
    }

    private static bool HeaderEquals(IDictionary<string, string?> headers, string name, string expectedValue)
    {
        return headers.TryGetValue(name, out var value) &&
               string.Equals(value, expectedValue, StringComparison.Ordinal);
    }

    private async Task SendReplyIfNeededAsync(
        Type responseType,
        IRequestEnvelope request,
        object reply,
        IDictionary<string, string?> headers)
    {
        if (request.ExpiresAt < DateTimeOffset.UtcNow)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "Request/Reply reply skipped: request expired. requestId={RequestId} expiresAt={ExpiresAt}",
                    request.RequestId,
                    request.ExpiresAt);
            }

            return;
        }

        var pending = await _requestStore.GetAsync(request.RequestId).ConfigureAwait(false);
        if (pending is { Status: not PendingRequestStatus.Pending and not PendingRequestStatus.Timeout })
        {
            return;
        }

        await SendReplyAsync(responseType, request, reply).ConfigureAwait(false);
    }

    private Task SendReplyAsync(Type responseType, IRequestEnvelope request, object reply)
    {
        var method = typeof(IReplyTransport)
            .GetMethod(nameof(IReplyTransport.SendAsync))!
            .MakeGenericMethod(responseType);
        var task = method.Invoke(
            _replyTransport,
            [ReplyAddress.Parse(request.ReplyTo), reply, CancellationToken.None]);

        return task as Task
               ?? throw new InvalidOperationException("Reply transport SendAsync did not return a task.");
    }

    private static object CreateReplyEnvelope(
        Type responseType,
        IRequestEnvelope request,
        bool success,
        object? data,
        string? errorCode,
        string? errorMessage)
    {
        return Activator.CreateInstance(
                   typeof(ReplyEnvelope<>).MakeGenericType(responseType),
                   request.RequestId,
                   request.CorrelationId,
                   success,
                   data ?? GetDefaultValue(responseType),
                   errorCode,
                   errorMessage)
               ?? throw new InvalidOperationException("Failed to create reply envelope.");
    }

    private static Type GetResponseType(Type returnType, object? result)
    {
        var unwrapped = UnwrapAwaitable(returnType);
        if (unwrapped.IsGenericType && unwrapped.GetGenericTypeDefinition() == typeof(ReplyEnvelope<>))
        {
            return unwrapped.GetGenericArguments()[0];
        }

        if (unwrapped == typeof(void) || unwrapped == typeof(Task))
        {
            return typeof(object);
        }

        return unwrapped == typeof(object) && result is not null
            ? result.GetType()
            : unwrapped;
    }

    private static Type UnwrapAwaitable(Type returnType)
    {
        if (returnType.IsGenericType &&
            (returnType.GetGenericTypeDefinition() == typeof(Task<>) ||
             returnType.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            return returnType.GetGenericArguments()[0];
        }

        return returnType;
    }

    private static bool IsReplyEnvelope(object? value)
    {
        return value?.GetType() is { IsGenericType: true } type &&
               type.GetGenericTypeDefinition() == typeof(ReplyEnvelope<>);
    }

    private static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
