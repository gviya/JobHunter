# 📏 Convenciones del proyecto

Reglas que TODO el código de esta plantilla cumple. Muchas las refuerzan el
`.editorconfig` y los tests de arquitectura; el resto son disciplina de review.

---

## Estilo C#

```csharp
// ✅ PRIMARY CONSTRUCTORS en handlers y servicios
internal sealed class CrearProyectoHandler(
    IProyectoRepository proyectoRepository,
    IUnitOfWork unitOfWork) : IRequestHandler<...>

// ❌ constructor clásico con campos _privados (boilerplate)

// ✅ COLLECTION EXPRESSIONS
private readonly List<IDomainEvent> _domainEvents = [];
List<ProyectoDto> dtos = [];

// ❌ new List<T>(), Array.Empty<T>(), Enumerable.Empty<T>()

// ✅ var SIEMPRE
var proyecto = await repository.GetByIdAsync(id);

// ✅ NAMESPACES DE BLOQUE (con llaves) — convención de ESTA plantilla
namespace RenewableEnergy.Domain.Entities
{
    public sealed class ProyectoRenovable : AggregateRoot { ... }
}

// ✅ sealed por defecto en clases que no se diseñaron para herencia
// ✅ records para Commands, Queries, DTOs, ViewModels y eventos (inmutables)
// ✅ switch expressions y pattern matching sobre if/else en cadena
```

## Nombrado

| Elemento | Patrón | Ejemplo |
|---|---|---|
| Command | `{Accion}{Entidad}Command` | `CrearProyectoCommand` |
| Query | `{Accion}{Entidad}Query` | `ObtenerProyectosQuery` |
| Handler | `{Accion}{Entidad}Handler` | `CrearProyectoHandler` |
| Validator | `{Accion}{Entidad}Validator` | `CrearProyectoValidator` |
| Respuesta de command | `{Accion}{Entidad}Response` | `CrearProyectoResponse` |
| ViewModel de query | `{Entidad}{Vista}ViewModel` | `ProyectoDetalleViewModel`, `ProyectoListadoViewModel` |
| DTO de entrada HTTP | `{Accion}{Entidad}Request` | `CrearProyectoRequest` (en API/Dto) |
| Evento de dominio | Hecho en PASADO | `ProyectoCreado`, `MedicionRegistrada` |
| Evento de integración | `...IntegrationEvent` | `ProyectoCreadoIntegrationEvent` |
| Specification | `{Criterio}Spec` | `ProyectosActivosSpec` |
| Test | `Metodo_Escenario_ResultadoEsperado` | `Activar_CuandoYaEstaActivo_DeberiaLanzarDomainException` |
| Carpeta de feature | `Features/{Entidad}/{Commands\|Queries}/{CasoDeUso}/` | `Features/Proyectos/Commands/CrearProyecto/` |

## Qué va en cada capa

| Necesito... | Capa | Carpeta |
|---|---|---|
| Una regla de negocio | **Domain** | La entidad/agregado correspondiente |
| Un concepto medible con validación (email, potencia...) | **Domain** | `ValueObjects/` |
| Un criterio de consulta con nombre | **Domain** | `Specifications/` |
| Un caso de uso nuevo | **Application** | `Features/...` (ver GUIA_NUEVAS_FEATURES.md) |
| Hablar con algo externo (email, bus, reloj...) | Interfaz en **Application**, implementación en **Infrastructure** |
| SQL, Include, índices, conversiones | **Infrastructure** | `Persistence/` |
| Un endpoint | **API** | Controller fino + DTO en `Dto/` |

## Prohibiciones (las vigilan los tests de arquitectura)

- ❌ **FluentAssertions** → usamos **AwesomeAssertions** (fork open source, misma API).
- ❌ `Version="..."` en un `.csproj` → todas las versiones en `Directory.Packages.props`.
- ❌ DataAnnotations (`[Required]`, `[MaxLength]`) en entidades → Fluent API en Infrastructure.
- ❌ Devolver entidades de dominio desde la API → siempre DTOs/ViewModels.
- ❌ `using Microsoft.EntityFrameworkCore` en Application → repositorios.
- ❌ Controllers llamando a repositorios → todo pasa por MediatR.
- ❌ Un handler inyectando `ISender`/`IMediator` (encadenar handlers) → eventos de dominio.
- ❌ Lógica de negocio en controllers o en handlers → al dominio.
- ❌ Secretos en appsettings.json del repo → variables de entorno / Key Vault.
- ❌ File-scoped namespaces → esta plantilla usa namespaces de bloque.

## Tests

- Patrón **AAA** (Arrange / Act / Assert), separado por líneas en blanco.
- **AwesomeAssertions**: `result.IsSuccess.Should().BeTrue();` — nunca `Assert.True(...)`.
- Objetos de prueba con **Builder** (`ProyectoRenovableBuilder`), no copies `Create(...)` gigantes.
- Un test = un comportamiento. Si el nombre lleva "y", son dos tests.
- Integración marcada con `[Trait("Category", "Integration")]` + `[DockerRequiredFact]`.

## Comentarios y documentación

- XML doc `///` en español en toda clase/método público explicando **qué es en
  el contexto de la arquitectura y por qué existe** — no lo que ya dice la firma.
- Comentarios de línea solo para el PORQUÉ no evidente (trade-offs, trampas).
- Cada capa tiene su `README.md`; los docs transversales viven en `docs/`.

## Git

- Mensajes estilo *conventional commits*: `feat:`, `fix:`, `test:`, `docs:`, `refactor:`.
- El cuerpo explica el porqué. Español o inglés, pero consistente.
- Nunca se commitea: `.env`, `bin/`, `obj/`, secretos, `.vs/`.
