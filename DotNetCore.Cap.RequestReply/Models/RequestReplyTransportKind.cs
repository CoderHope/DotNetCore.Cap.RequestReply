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
    MySql,

    /// <summary>
    /// 本机 IPC 响应通道。
    /// </summary>
    Ipc,

    /// <summary>
    /// CAP callbackName 响应通道。
    /// </summary>
    CapCallback
}
