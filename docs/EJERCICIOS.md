# 🏋️ Ejercicios guiados

La mejor forma de interiorizar esta arquitectura es **modificarla**. Estos
ejercicios están ordenados de menor a mayor dificultad; cada uno indica qué
concepto entrena, qué archivos tocarás y pistas concretas. Algunos son huecos
dejados A PROPÓSITO en la plantilla para que los completes tú.

La red de seguridad ya existe: si rompes una regla de capas, los tests de
`Architecture.Tests` te avisarán; si olvidas un mapeo, fallará
`ProyectoMappingProfileTests`. Trabaja en una rama (`git checkout -b ejercicio-1`)
y compara con `main` al terminar.

## 📖 Cómo trabajar cada ejercicio (el método)

Los niveles tienen niveles de guía DISTINTOS, a propósito:

- **Nivel 0** (testing desde cero): paso a paso completo, casi dictado —
  porque nadie aprende a nadar leyendo pistas.
- **Niveles 1–7**: enunciado + archivos a tocar + pistas, pero SIN la
  solución. La incomodidad de no tener la receta es donde ocurre el
  aprendizaje. No es que falte la guía: la guía genérica ya existe
  (`GUIA_NUEVAS_FEATURES.md` para features, `GUIA_TESTING.md` para tests) y
  el repo está lleno de código análogo que copiar-adaptando.

Cuando te atasques (te pasará; es la señal de que el ejercicio vale), escala
en este orden — cada peldaño es más "caro" que el anterior:

1. **Relee el doc del tema** (la tabla del mapa de abajo te dice cuál) — 5 min.
2. **Busca el código análogo**: casi todo ejercicio tiene un "hermano" ya
   implementado (¿endpoint de instalaciones? mira el de mediciones; ¿un
   behavior? mira los tres que hay). Copiar-adaptando NO es hacer trampa: es
   como se trabaja de verdad.
3. **Timebox**: si tras 45–60 min sigues sin avanzar NADA, pide ayuda — a tu
   asistente de IA, pero con una regla: pídele PISTAS o que te revise lo que
   llevas, no la solución entera ("¿por qué no compila esto?" enseña;
   "hazme el ejercicio 2.1" no).
4. Al terminar, **pide una revisión de código** de tu rama: `git diff main`
   y que te señale qué habría hecho distinto y por qué. Ahí se aprende lo
   que ningún enunciado cuenta.

---

## 🗺️ Mapa de cobertura: qué se aprende con qué

Usa esta tabla como checklist de aprendizaje: cuando termines todos los
ejercicios de una fila, esa tecnología/concepto es tuyo. Todo lo que existe
en la plantilla tiene al menos un ejercicio.

| Área | Se aprende en |
|---|---|
| **Escribir tests desde cero (xUnit, AwesomeAssertions, Moq)** | 0.1–0.4 + `GUIA_TESTING.md` |
| Agregados, invariantes, eventos de dominio | 1.1, 1.3, 2.1 |
| Value Objects | 1.2, 2.1 (PotenciaKW en el DTO) |
| Specification pattern | 1.2, 3.1 |
| CQRS: feature completa (command+validator+handler) | 2.1, 2.2 |
| Pipeline Behaviors de MediatR (escribir el TUYO) | 4.4 |
| EF Core: proyecciones, SQL generado | 3.1 |
| EF Core: migraciones y proveedores | 3.2 |
| EF Core: concurrencia optimista | 3.3 |
| EF Core: interceptors y query filters (soft delete) | 3.4 |
| Testcontainers (escribir un test de integración nuevo) | 3.5 |
| JWT, refresh tokens, roles | 4.1, 4.3 |
| MassTransit: consumers, idempotencia, colas de error | 4.2, 4.6 |
| Polly + HttpClient (API externa real) | 4.5 |
| Redis / caché distribuida | 4.4 (+ inspección con redis-cli) |
| Tests de arquitectura (ArchUnitNET) | 5.1 |
| Versionado de API | 5.2 |
| Criterio arquitectónico (ADR) | 5.3 |
| Serilog estructurado, correlation id, Seq | 6.1, 6.2 |
| OpenTelemetry / trazas distribuidas | 6.3 |
| Health checks personalizados | 6.4 |
| BackgroundService / hosted services | 6.5 |
| Outbox: mantenimiento y límites | 6.6 |
| Rate limiting | 6.7 |
| GitHub Actions (CI/CD real) | 7.1, 7.2 |
| SonarCloud | 7.3 |
| Kubernetes (probes, secrets, rolling update) | 7.4 |
| dotnet new (usar esto COMO plantilla) | 7.5 |
| Gestión de secretos en local (user-secrets) | 7.6 |

> Lo ÚNICO que no se puede practicar 100 % en local es el despliegue real en
> Azure (App Service + Key Vault): requiere una cuenta (hay capa gratuita).
> Está descrito como ejercicio opcional al final del nivel 7.

---

## Nivel 0 — Tus primeros tests (guiado paso a paso)

*Si nunca has escrito un test, este nivel es para ti. Lee ANTES
`docs/GUIA_TESTING.md` (15 min) y vuelve aquí. A diferencia del resto de
niveles, aquí las instrucciones son completas: el objetivo es que teclees,
ejecutes y VEAS, no que descubras.*

### 0.1 Ejecuta, filtra y rompe (sin escribir código aún)

1. Ejecuta toda la suite y lee el resumen final:
   ```bash
   dotnet test
   ```
2. Ejecuta UN solo test y mira su salida:
   ```bash
   dotnet test --filter "FullyQualifiedName~Activar_DesdeBorrador"
   ```
3. Ahora ROMPE algo: abre
   `tests/RenewableEnergy.Domain.Tests/Entities/ProyectoRenovableTests.cs`,
   busca `Activar_DesdeBorrador_...` y cambia
   `Should().Be(EstadoProyecto.Activo)` por `EstadoProyecto.Pausado`. Ejecuta
   el test y **lee el mensaje de fallo completo**: fíjate en que te dice qué
   esperaba, qué encontró y en qué línea.
4. Deshaz el cambio (`git checkout -- tests/`) y comprueba que vuelve el verde.
5. Bonus IDE: pon un breakpoint DENTRO del test, ejecútalo en modo debug y
   avanza con F10 viendo cómo se ejecuta `Activar()` por dentro.

*Has aprendido:* el ciclo rojo/verde, leer fallos, depurar a través de un test.

### 0.2 Tu primer [Fact]: probar Cancelar()

La entidad tiene `Cancelar()` y NADIE lo prueba desde Borrador. Escríbelo tú:

1. Abre `ProyectoRenovableTests.cs` y añade al final de la clase:
   ```csharp
   [Fact]
   public void Cancelar_DesdeBorrador_DeberiaCambiarACancelado()
   {
       // Arrange: usa el helper que ya existe en esta clase
       var proyecto = CrearProyectoValido();

       // Act
       proyecto.Cancelar();

       // Assert: completa TÚ las dos aserciones:
       // 1) el Estado debe ser EstadoProyecto.Cancelado
       // 2) PuedeSerEliminado debe ser true
   }
   ```
2. Completa los asserts (sintaxis: `algo.Should().Be(valor)` /
   `algo.Should().BeTrue()` — tienes 30 ejemplos alrededor).
3. `dotnet test --filter "FullyQualifiedName~Cancelar_DesdeBorrador"` → verde.
4. **Míralo fallar** (paso clave): en `ProyectoRenovable.Cancelar()` comenta
   la línea `Estado = EstadoProyecto.Cancelado;`, ejecuta, lee el rojo,
   descomenta.
5. Escribe el segundo escenario tú solo, copiando el patrón de
   `Activar_CuandoYaEstaActivo_...`:
   `Cancelar_CuandoYaEstaCancelado_DeberiaLanzarDomainException`.

*Has aprendido:* AAA, aserciones, ver el test proteger el código de verdad.

### 0.3 Tu primera [Theory]: más formatos de email inválidos

1. Abre `tests/RenewableEnergy.Domain.Tests/ValueObjects/EmailTests.cs` y
   estudia 2 minutos cómo un solo método se ejecuta 4 veces con `[InlineData]`.
2. Añade DOS casos inválidos nuevos que se te ocurran (¿espacios dentro?
   ¿"@dominio.com" sin usuario?). Ejecuta: ¿pasan?
3. Uno de estos dos NO lanzará excepción: `"usuario@dominio.c"` y
   `"usuario @dominio.com"`. Averigua cuál probándolo, mira la regex de
   `Email.cs` y entiende por qué. Decide: ¿es un bug o una decisión aceptable?
   (No hay respuesta única — eso también es aprender a testear.)

*Has aprendido:* Theory/InlineData y que los tests son una herramienta para
INTERROGAR al código, no solo para vigilarlo.

### 0.4 Tu primer mock: un caso que falta de verdad

`ActualizarProyectoHandlerTests` cubre éxito, nombre duplicado y estado
terminal... pero NO el caso "proyecto inexistente" (los demás handlers sí lo
tienen). Complétalo:

1. Abre `ActualizarProyectoHandlerTests.cs` y estudia el test
   `Handle_ConProyectoInexistente_...` de `ActivarProyectoHandlerTests.cs`
   (es tu plantilla exacta, está a un archivo de distancia).
2. Escribe en `ActualizarProyectoHandlerTests`:
   `Handle_ConProyectoInexistente_DeberiaDevolverNotFound`:
   - Arrange: programa el mock para devolver null →
     `.Setup(r => r.GetByIdAsync(...)).ReturnsAsync((ProyectoRenovable?)null);`
   - Act: llama al handler con `CommandValido(Guid.NewGuid())`.
   - Assert: `IsFailure` es true y `Error!.Tipo` es `ErrorType.NotFound`.
   - Verify (el arte de los mocks): comprueba que `SaveChangesAsync` NO se
     llamó — `Times.Never`.
3. Verde. Acabas de cerrar un hueco REAL de la suite (mira la cobertura si
   quieres: ese camino del handler estaba sin pisar).

*Has aprendido:* Setup/Returns, verificar interacciones, y a leer tests
vecinos como documentación.

➡️ A partir de aquí ya sabes lo suficiente: cada ejercicio de los niveles
1–7 te pedirá sus propios tests, y eso es práctica deliberada, no un extra.

---

## Nivel 1 — Calentamiento (solo Domain)

### 1.1 Pausar y reanudar un proyecto ⭐ hueco a propósito

El enum `EstadoProyecto` tiene `Pausado`... pero **no existe ninguna forma de
llegar a él**. Complétalo:

- `ProyectoRenovable.Pausar()`: solo desde `Activo`; y `Reanudar()`: solo desde `Pausado`.
- Decide: ¿un proyecto pausado admite mediciones? (decisión de NEGOCIO —
  documenta la tuya en el XML doc del método).
- Levanta un evento de dominio nuevo `ProyectoPausado`.
- Tests en `ProyectoRenovableTests` (mínimo: transición válida, transición
  inválida, evento levantado).

*Entrena:* invariantes, transiciones de estado, eventos de dominio.

### 1.2 Una Specification nueva

Crea `ProyectosPorPotenciaMinimaSpec(decimal minimoKW)` y pruébala en
`SpecificationTests` combinada con `And()`. Pista: la expresión debe comparar
`proyecto.Potencia.Valor` — descubrirás cómo se navega un Value Object dentro
de un árbol de expresión.

*Entrena:* Specification pattern, expresiones.

### 1.3 Rompe el dominio a propósito

Ejercicio de 15 minutos con moraleja: borra el guard de `Activar()` (el `if`
que exige estado Borrador), ejecuta `dotnet test` y apunta QUÉ tests fallan y
de QUÉ proyectos (dominio, application, E2E). Restaura con `git checkout`.
Repite borrando una línea del `ProyectoMappingProfile`.

*Entrena:* entender tu red de seguridad — qué nivel de test caza qué tipo de
error, y por qué la pirámide tiene varias plantas.

---

## Nivel 2 — Feature completa (todas las capas)

### 2.1 Endpoint para añadir instalaciones ⭐ hueco a propósito

El dominio ya sabe hacerlo (`ProyectoRenovable.AgregarInstalacion`), el seeder
lo usa… pero **no hay endpoint**. Añádelo siguiendo `GUIA_NUEVAS_FEATURES.md`
paso a paso:

```
POST /api/v1/proyectos/{proyectoId}/instalaciones
{ "nombre": "Aerogenerador AG-07", "potenciaInstaladaKW": 500 }
```

Necesitarás: `AgregarInstalacionCommand/Validator/Handler/Response`, el DTO de
entrada en `API/Dto/`, una acción en un `InstalacionesController` nuevo,
mapeo en el Profile, invalidación de la caché del detalle y tests. Es la
réplica exacta de Registrar Medición: si la guía se entendió, sale solo.

*Entrena:* el ciclo completo de una feature.

### 2.2 Completar y cancelar desde la API

`Completar()` y `Cancelar()` existen en el dominio pero no se exponen. Añade
`POST /api/v1/proyectos/{id}/completar` y `/cancelar` (fíjate en cómo está
hecho `/activar`). Pregunta a responder: ¿por qué endpoints de ACCIÓN y no un
`PATCH { "estado": "Completado" }`? (la respuesta está en `docs/API.md`).

*Entrena:* modelado de operaciones de negocio en REST.

---

## Nivel 3 — Persistencia y datos

### 3.1 Consulta agregada con proyección

`GET /api/v1/proyectos/estadisticas` → `{ totalProyectos, porTipo: {...},
potenciaTotalKW, energiaTotalKWh }`. Hazla SIN cargar entidades: una proyección
con `GroupBy`/`Sum` directamente en el repositorio (mira cómo `GetPaginadoAsync`
separa count y página). Compara el SQL generado activando
`Microsoft.EntityFrameworkCore.Database.Command: Information` en Serilog.

*Entrena:* lado query de CQRS, proyecciones eficientes, leer SQL de EF.

### 3.2 Cambiar a PostgreSQL de verdad

Sigue `docs/SETUP.md` §"Cambiar a PostgreSQL" y deja la suite de tests en
verde. Aprenderás qué es específico del proveedor (migraciones) y qué no
(todo lo demás — esa es la gracia).

*Entrena:* independencia del proveedor, migraciones.

### 3.3 Concurrencia optimista

Añade un token de concurrencia (`rowversion`) a `ProyectoRenovable`:
`builder.Property<byte[]>("Version").IsRowVersion()` en la configuración +
migración. Provoca el conflicto en un test de integración (dos contextos
editando el mismo proyecto) y decide qué HTTP devolver (pista: 412 o 409).

*Entrena:* DbUpdateConcurrencyException, tokens de concurrencia.

### 3.4 Soft delete con interceptor y query filter

Ahora los proyectos se borran físicamente. Cámbialo a BORRADO LÓGICO:

- Propiedad `EliminadoEn: DateTime?` en `BaseEntity`.
- Un `SaveChangesInterceptor` nuevo que convierta los `EntityState.Deleted`
  en `Modified` + sella `EliminadoEn` (inspírate en `AuditInterceptor`).
- `HasQueryFilter(p => p.EliminadoEn == null)` en las configuraciones: los
  "borrados" desaparecen de TODAS las queries sin tocar ni un handler.
- Test de integración: borrar → `GetByIdAsync` devuelve null → la fila sigue
  en la tabla (consúltala con `IgnoreQueryFilters()`).

*Entrena:* interceptors de EF, query filters globales — un clásico de
proyectos reales (auditoría y normativa).

### 3.5 Escribe un test de integración desde cero

`UsuarioRepository` no tiene tests de integración. Escríbelos en
`Infrastructure.Tests` (usa `SqlServerContainerFixture` como el de proyectos):

- `GetByEmailAsync` encuentra al usuario aunque busques con `"  DEMO@...  "`
  (el VO normaliza) — esto valida que el value converter de `Email` traduce
  bien la igualdad a SQL.
- Insertar dos usuarios con el mismo email lanza `DbUpdateException`
  (el índice único trabajando).

*Entrena:* Testcontainers por tu cuenta, converters en queries, índices únicos.

---

## Nivel 4 — Distribuido y seguridad

### 4.1 Refresh tokens ⭐ hueco a propósito

El login emite un JWT de 60 minutos y no hay forma de renovarlo sin volver a
mandar la contraseña. Implementa `POST /api/v1/auth/refresh`:

- Entidad `RefreshToken` (token opaco aleatorio, `UsuarioId`, expiración larga,
  `Revocado`) — piensa: ¿agregado propio o hijo de `Usuario`?
- El login pasa a devolver `{ token, refreshToken }`.
- `/refresh` valida el refresh token, lo ROTA (revoca el usado, emite otro) y
  devuelve un JWT nuevo.
- Postman: añade el request y su test que actualiza `{{token}}`.

*Entrena:* diseño de agregados, seguridad de sesiones, rotación de tokens.

### 4.2 Consumidor real de eventos

`ProyectoCreadoConsumer` solo loguea. Conviértelo en algo real: cuando llegue
el evento, que registre una fila en una tabla nueva `AuditoriaEventos`.
Hazlo IDEMPOTENTE: si el mismo evento llega dos veces (garantía at-least-once
del outbox), la segunda no debe duplicar la fila. Pista: clave única por
`EventoId` + `catch` del conflicto.

*Entrena:* consumers de MassTransit, idempotencia — la pareja de conceptos más
preguntada sobre mensajería.

### 4.3 Roles en el JWT

Añade un claim `role` (`Admin`/`Operador`) al `JwtTokenGenerator` y protege
`DELETE /api/v1/proyectos/{id}` con `[Authorize(Roles = "Admin")]`. El usuario
demo será Admin; siembra un segundo usuario Operador y prueba en un test E2E
que recibe 403.

*Entrena:* autorización por roles, claims.

### 4.4 Escribe tu propio Pipeline Behavior: caché declarativa

El cache-aside vive dentro de `ObtenerProyectoPorIdHandler`. Extráelo a un
behavior GENÉRICO para que cualquier query pueda cachearse declarándolo:

```csharp
public interface ICacheableQuery
{
    string ClaveCache { get; }
    TimeSpan TiempoDeVida { get; }
}
```

- `CachingBehavior<TRequest, TResponse>` que solo actúa si el request
  implementa `ICacheableQuery` (mira cómo los otros behaviors usan `next()`).
- `ObtenerProyectoPorIdQuery` implementa la interfaz; su handler queda LIMPIO
  (solo BD y mapeo).
- Piensa el orden: ¿el caching va antes o después de Validation? ¿Por qué?
- Extra con Docker: `Redis:Enabled=true`, arranca Redis del compose y mira
  las claves en vivo: `docker exec -it renewable-redis redis-cli KEYS '*'`.

*Entrena:* diseño de behaviors (el corazón de MediatR), genéricos con
restricciones, Redis de verdad.

### 4.5 API externa real con HttpClient + Polly: previsión meteorológica

El HttpClient `"ExternalApi"` con reintentos está registrado pero nadie lo
usa. Dale un uso REAL con [Open-Meteo](https://open-meteo.com) (gratuita, sin
API key): `GET /api/v1/proyectos/{id}/prevision` devuelve la previsión de
radiación solar/viento en las COORDENADAS del proyecto (¡el VO por fin luce!).

- Interfaz `IServicioMeteorologico` en Application; implementación en
  `Infrastructure/ExternalServices` con `IHttpClientFactory` (cliente tipado).
- Query + handler que carga el proyecto y pide la previsión para su Ubicacion.
- Añade un CIRCUIT BREAKER al pipeline de Polly y prueba qué pasa si apagas
  la red: ¿qué recibe el cliente? Decide el contrato de error (¿503? ¿respuesta
  degradada sin previsión?).
- Test unitario del handler mockeando `IServicioMeteorologico` (la API externa
  NUNCA se llama en tests — para eso está la interfaz).

*Entrena:* IHttpClientFactory, Polly (retry + circuit breaker), integración
con terceros sin acoplar Application, diseño de degradación.

### 4.6 Provoca la cola de errores de RabbitMQ

Con el compose completo arrancado (`Messaging:Enabled=true`): haz que
`ProyectoCreadoConsumer` lance una excepción, crea un proyecto y ve a la UI
de RabbitMQ (http://localhost:15672) a buscar la cola `_error` con tu mensaje
dentro. Después configura reintentos en MassTransit
(`cfg.UseMessageRetry(r => r.Exponential(...))`) y observa la diferencia.
Termina restaurando el consumer.

*Entrena:* qué hace MassTransit cuando un consumer falla — la pregunta de
mensajería que separa "lo he usado" de "lo entiendo".

---

## Nivel 5 — Arquitectura

### 5.1 Una regla de arquitectura nueva

Escribe en `ArchitectureTests` la regla: *"los tipos de Domain.DomainEvents
deben ser sealed e implementar IDomainEvent"*. Rómpela a propósito para ver el
mensaje de error, y arréglala.

*Entrena:* ArchUnitNET, arquitectura ejecutable.

### 5.2 Versionado real: v2 de un endpoint

Publica `GET /api/v2/proyectos` que devuelva un ViewModel distinto (p. ej. con
`potenciaMW` en lugar de kW). Controller nuevo con `[ApiVersion("2.0")]`, sin
tocar el v1: comprueba con Postman que ambos conviven.

*Entrena:* Asp.Versioning, evolución de contratos sin romper clientes.

### 5.3 El debate final (sin código)

Escribe en `docs/` tu propia ADR (Architecture Decision Record) de una página:
*"¿Debería este proyecto usar Minimal APIs en lugar de Controllers para todo?"*
Argumenta ambos lados con ejemplos del repo y toma una decisión. En una
entrevista, esta habilidad vale más que cualquier patrón.

---

## Nivel 6 — Observabilidad y operaciones

*Lo que diferencia "me compila" de "sé operarlo en producción".*

### 6.1 Correlation ID de punta a punta

Cuando un cliente reporta un error, necesitas encontrar TODOS los logs de ESA
petición. Implementa un `CorrelationIdMiddleware`:

- Lee la cabecera `X-Correlation-Id` (o genera un Guid si no viene).
- Lo mete en el contexto de Serilog con `LogContext.PushProperty(...)` — a
  partir de ahí, TODOS los logs de la petición (behaviors, handlers, EF)
  llevan la propiedad sin tocar ni una línea de ellos.
- Lo devuelve en la cabecera de la respuesta y lo añade a los ProblemDetails
  del `ExceptionHandlingMiddleware` (`Extensions["correlationId"]`).
- Compruébalo en Seq: filtra por un correlationId y ve la petición entera.

*Entrena:* middleware, logging estructurado con LogContext, diagnóstico real.

### 6.2 Explota Seq de verdad

Con el compose arrancado, genera tráfico (corre la colección de Postman
varias veces, provoca 400s y 409s) y en Seq (http://localhost:5341):

- Escribe la query que lista los casos de uso más lentos (pista: los warnings
  de `PerformanceBehavior` llevan la propiedad `Milisegundos`).
- Crea una señal (Signal) para `@Level = 'Error'` y otra para los 409.
- Encuentra el flujo completo de un `ProyectoCreado`: creación → outbox →
  email simulado, solo con filtros.

*Entrena:* consultar logs estructurados — la habilidad de soporte nº 1.

### 6.3 Trazas distribuidas con OpenTelemetry + Jaeger

OpenTelemetry está instrumentando... y tirando las trazas (no hay exporter).
Complétalo:

- Añade `jaegertracing/all-in-one` al docker-compose (UI en el puerto 16686).
- Paquete `OpenTelemetry.Exporter.OpenTelemetryProtocol` + `.AddOtlpExporter()`
  en `AddObservabilidad` apuntando a Jaeger.
- Añade también `.AddEntityFrameworkCoreInstrumentation()` (paquete aparte) y
  mira en Jaeger la traza completa: HTTP → handler → cada SQL con su duración.
- Pregunta a responder: ¿en qué se diferencia una TRAZA de un LOG y cuándo
  usarías cada uno?

*Entrena:* observabilidad moderna (lo que piden como "OpenTelemetry/APM").

### 6.4 Health checks de RabbitMQ y Redis

`/health/ready` solo comprueba la base de datos: la API puede estar "ready"
con el broker caído. Añade checks para RabbitMQ y Redis (paquetes
`AspNetCore.HealthChecks.Rabbitmq` y `AspNetCore.HealthChecks.Redis`), pero
REGISTRADOS SOLO si `Messaging:Enabled`/`Redis:Enabled` (mira cómo condiciona
`AddMessaging`). Con el compose arrancado: para Redis
(`docker stop renewable-redis`) y observa `/health/ready` pasar a Unhealthy.

*Entrena:* health checks compuestos, degradación observable.

### 6.5 Tu propio BackgroundService: informe diario

Crea `InformeEnergiaProcessor` (inspírate en `OutboxProcessor`): cada N
minutos (configurable con su Options) suma la energía de las últimas 24 h y
la "envía" por `IEmailService` a operaciones. Detalles que importan:

- BackgroundService es SINGLETON y el DbContext es SCOPED: necesitas
  `IServiceScopeFactory` (mira cómo lo resuelve el OutboxProcessor).
- Un fallo de una pasada NO debe matar el worker.
- Apagable por configuración, como el outbox.

*Entrena:* hosted services, PeriodicTimer, gestión de scopes — el patrón de
todos los "jobs" de una API.

### 6.6 Mantenimiento del outbox

La tabla `OutboxMessages` crece para siempre y los mensajes con `Error` se
quedan ahí muertos. Arréglalo:

- Limpieza: borra procesados con más de 7 días (en el propio OutboxProcessor
  o en un worker aparte — argumenta tu elección).
- Reintentos con límite: añade `Intentos` al mensaje; los fallidos se
  reintentan en la siguiente pasada hasta un máximo (ahora mismo un fallo lo
  marca procesado para siempre, ¿lo habías notado?); superado el máximo,
  márcalo "muerto" y loguea Error.
- Migración para las columnas nuevas + tests del comportamiento.

*Entrena:* el outbox más allá del happy path — operabilidad de verdad.

### 6.7 Rate limiting: verlo actuar

- Escribe un test E2E que dispare N+1 peticiones y compruebe que la última
  recibe 429 (usa una política con `PermitLimit` bajo vía `UseSetting` en una
  factory dedicada).
- Cambia la política "Fixed" por una `SlidingWindowLimiter` y explica en un
  comentario la diferencia práctica (ráfagas en el borde de la ventana).

*Entrena:* protección de APIs, testear configuración de infraestructura HTTP.

---

## Nivel 7 — Ecosistema: CI/CD, Kubernetes y la plantilla

*Nada de esto requiere pagar: GitHub y SonarCloud son gratis; K8s viene con
Docker Desktop.*

### 7.1 Sube el repo a GitHub y pon el pipeline en verde

Crea el repositorio en GitHub y haz push. El workflow `ci-cd.yml` correrá
solo. Bonus importante: en el runner de GitHub SÍ hay Docker, así que los 6
tests de integración con Testcontainers se ejecutarán POR PRIMERA VEZ
completos — míralos en el log del job. Añade el badge de estado al README.
Después haz un push a `main` y comprueba que la imagen aparece en GHCR
(pestaña Packages).

*Entrena:* CI/CD real de principio a fin, GHCR.

### 7.2 Informe de cobertura navegable + gate en CI

- Local: `dotnet tool install -g dotnet-reportgenerator-globaltool`, genera el
  HTML desde los `coverage.cobertura.xml` y navega qué líneas exactas te
  faltan por cubrir.
- CI: añade un paso que FALLE si la cobertura de Domain+Application baja
  del 80 % (reportgenerator o `dotnet test /p:Threshold=80` con coverlet.msbuild).
  Rompe el umbral a propósito borrando un test para verlo actuar.

*Entrena:* cobertura como herramienta (no como número decorativo).

### 7.3 SonarCloud

Gratis para repos públicos: crea la cuenta, añade `SONAR_TOKEN` como secret y
`SONAR_ENABLED=true` como variable — el job `code-quality` ya está escrito.
Revisa lo que encuentre: code smells, duplicación, security hotspots (avisará
del SHA-256 del PasswordHasher: ya sabes por qué está y qué contestarías).

*Entrena:* análisis estático, deuda técnica medible.

### 7.4 Kubernetes en tu máquina

Activa Kubernetes en Docker Desktop y despliega EN LOCAL:

```bash
docker build -f docker/Dockerfile -t renewable-energy-api:local .
# edita k8s/deployment.yaml: image local + imagePullPolicy: Never
kubectl create secret generic renewable-api-secrets --from-literal=... # (ver deployment.yaml)
kubectl apply -f k8s/configmap.yaml -f k8s/deployment.yaml -f k8s/service.yaml
kubectl get pods -w
kubectl port-forward svc/renewable-api 8080:80
```

Experimentos que enseñan más que cualquier tutorial:
- Sin la BD accesible, mira los pods: Running pero **no Ready** (readiness
  haciendo su trabajo — la diferencia liveness/readiness EN VIVO).
- `kubectl delete pod <uno>` → aparece otro solo (self-healing).
- Cambia `replicas: 3`, aplica, y mira el rolling update con `-w`.

*Entrena:* K8s de verdad con los manifests del repo, probes, secrets.

### 7.5 Úsala COMO plantilla: dotnet new

Para esto existe `.template.config/`:

```bash
dotnet new install .
dotnet new cleanarch -n MiEmpresa.Pedidos -o ../MiEmpresa.Pedidos
cd ../MiEmpresa.Pedidos && dotnet test
```

Comprueba qué renombró (proyectos, namespaces, sln) y qué NO (¿el dominio
"proyectos renovables" tiene sentido en Pedidos? ¿qué harías genérico y qué
reescribirías?). Esa reflexión ES el ejercicio.

*Entrena:* el sistema de plantillas de .NET y los límites del scaffolding.

### 7.6 Secretos de desarrollo con user-secrets

`appsettings.Development.json` lleva el secreto JWT de dev dentro del repo
(aceptable por ser de desarrollo, pero mejorable). Migra a User Secrets:
`<UserSecretsId>` en el csproj de la API, `dotnet user-secrets set "Jwt:Secret" ...`,
borra el valor del JSON y verifica que la API arranca igual. Explica en qué
se diferencia de .env y de Key Vault (los tres aparecen en este repo: ¿cuándo
corresponde cada uno?).

*Entrena:* la escalera completa de gestión de secretos.

### 7.7 (Opcional, requiere cuenta Azure) Despliegue real

Con una suscripción gratuita: App Service (plan F1) + Azure SQL (serverless) +
Key Vault con `ConnectionStrings--DefaultConnection` y `Jwt--Secret`, Managed
Identity para la API, y el job `deploy-staging` del workflow con tu
publish profile. Es el único ejercicio no 100 % local — todo lo demás del
repo se aprende sin gastar un euro.

---

## ¿Cómo sé que lo hice bien?

Para cada ejercicio, en este orden:

1. `dotnet build` — sin warnings.
2. `dotnet test` — todo verde, incluidos los tests NUEVOS que escribiste
   (si no escribiste tests, el ejercicio no está terminado).
3. Los tests de arquitectura siguen pasando (no rompiste las capas).
4. Pruébalo de verdad: Swagger o Postman contra la API corriendo.
5. `git diff main` — ¿tocaste solo las capas que esperabas tocar?
