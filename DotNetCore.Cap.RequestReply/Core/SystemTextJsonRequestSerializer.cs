using System.Text.Json;
using DotNetCore.Cap.RequestReply.Abstractions;

namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 基于 System.Text.Json 的默认序列化器。
/// </summary>
public sealed class SystemTextJsonRequestSerializer : IRequestSerializer
{
    private static readonly JsonSerializerOptions DefaultOptions = new(JsonSerializerDefaults.Web);

    private readonly JsonSerializerOptions _options;

    /// <summary>
    /// 使用 Web 默认 JSON 选项创建序列化器。
    /// </summary>
    public SystemTextJsonRequestSerializer()
        : this(DefaultOptions)
    {
    }

    /// <summary>
    /// 使用指定 JSON 选项创建序列化器。
    /// </summary>
    /// <param name="options">JSON 序列化选项。</param>
    public SystemTextJsonRequestSerializer(JsonSerializerOptions options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, _options);
    }

    /// <inheritdoc />
    public T? Deserialize<T>(string value)
    {
        return JsonSerializer.Deserialize<T>(value, _options);
    }

    /// <inheritdoc />
    public object? Deserialize(string value, Type type)
    {
        return JsonSerializer.Deserialize(value, type, _options);
    }
}
