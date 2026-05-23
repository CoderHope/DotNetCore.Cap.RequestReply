using System.Collections.Concurrent;
using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;

namespace DotNetCore.Cap.RequestReply.Storage;

/// <summary>
/// 基于进程内内存的 PendingRequestStore，主要用于本地开发和单元测试。
/// 进入终态后会从内存中移除条目，避免字典无限增长。
/// </summary>
public sealed class InMemoryRequestStore : IRequestStore
{
    private readonly ConcurrentDictionary<string, PendingRequest> _requests = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();
    private long _id;

    /// <inheritdoc />
    public Task CreateAsync(PendingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();
        var copy = request.Clone();
        copy.Id = copy.Id == 0 ? Interlocked.Increment(ref _id) : copy.Id;
        copy.Status = PendingRequestStatus.Pending;

        if (!_requests.TryAdd(copy.RequestId, copy))
        {
            throw new InvalidOperationException($"Pending request '{copy.RequestId}' already exists.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkCompletedAsync(string requestId, string responseBody, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_requests.TryGetValue(requestId, out var request))
            {
                return Task.CompletedTask;
            }

            // Timeout 后到达的响应不能覆盖为普通 Completed，否则会丢失真实等待结果。
            if (request.Status == PendingRequestStatus.Timeout)
            {
                request.Status = PendingRequestStatus.CompletedAfterTimeout;
                request.ResponseBody = responseBody;
                request.CompletedAt = DateTimeOffset.UtcNow;
                RemoveIfTerminal(requestId, request.Status);
                return Task.CompletedTask;
            }

            if (request.Status != PendingRequestStatus.Pending)
            {
                return Task.CompletedTask;
            }

            request.Status = PendingRequestStatus.Completed;
            request.ResponseBody = responseBody;
            request.CompletedAt = DateTimeOffset.UtcNow;
            RemoveIfTerminal(requestId, request.Status);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(string requestId, string errorCode, string errorMessage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!_requests.TryGetValue(requestId, out var request))
            {
                return Task.CompletedTask;
            }

            if (request.Status == PendingRequestStatus.Timeout)
            {
                request.Status = PendingRequestStatus.CompletedAfterTimeout;
                request.ErrorCode = errorCode;
                request.ErrorMessage = errorMessage;
                request.CompletedAt = DateTimeOffset.UtcNow;
                RemoveIfTerminal(requestId, request.Status);
                return Task.CompletedTask;
            }

            if (request.Status != PendingRequestStatus.Pending)
            {
                return Task.CompletedTask;
            }

            request.Status = PendingRequestStatus.Failed;
            request.ErrorCode = errorCode;
            request.ErrorMessage = errorMessage;
            request.CompletedAt = DateTimeOffset.UtcNow;
            RemoveIfTerminal(requestId, request.Status);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkTimeoutAsync(string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_requests.TryGetValue(requestId, out var request) &&
                request.Status == PendingRequestStatus.Pending)
            {
                request.Status = PendingRequestStatus.Timeout;
                RemoveIfTerminal(requestId, request.Status);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task MarkCanceledAsync(string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (_requests.TryGetValue(requestId, out var request) &&
                request.Status == PendingRequestStatus.Pending)
            {
                request.Status = PendingRequestStatus.Canceled;
                RemoveIfTerminal(requestId, request.Status);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PendingRequest?> GetAsync(string requestId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_requests.TryGetValue(requestId, out var request)
                ? request.Clone()
                : null);
        }
    }

    private void RemoveIfTerminal(string requestId, PendingRequestStatus status)
    {
        if (status is PendingRequestStatus.Completed
            or PendingRequestStatus.Failed
            or PendingRequestStatus.Timeout
            or PendingRequestStatus.Canceled
            or PendingRequestStatus.CompletedAfterTimeout)
        {
            _requests.TryRemove(requestId, out _);
        }
    }
}
