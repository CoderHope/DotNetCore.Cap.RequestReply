namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// PendingRequest 状态存储类型。
/// </summary>
public enum RequestReplyStoreKind
{
    /// <summary>
    /// 进程内内存存储。
    /// </summary>
    InMemory,

    /// <summary>
    /// PostgreSQL 存储。
    /// </summary>
    PostgreSql,

    /// <summary>
    /// MySQL 存储。
    /// </summary>
    MySql
}
