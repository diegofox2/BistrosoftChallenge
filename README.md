# BistrosoftChallenge

## Resumen

Proyecto ejemplo que ilustra una arquitectura dividida en capas, mensajería distribuida con MassTransit y sagas para orquestación de cambios de estado (CQRS implícito). Contiene una API web, un worker (procesador de sagas) y una librería de dominio e infraestructura.

## Estructura del repositorio

- `BistrosoftChallenge.Api/` — API HTTP (Controllers, autenticación, Swagger, middleware).
- `BistrosoftChallenge.Worker/` — Worker que corre `MassTransit` y las sagas (state machines).
- `BistrosoftChallenge.Domain/` — Entidades de dominio (`Customer`, `Order`, `Product`, `OrderStatus`).
- `BistrosoftChallenge.Infrastructure/` — `AppDbContext`, repositorios y lógica de persistencia.
- `BistrosoftChallenge.MessageContracts/` — Contratos de mensajes para mensajería entre procesos.
- `BistrosoftChallenge.Test/` — Tests de unidad/integración.

## Arquitectura (alto nivel)

La aplicación sigue una separación por capas clásica:

- Capa API: recibe peticiones HTTP y publica comandos/eventos vía MassTransit.
- Capa de Dominio: modelos y reglas de negocio.
- Capa de Infraestructura: implementaciones concretas (EF Core `AppDbContext`, repositorios).
- Worker/Mensajería: escucha mensajes, ejecuta sagas (orquestación de procesos de negocio) y actualiza el estado persistido.

Las modificaciones de estado del sistema se llevan adelante por medio de sagas en el worker, lo que constituye un CQRS implícito: la API actúa como punto de entrada para comandos (y consulta rápida por la BD), mientras que los cambios que requieren coordinación distribuida se ejecutan por las sagas.

## MassTransit vs MediatR

Este proyecto usa `MassTransit` (ver `BistrosoftChallenge.Worker` y `BistrosoftChallenge.Api`), no `MediatR`, porque necesitamos un bus de mensajes completo y resiliente:

- **Mensajería distribuida real**: `MassTransit` abstrae transports como RabbitMQ, Azure Service Bus o un bus in-memory, permitiendo que la API publique eventos/comandos que serán procesados por otros procesos sin dependencia directa. `MediatR` sólo enruta mensajes dentro del mismo proceso.
- **Soporte nativo para Sagas / State Machines**: `MassTransit` provee `MassTransitStateMachine<T>` con persistencia (EF Core, Mongo, etc.), timers, compensaciones y correlación automática. Es la base de la orquestación CQRS implícita del proyecto.
- **Características de bus empresarial**: incluye middleware para retries configurables, circuit breakers, outbox/inbox, enrutamiento topológico, priorización y observabilidad (diagnostics, OpenTelemetry). Estos elementos son claves para garantizar idempotencia, telemetría clara y resiliencia.
- **Escalabilidad y aislamiento**: los consumidores pueden ejecutarse en múltiples instancias del worker y el broker balancea la carga; además se pueden versionar mensajes y evolucionar la topología sin interrumpir el API.

`MediatR` es excelente como mediator in-process para desacoplar capas dentro de una misma aplicación monolítica, pero no proporciona transporte, durabilidad ni herramientas de orquestación distribuida. Para un escenario que exige sagas persistentes, mensajes fiables y separación API/Worker, `MassTransit` ofrece beneficios superiores y evita tener que construir manualmente componentes críticos (cola, reintentos, escalado, telemetría, etc.).

Consecuencias prácticas y por qué verás `await _dbContext.SaveChangesAsync()` después de `Publish` en los controllers:

- Publish + Outbox = persistencia local:
  - Llamar `_publishEndpoint.Publish(cmd)` con el Outbox activado **no** envía inmediatamente el mensaje al broker.
  - En su lugar, MassTransit genera una entrada de outbox asociada al `AppDbContext` (una fila que representa el mensaje a enviar) y la mantiene en memoria ligada al contexto.
- `SaveChangesAsync()` persiste esa entrada en la base de datos:
  - Si no llamas a `SaveChangesAsync()`, la entrada de outbox no se guardará y el mensaje nunca será despachado.
  - Por eso en el controller verás `await _publishEndpoint.Publish(cmd);` seguido de `await _dbContext.SaveChangesAsync();`.
- `UseBusOutbox()` — despacho coordinado en el mismo proceso:
  - Con `UseBusOutbox()`, justo después de que `SaveChanges` termine, MassTransit utiliza la misma instancia del bus para despachar los mensajes que se acaban de persistir. Es el patrón "bus outbox": persistencia y despacho coordinarse para garantizar atomicidad aparente entre persistencia y mensajería.
- Alternativa: dispatcher basado en BD (external dispatcher):
  - Si se elimina `UseBusOutbox()`, el outbox escribe mensajes en la tabla de outbox y un proceso/dispatcher separado (o un worker que lea esa tabla) es responsable de publicar esos mensajes al broker. Sigue requiriendo `SaveChangesAsync()` para persistir la entrada.
- Transporte en memoria vs broker real:
  - Si `RabbitMq:Host` no está configurado, la app usa `UsingInMemory` — los mensajes se entregan sólo dentro del mismo proceso. En ese caso, aunque el outbox despache, no saldrá a un broker externo. Para mensajería interprocesos necesita RabbitMQ u otro transporte configurado.

## Sagas: qué son y por qué son importantes

Una saga (o state machine) es un patrón para orquestar procesos de larga duración y/o que implican varios servicios/actores. Características y beneficios:

- Orquestación: coordinan una secuencia de pasos que pueden involucrar varios microservicios o componentes.
- Tolerancia a fallos: permiten compensaciones y reintentos, manteniendo el sistema consistente ante fallos parciales.
- Persistencia del estado: el progreso de la saga se persiste (p. ej. con EF Core), permitiendo continuar el flujo luego de reinicios.
- Desacoplamiento: la API publica mensajes; el worker responde y ejecuta la lógica de coordinación fuera del request HTTP.

En este proyecto las sagas se encuentran en `BistrosoftChallenge.Worker/Sagas` (ej. `CreateOrderStateMachine.cs`) y son la fuente de verdad para cambios de estado que requieren coordinación.

## Idempotencia y deduplicación (API + Worker + DB)

Para evitar dobles creaciones de órdenes frente a reintentos HTTP, redelivery del broker o mensajes duplicados en escenarios con múltiples workers, se incorporó una estrategia de protección en varias capas:

### 1) Idempotency key en API

- Endpoint `POST /api/orders` acepta una clave de idempotencia:
  - Header: `Idempotency-Key`
  - Body: `idempotencyKey`
- Si no llega una clave válida, la API responde `400 BadRequest`.
- Antes de publicar `CreateOrderCommand`, la API consulta `Orders` por `IdempotencyKey`:
  - Si ya existe, devuelve la orden existente (`200 OK`) y no vuelve a publicar.
  - Si no existe, publica el comando con esa clave y persiste outbox con `SaveChangesAsync()`.

Beneficio: protege contra doble submit del cliente y reintentos de red en el borde HTTP.

### 2) Contrato de mensaje con clave de idempotencia

- `CreateOrderCommand` ahora incluye `IdempotencyKey`.
- Esa clave viaja desde API hasta la saga del worker para mantener la misma identidad lógica de operación.

Beneficio: permite aplicar idempotencia de negocio también en el consumidor, no sólo en la API.

### 3) Idempotencia de dominio en `CreateOrderStateMachine`

Dentro de `CreateOrderStateMachine` se agregaron guardas idempotentes:

- Al inicio del consumo, busca una orden existente por `IdempotencyKey` o `OrderId`.
  - Si existe, no vuelve a crear orden ni descontar stock.
  - Publica `OrderCreated` con la orden ya existente y finaliza flujo.
- En la inserción, si hay carrera de concurrencia y ocurre colisión de clave única (`DbUpdateException`), reconsulta la orden existente y la trata como éxito idempotente.

Beneficio: evita efectos duplicados de negocio (doble orden / doble descuento de stock) incluso bajo concurrencia entre workers.

### 4) Restricción única en base de datos

- `Order` incorpora `IdempotencyKey` persistido.
- `AppDbContext` define índice único en `Orders.IdempotencyKey`.

Beneficio: última línea de defensa fuerte a nivel base de datos ante carreras o duplicados extremos.

### 5) Deduplicación de consumo en worker (MassTransit EF Outbox)

- El worker aplica `UseEntityFrameworkOutbox<AppDbContext>` en endpoints.
- Esto agrega deduplicación de consumo/publicación dentro del pipeline del consumidor.

Beneficio: reduce reprocesamiento de mensajes redeliverados y evita publicaciones duplicadas de eventos.

### Resultado práctico

Con esta combinación, la creación de órdenes queda protegida en capas:

- **Capa HTTP/API**: evita doble procesamiento por reintentos del cliente.
- **Capa de mensajería/worker**: dedup de consumo y ejecución idempotente de saga.
- **Capa DB**: unicidad por `IdempotencyKey` como protección definitiva.

Esto mejora consistencia, reduce errores por duplicación y hace el sistema más robusto al escalar a múltiples instancias de worker.

## Manejo de excepciones globales

La API utiliza un middleware global: `BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs`. Comportamiento clave:

- Captura cualquier excepción no manejada durante el pipeline HTTP.
- Loguea el error con `ILogger`.
- Intenta enviar un log externo (configurable) a SolarWinds si `SolarWinds:Url` y `SolarWinds:Token` están configurados.
- Devuelve una respuesta JSON con `StatusCode = 500` y un mensaje general (por simplicidad incluye `exception.Message` en entorno de challenge; en producción se recomienda omitir detalles internos).

Recomendaciones de producción:

- No retornar `exception.Message` al cliente; usar mensajes amigables y un `errorId` correlacionable con los logs.
- Asegurar que el logging externo no bloquee la respuesta; usar colas o fire-and-forget bien controlados.

Ver: [BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs](BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs)

## Seguridad

- Autenticación: la API usa JWT Bearer. La configuración se encuentra en `BistrosoftChallenge.Api/Program.cs` y usa `Jwt:Key` y `Jwt:Issuer` de `appsettings` o variables de entorno. En ausencia de configuración, se usa una clave por defecto para desarrollo.
- Autorización: la API aplica una política global que requiere usuario autenticado por defecto; los endpoints que permiten acceso anónimo (p. ej. token) deben especificarlo explícitamente.
- NuGet relevante: `Microsoft.AspNetCore.Authentication.JwtBearer` está referenciado en el proyecto API.

Archivos clave: [BistrosoftChallenge.Api/Program.cs](BistrosoftChallenge.Api/Program.cs)

## Configuración y variables importantes

- `ConnectionStrings:Default` — cadena de conexión SQL Server. Si no está presente la aplicación usa una DB en memoria (útil para pruebas).
- `Jwt:Key` y `Jwt:Issuer` — clave secreta y emisor para tokens JWT.
- `RabbitMq:Host`, `RabbitMq:Username`, `RabbitMq:Password` — configuración del broker; si no se configuran, MassTransit usará transporte in-memory.
- `SolarWinds:Url`, `SolarWinds:Token` — (opcional) para envío de logs desde el middleware global.
- `Idempotency-Key` (header HTTP en `POST /api/orders`) — clave recomendada para garantizar idempotencia en creación de órdenes.

## Inicialización y ejecución local

Requisitos locales: .NET SDK (recomiendo la misma versión objetivo del proyecto), opcionalmente RabbitMQ si quiere probar mensajería real.

Comandos básicos desde la raíz del repositorio:

```powershell
dotnet build BistrosoftChallenge.slnx --configuration Debug

# Ejecutar la API (puede usar Visual Studio/VS Code launch)
dotnet run --project BistrosoftChallenge.Api

# Ejecutar el worker
dotnet run --project BistrosoftChallenge.Worker
```

Notas:

- Si no configura `ConnectionStrings:Default`, la aplicación usará una BD en memoria para facilitar pruebas.
- Para activar RabbitMQ, configurar `RabbitMq:Host` (ej. `rabbitmq://localhost`) y credenciales.

## Pruebas

Ejecutar tests:

```powershell
dotnet test BistrosoftChallenge.Test
```

Los tests de sagas utilizan `UseInMemoryDatabase` del proveedor EF Core InMemory ([BistrosoftChallenge.Test/Sagas](BistrosoftChallenge.Test/Sagas)) para montar un `AppDbContext` completo sin requerir SQL Server real. Esto permite pruebas integrales del flujo de mensajes y persistencia en memoria, obteniendo la misma API EF Core pero con aislamiento y velocidad.

## Puntos de interés en el código

- Middleware de excepciones: [BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs](BistrosoftChallenge.Api/Middleware/GlobalExceptionMiddleware.cs)
- Programación de MassTransit y configuración: [BistrosoftChallenge.Api/Program.cs](BistrosoftChallenge.Api/Program.cs) y [BistrosoftChallenge.Worker/Program.cs](BistrosoftChallenge.Worker/Program.cs)
- Sagas / State machines: [BistrosoftChallenge.Worker/Sagas](BistrosoftChallenge.Worker/Sagas)
- Contexto de datos: [BistrosoftChallenge.Infrastructure/AppDbContext.cs](BistrosoftChallenge.Infrastructure/AppDbContext.cs)

## Recomendaciones y siguientes pasos

- En producción: mover secretos a un store seguro (Azure Key Vault, AWS Secrets Manager, etc.) y rotar claves.
- Añadir una política de retención/consulta para logs y correlación (`traceId`/`correlationId`) en middleware y mensajes.
- Considerar persistencia de sagas con `MassTransit.EntityFrameworkCore` (ya referenciado en el proyecto Worker) para durabilidad.

---
