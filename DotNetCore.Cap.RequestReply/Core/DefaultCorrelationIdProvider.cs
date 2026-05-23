using System.Diagnostics;
using DotNetCore.Cap.RequestReply.Abstractions;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 默认 CorrelationId 提供器，优先复用当前 Activity TraceId。
/// </summary>
public sealed class DefaultCorrelationIdProvider : ICorrelationIdProvider
{
    /// <inheritdoc />
    public string CreateCorrelationId()
    {
        var traceId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrWhiteSpace(traceId)
            ? $"corr_{Guid.NewGuid():N}"
            : $"corr_{traceId}";
    }
}
