namespace DotNetCore.Cap.RequestReply.Models;

/// <summary>
/// ReplyTransport 可解析的响应地址。
/// </summary>
/// <param name="Scheme">地址协议，例如 redis、postgres、memory。</param>
/// <param name="Value">协议内地址值。</param>
public sealed record ReplyAddress(string Scheme, string Value)
{
    /// <summary>
    /// 将地址格式化为 <c>scheme://value</c>。
    /// </summary>
    /// <returns>可写入 Header 的地址字符串。</returns>
    public override string ToString()
    {
        return $"{Scheme}://{Value}";
    }

    /// <summary>
    /// 从 Header 字符串解析响应地址。
    /// </summary>
    /// <param name="value">地址字符串。</param>
    /// <returns>响应地址。</returns>
    public static ReplyAddress Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var separatorIndex = value.IndexOf("://", StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            throw new FormatException($"Reply address '{value}' does not contain a scheme.");
        }

        return new ReplyAddress(value[..separatorIndex], value[(separatorIndex + 3)..]);
    }
}
