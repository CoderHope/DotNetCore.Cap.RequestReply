using System.Collections.Concurrent;
using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Models;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 默认 Redis 连接提供器，使用配置中的单个逻辑端点建立连接。
/// </summary>
public sealed class DefaultRedisConnectionProvider : IRedisConnectionProvider, IDisposable
{
    private readonly RedisReplyOptions _options;
    private readonly ConcurrentDictionary<string, IConnectionMultiplexer> _connections = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 创建默认 Redis 连接提供器。
    /// </summary>
    /// <param name="options">全局配置。</param>
    public DefaultRedisConnectionProvider(IOptions<RequestReplyOptions> options)
    {
        _options = options.Value.Redis;
    }

    /// <inheritdoc />
    public async Task<IConnectionMultiplexer> GetConnectionAsync(string endpointName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        var connectionString = ResolveConnectionString(endpointName);

        if (_connections.TryGetValue(endpointName, out var existingConnection) &&
            existingConnection.IsConnected)
        {
            return existingConnection;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connections.TryGetValue(endpointName, out existingConnection) &&
                existingConnection.IsConnected)
            {
                return existingConnection;
            }

            existingConnection?.Dispose();

            var connection = await ConnectionMultiplexer.ConnectAsync(connectionString).ConfigureAwait(false);
            _connections[endpointName] = connection;
            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string ResolveConnectionString(string endpointName)
    {
        if (string.Equals(endpointName, _options.EndpointName, StringComparison.Ordinal))
        {
            return _options.ConnectionString;
        }

        if (_options.Endpoints.TryGetValue(endpointName, out var connectionString))
        {
            return connectionString;
        }

        var configuredEndpoints = _options.Endpoints.Count == 0
            ? _options.EndpointName
            : $"{_options.EndpointName}, {string.Join(", ", _options.Endpoints.Keys)}";
        throw new InvalidOperationException(
            $"Redis endpoint '{endpointName}' is not configured. Configured endpoints: {configuredEndpoints}. " +
            $"Configure redis.EndpointName with the same logical route name or call redis.AddEndpoint(\"{endpointName}\", connectionString).");
    }

    /// <summary>
    /// 释放 Redis 连接和同步资源。
    /// </summary>
    public void Dispose()
    {
        foreach (var connection in _connections.Values)
        {
            connection.Dispose();
        }

        _gate.Dispose();
    }
}
