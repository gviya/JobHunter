# 🧩 Patrones de diseño de la plantilla

Cada patrón explicado con **el código real de este repositorio**: qué problema
resuelve, cómo está implementado aquí y qué preguntas suelen caer en entrevista.

---

## 1. CQRS (Command Query Responsibility Segregation)

**Problema:** las lecturas y las escrituras tienen necesidades opuestas. Las
escrituras exigen reglas, transacciones y consistencia; las lecturas quieren
proyecciones ligeras, caché y velocidad. Un "servicio" único acaba siendo un
cajón de sastre que no optimiza ninguna de las dos.

**Aquí:** cada operación es un mensaje con su handler:

- **Commands** (modifican): `CrearProyectoCommand`, `ActivarProyectoCommand`,
  `RegistrarMedicionCommand`... Cargan el AGREGADO, ejecutan reglas de dominio
  y hacen UN `SaveChangesAsync`.
- **Queries** (consultan): `ObtenerProyectosQuery`, `ObtenerProyectoPorIdQuery`...
  Usan `AsNoTracking`, proyectan a ViewModels y pueden cachear sin miedo.

**¿Cuándo usar cada uno?** Si la operación cambia el estado del sistema → Command.
Si solo devuelve datos → Query. Una query NUNCA modifica nada (ni levanta eventos).

**Entrevista:** este es "CQRS ligero" (mismo modelo de datos para ambos lados).
El CQRS completo (bases de datos separadas de lectura/escritura sincronizadas
por eventos) solo compensa a gran escala — saber decir esto suma puntos.

---

## 2. Repository + Unit of Work

**Problema:** si los handlers usan `DbContext` directamente quedan acoplados a
EF Core: no se pueden testear sin base de datos ni cambiar de ORM.

**Aquí:**
- La **interfaz** vive en Domain (`IProyectoRepository`) escrita en lenguaje de
  negocio; la **implementación** en Infrastructure (`ProyectoRepository`) con
  todo el EF (Include, AsNoTracking, paginación).
- `IUnitOfWork.SaveChangesAsync()` confirma **todo el trabajo del caso de uso
  en una transacción**. Los repositorios solo marcan (`Add`/`Delete`), nunca
  guardan: un caso de uso = un commit.

```csharp
proyectoRepository.Add(proyecto);          // marca en memoria
await unitOfWork.SaveChangesAsync(ct);     // confirma TODO (incluido el outbox)
```

**Matiz honesto (cae en entrevistas):** el DbContext YA implementa ambos
patrones internamente. Los envolvemos para invertir la dependencia (testear con
mocks, no filtrar EF a Application), no porque EF no lo haga.

---

## 3. Specification

**Problema:** los criterios de consulta ("activos", "solares") acaban duplicados
en lambdas repartidas por el código; cambiar la definición de "activo" implica
buscar y editar N sitios.

**Aquí:** `Domain/Specifications/`. Cada criterio es una clase con nombre de
negocio y son COMBINABLES:

```csharp
var spec = new ProyectosPorTipoSpec(TipoEnergia.Solar)
    .And(new ProyectosActivosSpec());          // también .Or() y .Not()

var proyectos = await repository.GetBySpecificationAsync(spec);
```

- `ToExpression()` devuelve un árbol de expresión que EF **traduce a SQL** (el
  filtro ocurre en la base de datos, no en memoria).
- `IsSatisfiedBy(objeto)` evalúa en memoria — perfecto para tests.
- El truco técnico está en `CombinadorExpresiones`: reescribe los parámetros de
  las lambdas con un `ExpressionVisitor` para poder fusionarlas.

---

## 4. Outbox

**Problema (el escenario del "evento perdido"):** guardas el proyecto en SQL y
después publicas `ProyectoCreado` en RabbitMQ. Si el proceso muere entre ambas
operaciones, la BD dice que el proyecto existe pero nadie se enteró: los
sistemas quedan **inconsistentes para siempre**. Invertir el orden solo invierte
el problema.

**Aquí, en dos mitades:**

1. **Escritura** — `OutboxInterceptor` (Infrastructure/Persistence/Interceptors):
   en cada `SaveChanges` recoge los eventos de dominio de las entidades y los
   serializa como filas de `OutboxMessages` **en la MISMA transacción** que los
   datos. O se guarda todo o no se guarda nada.
2. **Lectura** — `OutboxProcessor` (BackgroundService): cada N segundos lee los
   mensajes pendientes, los publica vía MediatR (con reintentos de Polly) y los
   marca procesados.

**Garantía resultante:** *at-least-once* (al menos una vez). Puede haber
duplicados si el proceso muere tras publicar y antes de marcar → los handlers
de eventos deben ser **idempotentes**. Exactamente-una-vez no existe en
sistemas distribuidos sin coordinación cara — decir esto en una entrevista
demuestra que entiendes el trade-off.

---

## 5. Pipeline Behaviors (MediatR)

**Problema:** validar, loguear y medir son necesidades de TODOS los casos de
uso. Copiarlas en cada handler es duplicación garantizada.

**Aquí:** tres behaviors registrados en `Application/DependencyInjection.cs`.
**El orden de registro ES el orden de ejecución**:

```
Controller ──▶ LoggingBehavior ──▶ PerformanceBehavior ──▶ ValidationBehavior ──▶ Handler
                (registra todo)      (cronometra, avisa       (400 si el command
                                      si tarda >500 ms)        no es válido)
```

- Logging va PRIMERO (el más externo) para capturar también los fallos de los
  demás behaviors.
- Validation va ÚLTIMO (el más interno) para que el handler reciba siempre
  requests válidos.
- Cada behavior decide si continúa (`await next()`) o corta la cadena
  (Validation lanza y el handler nunca se ejecuta).

---

## 6. Result vs Exception

**El criterio de esta plantilla** (lo importante es TENER un criterio):

| Situación | Mecanismo | Ejemplo real |
|---|---|---|
| Fallo ESPERABLE del negocio | `Result.Failure(Error)` | Nombre duplicado → `Error.Conflict` |
| Recurso inexistente | `Result` con `ErrorType.NotFound` | `GetByIdAsync` devuelve null → 404 |
| Invariante del DOMINIO violada | `DomainException` | Activar un proyecto ya activo |
| Entrada mal formada | `ValidationException` (la lanza el pipeline) | Potencia negativa → 400 |
| Error técnico inesperado | Excepción sin capturar | Caída de BD → 500 genérico |

- Los handlers **capturan `DomainException` y la traducen a Result** cuando el
  fallo es un flujo normal de la API (ver `ActivarProyectoHandler`).
- `ResultExtensions.ToProblem()` convierte el `ErrorType` a HTTP en UN sitio.
- ¿Por qué no excepciones para todo? Son caras (stack trace), invisibles en la
  firma del método y convierten flujo normal en control de flujo excepcional.
- ¿Por qué no Result para todo? Las violaciones de invariantes son bugs del
  llamante: deben explotar ruidosamente, no propagarse como datos.

---

## 7. Domain Events

**Problema:** al crear un proyecto hay que enviar email, publicar al bus,
actualizar estadísticas... Si `CrearProyectoHandler` hace todo eso, cada nueva
reacción obliga a tocarlo (violación de Open/Closed).

**Aquí:** la entidad LEVANTA el hecho (`RaiseDomainEvent(new ProyectoCreado(...))`)
y los interesados se SUSCRIBEN (`ProyectoCreadoEventHandler : INotificationHandler<ProyectoCreado>`).
El evento viaja por el outbox (patrón 4), así que la reacción es asíncrona y
sobrevive a caídas.

**Evento de dominio vs evento de integración** (pregunta estrella):

| | Dominio | Integración |
|---|---|---|
| Alcance | Interno al servicio | Contrato PÚBLICO entre servicios |
| Transporte | MediatR (in-process) | RabbitMQ / Azure Service Bus |
| Tipos | Puede usar tipos del dominio | Solo primitivos estables |
| Cambiarlo | Refactoring normal | Rompe consumidores: se versiona |

La traducción de uno a otro la hace `ProyectoCreadoEventHandler`, que publica
`ProyectoCreadoIntegrationEvent` vía `IEventPublisher` (MassTransit).

---

## 8. Value Objects

**Problema:** la "obsesión por primitivos": `double latitud` puede valer 200;
`string email` puede ser cualquier cosa; dos `decimal` de kW y MW se confunden.

**Aquí:** `Coordenadas`, `PotenciaKW`, `Email` (en `Domain/ValueObjects/`):
- `record` → igualdad POR VALOR e inmutabilidad.
- Constructor privado + `Create()` con validación → **imposible construir uno inválido**.
- Persistencia sin ensuciar el dominio: conversión (`PotenciaKW` ⇄ decimal) u
  owned type (`Coordenadas` → columnas Latitud/Longitud). Ver `ProyectoConfiguration`.

---

## 9. Builder (en tests)

`tests/RenewableEnergy.Application.Tests/Builders/ProyectoRenovableBuilder.cs`:
construye objetos de prueba con defaults razonables y API fluida:

```csharp
var proyecto = new ProyectoRenovableBuilder()
    .ConTipo(TipoEnergia.Eolica)
    .Activado()
    .Build();
```

Si mañana `Create()` gana un parámetro, se toca UNA clase y no cien tests.

---

## 10. Otros patrones presentes (búscalos en el código)

| Patrón | Dónde |
|---|---|
| Factory Method | `ProyectoRenovable.Create()`, `Coordenadas.Create()` |
| Null Object | `NoOpEventPublisher` (messaging apagado sin `if`s por el código) |
| Interceptor | `AuditInterceptor`, `OutboxInterceptor` (EF Core) |
| Cache-Aside | `ObtenerProyectoPorIdHandler` (+ invalidación en los commands) |
| Retry con backoff | `OutboxProcessor` (Polly) y HttpClient `"ExternalApi"` |
| Options | `OutboxOptions`, `JwtOptions`, `RateLimitSettings` |
| Composition Root | `Program.cs` + los tres `DependencyInjection.cs` |
