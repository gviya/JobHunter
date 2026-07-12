# 🔌 RenewableEnergy.Infrastructure — Los detalles técnicos

Implementa los contratos que Domain y Application definen. Es la capa MÁS
REEMPLAZABLE: cambiar SQL Server por PostgreSQL, RabbitMQ por Azure Service Bus
o el email por SendGrid solo toca este proyecto.

## Mapa de la capa

| Carpeta | Qué contiene | Pieza estrella |
|---|---|---|
| `Persistence/` | ApplicationDbContext, UnitOfWork, factory de diseño para dotnet-ef | `ApplicationDbContext.cs` |
| `Persistence/Configurations/` | Mapeo Fluent API entidad a entidad (tablas, índices, VOs, enums→string) | `ProyectoConfiguration.cs` ⭐ |
| `Persistence/Interceptors/` | **AuditInterceptor** (CreatedAt/UpdatedAt) y **OutboxInterceptor** (eventos → tabla outbox, misma transacción) | `OutboxInterceptor.cs` ⭐ |
| `Persistence/Repositories/` | Implementaciones EF de los contratos de Domain | `ProyectoRepository.cs` |
| `Persistence/Migrations/` | Generadas por `dotnet-ef` (no se editan a mano) | — |
| `Persistence/Seeds/` | Datos demo para Development (idempotente) | `DatabaseSeeder.cs` |
| `Messaging/Outbox/` | OutboxMessage, OutboxProcessor (worker con Polly), OutboxService | `OutboxProcessor.cs` ⭐ |
| `Messaging/MassTransit/` | EventPublisher (+ NoOp) y ProyectoCreadoConsumer | `EventPublisher.cs` |
| `ExternalServices/` | EmailService (log en desarrollo) | — |
| `Identity/` | CurrentUserService (claims del JWT), JwtTokenGenerator, PasswordHasher | `JwtTokenGenerator.cs` |
| `DependencyInjection.cs` | El cableado interfaz → implementación de TODO lo anterior | ⭐ |

## Interruptores de configuración (appsettings / env)

| Clave | Efecto |
|---|---|
| `Messaging:Enabled` | `false` → publisher nulo, la app funciona SIN RabbitMQ |
| `Redis:Enabled` | `false` → caché en memoria en lugar de Redis |
| `Outbox:Enabled` | `false` → sin worker de outbox (tests) |

Gracias a estos flags la API arranca en local con `dotnet run` a secas y se
enciende todo al completo con docker-compose.

## Reglas de esta capa

- Las clases son `internal sealed`: el mundo exterior solo conoce las interfaces.
- Nada de lógica de negocio: si un repositorio empieza a decidir cosas del
  dominio, esa lógica está en el sitio equivocado.
- Un DbContext por request (scoped); los workers crean su propio scope.
