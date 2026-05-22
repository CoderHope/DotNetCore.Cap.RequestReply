using DotNetCore.Cap.RequestReply.Models;

namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// PendingRequest 生命周期存储抽象。
/// </summary>
public interface IRequestStore
{
    /// <summary>
    /// 创建新的待处理请求。
    /// </summary>
    /// <param name="request">待处理请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task CreateAsync(PendingRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将请求标记为完成，并保存响应正文。
    /// </summary>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="responseBody">响应正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkCompletedAsync(string requestId, string responseBody, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将请求标记为失败。
    /// </summary>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="errorCode">错误码。</param>
    /// <param name="errorMessage">错误消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkFailedAsync(string requestId, string errorCode, string errorMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将请求标记为超时。
    /// </summary>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task MarkTimeoutAsync(string requestId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 根据 RequestId 查询请求状态。
    /// </summary>
    /// <param name="requestId">请求 ID。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>请求状态；不存在时返回空。</returns>
    Task<PendingRequest?> GetAsync(string requestId, CancellationToken cancellationToken = default);
}