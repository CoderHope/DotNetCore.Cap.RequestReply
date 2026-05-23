using DotNetCore.CAP;
using DotNetCore.Cap.RequestReply.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetCore.Cap.RequestReply.Extensions;

/// <summary>
/// CAP 发布器的 Request/Reply 扩展方法。
/// </summary>
public static class CapPublisherRequestReplyExtensions
{
    /// <summary>
    /// 发布请求消息并等待指定类型的响应。
    /// </summary>
    /// <typeparam name="TRequest">请求数据类型。</typeparam>
    /// <typeparam name="TResponse">响应数据类型。</typeparam>
    /// <param name="capPublisher">CAP 发布器。</param>
    /// <param name="topic">CAP 请求主题。</param>
    /// <param name="request">请求数据。</param>
    /// <param name="timeout">本次请求超时时间；为空时使用默认配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应数据。</returns>
    public static Task<TResponse> RequestAsync<TRequest, TResponse>(this ICapPublisher capPublisher, string topic, TRequest request,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capPublisher);

        var serviceProvider = capPublisher.ServiceProvider
            ?? throw new InvalidOperationException("ICapPublisher.ServiceProvider is required to use ICapPublisher.RequestAsync.");

        var requester = serviceProvider.GetService<ICapRequestReplyRequester>()
            ?? throw new InvalidOperationException("Call services.AddCapRequestReply(...) before using ICapPublisher.RequestAsync.");

        return requester.RequestAsync<TRequest, TResponse>(capPublisher, topic, request, timeout, cancellationToken);
    }
}
