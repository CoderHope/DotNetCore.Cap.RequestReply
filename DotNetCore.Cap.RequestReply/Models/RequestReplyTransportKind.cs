namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// ReplyTransport 类型。
/// </summary>
public enum RequestReplyTransportKind
{
    /// <summary>
    /// 进程内响应通道。
    /// </summary>
    InMemory,

    /// <summary>
    /// Redis Streams 响应通道。
    /// </summary>
    Redis,

    /// <summary>
    /// PostgreSQL 响应通道。
    /// </summary>
    PostgreSql,

    /// <summary>
    /// MySQL 响应通道。
    /// </summary>
    MySql
}
