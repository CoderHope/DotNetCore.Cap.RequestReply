# DotNetCore.Cap.RequestReply

[English](README.md)

基于 [DotNetCore.CAP](https://github.com/dotnetcore/CAP)的 Request/Reply 扩展库。

---

## 背景与目的

### 背景

[CAP](https://github.com/dotnetcore/CAP) 的定位是**事件总线**与**可靠消息投递**（Outbox、重试、最终一致性）。它擅长「发消息、异步消费」，但**不直接提供**「发一条消息并同步等待业务结果」的编程模型。

在微服务里，这类需求很常见：订单服务问库存服务「能否扣减」、网关问下游「用户是否合法」，调用方需要**明确的成功/失败结果**和**超时控制**，而不是再自建一套 RPC 或手写 Redis 轮询。

### 本库要解决什么

在**不修改 CAP 源码**、**不改变现有 Publish/Subscribe 习惯**的前提下，增加 Request/Reply 能力：

| 你希望做的事 | 本库提供的能力 |
| --- | --- |
| `await` 等待一次远程调用的业务结果 | `ICapPublisher.RequestAsync<TRequest, TResponse>()` |
| 自动带上 `RequestId`、`ReplyTo`、超时等约定 | 统一信封与 CAP Header |
| 订阅方只写业务代码、像写本地方法一样 `return` 结果 | `[CapRequestReply]` + 自动回写 `ReplyEnvelope` |
| 跨服务时响应能回到**正确的调用方实例** | 可插拔的 `IReplyTransport`（Redis / PostgreSQL / MySQL 等） |
| 可选地记录每次调用的状态 | `IRequestStore`（内存或数据库） |

### 架构上怎么分工

- **请求**：仍走 CAP + 消息队列（RabbitMQ、Kafka 等）—— 与现有事件投递一致。
- **响应**：走独立的 `IReplyTransport` —— 按 `ReplyTo` 把结果送回**发起请求的那台实例**。
- **台账**：走 `IRequestStore` —— 只记本次调用的 Pending/Completed/Timeout 等，**不负责**把响应正文传到另一台机器。

跨服务时：**不能**用 `InMemoryTransport`。

---

## 快速开始

### 1. 安装与注册

先按 CAP 官方方式配置 MQ 与（可选）CAP 存储，再注册本库：

```csharp
using DotNetCore.Cap.RequestReply.Extensions;

// CAP（示例：RabbitMQ）
builder.Services.AddCap(options =>
{
    options.UseRabbitMQ(r => r.HostName = "localhost");
});

// Request/Reply
builder.Services.AddCapRequestReply(options =>
{
    options.ServiceName = "order-service";           // 参与生成 ReplyTo
    options.InstanceId = Environment.MachineName;  // 多实例区分
    options.DefaultTimeout = TimeSpan.FromSeconds(30);

    options.UseRedisRequestReply(redis =>           // 跨服务见下文选型
    {
        redis.ConnectionString = "localhost:6379";
        redis.StreamPrefix = "cap:reply";
    });
});
```

单进程联调可改为：`options.UseInMemoryRequestReply();`

### 2. 调用方

```csharp
var result = await capPublisher.RequestAsync<StockRequest, StockResult>(
    "stock.check",
    new StockRequest(productId, quantity),
    TimeSpan.FromSeconds(10),
    cancellationToken);
```

要求：已调用 `AddCapRequestReply`，且 `ICapPublisher.ServiceProvider` 可用。

### 3. 处理方

```csharp
using DotNetCore.Cap.RequestReply.Core;
using DotNetCore.Cap.RequestReply.Models;

public sealed class StockSubscriber : ICapSubscribe
{
    [CapSubscribe("stock.check")]
    [CapRequestReply]   // 必须：标记本方法参与 Request/Reply
    public Task<StockResult> HandleAsync(RequestEnvelope<StockRequest> request)
    {
        var ok = request.Data.Quantity > 0;
        return Task.FromResult(new StockResult(ok));
    }
}
```

### 使用约定（简）

- 处理方必须加 **`[CapRequestReply]`**。
- 信封版本 Header 必须为 **`1`**（由库自动写入）。
- **InMemory Store** 在终态后会删除条目，避免内存无限增长。

---

## 简单示例

### 单进程（开发 / 单元测试）

配置与调用都在同一进程，CAP 可用 InMemory MQ：

```csharp
// Program.cs
builder.Services.AddCap(options => options.UseInMemoryMessageQueue());
builder.Services.AddCapRequestReply(options =>
{
    options.ServiceName = "demo";
    options.InstanceId = "local";
    options.UseInMemoryRequestReply();
});
```

```csharp
// 调用
var echo = await publisher.RequestAsync<EchoRequest, EchoResponse>(
    "demo.echo",
    new EchoRequest("hello", 2));

// 订阅
[CapSubscribe("demo.echo")]
[CapRequestReply]
public Task<EchoResponse> OnEchoAsync(RequestEnvelope<EchoRequest> req)
    => Task.FromResult(new EchoResponse(req.Data.Message + req.Data.Message));
```


### 跨服务（生产常见）

服务 A 发请求，服务 B 处理，响应经 **Redis**（或 PG/MySQL）回到 A：

```text
服务 A  RequestAsync → RabbitMQ → 服务 B  [CapRequestReply] Handler
                ↑                                    ↓
                └──────── Redis Stream（ReplyTo）────┘
```

```csharp
// 服务 A、B 均需：
options.UseRedisRequestReply(redis =>
{
    redis.ConnectionString = "localhost:6379";
    redis.StreamPrefix = "cap:reply";
});

// 服务 B 若连接不同 Redis 集群，可映射逻辑端点名：
redis.AddEndpoint("service-a", "redis-a:6379");
```


### 业务失败（不抛异常，返回失败信封）

```csharp
[CapRequestReply]
public Task<OrderResult> HandleAsync(RequestEnvelope<OrderRequest> request)
{
    if (!IsValid(request.Data))
    {
        return Task.FromResult(new ReplyEnvelope<OrderResult>(
            request.RequestId,
            request.CorrelationId,
            success: false,
            data: default,
            errorCode: "INVALID_ORDER",
            errorMessage: "参数不合法"));
    }

    return Task.FromResult(new OrderResult { /* ... */ });
}
```

调用方收到失败时会抛 `RequestFailedException`；超时抛 `RequestTimeoutException`。

---

## 支持的 Reply Transport（IReplyTransport）

负责：**生成 `ReplyTo`、发送响应、阻塞等待响应**。与 CAP 的 MQ **无关**，专门解决「回给哪台机器」。

| Transport | 配置 | 适用场景 | 不适用 |
| --- | --- | --- | --- |
| **InMemory** | `UseInMemoryReply()` 或 `UseInMemoryRequestReply()` | 单进程联调、单元测试、本地 Demo | 跨服务、跨 Pod |
| **Redis Streams** | `UseRedisReply(...)` / `UseRedisRequestReply(...)` | **跨服务生产首选**；要低延迟、已有 Redis | 完全没有 Redis 的环境 |
| **PostgreSQL** | `UsePostgreSqlReply(...)` / `UsePostgreSqlRequestReply(...)` | 已有 PG、希望少维护 Redis；能接受 Inbox 表 + 通知 | 没有 PostgreSQL |
| **MySQL** | `UseMySqlReply(...)` / `UseMySqlRequestReply(...)` | 仅有 MySQL；可接受轮询延迟 | 没有 MySQL、要极低延迟 |

**ReplyTo 形式示例**

| Transport | 示例 |
| --- | --- |
| InMemory | `memory://{requestId}` |
| Redis | `redis://default/cap:reply:order-service:HOSTNAME` |
| PostgreSQL | `postgres://cap_reply_order-service_hostname` |
| MySQL | `mysql://cap_reply_order-service_hostname` |

**组合配置（Reply + 默认 Store）**

| 方法 | 含义 |
| --- | --- |
| `UseInMemoryRequestReply()` | InMemory Reply + InMemory Store |
| `UseRedisRequestReply(...)` | Redis Reply + InMemory Store |
| `UsePostgreSqlRequestReply(...)` | PostgreSQL Reply + PostgreSQL Store |
| `UseMySqlRequestReply(...)` | MySQL Reply + MySQL Store |

Reply 与 Store 也可拆开配置，例如 `UseRedisReply` + `UsePostgreSqlStore`。

---

## 支持的 Request Store（IRequestStore）

负责：**记录单次 `RequestAsync` 的状态**（Pending / Completed / Failed / Timeout 等）。**不传递**响应正文（正文走 Reply Transport）。

| Store | 配置 | 适用场景 | 不适用 |
| --- | --- | --- | --- |
| **InMemory** | `UseInMemoryStore()` | 开发测试、调用方无需持久化台账；与 Redis Reply 搭配很常见 | 要查历史请求、进程重启后保留状态 |
| **PostgreSQL** | `UsePostgreSqlStore(...)` | 需要审计、运维查询、多实例共享台账视图 | 不想引入 PG 表 |
| **MySQL** | `UseMySqlStore(...)` | 同上，技术栈为 MySQL | 不想引入 MySQL 表 |

说明：

- 跨服务时，**处理方不需要**能访问调用方的 Store；各服务各记各的即可。
- **InMemory Store** 在终态后会**自动删除**条目，避免内存只增不减。
- Store 表与 Reply Inbox 表**相互独立**，由库按 `AutoCreateTable` 自行建表，不占用 CAP 的 `published` / `received` 表。

---

## 选型速查

| 你的情况 | 建议 |
| --- | --- |
| 只在本地跑通流程 | `UseInMemoryRequestReply()` |
| 多服务、要上生产 | `UseRedisRequestReply` |
| 有 PG、不想上 Redis | `UsePostgreSqlRequestReply` |
| 只有 MySQL | `UseMySqlRequestReply` |
| 要长期保存每次调用记录 | 在上述基础上用 `UsePostgreSqlStore` / `UseMySqlStore`，或 Redis Reply + PG Store |

---

## 依赖

| 包 | 版本 |
| --- | --- |
| `DotNetCore.CAP` | 10.0.1 |
| `StackExchange.Redis` | 2.9.32 |
| `Npgsql` | 9.0.4 |
| `MySqlConnector` | 2.5.0 |

可选：`options.UseOpenTelemetryDiagnostics()` 接入追踪。
