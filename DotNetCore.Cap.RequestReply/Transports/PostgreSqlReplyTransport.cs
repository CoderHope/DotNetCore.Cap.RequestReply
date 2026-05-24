using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Exceptions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DotNetCore.Cap.RequestReply.Transports;

/// <summary>
/// 基于 PostgreSQL 表 + LISTEN/NOTIFY 的响应通道。
/// </summary>
public sealed class PostgreSqlReplyTransport : IReplyTransport
{
    private readonly PostgreSqlReplyOptions _options;
    private readonly RequestReplyOptions _requestReplyOptions;
    private readonly IRequestSerializer _serializer;
    private readonly ILogger<PostgreSqlReplyTransport> _logger;
    private readonly SemaphoreSlim _initializerLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// 创建 PostgreSQL 响应通道。
    /// </summary>
    public PostgreSqlReplyTransport(IRequestSerializer serializer,
        IOptions<RequestReplyOptions> options, ILogger<PostgreSqlReplyTransport> logger)
    {
        _serializer = serializer;
        _requestReplyOptions = options.Value;
        _options = options.Value.PostgreSqlReply;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "postgres";

    /// <inheritdoc />
    public Task<ReplyAddress> CreateReplyAddressAsync(RequestContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ReplyAddress(Name, BuildNotifyChannel()));
    }

    /// <inheritdoc />
    public async Task SendAsync<TResponse>(ReplyAddress address, ReplyEnvelope<TResponse> reply,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(address.Scheme, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReplyTransportException($"PostgreSQL reply transport cannot send to '{address}'.");
        }

        var channel = ParseChannel(address);
        var payload = _serializer.Serialize(reply);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await UpsertReplyAsync(channel, reply.RequestId, reply.CorrelationId, payload, cancellationToken)
                .ConfigureAwait(false);
            if (_options.UseNotify)
            {
                await NotifyAsync(channel, reply.RequestId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReplyTransportException(
                $"Failed to write PostgreSQL reply for request '{reply.RequestId}' on channel '{channel}'.",
                exception);
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "PostgreSQL reply sent. requestId={RequestId} correlationId={CorrelationId} channel={Channel} transport={Transport}",
                reply.RequestId,
                reply.CorrelationId,
                channel,
                Name);
        }
    }

    /// <inheritdoc />
    public async Task<ReplyEnvelope<TResponse>> WaitAsync<TResponse>(RequestContext context, TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var address = ReplyAddress.Parse(context.ReplyTo);
        if (!string.Equals(address.Scheme, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReplyTransportException($"PostgreSQL reply transport cannot wait on '{context.ReplyTo}'.");
        }

        var channel = ParseChannel(address);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        await using var listenConnection = _options.UseNotify
            ? await OpenConnectionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        if (listenConnection is not null)
        {
            await ListenAsync(listenConnection, channel, cancellationToken).ConfigureAwait(false);
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tableReply = await TryLoadReplyFromTableAsync<TResponse>(context, channel, cancellationToken)
                .ConfigureAwait(false);
            if (tableReply is not null)
            {
                if (_options.DeleteAfterConsume)
                {
                    await DeleteReplyAsync(context.RequestId, cancellationToken).ConfigureAwait(false);
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug(
                        "PostgreSQL reply received. requestId={RequestId} correlationId={CorrelationId} channel={Channel} transport={Transport}",
                        context.RequestId,
                        context.CorrelationId,
                        channel,
                        Name);
                }
               

                return tableReply;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"PostgreSQL reply timed out for request '{context.RequestId}'.");
            }

            if (listenConnection is null)
            {
                var delay = remaining < _options.PollingFallbackInterval
                    ? remaining
                    : _options.PollingFallbackInterval;
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var wait = remaining < _options.PollingFallbackInterval
                ? remaining
                : _options.PollingFallbackInterval;
            try
            {
                await listenConnection.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                // Npgsql WaitAsync timeout: fall through to poll again.
            }
        }
    }

    /// <inheritdoc />
    public Task AbandonAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        return DeleteReplyAsync(context.RequestId, cancellationToken);
    }

    private async Task UpsertReplyAsync(
        string channel,
        string requestId,
        string correlationId,
        string payload,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   INSERT INTO {GetTableName()}(
                       "RequestId", "CorrelationId", "NotifyChannel", "Payload", "CreatedAt")
                   VALUES(@RequestId, @CorrelationId, @NotifyChannel, @Payload, @CreatedAt)
                   ON CONFLICT ("RequestId") DO UPDATE SET
                       "CorrelationId" = EXCLUDED."CorrelationId",
                       "NotifyChannel" = EXCLUDED."NotifyChannel",
                       "Payload" = EXCLUDED."Payload",
                       "CreatedAt" = EXCLUDED."CreatedAt";
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, "RequestId", requestId);
        Add(command, "CorrelationId", correlationId);
        Add(command, "NotifyChannel", channel);
        Add(command, "Payload", payload);
        Add(command, "CreatedAt", DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyAsync(string channel, string requestId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        // NOTIFY 的 payload 只能是字面量，不能 @Payload；使用 pg_notify 支持参数化。
        await using var command = new NpgsqlCommand("SELECT pg_notify(@Channel, @Payload);", connection);
        Add(command, "Channel", channel);
        Add(command, "Payload", requestId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ListenAsync(NpgsqlConnection connection, string channel,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"LISTEN {QuoteIdentifier(channel)};", connection);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReplyEnvelope<TResponse>?> TryLoadReplyFromTableAsync<TResponse>(
        RequestContext context,
        string channel,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT "Payload"
                   FROM {GetTableName()}
                   WHERE "RequestId" = @RequestId
                     AND "NotifyChannel" = @NotifyChannel
                   LIMIT 1;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, "RequestId", context.RequestId);
        Add(command, "NotifyChannel", channel);

        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var reply = _serializer.Deserialize<ReplyEnvelope<TResponse>>(payload)
                    ?? throw new ReplyTransportException(
                        $"PostgreSQL reply payload cannot be deserialized for request '{context.RequestId}'.");

        if (!string.Equals(reply.RequestId, context.RequestId, StringComparison.Ordinal))
        {
            throw new ReplyTransportException(
                $"PostgreSQL reply request id '{reply.RequestId}' does not match pending request '{context.RequestId}'.");
        }

        if (!string.Equals(reply.CorrelationId, context.CorrelationId, StringComparison.Ordinal))
        {
            throw new ReplyTransportException(
                $"PostgreSQL reply correlation id '{reply.CorrelationId}' does not match pending request '{context.CorrelationId}'.");
        }

        return reply;
    }

    private async Task DeleteReplyAsync(string requestId, CancellationToken cancellationToken)
    {
        var sql = $"""DELETE FROM {GetTableName()} WHERE "RequestId" = @RequestId;""";

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        Add(command, "RequestId", requestId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_options.AutoCreateTable || Volatile.Read(ref _initialized))
        {
            return;
        }

        await _initializerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _initialized))
            {
                return;
            }

            await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(CreateDbTablesScript(), connection);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _initialized, true);
        }
        finally
        {
            _initializerLock.Release();
        }
    }

    private string CreateDbTablesScript()
    {
        var tableName = GetTableName();
        return $"""
                CREATE SCHEMA IF NOT EXISTS {QuoteIdentifier(_options.Schema)};

                CREATE TABLE IF NOT EXISTS {tableName}(
                    "RequestId" VARCHAR(128) PRIMARY KEY NOT NULL,
                    "CorrelationId" VARCHAR(128) NOT NULL,
                    "NotifyChannel" VARCHAR(128) NOT NULL,
                    "Payload" TEXT NOT NULL,
                    "CreatedAt" TIMESTAMP NOT NULL
                );

                CREATE INDEX IF NOT EXISTS "idx_request_reply_inbox_channel" ON {tableName}("NotifyChannel");
                """;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("PostgreSQL reply transport connection string is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private string GetTableName()
    {
        return $"{QuoteIdentifier(_options.Schema)}.{QuoteIdentifier(_options.TableName)}";
    }

    private string BuildNotifyChannel()
    {
        var raw =
            $"{TrimToken(_options.NotifyChannelPrefix)}_{TrimToken(_requestReplyOptions.ServiceName)}_{TrimToken(_requestReplyOptions.InstanceId)}";
        return SanitizeNotifyChannel(raw);
    }

    private static string ParseChannel(ReplyAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.Value))
        {
            throw new ReplyTransportException($"PostgreSQL reply address '{address}' is invalid.");
        }

        return address.Value;
    }

    private static string TrimToken(string value)
    {
        return value.Trim().Trim('_');
    }

    private static string SanitizeNotifyChannel(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 63)];
        var length = 0;

        foreach (var character in value)
        {
            var normalized = character switch
            {
                >= 'a' and <= 'z' => character,
                >= 'A' and <= 'Z' => char.ToLowerInvariant(character),
                >= '0' and <= '9' => character,
                '_' => character,
                '-' or '.' or ':' => '_',
                _ => '\0'
            };

            if (normalized == '\0')
            {
                continue;
            }

            if (length == 0 && normalized >= '0' && normalized <= '9')
            {
                buffer[length++] = '_';
            }

            buffer[length++] = normalized;
            if (length >= 63)
            {
                break;
            }
        }

        return length == 0 ? "cap_reply_default" : new string(buffer[..length]);
    }

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void Add(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
