# 🏛️ Arquitectura

## El diagrama de capas

```
        ┌───────────────────────────────────────────────┐
        │            RenewableEnergy.API                │  Controllers, Middleware,
        │         (Presentación / HTTP / DI root)       │  JWT, Swagger, Minimal APIs
        └───────────────────┬───────────────────────────┘
                            │ depende de
        ┌───────────────────▼───────────────────────────┐
        │         RenewableEnergy.Application           │  Commands, Queries, Handlers,
        │              (Casos de uso / CQRS)            │  Validators, Behaviors, DTOs
        └───────────────────┬───────────────────────────┘
                            │ depende de
        ┌───────────────────▼───────────────────────────┐
        │           RenewableEnergy.Domain              │  Entidades, Value Objects,
        │         (Negocio puro — SIN dependencias)     │  Eventos, Specifications,
        └───────────────────▲───────────────────────────┘  Interfaces de repositorio
                            │ implementa los contratos de
        ┌───────────────────┴───────────────────────────┐
        │        RenewableEnergy.Infrastructure         │  EF Core, Repositorios,
        │         (Detalles técnicos reemplazables)     │  MassTransit, Redis, Outbox,
        └───────────────────────────────────────────────┘  Email, Identidad
```

**La regla de oro:** las dependencias apuntan **hacia dentro** (hacia Domain).
- Domain no conoce a NADIE (su único paquete es `MediatR.Contracts`, solo interfaces).
- Application conoce Domain, pero no sabe si los datos vienen de SQL Server o de un CSV.
- Infrastructure IMPLEMENTA los contratos que Domain/Application definen
  (inversión de dependencias): `IProyectoRepository` → `ProyectoRepository`.
- API es la *composition root*: el único proyecto que ve todas las piezas para
  cablear la inyección de dependencias.

Los tests de `RenewableEnergy.Architecture.Tests` (ArchUnitNET) convierten este
diagrama en **reglas ejecutables**: romperlo pone el CI en rojo.

## ¿Por qué Clean Architecture (y no capas clásicas)?

En la arquitectura de capas clásica, el negocio depende del acceso a datos
(BLL → DAL): cambiar el ORM arrastra al negocio, y testear exige base de datos.
Clean Architecture invierte esa flecha:

| Ventaja | Consecuencia práctica en este repo |
|---|---|
| Negocio testeable en aislamiento | `Domain.Tests` corre en milisegundos, sin mocks ni BD |
| Infraestructura reemplazable | Cambiar a PostgreSQL = tocar SOLO Infrastructure |
| Reglas protegidas | Imposible "un UPDATE rápido desde el controller" (no compila / falla el test de arquitectura) |
| Marco para decidir dónde va el código | ¿Regla de negocio? Domain. ¿Orquestación? Application. ¿SQL? Infrastructure |

**Trade-off honesto:** más proyectos, más archivos, más mapeos. Para un CRUD
trivial es sobre-ingeniería; compensa cuando el dominio tiene reglas de verdad
y el equipo crece. Saber ARGUMENTAR esto vale tanto como saber implementarlo.

## El flujo completo de un request

`POST /api/v1/proyectos` de principio a fin:

```
HTTP POST /api/v1/proyectos  { "nombre": "Parque Solar...", "tipo": "Solar", ... }
  │
  ▼
RequestLoggingMiddleware / ExceptionHandlingMiddleware   (API/Middleware)
  │   cronometran y envuelven TODO en un try/catch global
  ▼
JWT Bearer AuthN + AuthZ                                  ([Authorize])
  │   sin token válido → 401 y aquí se acaba
  ▼
ProyectosController.CrearProyecto                         (API/Controllers)
  │   DTO → CrearProyectoCommand → _mediator.Send(command)
  ▼
LoggingBehavior → PerformanceBehavior → ValidationBehavior (Application/Behaviors)
  │   el validator FluentValidation rechaza con 400 ANTES de llegar al handler
  ▼
CrearProyectoHandler                                      (Application/Features)
  │   1. ¿nombre duplicado? → Result.Conflict (409)
  │   2. PotenciaKW.Create / Coordenadas.Create            (VOs se auto-validan)
  │   3. ProyectoRenovable.Create(...)                     (Domain: nace en Borrador
  │      y LEVANTA el evento ProyectoCreado)
  │   4. repository.Add + unitOfWork.SaveChangesAsync
  ▼
SaveChanges intercepted                                   (Infrastructure/Interceptors)
  │   AuditInterceptor  → CreatedAt = UtcNow
  │   OutboxInterceptor → ProyectoCreado serializado a la tabla OutboxMessages
  │   ⚡ datos + evento en la MISMA transacción (patrón Outbox)
  ▼
HTTP 201 Created + Location: /api/v1/proyectos/{id}       (respuesta al cliente)

────────── segundos después, EN SEGUNDO PLANO ──────────

OutboxProcessor (BackgroundService)                       (Infrastructure/Outbox)
  │   lee OutboxMessages pendientes, deserializa, publica con Polly (reintentos)
  ▼
MediatR IPublisher → ProyectoCreadoEventHandler           (Application/EventHandlers)
  │   1. IEmailService.EnviarAsync(...)                    (email de notificación)
  │   2. IEventPublisher.PublicarAsync(IntegrationEvent)   (traducción a contrato público)
  ▼
MassTransit → RabbitMQ → ProyectoCreadoConsumer           (otros servicios reaccionan)
```

## Decisiones de diseño y sus porqués

| Decisión | Alternativa descartada | Por qué |
|---|---|---|
| MediatR 12.5 / AutoMapper 14 / MassTransit 8 | Versiones nuevas comerciales | Últimas versiones open source; decisión de licencia consciente (ver Directory.Packages.props) |
| Guid generado por el DOMINIO (`ValueGeneratedNever`) | Identity de la BD | El agregado conoce su Id al nacer (eventos lo necesitan); evita el bug UPDATE-en-vez-de-INSERT con hijas descubiertas por navegación |
| Enums como string en BD | int | Tablas legibles (`'Solar'`, no `1`) a coste de unos bytes |
| Validación duplicada (FluentValidation + dominio) | Solo una | El 400 amable es UX de API; la invariante del dominio es la última defensa |
| SQLite en tests de API / Testcontainers en integración | InMemory provider | InMemory no es relacional (acepta lo que SQL rechaza); SQLite da velocidad, Testcontainers da fidelidad |
| Outbox propio (~150 líneas) | Outbox integrado de MassTransit | Verlo por dentro es el objetivo didáctico de la plantilla |
| Migraciones al arrancar SOLO en Development | Siempre | En producción las aplica el pipeline: paso controlado y auditable |
