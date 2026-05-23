using StackExchange.Redis;

namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 根据 Redis 逻辑端点名称解析连接。
/// </summary>
public interface IRedisConnectionProvider
{
    /// <summary>
    /// 获取指定逻辑端点的 Redis 连接。
    /// </summary>
    /// <param name="endpointName">逻辑端点名称。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Redis 连接。</returns>
    Task<IConnectionMultiplexer> GetConnectionAsync(string endpointName, CancellationToken cancellationToken = default);
}
