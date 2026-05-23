namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// 消费方返回给请求方的响应信封。
/// </summary>
/// <typeparam name="T">响应数据类型。</typeparam>
/// <param name="RequestId">请求唯一 ID。</param>
/// <param name="CorrelationId">关联链路 ID。</param>
/// <param name="Success">业务处理是否成功。</param>
/// <param name="Data">成功响应数据。</param>
/// <param name="ErrorCode">失败错误码。</param>
/// <param name="ErrorMessage">失败错误消息。</param>
public sealed record ReplyEnvelope<T>(string RequestId, string CorrelationId, bool Success, T? Data, string? ErrorCode, string? ErrorMessage);
