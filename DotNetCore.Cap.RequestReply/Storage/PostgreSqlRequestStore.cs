using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DotNetCore.Cap.RequestReply.Storage;

/// <summary>
/// PostgreSQL PendingRequestStore。
/// </summary>
public sealed class PostgreSqlRequestStore : IRequestStore
{
    private readonly PostgreSqlStoreOptions _options;
    private readonly SemaphoreSlim _initializerLock = new(1, 1);
    private bool _initialized;

    /// <summary>
    /// 创建 PostgreSQL PendingRequestStore。
    /// </summary>
    /// <param name="options">全局配置。</param>
    public PostgreSqlRequestStore(IOptions<RequestReplyOptions> options)
    {
        _options = options.Value.PostgreSqlStore;
    }

    /// <inheritdoc />
    public async Task CreateAsync(PendingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var sql = $"""
                   INSERT INTO {GetTableName()}(
                       "RequestId", "CorrelationId", "RequestTopic", "ReplyTo", "ReplyTransport", "StatusName",
                       "RequestType", "ResponseType", "RequestBody", "ResponseBody", "ErrorCode", "ErrorMessage",
                       "ExpiresAt", "CreatedAt", "CompletedAt")
                   VALUES(
                       @RequestId, @CorrelationId, @RequestTopic, @ReplyTo, @ReplyTransport, @StatusName,
                       @RequestType, @ResponseType, @RequestBody, @ResponseBody, @ErrorCode, @ErrorMessage,
                       @ExpiresAt, @CreatedAt, @CompletedAt)
                   RETURNING "Id";
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        AddPendingRequestParameters(command, request, PendingRequestStatus.Pending);

        try
        {
            var id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            request.Id = Convert.ToInt64(id);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
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
                   SET "StatusName" = CASE "StatusName"
                           WHEN @TimeoutStatus THEN @CompletedAfterTimeoutStatus
                           ELSE @CompletedStatus
                       END,
                       "ResponseBody" = @ResponseBody,
                       "CompletedAt" = @CompletedAt
                   WHERE "RequestId" = @RequestId
                     AND "StatusName" IN (@PendingStatus, @TimeoutStatus);
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        AddLifecycleParameters(command, requestId);
        Add(command, "ResponseBody", responseBody);
        Add(command, "CompletedAt", ToTimestamp(DateTimeOffset.UtcNow));

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
                   SET "StatusName" = CASE "StatusName"
                           WHEN @TimeoutStatus THEN @CompletedAfterTimeoutStatus
                           ELSE @FailedStatus
                       END,
                       "ErrorCode" = @ErrorCode,
                       "ErrorMessage" = @ErrorMessage,
                       "CompletedAt" = @CompletedAt
                   WHERE "RequestId" = @RequestId
                     AND "StatusName" IN (@PendingStatus, @TimeoutStatus);
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        AddLifecycleParameters(command, requestId);
        Add(command, "ErrorCode", errorCode);
        Add(command, "ErrorMessage", errorMessage);
        Add(command, "CompletedAt", ToTimestamp(DateTimeOffset.UtcNow));

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
                   SET "StatusName" = @TimeoutStatus
                   WHERE "RequestId" = @RequestId
                     AND "StatusName" = @PendingStatus;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
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
                   SET "StatusName" = @CanceledStatus
                   WHERE "RequestId" = @RequestId
                     AND "StatusName" = @PendingStatus;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
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
                   SELECT "Id", "RequestId", "CorrelationId", "RequestTopic", "ReplyTo", "ReplyTransport", "StatusName",
                          "RequestType", "ResponseType", "RequestBody", "ResponseBody", "ErrorCode", "ErrorMessage",
                          "ExpiresAt", "CreatedAt", "CompletedAt"
                   FROM {GetTableName()}
                   WHERE "RequestId" = @RequestId
                   LIMIT 1;
                   """;

        await using var connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
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
                    "Id" BIGSERIAL PRIMARY KEY NOT NULL,
                    "RequestId" VARCHAR(128) NOT NULL,
                    "CorrelationId" VARCHAR(128) NOT NULL,
                    "RequestTopic" VARCHAR(200) NOT NULL,
                    "ReplyTo" VARCHAR(512) NOT NULL,
                    "ReplyTransport" VARCHAR(64) NOT NULL,
                    "StatusName" VARCHAR(50) NOT NULL,
                    "RequestType" VARCHAR(500) NULL,
                    "ResponseType" VARCHAR(500) NULL,
                    "RequestBody" TEXT NULL,
                    "ResponseBody" TEXT NULL,
                    "ErrorCode" VARCHAR(100) NULL,
                    "ErrorMessage" TEXT NULL,
                    "ExpiresAt" TIMESTAMP NOT NULL,
                    "CreatedAt" TIMESTAMP NOT NULL,
                    "CompletedAt" TIMESTAMP NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "idx_request_reply_RequestId" ON {tableName}("RequestId");
                CREATE INDEX IF NOT EXISTS "idx_request_reply_ExpiresAt_StatusName" ON {tableName}("ExpiresAt", "StatusName");
                CREATE INDEX IF NOT EXISTS "idx_request_reply_CorrelationId" ON {tableName}("CorrelationId");
                """;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("PostgreSQL request store connection string is not configured.");
        }

        var connection = new NpgsqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private string GetTableName()
    {
        return $"{QuoteIdentifier(_options.Schema)}.{QuoteIdentifier(_options.TableName)}";
    }

    private static string QuoteIdentifier(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void AddPendingRequestParameters(NpgsqlCommand command, PendingRequest request, PendingRequestStatus status)
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
        Add(command, "ExpiresAt", ToTimestamp(request.ExpiresAt));
        Add(command, "CreatedAt", ToTimestamp(request.CreatedAt));
        Add(command, "CompletedAt", ToTimestamp(request.CompletedAt));
    }

    private static void AddLifecycleParameters(NpgsqlCommand command, string requestId)
    {
        Add(command, "RequestId", requestId);
        Add(command, "PendingStatus", PendingRequestStatus.Pending.ToString());
        Add(command, "TimeoutStatus", PendingRequestStatus.Timeout.ToString());
        Add(command, "CompletedStatus", PendingRequestStatus.Completed.ToString());
        Add(command, "FailedStatus", PendingRequestStatus.Failed.ToString());
        Add(command, "CompletedAfterTimeoutStatus", PendingRequestStatus.CompletedAfterTimeout.ToString());
    }

    private static void Add(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static PendingRequest ReadPendingRequest(NpgsqlDataReader reader)
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

    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset ToDateTimeOffset(DateTime value)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }

    private static DateTime ToTimestamp(DateTimeOffset value)
    {
        return DateTime.SpecifyKind(value.UtcDateTime, DateTimeKind.Unspecified);
    }

    private static DateTime? ToTimestamp(DateTimeOffset? value)
    {
        return value is null ? null : ToTimestamp(value.Value);
    }
}
