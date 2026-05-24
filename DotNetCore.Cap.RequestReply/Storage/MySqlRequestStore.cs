using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace DotNetCore.Cap.RequestReply.Storage;

/// <summary>
/// MySQL PendingRequestStore。
/// </summary>
public sealed class MySqlRequestStore : IRequestStore
{
    private const int DuplicateEntryErrorNumber = 1062;
    private readonly MySqlStoreOptions _options;
    private readonly SemaphoreSlim _initializerLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// 创建 MySQL PendingRequestStore。
    /// </summary>
    /// <param name="options">全局配置。</param>
    public MySqlRequestStore(IOptions<RequestReplyOptions> options)
    {
        _options = options.Value.MySqlStore;
    }

    /// <inheritdoc />
    public async Task CreateAsync(PendingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
                   INSERT INTO {GetTableName()}(
                       `RequestId`, `CorrelationId`, `RequestTopic`, `ReplyTo`, `ReplyTransport`, `StatusName`,
                       `RequestType`, `ResponseType`, `RequestBody`, `ResponseBody`, `ErrorCode`, `ErrorMessage`,
                       `ExpiresAt`, `CreatedAt`, `CompletedAt`)
                   VALUES(
                       @RequestId, @CorrelationId, @RequestTopic, @ReplyTo, @ReplyTransport, @StatusName,
                       @RequestType, @ResponseType, @RequestBody, @ResponseBody, @ErrorCode, @ErrorMessage,
                       @ExpiresAt, @CreatedAt, @CompletedAt);
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        AddPendingRequestParameters(command, request, PendingRequestStatus.Pending);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            request.Id = command.LastInsertedId;
        }
        catch (MySqlException exception) when (exception.Number == DuplicateEntryErrorNumber)
        {
            throw new InvalidOperationException($"Pending request '{request.RequestId}' already exists.", exception);
        }
    }

    /// <inheritdoc />
    public async Task MarkCompletedAsync(string requestId, string responseBody, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
                   UPDATE {GetTableName()}
                   SET `StatusName` = CASE `StatusName`
                           WHEN @TimeoutStatus THEN @CompletedAfterTimeoutStatus
                           ELSE @CompletedStatus
                       END,
                       `ResponseBody` = @ResponseBody,
                       `CompletedAt` = @CompletedAt
                   WHERE `RequestId` = @RequestId
                     AND `StatusName` IN (@PendingStatus, @TimeoutStatus);
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        AddLifecycleParameters(command, requestId);
        Add(command, "ResponseBody", responseBody);
        Add(command, "CompletedAt", DateTimeOffset.UtcNow.UtcDateTime);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkFailedAsync(string requestId, string errorCode, string errorMessage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
                   UPDATE {GetTableName()}
                   SET `StatusName` = CASE `StatusName`
                           WHEN @TimeoutStatus THEN @CompletedAfterTimeoutStatus
                           ELSE @FailedStatus
                       END,
                       `ErrorCode` = @ErrorCode,
                       `ErrorMessage` = @ErrorMessage,
                       `CompletedAt` = @CompletedAt
                   WHERE `RequestId` = @RequestId
                     AND `StatusName` IN (@PendingStatus, @TimeoutStatus);
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        AddLifecycleParameters(command, requestId);
        Add(command, "ErrorCode", errorCode);
        Add(command, "ErrorMessage", errorMessage);
        Add(command, "CompletedAt", DateTimeOffset.UtcNow.UtcDateTime);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkTimeoutAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
                   UPDATE {GetTableName()}
                   SET `StatusName` = @TimeoutStatus
                   WHERE `RequestId` = @RequestId
                     AND `StatusName` = @PendingStatus;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        Add(command, "RequestId", requestId);
        Add(command, "PendingStatus", PendingRequestStatus.Pending.ToString());
        Add(command, "TimeoutStatus", PendingRequestStatus.Timeout.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkCanceledAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
                   UPDATE {GetTableName()}
                   SET `StatusName` = @CanceledStatus
                   WHERE `RequestId` = @RequestId
                     AND `StatusName` = @PendingStatus;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        Add(command, "RequestId", requestId);
        Add(command, "PendingStatus", PendingRequestStatus.Pending.ToString());
        Add(command, "CanceledStatus", PendingRequestStatus.Canceled.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<PendingRequest?> GetAsync(string requestId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
                   SELECT `Id`, `RequestId`, `CorrelationId`, `RequestTopic`, `ReplyTo`, `ReplyTransport`, `StatusName`,
                          `RequestType`, `ResponseType`, `RequestBody`, `ResponseBody`, `ErrorCode`, `ErrorMessage`,
                          `ExpiresAt`, `CreatedAt`, `CompletedAt`
                   FROM {GetTableName()}
                   WHERE `RequestId` = @RequestId
                   LIMIT 1;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new MySqlCommand(sql, connection);
        Add(command, "RequestId", requestId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadPendingRequest(reader)
            : null;
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
                    `Id` bigint NOT NULL AUTO_INCREMENT,
                    `RequestId` varchar(128) NOT NULL,
                    `CorrelationId` varchar(128) NOT NULL,
                    `RequestTopic` varchar(200) NOT NULL,
                    `ReplyTo` varchar(512) NOT NULL,
                    `ReplyTransport` varchar(64) NOT NULL,
                    `StatusName` varchar(50) NOT NULL,
                    `RequestType` varchar(500) DEFAULT NULL,
                    `ResponseType` varchar(500) DEFAULT NULL,
                    `RequestBody` longtext,
                    `ResponseBody` longtext,
                    `ErrorCode` varchar(100) DEFAULT NULL,
                    `ErrorMessage` longtext,
                    `ExpiresAt` datetime NOT NULL,
                    `CreatedAt` datetime NOT NULL,
                    `CompletedAt` datetime DEFAULT NULL,
                    PRIMARY KEY (`Id`),
                    UNIQUE KEY `UX_RequestId` (`RequestId`),
                    INDEX `IX_ExpiresAt_StatusName` (`ExpiresAt`, `StatusName`),
                    INDEX `IX_CorrelationId` (`CorrelationId`)
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
                """;
    }

    private async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("MySQL request store connection string is not configured.");
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

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
    }

    private static void AddPendingRequestParameters(MySqlCommand command, PendingRequest request, PendingRequestStatus status)
    {
        Add(command, "RequestId", request.RequestId);
        Add(command, "CorrelationId", request.CorrelationId);
        Add(command, "RequestTopic", request.RequestTopic);
        Add(command, "ReplyTo", request.ReplyTo);
        Add(command, "ReplyTransport", request.ReplyTransport);
        Add(command, "StatusName", status.ToString());
        Add(command, "RequestType", request.RequestType);
        Add(command, "ResponseType", request.ResponseType);
        Add(command, "RequestBody", request.RequestBody);
        Add(command, "ResponseBody", request.ResponseBody);
        Add(command, "ErrorCode", request.ErrorCode);
        Add(command, "ErrorMessage", request.ErrorMessage);
        Add(command, "ExpiresAt", request.ExpiresAt.UtcDateTime);
        Add(command, "CreatedAt", request.CreatedAt.UtcDateTime);
        Add(command, "CompletedAt", request.CompletedAt?.UtcDateTime);
    }

    private static void AddLifecycleParameters(MySqlCommand command, string requestId)
    {
        Add(command, "RequestId", requestId);
        Add(command, "PendingStatus", PendingRequestStatus.Pending.ToString());
        Add(command, "TimeoutStatus", PendingRequestStatus.Timeout.ToString());
        Add(command, "CompletedStatus", PendingRequestStatus.Completed.ToString());
        Add(command, "FailedStatus", PendingRequestStatus.Failed.ToString());
        Add(command, "CompletedAfterTimeoutStatus", PendingRequestStatus.CompletedAfterTimeout.ToString());
    }

    private static void Add(MySqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static PendingRequest ReadPendingRequest(MySqlDataReader reader)
    {
        var statusName = reader.GetString(6);
        if (!Enum.TryParse<PendingRequestStatus>(statusName, out var status))
        {
            throw new InvalidOperationException($"Unknown pending request status '{statusName}'.");
        }

        return new PendingRequest
        {
            Id = reader.GetInt64(0),
            RequestId = reader.GetString(1),
            CorrelationId = reader.GetString(2),
            RequestTopic = reader.GetString(3),
            ReplyTo = reader.GetString(4),
            ReplyTransport = reader.GetString(5),
            Status = status,
            RequestType = GetNullableString(reader, 7),
            ResponseType = GetNullableString(reader, 8),
            RequestBody = GetNullableString(reader, 9),
            ResponseBody = GetNullableString(reader, 10),
            ErrorCode = GetNullableString(reader, 11),
            ErrorMessage = GetNullableString(reader, 12),
            ExpiresAt = ToDateTimeOffset(reader.GetDateTime(13)),
            CreatedAt = ToDateTimeOffset(reader.GetDateTime(14)),
            CompletedAt = reader.IsDBNull(15) ? null : ToDateTimeOffset(reader.GetDateTime(15))
        };
    }

    private static string? GetNullableString(MySqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
