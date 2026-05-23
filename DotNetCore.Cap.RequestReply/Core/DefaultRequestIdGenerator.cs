using DotNetCore.Cap.RequestReply.Abstractions;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 使用 GUID 创建 RequestId 的默认实现。
/// </summary>
public sealed class DefaultRequestIdGenerator : IRequestIdGenerator
{
    /// <inheritdoc />
    public string CreateRequestId()
    {
        return $"req_{Guid.NewGuid():N}";
    }
}
