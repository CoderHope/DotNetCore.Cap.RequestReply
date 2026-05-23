using DotNetCore.CAP.Filter;
using DotNetCore.Cap.RequestReply.Abstractions;
using DotNetCore.Cap.RequestReply.Core;
using DotNetCore.Cap.RequestReply.Models;
using DotNetCore.Cap.RequestReply.Storage;
using DotNetCore.Cap.RequestReply.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DotNetCore.Cap.RequestReply.Extensions;

/// <summary>
/// 注册 CAP Request/Reply 扩展服务的 DI 扩展方法。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 CAP Request/Reply 所需服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">配置委托。</param>
    /// <returns>服务集合。</returns>
    public static IServiceCollection AddCapRequestReply(this IServiceCollection services, Action<RequestReplyOptions>? configure = null)
    {
        services.AddOptions<RequestReplyOptions>();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IRequestSerializer, SystemTextJsonRequestSerializer>();
        services.TryAddSingleton<IRequestIdGenerator, DefaultRequestIdGenerator>();
        services.TryAddSingleton<ICorrelationIdProvider, DefaultCorrelationIdProvider>();
        services.TryAddSingleton<IRequestReplyDiagnostics, NoopRequestReplyDiagnostics>();
        services.TryAddSingleton<IRedisConnectionProvider, DefaultRedisConnectionProvider>();

        services.AddSingleton(CreateRequestStore);
        services.AddSingleton(CreateReplyTransport);
        services.AddSingleton<ICapRequestReplyRequester>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<CapRequestReplyRequester>>()
                ?? NullLogger<CapRequestReplyRequester>.Instance;

            return ActivatorUtilities.CreateInstance<CapRequestReplyRequester>(serviceProvider, logger);
        });
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISubscribeFilter, CapRequestReplySubscribeFilter>());

        return services;
    }

    private static IRequestStore CreateRequestStore(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<RequestReplyOptions>>().Value;
        return options.RequestStore switch
        {
            RequestReplyStoreKind.InMemory => ActivatorUtilities.CreateInstance<InMemoryRequestStore>(serviceProvider),
            RequestReplyStoreKind.PostgreSql => ActivatorUtilities.CreateInstance<PostgreSqlRequestStore>(serviceProvider),
            RequestReplyStoreKind.MySql => ActivatorUtilities.CreateInstance<MySqlRequestStore>(serviceProvider),
            _ => throw new InvalidOperationException($"Unsupported request store '{options.RequestStore}'.")
        };
    }

    private static IReplyTransport CreateReplyTransport(IServiceProvider serviceProvider)
    {
        var options = serviceProvider.GetRequiredService<IOptions<RequestReplyOptions>>().Value;
        return options.ReplyTransport switch
        {
            RequestReplyTransportKind.InMemory => ActivatorUtilities.CreateInstance<InMemoryReplyTransport>(
                serviceProvider,
                GetLogger<InMemoryReplyTransport>(serviceProvider)),
            RequestReplyTransportKind.Redis => ActivatorUtilities.CreateInstance<RedisStreamReplyTransport>(
                serviceProvider,
                GetLogger<RedisStreamReplyTransport>(serviceProvider)),
            RequestReplyTransportKind.PostgreSql => throw NotImplemented("PostgreSQL reply transport"),
            RequestReplyTransportKind.MySql => throw NotImplemented("MySQL reply transport"),
            RequestReplyTransportKind.Ipc => throw NotImplemented("IPC reply transport"),
            _ => throw new InvalidOperationException($"Unsupported reply transport '{options.ReplyTransport}'.")
        };
    }

    private static ILogger<T> GetLogger<T>(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
    }

    private static NotImplementedException NotImplemented(string feature)
    {
        return new NotImplementedException($"{feature} is planned but not implemented in this foundation build.");
    }
}
