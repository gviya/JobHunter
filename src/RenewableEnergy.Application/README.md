# ⚙️ RenewableEnergy.Application — Los casos de uso

Orquesta QUÉ hace la aplicación (crear proyecto, registrar medición...) sin
saber CÓMO se persiste ni desde dónde se invoca. Depende solo de Domain +
librerías de orquestación (MediatR, FluentValidation, AutoMapper).

## Mapa de la capa

| Carpeta | Qué contiene |
|---|---|
| `Features/{Entidad}/Commands/{CasoDeUso}/` | Command + Validator + Handler + Response — **una carpeta por caso de uso** (vertical slice) |
| `Features/{Entidad}/Queries/{CasoDeUso}/` | Query + Handler + ViewModel |
| `Features/.../EventHandlers/` | Reacciones a eventos de dominio (email, integración) |
| `Common/Behaviors/` | Pipeline de MediatR: Logging → Performance → Validation |
| `Common/Models/` | `Result`/`Error` (fallos sin excepciones) y `PaginatedList` |
| `Common/Exceptions/` | ValidationException, NotFoundException, ForbiddenException |
| `Interfaces/` | Contratos de servicios externos: ICurrentUserService, IEmailService, IEventPublisher, IPasswordHasher... (los implementa Infrastructure) |
| `IntegrationEvents/` | Contratos PÚBLICOS que viajan por el bus |
| `Mappings/` | Perfil de AutoMapper Entity → DTO/ViewModel |

## Anatomía de un caso de uso (síguela siempre)

```
Features/Proyectos/Commands/CrearProyecto/
├── CrearProyectoCommand.cs     ← record inmutable con la entrada
├── CrearProyectoValidator.cs   ← reglas de FORMA (FluentValidation)
├── CrearProyectoHandler.cs     ← orquestación (internal sealed)
└── CrearProyectoResponse.cs    ← DTO de salida
```

## Reglas de esta capa

- Los handlers **no contienen reglas de negocio** (van al dominio) ni SQL (va a Infrastructure).
- Un handler **jamás llama a otro handler** (nada de `ISender` aquí): para
  encadenar reacciones existen los eventos de dominio. Lo vigila un test de arquitectura.
- Fallos esperables → `Result.Failure`; el detalle en `docs/PATRONES.md` (§6).
- `using Microsoft.EntityFrameworkCore` aquí = test de arquitectura en rojo.

La guía paso a paso para añadir un caso de uso: `docs/GUIA_NUEVAS_FEATURES.md`.
