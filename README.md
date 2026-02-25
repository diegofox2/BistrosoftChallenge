# BistrosoftChallenge

## Summary

Example project illustrating a layered architecture, distributed messaging with MassTransit, and sagas for orchestrating state changes (implicit CQRS). It contains a web API, a worker (saga processor), and a domain and infrastructure library.

## Repository Structure

- `BistrosoftChallenge.Api/` — HTTP API (Controllers, authentication, Swagger, middleware).
- `BistrosoftChallenge.Worker/` — Worker that runs `MassTransit` and the sagas (state machines).
- `BistrosoftChallenge.Domain/` — Domain entities (`Customer`, `Order`, `Product`, `OrderStatus`).
- `BistrosoftChallenge.Infrastructure/` — `AppDbContext`, repositories, and persistence logic.
- `BistrosoftChallenge.MessageContracts/` — Message contracts for inter-process messaging.
- `BistrosoftChallenge.Test/` — Unit/integration tests.

## Architecture (High Level)

The application follows a classic layered separation:

- API Layer: receives HTTP requests and publishes commands/events via MassTransit.
- Domain Layer: models and business rules.
- Infrastructure Layer: concrete implementations (EF Core `AppDbContext`, repositories).
- Worker/Messaging: listens for messages, executes sagas (orchestration of business processes), and updates persisted state.

State changes in the system are driven by sagas in the worker, which constitutes implicit CQRS: the API acts as an entry point for commands (and fast DB queries), while changes requiring distributed coordination are executed by the sagas.

## MassTransit vs MediatR

This project uses `MassTransit` (see `BistrosoftChallenge.Worker` and `BistrosoftChallenge.Api`), not `MediatR`, because a full, resilient message bus is needed:

- **Real distributed messaging**: `MassTransit` abstracts transports such as RabbitMQ, Azure Service Bus, or an in-memory bus, allowing the API to publish events/commands that will be processed by other processes without direct dependency. `MediatR` only routes messages within the same process.
- **Native Saga / State Machine support**: `MassTransit` provides `MassTransitStateMachine<T>` with persistence (EF Core, Mongo, etc.), timers, compensations, and automatic correlation. It is the foundation of the project's implicit CQRS orchestration.
- **Enterprise bus features**: includes middleware for configurable retries, circuit breakers, outbox/inbox, topological routing, prioritization, and observability (diagnostics, OpenTelemetry). These elements are key to ensuring idempotency, clear telemetry, and resilience.
- **Scalability and isolation**: consumers can run in multiple worker instances and the broker balances the load; messages can also be versioned and the topology evolved without interrupting the API.

`MediatR` is excellent as an in-process mediator for decoupling layers within a monolithic application, but it does not provide transport, durability, or distributed orchestration tools. For a scenario that requires persistent sagas, reliable messages, and API/Worker separation, `MassTransit` offers superior benefits and avoids having to manually build critical components (queue, retries, scaling, telemetry, etc.).

Practical implications and why you will see `await _dbContext.SaveChangesAsync()` after `Publish` in the controllers:

- Publish + Outbox = local persistence:
  - Calling `_publishEndpoint.Publish(cmd)` with the Outbox enabled does **not** immediately send the message to the broker.
  - Instead, MassTransit generates an outbox entry associated with the `AppDbContext` (a row representing the message to be sent) and keeps it in memory tied to the context.
- `SaveChangesAsync()` persists that entry in the database:
  - If you don't call `SaveChangesAsync()`, the outbox entry will not be saved and the message will never be dispatched.
  - That is why in the controller you will see `await _publishEndpoint.Publish(cmd);` followed by `await _dbContext.SaveChangesAsync();`.
- `UseBusOutbox()` — coordinated dispatch in the same process:
  - With `UseBusOutbox()`, right after `SaveChanges` completes, MassTransit uses the same bus instance to dispatch the messages that were just persisted. This is the "bus outbox" pattern: persistence and dispatch are coordinated to guarantee apparent atomicity between persistence and messaging.
- Alternative: DB-based dispatcher (external dispatcher):
  - If `UseBusOutbox()` is removed, the outbox writes messages to the outbox table and a separate process/dispatcher (or a worker that reads that table) is responsible for publishing those messages to the broker. It still requires `SaveChangesAsync()` to persist the entry.
- In-memory transport vs real broker:
  - If `RabbitMq:Host` is not configured, the app uses `UsingInMemory` — messages are delivered only within the same process. In that case, even if the outbox dispatches, it will not reach an external broker. For inter-process messaging, RabbitMQ or another configured transport is required.

## Sagas: What They Are and Why They Matter

A saga (or state machine) is a pattern for orchestrating long-running processes and/or those involving multiple services/actors. Features and benefits:

- Orchestration: they coordinate a sequence of steps that can involve multiple microservices or components.
- Fault tolerance: they allow compensations and retries, keeping the system consistent in the face of partial failures.
- State persistence: the saga's progress is persisted (e.g. with EF Core), allowing the flow to continue after restarts.
- Decoupling: the API publishes messages; the worker responds and executes coordination logic outside the HTTP request.

In this project, sagas are located in `BistrosoftChallenge.Worker/Sagas` (e.g. `CreateOrderStateMachine.cs`) and are the source of truth for state changes that require coordination.

## Global Exception Handling

The API uses a global middleware: `BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs`. Key behavior:

- Catches any unhandled exception during the HTTP pipeline.
- Logs the error with `ILogger`.
- Attempts to send an external log (configurable) to SolarWinds if `SolarWinds:Url` and `SolarWinds:Token` are configured.
- Returns a JSON response with `StatusCode = 500` and a general message (for simplicity it includes `exception.Message` in a challenge environment; in production it is recommended to omit internal details).

Production recommendations:

- Do not return `exception.Message` to the client; use friendly messages and a `errorId` that can be correlated with logs.
- Ensure that external logging does not block the response; use well-controlled queues or fire-and-forget patterns.

See: [BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs](BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs)

## Security

- Authentication: the API uses JWT Bearer. The configuration is in `BistrosoftChallenge.Api/Program.cs` and uses `Jwt:Key` and `Jwt:Issuer` from `appsettings` or environment variables. In the absence of configuration, a default key is used for development.
- Authorization: the API applies a global policy that requires an authenticated user by default; endpoints that allow anonymous access (e.g. token) must specify it explicitly.
- Relevant NuGet: `Microsoft.AspNetCore.Authentication.JwtBearer` is referenced in the API project.

Key files: [BistrosoftChallenge.Api/Program.cs](BistrosoftChallenge.Api/Program.cs)

## Configuration and Important Variables

- `ConnectionStrings:Default` — SQL Server connection string. If not present, the application uses an in-memory DB (useful for testing).
- `Jwt:Key` and `Jwt:Issuer` — secret key and issuer for JWT tokens.
- `RabbitMq:Host`, `RabbitMq:Username`, `RabbitMq:Password` — broker configuration; if not set, MassTransit will use in-memory transport.
- `SolarWinds:Url`, `SolarWinds:Token` — (optional) for sending logs from the global middleware.

## Local Setup and Execution

Local requirements: .NET SDK (recommended: the same target version as the project), optionally RabbitMQ if you want to test real messaging.

Basic commands from the repository root:

```powershell
dotnet build BistrosoftChallenge.slnx --configuration Debug

# Run the API (you can also use Visual Studio/VS Code launch)
dotnet run --project BistrosoftChallenge.Api

# Run the worker
dotnet run --project BistrosoftChallenge.Worker
```

Notes:

- If you do not configure `ConnectionStrings:Default`, the application will use an in-memory DB to facilitate testing.
- To enable RabbitMQ, configure `RabbitMq:Host` (e.g. `rabbitmq://localhost`) and credentials.

## Tests

Run tests:

```powershell
dotnet test BistrosoftChallenge.Test
```

The saga tests use `UseInMemoryDatabase` from the EF Core InMemory provider ([BistrosoftChallenge.Test/Sagas](BistrosoftChallenge.Test/Sagas)) to set up a full `AppDbContext` without requiring a real SQL Server. This enables comprehensive tests of the message flow and in-memory persistence, using the same EF Core API but with isolation and speed.

## Points of Interest in the Code

- Exception middleware: [BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs](BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs)
- MassTransit setup and configuration: [BistrosoftChallenge.Api/Program.cs](BistrosoftChallenge.Api/Program.cs) and [BistrosoftChallenge.Worker/Program.cs](BistrosoftChallenge.Worker/Program.cs)
- Sagas / State machines: [BistrosoftChallenge.Worker/Sagas](BistrosoftChallenge.Worker/Sagas)
- Data context: [BistrosoftChallenge.Infrastructure/AppDbContext.cs](BistrosoftChallenge.Infrastructure/AppDbContext.cs)

## Recommendations and Next Steps

- In production: move secrets to a secure store (Azure Key Vault, AWS Secrets Manager, etc.) and rotate keys.
- Add a retention/query policy for logs and correlation (`traceId`/`correlationId`) in middleware and messages.
- Consider saga persistence with `MassTransit.EntityFrameworkCore` (already referenced in the Worker project) for durability.

---
