# 🎤 Preguntas de entrevista respondidas con este repositorio

Las preguntas que más se repiten en procesos backend .NET, con la respuesta
corta que darías en voz alta y **el archivo del repo que la demuestra**. Úsalo
como repaso antes de una entrevista: cada respuesta la puedes defender
enseñando código tuyo.

---

## Arquitectura

**P: ¿Qué es Clean Architecture y en qué se diferencia de la arquitectura en capas clásica?**
R: Organizar el código para que las dependencias apunten hacia el dominio, no
hacia la base de datos. En capas clásicas el negocio depende del DAL; aquí la
persistencia IMPLEMENTA interfaces que define el dominio (inversión de
dependencias). Consecuencia práctica: el negocio se testea sin BD y la
infraestructura se puede cambiar.
📂 `docs/ARCHITECTURE.md` (diagrama) · `Domain/Interfaces/IProyectoRepository.cs` (contrato) vs `Infrastructure/.../ProyectoRepository.cs` (implementación).

**P: ¿Cómo evitas que el equipo rompa la arquitectura con el tiempo?**
R: Con tests de arquitectura: las reglas de capas son tests ejecutables que el
CI comprueba en cada PR. "Application no referencia EF", "los controllers no
tocan repositorios"... si alguien lo rompe, compila, pero el pipeline se pone rojo.
📂 `tests/RenewableEnergy.Architecture.Tests/ArchitectureTests.cs` (9 reglas con ArchUnitNET).

**P: ¿Dónde pones la lógica de negocio? ¿Y la validación?**
R: Reglas de negocio en las ENTIDADES (el proyecto decide si puede activarse);
orquestación en los handlers; validación de FORMA en FluentValidation antes del
handler; e invariantes otra vez en el dominio como última defensa. Duplicar
validación de entrada e invariantes es deliberado: sirven a propósitos distintos.
📂 `ProyectoRenovable.Activar()` · `CrearProyectoValidator.cs` · `docs/PATRONES.md` §6.

## CQRS y MediatR

**P: ¿Qué te aporta CQRS? ¿Y cuándo NO lo usarías?**
R: Separar escrituras (reglas, transacción) de lecturas (proyecciones,
AsNoTracking, caché) y una unidad natural de trabajo: un caso de uso = una
carpeta. No lo usaría en un CRUD trivial sin reglas: es ceremonia sin retorno.
📂 `Application/Features/Proyectos/` (compara `Commands/CrearProyecto/` con `Queries/ObtenerProyectos/`).

**P: ¿Cómo resuelves cross-cutting concerns sin repetir código en cada handler?**
R: Pipeline behaviors de MediatR: logging, métrica de rendimiento y validación
envuelven a TODOS los handlers. El orden de registro es el orden de ejecución.
📂 `Application/Common/Behaviors/` + el registro comentado en `Application/DependencyInjection.cs`.

**P: ¿Un handler puede llamar a otro handler?**
R: No — acoplamiento oculto y transacciones confusas. Si un caso de uso debe
provocar otro efecto, se hace con eventos de dominio. Aquí además lo prohíbe
un test de arquitectura (ningún handler puede inyectar ISender).
📂 `ArchitectureTests.Handlers_NoDebenInvocarOtrosHandlers`.

## DDD táctico

**P: ¿Qué es un agregado y por qué importa?**
R: Un grupo de entidades que cambian juntas con consistencia garantizada por
su RAÍZ: toda modificación entra por ella. Aquí `Medicion` ni siquiera puede
crearse desde fuera (factory `internal`): solo `proyecto.RegistrarMedicion(...)`,
que valida "solo proyectos activos miden".
📂 `Domain/Entities/ProyectoRenovable.cs` y `Medicion.cs`.

**P: ¿Value Object vs Entidad?**
R: La entidad tiene identidad (dos proyectos con iguales datos son distintos);
el VO es su valor (dos coordenadas iguales SON la misma). Los VOs se
auto-validan al construirse: un `Email` existente es siempre válido, se acabó
el `string email` que puede ser cualquier cosa.
📂 `Domain/ValueObjects/` + su persistencia en `ProyectoConfiguration.cs` (conversión y owned type).

**P: ¿Para qué sirven los eventos de dominio?**
R: Para desacoplar el hecho ("se creó un proyecto") de las reacciones (email,
publicar al bus). La entidad levanta el evento; añadir una reacción nueva es
añadir un handler, sin tocar el caso de uso original.
📂 `Domain/DomainEvents/ProyectoCreado.cs` → `Application/.../ProyectoCreadoEventHandler.cs`.

## Mensajería y consistencia

**P: Guardas en BD y publicas en RabbitMQ. ¿Qué puede salir mal y cómo lo resuelves?**
R: Que una de las dos operaciones falle y la otra no: inconsistencia
permanente. Se resuelve con el patrón OUTBOX: el evento se guarda como fila en
la misma transacción que los datos, y un worker lo publica después con
reintentos. La garantía resultante es at-least-once, así que los consumidores
deben ser idempotentes.
📂 `OutboxInterceptor.cs` (escritura) + `OutboxProcessor.cs` (publicación con Polly) + `docs/PATRONES.md` §4.

**P: ¿Diferencia entre evento de dominio y evento de integración?**
R: El de dominio es interno (viaja por MediatR, puede usar tipos del dominio,
se refactoriza libremente); el de integración es un contrato público entre
servicios (primitivos estables, se versiona con cuidado). Hay una traducción
explícita entre ambos.
📂 `ProyectoCreado.cs` vs `ProyectoCreadoIntegrationEvent.cs`, traducción en `ProyectoCreadoEventHandler`.

**P: ¿Por qué MassTransit en lugar de usar el cliente de RabbitMQ a pelo?**
R: Topología automática (exchange por tipo de mensaje), serialización,
reintentos y colas de error de serie, y abstracción del transporte: pasar de
RabbitMQ a Azure Service Bus cambia solo la configuración del bus, no los
publishers ni consumers.
📂 `Infrastructure/DependencyInjection.cs` → `AddMessaging` (con la alternativa ASB comentada).

## EF Core

**P: ¿DataAnnotations o Fluent API? ¿Por qué?**
R: Fluent API en Infrastructure: las anotaciones acoplan el dominio a la
persistencia y se quedan cortas (owned types, conversiones, backing fields,
índices filtrados).
📂 `Infrastructure/Persistence/Configurations/ProyectoConfiguration.cs` (tiene de todo: conversiones, owned type, backing fields, índice único).

**P: ¿Un problema real que hayas depurado con EF Core?**
R: `DbUpdateConcurrencyException: expected 1 row, affected 0` al añadir una
hija a un agregado ya trackeado. Causa: el Guid lo genera el cliente y EF, al
descubrir la entidad por navegación, asumía que era existente y emitía UPDATE.
Solución: `ValueGeneratedNever()` en las claves — el dominio es quien genera
sus Ids.
📂 Comentario completo en `MedicionConfiguration.cs`. (Historia real de este repo: commit `f0e786e`.)

**P: ¿Cómo pruebas el acceso a datos?**
R: En tres niveles: unitario con mocks del repositorio (rápido), E2E con SQLite
en memoria (relacional real sin Docker) y de integración con Testcontainers
(SQL Server auténtico, valida hasta las migraciones). InMemory provider no:
acepta cosas que SQL rechaza.
📂 `ApiTestFactory.cs` (SQLite) · `SqlServerContainerFixture.cs` (Testcontainers).

## API y seguridad

**P: ¿Cómo gestionas los errores en una API?**
R: Un middleware global traduce cada excepción a ProblemDetails (RFC 7807) con
código HTTP consistente, y los fallos de negocio esperables viajan como Result
sin excepciones. El cliente siempre recibe el mismo formato; el stack trace
jamás sale del servidor.
📂 `ExceptionHandlingMiddleware.cs` · `ResultExtensions.ToProblem()` · `docs/API.md` (tabla de códigos).

**P: ¿JWT o API Key?**
R: JWT para usuarios (identidad rica en claims, expira, se emite tras login);
API Key para máquina-a-máquina donde no hay usuario que loguearse. Este repo
tiene ambos conviviendo.
📂 `JwtTokenGenerator.cs` + `AddAutenticacionJwt` · `ApiKeyAuthFilter.cs` + `OpsController`.

**P: ¿Dónde guardas los secretos?**
R: Nunca en el repositorio. En local, variables de entorno (.env fuera de git);
en producción, Azure Key Vault cargado como fuente de configuración al
arrancar, con Managed Identity (sin credenciales en ningún sitio).
📂 `Program.cs` (bloque Key Vault) · `.env.example` · `k8s/deployment.yaml` (secretKeyRef).

**P: ¿Qué harías distinto en tu plantilla si fuera a producción real?**
(Pregunta trampa: buscan autocrítica.) R: El PasswordHasher SHA-256 es
didáctico — usaría Identity/Argon2; añadiría refresh tokens; el outbox
necesitaría limpieza de mensajes procesados; y evaluaría los health checks de
RabbitMQ/Redis además del de BD. Saber los límites de tu propio código es señal
de seniority.
📂 Los avisos están escritos en el propio código: `PasswordHasher.cs`, `docs/EJERCICIOS.md` §4.1.

## Testing

**P: ¿Cómo estructuras los tests de un proyecto así?**
R: Pirámide de cuatro niveles: dominio (puro, sin mocks), casos de uso (Moq +
builders), arquitectura (ArchUnitNET) y E2E (WebApplicationFactory), más
integración real con Testcontainers donde hay Docker. Cobertura >80 % en
Domain y Application, que es donde vive el negocio.
📂 La carpeta `tests/` entera; empieza por `ProyectoRenovableTests.cs`.

**P: ¿Cómo evitas que los tests se rompan en cascada cuando cambia un constructor?**
R: Patrón Builder para los objetos de prueba: defaults razonables + API fluida
para personalizar solo lo relevante. Un cambio de firma se absorbe en una clase.
📂 `tests/.../Builders/ProyectoRenovableBuilder.cs`.

## DevOps

**P: ¿Qué es un Dockerfile multi-stage y por qué se usa?**
R: Compilar con la imagen SDK (~1 GB) y copiar solo el publish a la de runtime
(~220 MB): imagen final menor y sin compiladores (menos superficie de ataque).
Además, copiar primero los csproj y hacer restore aparte aprovecha la caché de
capas de Docker.
📂 `docker/Dockerfile` (comentado paso a paso).

**P: ¿Liveness vs readiness probe?**
R: Liveness = "¿el proceso está vivo?" → si falla, Kubernetes REINICIA el pod.
Readiness = "¿puede atender tráfico?" (incluye dependencias como la BD) → si
falla, lo SACA del balanceador sin reiniciarlo. Confundirlas provoca reinicios
en cascada cuando la BD parpadea.
📂 `k8s/deployment.yaml` · endpoints en `WebApplicationExtensions.MapHealthEndpoints`.

**P: ¿Cómo aplicas las migraciones en producción?**
R: Desde el pipeline, como paso explícito y auditable — nunca en el arranque
del pod (varios pods arrancando a la vez migrarían en carrera). En Development
sí se aplican al arrancar, por comodidad.
📂 `WebApplicationExtensions.ApplyMigrationsAndSeedAsync` (gate por entorno) · `docs/ARCHITECTURE.md` (tabla de decisiones).

---

> 💡 **Cómo usar este documento**: no memorices las respuestas. Abre el archivo
> referenciado, entiende el código y REESCRIBE la respuesta con tus palabras.
> En la entrevista, poder decir "esto lo tengo implementado en mi plantilla,
> te lo enseño" vale por diez respuestas teóricas.
