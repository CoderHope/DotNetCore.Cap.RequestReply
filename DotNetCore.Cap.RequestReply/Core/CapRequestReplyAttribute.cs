namespace DotNetCore.Cap.RequestReply.Core;

/// <summary>
/// 标记 CAP 订阅方法参与 Request/Reply，由 <see cref="CapRequestReplySubscribeFilter"/> 自动写回响应。
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CapRequestReplyAttribute : Attribute;
