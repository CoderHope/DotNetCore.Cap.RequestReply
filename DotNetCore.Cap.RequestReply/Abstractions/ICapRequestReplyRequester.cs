using DotNetCore.CAP;

namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 请求者接口，用于请求服务。
/// </summary>
internal interface ICapRequestReplyRequester
{
    /// <summary>
    /// 请求服务。
    /// </summary>
    /// <param name="capPublisher">CAP 发布器。</param>
    /// <param name="topic">请求主题。</param>
    /// <param name="request">请求数据。</param>
    /// <param name="timeout">请求超时时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <typeparam name="TRequest">请求数据类型。</typeparam>
    /// <typeparam name="TResponse">响应数据类型。</typeparam>
    /// <returns>响应数据。</returns>
    Task<TResponse> RequestAsync<TRequest, TResponse>(ICapPublisher capPublisher, string topic, TRequest request,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default);
}