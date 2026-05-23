using System.Diagnostics;
using DotNetCore.CAP;
using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Exceptions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 请求者实现，用于请求服务。
/// </summary>
internal sealed class CapRequestReplyRequester : ICapRequestReplyRequester
{
    private readonly IReplyTransport _replyTransport;
    private readonly IRequestStore _requestStore;
    private readonly IRequestSerializer _serializer;
    private readonly IRequestIdGenerator _requestIdGenerator;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly IRequestReplyDiagnostics _diagnostics;
    private readonly RequestReplyOptions _options;
    private readonly ILogger<CapRequestReplyRequester> _logger;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="replyTransport">响应通道。</param>
    /// <param name="requestStore">请求状态存储。</param>
    /// <param name="serializer">请求序列化器。</param>
    /// <param name="requestIdGenerator">请求 ID 生成器。</param>
    /// <param name="correlationIdProvider">关联链路 ID 生成器。</param>
    /// <param name="diagnostics">请求回复诊断。</param>
    /// <param name="options">请求回复选项。</param>
    /// <param name="logger">日志记录器。</param>
    public CapRequestReplyRequester(IReplyTransport replyTransport, IRequestStore requestStore, IRequestSerializer serializer,
        IRequestIdGenerator requestIdGenerator, ICorrelationIdProvider correlationIdProvider, IRequestReplyDiagnostics diagnostics,
        IOptions<RequestReplyOptions> options, ILogger<CapRequestReplyRequester> logger)
    {
        _replyTransport = replyTransport;
        _requestStore = requestStore;
        _serializer = serializer;
        _requestIdGenerator = requestIdGenerator;
        _correlationIdProvider = correlationIdProvider;
        _diagnostics = diagnostics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<TResponse> RequestAsync<TRequest, TResponse>(ICapPublisher capPublisher, string topic, TRequest request, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capPublisher);
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);

        var effectiveTimeout = timeout ?? _options.DefaultTimeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Request timeout must be greater than zero.");
        }

        var startedAt = Stopwatch.GetTimestamp();
        var createdAt = DateTimeOffset.UtcNow;
        var preliminaryContext = new RequestContext
        {
            RequestId = _requestIdGenerator.CreateRequestId(),
            CorrelationId = _correlationIdProvider.CreateCorrelationId(),
            Topic = topic,
            ReplyTo = string.Empty,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.Add(effectiveTimeout),
            Timeout = effectiveTimeout,
            TransportName = _replyTransport.Name
        };

        var replyAddress = await _replyTransport.CreateReplyAddressAsync(preliminaryContext, cancellationToken)
            .ConfigureAwait(false);
        var context = new RequestContext
        {
            RequestId = preliminaryContext.RequestId,
            CorrelationId = preliminaryContext.CorrelationId,
            Topic = preliminaryContext.Topic,
            ReplyTo = replyAddress.ToString(),
            CreatedAt = preliminaryContext.CreatedAt,
            ExpiresAt = preliminaryContext.ExpiresAt,
            Timeout = preliminaryContext.Timeout,
            TransportName = preliminaryContext.TransportName
        };

        using var activity = _diagnostics.StartRequestActivity(context);
        var pendingRequest = new PendingRequest
        {
            RequestId = context.RequestId,
            CorrelationId = context.CorrelationId,
            RequestTopic = context.Topic,
            ReplyTo = context.ReplyTo,
            ReplyTransport = context.TransportName,
            Status = PendingRequestStatus.Pending,
            RequestType = RequestReplyHeaders.FormatTypeName(typeof(TRequest)),
            ResponseType = RequestReplyHeaders.FormatTypeName(typeof(TResponse)),
            RequestBody = _serializer.Serialize(request),
            ExpiresAt = context.ExpiresAt,
            CreatedAt = context.CreatedAt
        };
        var envelope = new RequestEnvelope<TRequest>(context.RequestId, context.CorrelationId, context.ReplyTo, context.ExpiresAt, request);
        var headers = RequestReplyHeaders.Create<TRequest, TResponse>(context);
        var capHeaders = headers.ToDictionary(static item => item.Key,
            static string? (item) => item.Value);

        try
        {
            await _requestStore.CreateAsync(pendingRequest, cancellationToken).ConfigureAwait(false);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "CAP request created. requestId={RequestId} correlationId={CorrelationId} topic={Topic} replyTo={ReplyTo} transport={Transport} status={Status}",
                    context.RequestId,
                    context.CorrelationId,
                    context.Topic,
                    context.ReplyTo,
                    context.TransportName,
                    PendingRequestStatus.Pending);
            }

            try
            {
                await capPublisher.PublishAsync(topic, envelope, capHeaders, cancellationToken)
                    .ConfigureAwait(false);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "CAP request published. requestId={RequestId} correlationId={CorrelationId} topic={Topic} replyTo={ReplyTo} transport={Transport}",
                        context.RequestId,
                        context.CorrelationId,
                        context.Topic,
                        context.ReplyTo,
                        context.TransportName);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _requestStore.MarkFailedAsync(context.RequestId, "PUBLISH_FAILED", exception.Message, cancellationToken)
                    .ConfigureAwait(false);
                _diagnostics.MarkFailed(context, exception);
                throw;
            }

            var reply = await _replyTransport.WaitAsync<TResponse>(context, effectiveTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(reply.RequestId, context.RequestId, StringComparison.Ordinal))
            {
                throw new ReplyTransportException(
                    $"Reply request id '{reply.RequestId}' does not match pending request '{context.RequestId}'.");
            }

            if (!string.Equals(reply.CorrelationId, context.CorrelationId, StringComparison.Ordinal))
            {
                throw new ReplyTransportException(
                    $"Reply correlation id '{reply.CorrelationId}' does not match pending request '{context.CorrelationId}'.");
            }

            var responseBody = _serializer.Serialize(reply);
            if (reply.Success)
            {
                await _requestStore.MarkCompletedAsync(context.RequestId, responseBody, cancellationToken)
                    .ConfigureAwait(false);
                _diagnostics.MarkReplyReceived(context);
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "CAP request completed. requestId={RequestId} correlationId={CorrelationId} topic={Topic} replyTo={ReplyTo} transport={Transport} status={Status} elapsedMs={ElapsedMs}",
                        context.RequestId,
                        context.CorrelationId,
                        context.Topic,
                        context.ReplyTo,
                        context.TransportName,
                        PendingRequestStatus.Completed,
                        Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                }

                return reply.Data!;
            }

            await _requestStore.MarkFailedAsync(context.RequestId, reply.ErrorCode ?? "REQUEST_FAILED",
                    reply.ErrorMessage ?? "Request handler returned a failure reply.", cancellationToken)
                .ConfigureAwait(false);
            var requestFailedException = new RequestFailedException(context.RequestId, reply.ErrorCode, reply.ErrorMessage);
            _diagnostics.MarkFailed(context, requestFailedException);
            throw requestFailedException;
        }
        catch (TimeoutException)
        {
            await MarkTimeoutAsync(context, startedAt, cancellationToken).ConfigureAwait(false);
            throw new RequestTimeoutException(context.RequestId, effectiveTimeout);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await MarkTimeoutAsync(context, startedAt, CancellationToken.None).ConfigureAwait(false);
            throw new RequestTimeoutException(context.RequestId, effectiveTimeout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await _requestStore.MarkCanceledAsync(context.RequestId, cancellationToken).ConfigureAwait(false);
            await _replyTransport.AbandonAsync(context, CancellationToken.None).ConfigureAwait(false);
            _diagnostics.MarkCanceled(context);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug(
                    "CAP request canceled. requestId={RequestId} correlationId={CorrelationId} topic={Topic} replyTo={ReplyTo} transport={Transport} status={Status} elapsedMs={ElapsedMs}",
                    context.RequestId,
                    context.CorrelationId,
                    context.Topic,
                    context.ReplyTo,
                    context.TransportName,
                    PendingRequestStatus.Canceled,
                    Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            }
            throw;
        }
    }
    
    private async Task MarkTimeoutAsync(RequestContext context, long startedAt, CancellationToken cancellationToken)
    {
        await _requestStore.MarkTimeoutAsync(context.RequestId, cancellationToken).ConfigureAwait(false);
        await _replyTransport.AbandonAsync(context, CancellationToken.None).ConfigureAwait(false);
        _diagnostics.MarkTimeout(context);
        _logger.LogWarning(
            "CAP request timed out. requestId={RequestId} correlationId={CorrelationId} topic={Topic} replyTo={ReplyTo} transport={Transport} status={Status} elapsedMs={ElapsedMs}",
            context.RequestId,
            context.CorrelationId,
            context.Topic,
            context.ReplyTo,
            context.TransportName,
            PendingRequestStatus.Timeout,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
    }
}
