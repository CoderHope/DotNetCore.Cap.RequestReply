using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Exceptions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DotNetCore.Cap.RequestReply.Transports;

/// <summary>
/// 基于 Redis Streams 的响应通道。
/// </summary>
public sealed class RedisStreamReplyTransport : IReplyTransport
{
    private const string RequestIdField = "requestId";
    private const string CorrelationIdField = "correlationId";
    private const string PayloadField = "payload";

    private readonly IRedisConnectionProvider _connectionProvider;
    private readonly IRequestStore _requestStore;
    private readonly IRequestSerializer _serializer;
    private readonly RequestReplyOptions _options;
    private readonly ILogger<RedisStreamReplyTransport> _logger;

    /// <summary>
    /// 创建 Redis Streams 响应通道。
    /// </summary>
    /// <param name="connectionProvider">Redis 连接提供器。</param>
    /// <param name="requestStore">请求状态存储。</param>
    /// <param name="serializer">序列化器。</param>
    /// <param name="options">全局配置。</param>
    /// <param name="logger">日志记录器。</param>
    public RedisStreamReplyTransport(IRedisConnectionProvider connectionProvider, IRequestStore requestStore, IRequestSerializer serializer,
        IOptions<RequestReplyOptions> options, ILogger<RedisStreamReplyTransport> logger)
    {
        _connectionProvider = connectionProvider;
        _requestStore = requestStore;
        _serializer = serializer;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "redis";

    /// <inheritdoc />
    public Task<ReplyAddress> CreateReplyAddressAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // ReplyTo 使用逻辑 endpoint 名称，不把 Redis 连接串或凭据写入 CAP Header。
        return Task.FromResult(new ReplyAddress("redis", $"{_options.Redis.EndpointName}/{BuildStreamName()}"));
    }

    /// <inheritdoc />
    public async Task SendAsync<TResponse>(ReplyAddress address, ReplyEnvelope<TResponse> reply, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(address.Scheme, "redis", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReplyTransportException($"Redis reply transport cannot send to '{address}'.");
        }

        var payload = _serializer.Serialize(reply);
        var target = ParseRedisAddress(address);
        var database = await GetDatabaseAsync(target.EndpointName, cancellationToken).ConfigureAwait(false);
        var entries = new[]
        {
            new NameValueEntry(RequestIdField, reply.RequestId),
            new NameValueEntry(CorrelationIdField, reply.CorrelationId),
            new NameValueEntry(PayloadField, payload)
        };

        try
        {
            await database.StreamAddAsync(
                    target.StreamName,
                    entries,
                    maxLength: _options.Redis.MaxStreamLength,
                    useApproximateMaxLength: true)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);

            await RefreshStreamKeyExpireAsync(database, target.StreamName, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReplyTransportException(
                $"Failed to write Redis reply stream '{target.StreamName}' for request '{reply.RequestId}'.",
                exception);
        }

        await MarkStoreAsync(reply, payload, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Redis reply sent. requestId={RequestId} correlationId={CorrelationId} replyTo={ReplyTo} transport={Transport} status={Status}",
            reply.RequestId,
            reply.CorrelationId,
            address,
            Name,
            reply.Success ? PendingRequestStatus.Completed : PendingRequestStatus.Failed);
    }

    /// <inheritdoc />
    public async Task<ReplyEnvelope<TResponse>> WaitAsync<TResponse>(RequestContext context, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var address = ReplyAddress.Parse(context.ReplyTo);
        if (!string.Equals(address.Scheme, "redis", StringComparison.OrdinalIgnoreCase))
        {
            throw new ReplyTransportException($"Redis reply transport cannot wait on '{context.ReplyTo}'.");
        }

        var target = ParseRedisAddress(address);
        var database = await GetDatabaseAsync(target.EndpointName, cancellationToken).ConfigureAwait(false);
        await RefreshStreamKeyExpireAsync(database, target.StreamName, cancellationToken).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        RedisValue lastId = "0-0";

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pendingReply = await TryReadReplyFromStoreAsync<TResponse>(context, cancellationToken)
                .ConfigureAwait(false);
            if (pendingReply is not null)
            {
                return pendingReply;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"Redis reply timed out for request '{context.RequestId}'.");
            }

            var block = remaining < _options.Redis.ReadBlockTimeout
                ? remaining
                : _options.Redis.ReadBlockTimeout;
            var entries = await ReadStreamAsync(database, target.StreamName, lastId, block, cancellationToken)
                .ConfigureAwait(false);

            await RefreshStreamKeyExpireAsync(database, target.StreamName, cancellationToken).ConfigureAwait(false);

            foreach (var entry in entries)
            {
                lastId = entry.Id;
                if (!entry.Values.TryGetValue(RequestIdField, out var requestId) ||
                    !string.Equals(requestId, context.RequestId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!entry.Values.TryGetValue(PayloadField, out var payload) ||
                    string.IsNullOrWhiteSpace(payload))
                {
                    throw new ReplyTransportException(
                        $"Redis reply stream '{target.StreamName}' contains an empty payload for request '{context.RequestId}'.");
                }

                var reply = _serializer.Deserialize<ReplyEnvelope<TResponse>>(payload)
                            ?? throw new ReplyTransportException(
                                $"Redis reply stream '{target.StreamName}' payload cannot be deserialized for request '{context.RequestId}'.");

                if (_options.Redis.DeleteEntryAfterConsume)
                {
                    await database.StreamDeleteAsync(target.StreamName, [entry.Id])
                        .WaitAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "Redis reply received. requestId={RequestId} correlationId={CorrelationId} replyTo={ReplyTo} transport={Transport}",
                    context.RequestId,
                    context.CorrelationId,
                    context.ReplyTo,
                    Name);

                return reply;
            }
        }
    }

    private async Task RefreshStreamKeyExpireAsync(IDatabase database, string streamName, CancellationToken cancellationToken)
    {
        var expire = _options.Redis.StreamKeyExpire;
        if (expire <= TimeSpan.Zero)
        {
            return;
        }

        await database.KeyExpireAsync(streamName, expire).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private string BuildStreamName()
    {
        return $"{TrimSeparator(_options.Redis.StreamPrefix)}:{TrimSeparator(_options.ServiceName)}:{TrimSeparator(_options.InstanceId)}";
    }

    private static string TrimSeparator(string value)
    {
        return value.Trim().Trim(':');
    }

    private async Task<IDatabase> GetDatabaseAsync(string endpointName, CancellationToken cancellationToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(endpointName, cancellationToken).ConfigureAwait(false);
        return connection.GetDatabase();
    }

    private RedisReplyTarget ParseRedisAddress(ReplyAddress address)
    {
        var separatorIndex = address.Value.IndexOf('/', StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return new RedisReplyTarget(_options.Redis.EndpointName, address.Value);
        }

        var endpointName = address.Value[..separatorIndex];
        var streamName = address.Value[(separatorIndex + 1)..];
        if (string.IsNullOrWhiteSpace(endpointName) || string.IsNullOrWhiteSpace(streamName))
        {
            throw new ReplyTransportException($"Redis reply address '{address}' is invalid.");
        }

        return new RedisReplyTarget(endpointName, streamName);
    }

    private async Task MarkStoreAsync<TResponse>(ReplyEnvelope<TResponse> reply, string payload, CancellationToken cancellationToken)
    {
        if (reply.Success)
        {
            await _requestStore.MarkCompletedAsync(reply.RequestId, payload, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        await _requestStore.MarkFailedAsync(
                reply.RequestId,
                reply.ErrorCode ?? "REQUEST_FAILED",
                reply.ErrorMessage ?? "Request handler returned a failure reply.",
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ReplyEnvelope<TResponse>?> TryReadReplyFromStoreAsync<TResponse>(RequestContext context, CancellationToken cancellationToken)
    {
        var pending = await _requestStore.GetAsync(context.RequestId, cancellationToken).ConfigureAwait(false);
        if (pending is null)
        {
            return null;
        }

        if (pending.Status == PendingRequestStatus.Completed ||
            pending.Status == PendingRequestStatus.CompletedAfterTimeout)
        {
            if (string.IsNullOrWhiteSpace(pending.ResponseBody))
            {
                return null;
            }

            return _serializer.Deserialize<ReplyEnvelope<TResponse>>(pending.ResponseBody)
                   ?? throw new ReplyTransportException(
                       $"Pending request '{context.RequestId}' response body cannot be deserialized.");
        }

        if (pending.Status == PendingRequestStatus.Failed)
        {
            return new ReplyEnvelope<TResponse>(
                context.RequestId,
                context.CorrelationId,
                false,
                default,
                pending.ErrorCode,
                pending.ErrorMessage);
        }

        return null;
    }

    private async Task<IReadOnlyList<RedisStreamEntry>> ReadStreamAsync(IDatabase database, string streamName, RedisValue lastId, TimeSpan block,
        CancellationToken cancellationToken)
    {
        var blockMilliseconds = Math.Max(1, (long)block.TotalMilliseconds);
        RedisResult result;

        try
        {
            result = await database.ExecuteAsync(
                    "XREAD",
                    "BLOCK",
                    blockMilliseconds,
                    "COUNT",
                    Math.Max(1, _options.Redis.ReadCount),
                    "STREAMS",
                    streamName,
                    lastId)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReplyTransportException(
                $"Failed to read Redis reply stream '{streamName}'.",
                exception);
        }

        if (result.IsNull)
        {
            return Array.Empty<RedisStreamEntry>();
        }

        return ParseReadResult(result);
    }

    private static IReadOnlyList<RedisStreamEntry> ParseReadResult(RedisResult result)
    {
        var streamResults = (RedisResult[]?)result;
        if (streamResults is null)
        {
            return Array.Empty<RedisStreamEntry>();
        }

        var entries = new List<RedisStreamEntry>();

        foreach (var streamResult in streamResults)
        {
            var streamParts = (RedisResult[]?)streamResult;
            if (streamParts is null || streamParts.Length < 2)
            {
                continue;
            }

            var messageResults = (RedisResult[]?)streamParts[1];
            if (messageResults is null)
            {
                continue;
            }

            foreach (var messageResult in messageResults)
            {
                var messageParts = (RedisResult[]?)messageResult;
                if (messageParts is null || messageParts.Length < 2)
                {
                    continue;
                }

                var id = (RedisValue)messageParts[0];
                var fieldResults = (RedisResult[]?)messageParts[1];
                if (fieldResults is null)
                {
                    continue;
                }

                var values = new Dictionary<string, string>(StringComparer.Ordinal);

                for (var index = 0; index + 1 < fieldResults.Length; index += 2)
                {
                    var name = (RedisValue)fieldResults[index];
                    var value = (RedisValue)fieldResults[index + 1];
                    values[name.ToString()] = value.ToString();
                }

                entries.Add(new RedisStreamEntry(id, values));
            }
        }

        return entries;
    }

    private sealed record RedisStreamEntry(RedisValue Id, IReadOnlyDictionary<string, string> Values);

    private sealed record RedisReplyTarget(string EndpointName, string StreamName);
}