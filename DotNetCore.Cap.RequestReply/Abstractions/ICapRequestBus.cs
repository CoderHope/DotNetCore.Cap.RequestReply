namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 业务侧发起 CAP Request/Reply 调用的统一入口。
/// </summary>
public interface ICapRequestBus
{
    /// <summary>
    /// 发布请求消息并等待指定类型的响应。
    /// </summary>
    /// <typeparam name="TRequest">请求数据类型。</typeparam>
    /// <typeparam name="TResponse">响应数据类型。</typeparam>
    /// <param name="topic">CAP 请求主题。</param>
    /// <param name="request">请求数据。</param>
    /// <param name="timeout">本次请求超时时间；为空时使用默认配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应数据。</returns>
    Task<TResponse> RequestAsync<TRequest, TResponse>(
        string topic,
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);
}
