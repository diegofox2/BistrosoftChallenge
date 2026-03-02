# BistrosoftChallenge

## Summary

Sample project that demonstrates a layered architecture, distributed messaging with MassTransit, and sagas for orchestrating state changes (implicit CQRS). It contains a web API, a worker (saga processor), and a domain/infrastructure library.

## Repository structure

- `BistrosoftChallenge.Api/` — HTTP API (Controllers, authentication, Swagger, middleware).
- `BistrosoftChallenge.Worker/` — Worker that runs `MassTransit` and the sagas (state machines).
- `BistrosoftChallenge.Domain/` — Domain entities (`Customer`, `Order`, `Product`, `OrderStatus`).
- `BistrosoftChallenge.Infrastructure/` — `AppDbContext`, repositories and persistence logic.
- `BistrosoftChallenge.MessageContracts/` — Message contracts used for inter-process messaging.
- `BistrosoftChallenge.Test/` — Unit and integration tests.

## Architecture (high level)

The application follows a classic layered separation:

- API layer: receives HTTP requests and publishes commands/events via MassTransit.
- Domain layer: models and business rules.
- Infrastructure layer: concrete implementations (EF Core `AppDbContext`, repositories).
- Worker/Messaging: listens for messages, runs sagas (business process orchestration) and updates persisted state.

State changes that require coordination are executed by sagas in the worker, providing an implicit CQRS: the API is the entry point for commands (and quick read access to the DB), while distributed coordination is handled by the sagas.

## Sequence diagram (visual example)

A visual example of the order creation flow is available in the repository: see the `Sequence Diagram.mmd` file at the project root. The diagram illustrates the interaction between the client, API, MassTransit outbox, the bus/broker and the worker sagas. Open `Sequence Diagram.mmd` to view the flow.

File: `Sequence Diagram.mmd` (located at repository root)

## MassTransit vs MediatR

This project uses `MassTransit` (see `BistrosoftChallenge.Worker` and `BistrosoftChallenge.Api`) rather than `MediatR` because we need a full, resilient message bus:

- Real distributed messaging: `MassTransit` abstracts transports such as RabbitMQ, Azure Service Bus or an in-memory bus, allowing the API to publish events/commands that other processes can consume. `MediatR` only routes messages in-process.
- Native support for Sagas / State Machines: `MassTransit` provides `MassTransitStateMachine<T>` with persistence (EF Core, Mongo, etc.), timers, compensations and automatic correlation. This is the foundation for the project's implicit CQRS orchestration.
- Enterprise bus features: configurable retry middleware, circuit breakers, outbox/inbox, topology-based routing, prioritization and observability (diagnostics, OpenTelemetry). These are important to ensure idempotency, clear telemetry and resilience.
- Scalability and isolation: consumers can run in multiple worker instances and the broker balances load; message contracts and topology can also evolve without breaking the API.

`MediatR` is excellent as an in-process mediator to decouple layers inside the same application, but it does not provide transport, durability or distributed orchestration. For scenarios that require persistent sagas, reliable messaging and separation between API and worker processes, `MassTransit` provides significant benefits and avoids reimplementing critical components (queueing, retries, scaling, telemetry, etc.).

Practical consequences and why you will see `await _dbContext.SaveChangesAsync()` after `Publish` in controllers:

- Publish + Outbox = local persistence:
  - Calling `_publishEndpoint.Publish(cmd)` with the Outbox enabled does NOT immediately send the message to the broker.
  - Instead, MassTransit creates an outbox entry associated with the `AppDbContext` (a row describing the message to send) and keeps it in-memory attached to the context.
- `SaveChangesAsync()` persists that outbox entry to the database:
  - If you don't call `SaveChangesAsync()`, the outbox row will not be stored and the message will never be dispatched.
  - That's why controllers will typically call `await _publishEndpoint.Publish(cmd);` followed by `await _dbContext.SaveChangesAsync();`.
- `UseBusOutbox()` — coordinated dispatch in the same process:
  - With `UseBusOutbox()`, right after `SaveChanges` completes, MassTransit uses the same bus instance to dispatch the messages that were just persisted. This is the "bus outbox" pattern: persistence and dispatch coordinate to give the illusion of atomicity between DB changes and messaging.
- Alternative: DB-based external dispatcher:
  - If `UseBusOutbox()` is not used, the outbox writes messages to the outbox table and an external dispatcher process (or a worker) is responsible for publishing those messages to the broker. It still requires `SaveChangesAsync()` to persist the entry.
- In-memory transport vs real broker:
  - If `RabbitMq:Host` is not configured, the app uses an in-memory transport (`UsingInMemory`) — messages are delivered only within the same process. Even if the outbox dispatches, messages will not reach an external broker. To enable inter-process messaging, configure RabbitMQ or another transport.

## Sagas: what they are and why they matter

A saga (or state machine) is a pattern to orchestrate long-running processes and/or flows that involve multiple services or actors. Key characteristics and benefits:

- Orchestration: they coordinate a sequence of steps that may span multiple microservices or components.
- Fault tolerance: they support compensations and retries to keep the system consistent under partial failures.
- State persistence: saga progress is persisted (e.g., with EF Core), allowing flows to continue after restarts.
- Decoupling: the API publishes messages; the worker consumes them and executes the coordination logic outside the HTTP request.

In this project, sagas live under `BistrosoftChallenge.Worker/Sagas` (for example `CreateOrderStateMachine.cs`) and act as the source of truth for coordinated state changes.

## Idempotency and deduplication (API + Worker + DB)

To avoid duplicate order creation due to HTTP retries, broker redelivery or duplicate messages in multi-worker scenarios, the project applies layered protection:

### 1) Idempotency key in the API

- `POST /api/orders` accepts an idempotency key:
  - Header: `Idempotency-Key`
  - Body: `idempotencyKey`
- If a valid key is not provided, the API returns `400 BadRequest`.
- Before publishing `CreateOrderCommand`, the API queries `Orders` by `IdempotencyKey`:
  - If an order exists, it returns the existing order (`200 OK`) and does not publish again.
  - If not, it publishes the command with that key and persists the outbox entry with `SaveChangesAsync()`.

Benefit: protects against duplicate client submits and network retries at the HTTP edge.

### 2) Message contract with idempotency key

- `CreateOrderCommand` now includes `IdempotencyKey`.
- The key travels from the API to the worker saga to keep the same logical identity for the operation.

Benefit: allows applying business idempotency in the consumer as well, not only in the API.

### 3) Domain idempotency in `CreateOrderStateMachine`

The `CreateOrderStateMachine` includes idempotency guards:

- On initial consumption the saga queries for an existing order by `IdempotencyKey` or `OrderId`.
  - If found, it does not recreate the order or deduct stock.
  - It publishes `OrderCreated` with the existing order and completes.
- On insertion, if a concurrency race causes a unique constraint collision (`DbUpdateException`), the saga re-queries the existing order and treats it as a successful idempotent result.

Benefit: prevents duplicate business effects (double order / double stock deduction) even under worker concurrency.

### 4) Unique constraint at the database level

- `Order` persists `IdempotencyKey`.
- `AppDbContext` defines a unique index on `Orders.IdempotencyKey`.

Benefit: serves as the last strong defense at the database level against races or extreme duplicates.

### 5) Consumer deduplication in the worker (MassTransit EF Outbox)

- The worker configures `UseEntityFrameworkOutbox<AppDbContext>` on endpoints.
- This adds consumption/publication deduplication inside the consumer pipeline.

Benefit: reduces reprocessing of redelivered messages and prevents duplicate event publications.

### Practical outcome

With this combination, order creation is protected in layers:

- HTTP/API layer: prevents duplicate processing from client retries.
- Messaging/worker layer: consumer dedup and saga idempotency.
- DB layer: uniqueness by `IdempotencyKey` as the final safeguard.

This improves consistency, reduces duplication errors, and makes the system more robust when scaling worker instances.

## Global exception handling

The API uses a global middleware: `BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs`. Key behavior:

- Catches any unhandled exception during the HTTP pipeline.
- Logs the error using `ILogger`.
- Attempts to send an external log (configurable) to SolarWinds if `SolarWinds:Url` and `SolarWinds:Token` are configured.
- Returns a JSON response with `StatusCode = 500` and a general message (for the challenge we include `exception.Message`; in production avoid exposing internal details).

Production recommendations:

- Do not return `exception.Message` to clients; use friendly messages and an `errorId` that can be correlated with logs.
- Ensure external logging does not block the response; use queues or controlled fire-and-forget strategies.

See: `BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs`

## Security

- Authentication: the API uses JWT Bearer. Configuration lives in `BistrosoftChallenge.Api/Program.cs` and uses `Jwt:Key` and `Jwt:Issuer` from `appsettings` or environment variables. A default key is used for development if none is provided.
- Authorization: the API applies a global policy that requires authenticated users by default; anonymous endpoints (e.g., token) must explicitly allow it.
- Relevant NuGet: `Microsoft.AspNetCore.Authentication.JwtBearer` is referenced by the API project.

Key file: `BistrosoftChallenge.Api/Program.cs`

## Configuration and important variables

- `ConnectionStrings:Default` — SQL Server connection string. If absent, the app uses an in-memory database (useful for tests).
- `Jwt:Key` and `Jwt:Issuer` — secret key and issuer for JWT tokens.
- `RabbitMq:Host`, `RabbitMq:Username`, `RabbitMq:Password` — broker configuration; if not configured MassTransit uses an in-memory transport.
- `SolarWinds:Url`, `SolarWinds:Token` — (optional) for sending logs from the global middleware.
- `Idempotency-Key` (HTTP header for `POST /api/orders`) — recommended key to ensure idempotent order creation.

## Local setup and running

Local requirements: .NET SDK (match target framework), optionally RabbitMQ to test real messaging.

Basic commands from the repository root:

```powershell
dotnet build BistrosoftChallenge.slnx --configuration Debug

# Run the API (use Visual Studio / VS Code launch if preferred)
dotnet run --project BistrosoftChallenge.Api

# Run the worker
dotnet run --project BistrosoftChallenge.Worker
```

Notes:

- If `ConnectionStrings:Default` is not configured, the application will use an in-memory database for convenience in tests.
- To enable RabbitMQ, configure `RabbitMq:Host` (e.g. `rabbitmq://localhost`) and credentials.

## Tests

Run tests:

```powershell
dotnet test BistrosoftChallenge.Test
```

Saga tests use `UseInMemoryDatabase` from the EF Core InMemory provider (`BistrosoftChallenge.Test/Sagas`) to create a full `AppDbContext` without requiring a real SQL Server. This enables comprehensive message-flow and persistence tests in memory with speed and isolation.

## Notable code locations

- Global exception middleware: `BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs`
- MassTransit configuration: `BistrosoftChallenge.Api/Program.cs` and `BistrosoftChallenge.Worker/Program.cs`
- Sagas / State machines: `BistrosoftChallenge.Worker/Sagas`
- Data context: `BistrosoftChallenge.Infrastructure/AppDbContext.cs`

## Recommendations and next steps

- In production: move secrets to a secure store (Azure Key Vault, AWS Secrets Manager, etc.) and rotate keys regularly.
- Add retention and correlation policies for logs (`traceId`/`correlationId`) in middleware and messages.
- Consider saga persistence options like `MassTransit.EntityFrameworkCore` (already referenced in the Worker project) for durability.

---
