# DotNetCore.Cap.RequestReply

[中文](README.zh-CN.md)

Request/Reply extension for [DotNetCore.CAP](https://github.com/dotnetcore/CAP) .

---

## Overview

### Motivation

[CAP](https://github.com/dotnetcore/CAP) is an **event bus** focused on **reliable messaging** (outbox, retries, eventual consistency). It excels at “publish and consume asynchronously,” but does **not** provide a built-in model for “publish one message and synchronously wait for a business result.”

In microservices this is common: the order service asks inventory “can you deduct stock?”, or an API gateway asks a downstream service “is this user valid?” The caller needs a **clear success/failure outcome** and **timeout control**, without building a separate RPC stack or hand-rolling Redis polling.

### Features

Adds Request/Reply **without changing CAP source code** or **breaking existing Publish/Subscribe habits**:

| What you want | What this library provides |
| --- | --- |
| `await` a remote business result | `ICapPublisher.RequestAsync<TRequest, TResponse>()` |
| Automatic `RequestId`, `ReplyTo`, timeout conventions | Unified envelopes and CAP headers |
| Handlers write business code and `return` like a local method | `[CapRequestReply]` + automatic `ReplyEnvelope` write-back |
| Replies reach the **correct caller instance** cross-service | Pluggable `IReplyTransport` (Redis / PostgreSQL / MySQL, etc.) |
| Optionally record per-call state | `IRequestStore` (in-memory or database) |

### Architecture

- **Request**: Still via CAP + message queue (RabbitMQ, Kafka, etc.) — same as normal event publishing.
- **Reply**: Via a separate `IReplyTransport` — delivers results to the **instance that initiated the request**, using `ReplyTo`.
- **Ledger**: Via `IRequestStore` — tracks Pending/Completed/Timeout for that call only; does **not** carry response bodies to another machine.

Cross-service deployments **must not** use the InMemory reply transport.

---

## Getting Started

### Installation

Configure MQ (and optional CAP storage) per CAP docs first, then register this library:

```csharp
using DotNetCore.Cap.RequestReply.Extensions;

// CAP (example: RabbitMQ)
builder.Services.AddCap(options =>
{
    options.UseRabbitMQ(r => r.HostName = "localhost");
});

// Request/Reply
builder.Services.AddCapRequestReply(options =>
{
    options.ServiceName = "order-service";           // Used when building ReplyTo
    options.InstanceId = Environment.MachineName;  // Distinguish instances
    options.DefaultTimeout = TimeSpan.FromSeconds(30);

    options.UseRedisRequestReply(redis =>           // See Configuration below
    {
        redis.ConnectionString = "localhost:6379";
        redis.StreamPrefix = "cap:reply";
    });
});
```

For single-process development, use `options.UseInMemoryRequestReply();`.

### Usage

**Caller**

```csharp
var result = await capPublisher.RequestAsync<StockRequest, StockResult>(
    "stock.check",
    new StockRequest(productId, quantity),
    TimeSpan.FromSeconds(10),
    cancellationToken);
```

Requires `AddCapRequestReply` and a usable `ICapPublisher.ServiceProvider`.

**Handler**

```csharp
using DotNetCore.Cap.RequestReply.Core;
using DotNetCore.Cap.RequestReply.Models;

public sealed class StockSubscriber : ICapSubscribe
{
    [CapSubscribe("stock.check")]
    [CapRequestReply]   // Required: marks this method as Request/Reply
    public Task<StockResult> HandleAsync(RequestEnvelope<StockRequest> request)
    {
        var ok = request.Data.Quantity > 0;
        return Task.FromResult(new StockResult(ok));
    }
}
```

### Conventions

- Handlers **must** be annotated with **`[CapRequestReply]`**.
- Envelope version header must be **`1`** (set automatically by the library).
- **InMemory Store** removes entries after a terminal state to avoid unbounded memory growth.

---

## Examples

### Single process

Configuration and calls live in one process; CAP can use an in-memory MQ:

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
// Caller
var echo = await publisher.RequestAsync<EchoRequest, EchoResponse>(
    "demo.echo",
    new EchoRequest("hello", 2));

// Subscriber
[CapSubscribe("demo.echo")]
[CapRequestReply]
public Task<EchoResponse> OnEchoAsync(RequestEnvelope<EchoRequest> req)
    => Task.FromResult(new EchoResponse(req.Data.Message + req.Data.Message));
```

### Cross-service

Service A sends the request, service B handles it; the response returns to A via **Redis** (or PostgreSQL / MySQL):

```text
Service A  RequestAsync → RabbitMQ → Service B  [CapRequestReply] Handler
                ↑                                    ↓
                └──────── Redis Stream (ReplyTo) ────┘
```

```csharp
// Both service A and B:
options.UseRedisRequestReply(redis =>
{
    redis.ConnectionString = "localhost:6379";
    redis.StreamPrefix = "cap:reply";
});

// If service B uses a different Redis cluster, map logical endpoint names:
redis.AddEndpoint("service-a", "redis-a:6379");
```

### Business failures

Return a failure envelope instead of throwing from the handler:

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
            errorMessage: "Invalid parameters"));
    }

    return Task.FromResult(new OrderResult { /* ... */ });
}
```

The caller receives `RequestFailedException` on failure and `RequestTimeoutException` on timeout.

---

## Configuration

### Reply transport (`IReplyTransport`)

Creates `ReplyTo`, delivers replies, and blocks until a reply arrives. Independent of CAP’s MQ; routes responses to the correct caller instance.

| Transport | Configuration | Good for | Not for |
| --- | --- | --- | --- |
| **InMemory** | `UseInMemoryReply()` or `UseInMemoryRequestReply()` | Single-process dev, unit tests, local demos | Cross-service, cross-Pod |
| **Redis Streams** | `UseRedisReply(...)` / `UseRedisRequestReply(...)` | **Default for cross-service production**; low latency; existing Redis | Environments with no Redis |
| **PostgreSQL** | `UsePostgreSqlReply(...)` / `UsePostgreSqlRequestReply(...)` | Existing PG; fewer moving parts than Redis; inbox table + notifications OK | No PostgreSQL |
| **MySQL** | `UseMySqlReply(...)` / `UseMySqlRequestReply(...)` | MySQL-only stacks; polling delay acceptable | No MySQL; ultra-low latency |

**`ReplyTo` examples**

| Transport | Example |
| --- | --- |
| InMemory | `memory://{requestId}` |
| Redis | `redis://default/cap:reply:order-service:HOSTNAME` |
| PostgreSQL | `postgres://cap_reply_order-service_hostname` |
| MySQL | `mysql://cap_reply_order-service_hostname` |

**Preset helpers (reply + default store)**

| Method | Meaning |
| --- | --- |
| `UseInMemoryRequestReply()` | InMemory reply + InMemory store |
| `UseRedisRequestReply(...)` | Redis reply + InMemory store |
| `UsePostgreSqlRequestReply(...)` | PostgreSQL reply + PostgreSQL store |
| `UseMySqlRequestReply(...)` | MySQL reply + MySQL store |

Reply and store can be configured separately, e.g. `UseRedisReply` + `UsePostgreSqlStore`.

### Request store (`IRequestStore`)

Records the state of each `RequestAsync` call (Pending / Completed / Failed / Timeout, etc.). Does **not** carry response bodies — those use the reply transport.

| Store | Configuration | Good for | Not for |
| --- | --- | --- | --- |
| **InMemory** | `UseInMemoryStore()` | Dev/test; no persistent ledger on the caller; pairs well with Redis reply | Historical queries; state after process restart |
| **PostgreSQL** | `UsePostgreSqlStore(...)` | Audit, ops queries, shared ledger view across instances | Avoiding PostgreSQL tables |
| **MySQL** | `UseMySqlStore(...)` | Same as above on MySQL | Avoiding MySQL tables |

**Notes**

- Cross-service: handlers do **not** need the caller’s store; each service maintains its own ledger.
- **InMemory store** auto-deletes entries after a terminal state.
- Store tables and reply inbox tables are separate; the library creates them when `AutoCreateTable` is enabled and does not use CAP’s `published` / `received` tables.

### Choosing a setup

| Scenario | Suggested configuration |
| --- | --- |
| Local / single process only | `UseInMemoryRequestReply()` |
| Multiple services (production) | `UseRedisRequestReply` |
| PostgreSQL available, prefer no Redis | `UsePostgreSqlRequestReply` |
| MySQL only | `UseMySqlRequestReply` |
| Long-term persistence of every call | Add `UsePostgreSqlStore` / `UseMySqlStore`, or Redis reply + PostgreSQL store |

---

## Dependencies

| Package | Version |
| --- | --- |
| `DotNetCore.CAP` | 10.0.1 |
| `StackExchange.Redis` | 2.9.32 |
| `Npgsql` | 9.0.4 |
| `MySqlConnector` | 2.5.0 |

Optional: `options.UseOpenTelemetryDiagnostics()` for distributed tracing.
