namespace DotNetCore.Cap.RequestReply.Abstractions;

/// <summary>
/// 请求和响应信封序列化抽象。
/// </summary>
public interface IRequestSerializer
{
    /// <summary>
    /// 将对象序列化为文本。
    /// </summary>
    /// <typeparam name="T">对象类型。</typeparam>
    /// <param name="value">对象实例。</param>
    /// <returns>序列化后的文本。</returns>
    string Serialize<T>(T value);

    /// <summary>
    /// 将文本反序列化为指定类型。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="value">序列化文本。</param>
    /// <returns>反序列化后的对象。</returns>
    T? Deserialize<T>(string value);

    /// <summary>
    /// 将文本反序列化为运行时指定的类型。
    /// </summary>
    /// <param name="value">序列化文本。</param>
    /// <param name="type">目标类型。</param>
    /// <returns>反序列化后的对象。</returns>
    object? Deserialize(string value, Type type);
}
