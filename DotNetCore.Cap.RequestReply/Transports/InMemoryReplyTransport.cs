using System.Collections.Concurrent;
using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Logging;

namespace DotNetCore.Cap.RequestReply.Transports;

/// <summary>
/// 基于进程内 TaskCompletionSource 的响应通道。
/// </summary>
public sealed class InMemoryReplyTransport : IReplyTransport
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<object>> _waiters = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, object> _completedReplies = new(StringComparer.Ordinal);
    private readonly ILogger<InMemoryReplyTransport> _logger;

    /// <summary>
    /// 创建内存响应通道。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public InMemoryReplyTransport(ILogger<InMemoryReplyTransport> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "memory";

    /// <inheritdoc />
    public Task<ReplyAddress> CreateReplyAddressAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ReplyAddress("memory", context.RequestId));
    }

    /// <inheritdoc />
    public Task SendAsync<TResponse>(ReplyAddress address, ReplyEnvelope<TResponse> reply, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_waiters.TryRemove(reply.RequestId, out var waiter))
        {
            waiter.TrySetResult(reply);
            return Task.CompletedTask;
        }

        _completedReplies[reply.RequestId] = reply;
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("In-memory reply stored without active waiter. requestId={RequestId} replyTo={ReplyTo}",
                reply.RequestId,
                address);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ReplyEnvelope<TResponse>> WaitAsync<TResponse>(RequestContext context, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var waiter = _waiters.GetOrAdd(
            context.RequestId,
            static _ => new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously));

        if (_completedReplies.TryRemove(context.RequestId, out var completedReply))
        {
            _waiters.TryRemove(context.RequestId, out _);
            return CastReply<TResponse>(context.RequestId, completedReply);
        }

        try
        {
            var result = await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return CastReply<TResponse>(context.RequestId, result);
        }
        catch
        {
            _waiters.TryRemove(context.RequestId, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public Task AbandonAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _waiters.TryRemove(context.RequestId, out _);
        _completedReplies.TryRemove(context.RequestId, out _);
        return Task.CompletedTask;
    }

    private static ReplyEnvelope<TResponse> CastReply<TResponse>(string requestId, object reply)
    {
        if (reply is ReplyEnvelope<TResponse> typedReply)
        {
            return typedReply;
        }

        throw new InvalidOperationException(
            $"Reply for request '{requestId}' has type '{reply.GetType().FullName}', expected '{typeof(ReplyEnvelope<TResponse>).FullName}'.");
    }
}
