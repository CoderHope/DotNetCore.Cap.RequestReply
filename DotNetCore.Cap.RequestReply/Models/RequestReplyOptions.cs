namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// CAP Request/Reply 扩展的全局配置。
/// </summary>
public sealed class RequestReplyOptions
{
    /// <summary>
    /// 当前服务名，用于生成 ReplyTo 地址。
    /// </summary>
    public string ServiceName { get; set; } = AppDomain.CurrentDomain.FriendlyName;

    /// <summary>
    /// 当前服务实例 ID，用于区分多实例回复通道。
    /// </summary>
    public string InstanceId { get; set; } = Environment.MachineName;

    /// <summary>
    /// 未显式传入 timeout 时使用的默认超时时间。
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 当前选择的响应通道类型。
    /// </summary>
    public RequestReplyTransportKind ReplyTransport { get; private set; } = RequestReplyTransportKind.InMemory;

    /// <summary>
    /// 当前选择的请求状态存储类型。
    /// </summary>
    public RequestReplyStoreKind RequestStore { get; private set; } = RequestReplyStoreKind.InMemory;

    /// <summary>
    /// 是否使用 OpenTelemetry 诊断实现。
    /// </summary>
    public bool EnableOpenTelemetryDiagnostics { get; private set; }

    /// <summary>
    /// Redis ReplyTransport 配置。
    /// </summary>
    public RedisReplyOptions Redis { get; } = new();

    /// <summary>
    /// PostgreSQL ReplyTransport 配置。
    /// </summary>
    public PostgreSqlReplyOptions PostgreSqlReply { get; } = new();

    /// <summary>
    /// MySQL ReplyTransport 配置。
    /// </summary>
    public MySqlReplyOptions MySqlReply { get; } = new();

    /// <summary>
    /// PostgreSQL PendingRequestStore 配置。
    /// </summary>
    public PostgreSqlStoreOptions PostgreSqlStore { get; } = new();

    /// <summary>
    /// MySQL PendingRequestStore 配置。
    /// </summary>
    public MySqlStoreOptions MySqlStore { get; } = new();

    /// <summary>
    /// 使用进程内 Request/Reply（InMemory Reply + InMemory Store）。
    /// </summary>
    public void UseInMemoryRequestReply()
    {
        UseInMemoryReply();
        UseInMemoryStore();
    }

    /// <summary>
    /// 使用 Redis Reply + InMemory Store。
    /// </summary>
    /// <param name="configureRedis">Redis 配置委托。</param>
    public void UseRedisRequestReply(Action<RedisReplyOptions>? configureRedis = null)
    {
        UseRedisReply(configureRedis);
        UseInMemoryStore();
    }

    /// <summary>
    /// 使用 PostgreSQL Reply + PostgreSQL Store（共享连接串与 Schema）。
    /// </summary>
    /// <param name="configure">PostgreSQL 配置委托。</param>
    public void UsePostgreSqlRequestReply(Action<PostgreSqlReplyOptions>? configure = null)
    {
        UsePostgreSqlReply(options =>
        {
            configure?.Invoke(options);
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                PostgreSqlStore.ConnectionString = options.ConnectionString;
            }

            if (!string.IsNullOrWhiteSpace(options.Schema))
            {
                PostgreSqlStore.Schema = options.Schema;
            }
        });
        UsePostgreSqlStore();
    }

    /// <summary>
    /// 使用 MySQL Reply + MySQL Store（共享连接串）。
    /// </summary>
    /// <param name="configure">MySQL 配置委托。</param>
    public void UseMySqlRequestReply(Action<MySqlReplyOptions>? configure = null)
    {
        UseMySqlReply(options =>
        {
            configure?.Invoke(options);
            if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                MySqlStore.ConnectionString = options.ConnectionString;
            }

            if (!string.IsNullOrWhiteSpace(options.TableNamePrefix))
            {
                MySqlStore.TableNamePrefix = options.TableNamePrefix;
            }
        });
        UseMySqlStore();
    }

    /// <summary>
    /// 启用 OpenTelemetry 诊断（<see cref="Core.OpenTelemetryRequestReplyDiagnostics"/>）。
    /// </summary>
    public void UseOpenTelemetryDiagnostics()
    {
        EnableOpenTelemetryDiagnostics = true;
    }

    /// <summary>
    /// 使用进程内响应通道。
    /// </summary>
    public void UseInMemoryReply()
    {
        ReplyTransport = RequestReplyTransportKind.InMemory;
    }

    /// <summary>
    /// 使用 Redis Streams 响应通道。
    /// </summary>
    /// <param name="configure">Redis 配置委托。</param>
    public void UseRedisReply(Action<RedisReplyOptions>? configure = null)
    {
        ReplyTransport = RequestReplyTransportKind.Redis;
        configure?.Invoke(Redis);
    }

    /// <summary>
    /// 使用 PostgreSQL 响应通道。
    /// </summary>
    /// <param name="configure">PostgreSQL 配置委托。</param>
    public void UsePostgreSqlReply(Action<PostgreSqlReplyOptions>? configure = null)
    {
        ReplyTransport = RequestReplyTransportKind.PostgreSql;
        configure?.Invoke(PostgreSqlReply);
    }

    /// <summary>
    /// 使用 MySQL 响应通道。
    /// </summary>
    /// <param name="configure">MySQL 配置委托。</param>
    public void UseMySqlReply(Action<MySqlReplyOptions>? configure = null)
    {
        ReplyTransport = RequestReplyTransportKind.MySql;
        configure?.Invoke(MySqlReply);
    }

    /// <summary>
    /// 使用 PostgreSQL 保存 PendingRequest 状态。
    /// </summary>
    /// <param name="configure">PostgreSQL Store 配置委托。</param>
    public void UsePostgreSqlStore(Action<PostgreSqlStoreOptions>? configure = null)
    {
        RequestStore = RequestReplyStoreKind.PostgreSql;
        configure?.Invoke(PostgreSqlStore);
    }

    /// <summary>
    /// 使用 MySQL 保存 PendingRequest 状态。
    /// </summary>
    /// <param name="configure">MySQL Store 配置委托。</param>
    public void UseMySqlStore(Action<MySqlStoreOptions>? configure = null)
    {
        RequestStore = RequestReplyStoreKind.MySql;
        configure?.Invoke(MySqlStore);
    }

    /// <summary>
    /// 使用内存保存 PendingRequest 状态。
    /// </summary>
    public void UseInMemoryStore()
    {
        RequestStore = RequestReplyStoreKind.InMemory;
    }

}
