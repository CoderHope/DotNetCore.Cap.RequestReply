using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Exceptions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DotNetCore.Cap.RequestReply.Transports;

/// <summary>
/// 基于 MySQL 表 + 退避轮询的响应通道。
/// </summary>
public sealed class MySqlReplyTransport : IReplyTransport
{
    private readonly MySqlReplyOptions _options;
    private readonly RequestReplyOptions _requestReplyOptions;
    private readonly IRequestSerializer _serializer;
    private readonly ILogger<MySqlReplyTransport> _logger;
    private readonly SemaphoreSlim _initializerLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// 创建 MySQL 响应通道。
    /// </summary>
    public MySqlReplyTransport(
        IRequestSerializer serializer,
        IOptions<RequestReplyOptions> options,
        ILogger<MySqlReplyTransport> logger)
    {
        _serializer = serializer;
        _requestReplyOptions = options.Value;
        _options = options.Value.MySqlReply;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "mysql";

    /// <inheritdoc />
    public Task<ReplyAddress> CreateReplyAddressAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ReplyAddress(Name, BuildInboxKey()));
    }

    /// <inheritdoc />
    public async Task SendAsync<TResponse>(ReplyAddress address, ReplyEnvelope<TResponse> reply, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(address.Scheme, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReplyTransportException($"MySQL reply transport cannot send to '{address}'.");
        }

        var inboxKey = ParseInboxKey(address);
        var payload = _serializer.Serialize(reply);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await UpsertReplyAsync(inboxKey, reply.RequestId, reply.CorrelationId, payload, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ReplyTransportException(
                $"Failed to write MySQL reply for request '{reply.RequestId}' on inbox '{inboxKey}'.",
                exception);
        }

        _logger.LogInformation(
            "MySQL reply sent. requestId={RequestId} correlationId={CorrelationId} inbox={InboxKey} transport={Transport}",
            reply.RequestId,
            reply.CorrelationId,
            inboxKey,
            Name);
    }

    /// <inheritdoc />
    public async Task<ReplyEnvelope<TResponse>> WaitAsync<TResponse>(RequestContext context, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var address = ReplyAddress.Parse(context.ReplyTo);
        if (!string.Equals(address.Scheme, Name, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReplyTransportException($"MySQL reply transport cannot wait on '{context.ReplyTo}'.");
        }

        var inboxKey = ParseInboxKey(address);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var pollingInterval = _options.InitialPollingInterval;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tableReply = await TryLoadReplyFromTableAsync<TResponse>(context, inboxKey, cancellationToken)
                .ConfigureAwait(false);
            if (tableReply is not null)
            {
                if (_options.DeleteAfterConsume)
                {
                    await DeleteReplyAsync(context.RequestId, cancellationToken).ConfigureAwait(false);
                }

                _logger.LogInformation(
                    "MySQL reply received. requestId={RequestId} correlationId={CorrelationId} inbox={InboxKey} transport={Transport}",
                    context.RequestId,
                    context.CorrelationId,
                    inboxKey,
                    Name);

                return tableReply;
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"MySQL reply timed out for request '{context.RequestId}'.");
            }

            var delay = remaining < pollingInterval ? remaining : pollingInterval;
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            pollingInterval = NextPollingInterval(pollingInterval);
        }
    }

    /// <inheritdoc />
    public Task AbandonAsync(RequestContext context, CancellationToken cancellationToken = default)
    {
        return DeleteReplyAsync(context.RequestId, cancellationToken);
    }

    private TimeSpan NextPollingInterval(TimeSpan current)
    {
        var doubled = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        return doubled > _options.MaxPollingInterval ? _options.MaxPollingInterval : doubled;
    }

    private async Task UpsertReplyAsync(
        string inboxKey,
        string requestId,
        string correlationId,
        string payload,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   INSERT INTO {GetTableName()}(
                       `RequestId`, `CorrelationId`, `InboxKey`, `Payload`, `CreatedAt`)
                   VALUES(@RequestId, @CorrelationId, @InboxKey, @Payload, @CreatedAt)
                   ON DUPLICATE KEY UPDATE
                       `CorrelationId` = VALUES(`CorrelationId`),
                       `InboxKey` = VALUES(`InboxKey`),
                       `Payload` = VALUES(`Payload`),
                       `CreatedAt` = VALUES(`CreatedAt`);
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        Add(command, "RequestId", requestId);
        Add(command, "CorrelationId", correlationId);
        Add(command, "InboxKey", inboxKey);
        Add(command, "Payload", payload);
        Add(command, "CreatedAt", DateTime.UtcNow);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ReplyEnvelope<TResponse>?> TryLoadReplyFromTableAsync<TResponse>(
        RequestContext context,
        string inboxKey,
        CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT `Payload`
                   FROM {GetTableName()}
                   WHERE `RequestId` = @RequestId
                     AND `InboxKey` = @InboxKey
                   LIMIT 1;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        Add(command, "RequestId", context.RequestId);
        Add(command, "InboxKey", inboxKey);

        var payload = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var reply = _serializer.Deserialize<ReplyEnvelope<TResponse>>(payload)
                    ?? throw new ReplyTransportException(
                        $"MySQL reply payload cannot be deserialized for request '{context.RequestId}'.");

        if (!string.Equals(reply.RequestId, context.RequestId, StringComparison.Ordinal))
        {
            throw new ReplyTransportException(
                $"MySQL reply request id '{reply.RequestId}' does not match pending request '{context.RequestId}'.");
        }

        if (!string.Equals(reply.CorrelationId, context.CorrelationId, StringComparison.Ordinal))
        {
            throw new ReplyTransportException(
                $"MySQL reply correlation id '{reply.CorrelationId}' does not match pending request '{context.CorrelationId}'.");
        }

        return reply;
    }

    private async Task DeleteReplyAsync(string requestId, CancellationToken cancellationToken)
    {
        var sql = $"""DELETE FROM {GetTableName()} WHERE `RequestId` = @RequestId;""";

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
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
            await using var command = new MySqlCommand(CreateDbTablesScript(), connection);
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
        return $"""
                CREATE TABLE IF NOT EXISTS {GetTableName()}(
                    `RequestId` varchar(128) NOT NULL,
                    `CorrelationId` varchar(128) NOT NULL,
                    `InboxKey` varchar(128) NOT NULL,
                    `Payload` longtext NOT NULL,
                    `CreatedAt` datetime NOT NULL,
                    PRIMARY KEY (`RequestId`),
                    INDEX `IX_InboxKey` (`InboxKey`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """;
    }

    private async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("MySQL reply transport connection string is not configured.");
        }

        var connection = new MySqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private string GetTableName()
    {
        var tableName = string.IsNullOrWhiteSpace(_options.TableNamePrefix)
            ? _options.TableName
            : $"{_options.TableNamePrefix}.{_options.TableName}";

        return QuoteIdentifier(tableName);
    }

    private string BuildInboxKey()
    {
        var raw = $"{TrimToken(_options.InboxKeyPrefix)}_{TrimToken(_requestReplyOptions.ServiceName)}_{TrimToken(_requestReplyOptions.InstanceId)}";
        return SanitizeInboxKey(raw);
    }

    private static string ParseInboxKey(ReplyAddress address)
    {
        if (string.IsNullOrWhiteSpace(address.Value))
        {
            throw new ReplyTransportException($"MySQL reply address '{address}' is invalid.");
        }

        return address.Value;
    }

    private static string TrimToken(string value)
    {
        return value.Trim().Trim('_');
    }

    private static string SanitizeInboxKey(string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 128)];
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

            if (length == 0 && normalized is >= '0' and <= '9')
            {
                buffer[length++] = '_';
            }

            buffer[length++] = normalized;
            if (length >= 128)
            {
                break;
            }
        }

        return length == 0 ? "cap_reply_default" : new string(buffer[..length]);
    }

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
    }

    private static void Add(MySqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}
