namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// Redis Streams 响应通道配置。
/// </summary>
public sealed class RedisReplyOptions
{
    /// <summary>
    /// 当前服务作为请求方时写入 ReplyTo 的 Redis 逻辑端点名称；连接串仍保留在本地配置中。
    /// </summary>
    public string EndpointName { get; set; } = "default";

    /// <summary>
    /// Redis 连接串。
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// 额外 Redis 逻辑端点映射。用于消费端把请求消息中的远端 endpoint 名称解析到本地连接串。
    /// </summary>
    public IDictionary<string, string> Endpoints { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 添加 Redis 逻辑端点映射。
    /// </summary>
    /// <param name="endpointName">请求消息 ReplyTo 中携带的逻辑端点名称。</param>
    /// <param name="connectionString">本服务用于连接该端点的 Redis 连接串。</param>
    public void AddEndpoint(string endpointName, string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        Endpoints[endpointName] = connectionString;
    }

    /// <summary>
    /// Reply Stream 名称前缀。
    /// </summary>
    public string StreamPrefix { get; set; } = "cap:reply";

    /// <summary>
    /// Stream 最大近似长度，用于限制历史回复堆积。
    /// </summary>
    public int MaxStreamLength { get; set; } = 10000;

    /// <summary>
    /// 每次 XREAD 最多读取的消息数量。
    /// </summary>
    public int ReadCount { get; set; } = 100;

    /// <summary>
    /// 单次 XREAD BLOCK 等待时间。
    /// </summary>
    public TimeSpan ReadBlockTimeout { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// PostgreSQL 响应通道配置。
/// </summary>
public sealed class PostgreSqlReplyOptions
{
    /// <summary>
    /// PostgreSQL 连接串。
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 是否优先使用 LISTEN/NOTIFY。
    /// </summary>
    public bool UseNotify { get; set; } = true;

    /// <summary>
    /// NOTIFY 不可用或丢失时的轮询兜底间隔。
    /// </summary>
    public TimeSpan PollingFallbackInterval { get; set; } = TimeSpan.FromMilliseconds(300);
}

/// <summary>
/// MySQL 响应通道配置。
/// </summary>
public sealed class MySqlReplyOptions
{
    /// <summary>
    /// MySQL 连接串。
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 初始轮询间隔。
    /// </summary>
    public TimeSpan InitialPollingInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 最大轮询间隔。
    /// </summary>
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// IPC 响应通道配置。
/// </summary>
public sealed class IpcReplyOptions
{
    /// <summary>
    /// 本机监听地址。
    /// </summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>
    /// 本机监听端口。
    /// </summary>
    public int Port { get; set; } = 39001;
}

/// <summary>
/// PostgreSQL PendingRequestStore 配置。
/// </summary>
public sealed class PostgreSqlStoreOptions
{
    /// <summary>
    /// PostgreSQL 连接串。
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// PostgreSQL schema 名称，默认与 CAP PostgreSQL 存储一致使用 cap。
    /// </summary>
    public string Schema { get; set; } = "cap";

    /// <summary>
    /// PendingRequest 表名。
    /// </summary>
    public string TableName { get; set; } = "request_reply";

    /// <summary>
    /// 是否在首次使用时自动创建表。
    /// </summary>
    public bool AutoCreateTable { get; set; } = true;
}

/// <summary>
/// MySQL PendingRequestStore 配置。
/// </summary>
public sealed class MySqlStoreOptions
{
    /// <summary>
    /// MySQL 连接串。
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// MySQL 表名前缀，默认与 CAP MySQL 存储一致使用 cap。
    /// </summary>
    public string TableNamePrefix { get; set; } = "cap";

    /// <summary>
    /// PendingRequest 表名。
    /// </summary>
    public string TableName { get; set; } = "request_reply";

    /// <summary>
    /// 是否在首次使用时自动创建表。
    /// </summary>
    public bool AutoCreateTable { get; set; } = true;
}
