# 🧭 Guía: cómo añadir una feature nueva

Esta es la guía **más importante** del repositorio. Recorre los 10 pasos para añadir
un caso de uso completo usando una feature REAL que ya está implementada:
**"Registrar Medición"** (`POST /api/v1/proyectos/{id}/mediciones`).

Cada paso enlaza el archivo real del repositorio: ábrelo al lado mientras lees.
Cuando tengas que crear tu propia feature, copia esta receta cambiando los nombres.

> 🔑 **Regla mental**: las features se construyen **de dentro hacia fuera**.
> Primero el dominio (las reglas), luego el caso de uso, luego la persistencia
> y al final el HTTP. Nunca al revés.

---

## PASO 1 — El dominio: la regla de negocio vive en el agregado

**Archivo:** `src/RenewableEnergy.Domain/Entities/ProyectoRenovable.cs`

Antes de escribir ningún command, pregúntate: *¿qué regla de negocio hay aquí?*
En nuestro caso: "solo los proyectos **Activos** admiten mediciones" y "no se
puede medir el futuro". Esas reglas pertenecen al AGREGADO, no al handler:

```csharp
public Medicion RegistrarMedicion(DateTime fechaMedicion, decimal energiaGeneradaKWh)
{
    if (Estado != EstadoProyecto.Activo)
    {
        throw new DomainException(
            $"Solo se pueden registrar mediciones en proyectos Activos. Estado actual: {Estado}.");
    }

    if (fechaMedicion > DateTime.UtcNow)
    {
        throw new DomainException("La fecha de la medición no puede ser futura.");
    }

    var medicion = Medicion.Create(Id, fechaMedicion, energiaGeneradaKWh);
    _mediciones.Add(medicion);

    RaiseDomainEvent(new MedicionRegistrada(Id, medicion.Id, medicion.EnergiaGeneradaKWh, DateTime.UtcNow));

    return medicion;
}
```

Detalles a copiar:
- La entidad hija (`Medicion`) tiene factory **`internal`**: nadie puede crear
  mediciones saltándose el agregado (ni siquiera compila desde Application).
- El método **levanta un evento de dominio** (`MedicionRegistrada`): otras
  partes del sistema reaccionarán sin acoplarse a este código.

Si tu feature necesita un evento nuevo, créalo en `Domain/DomainEvents/` como
`record` inmutable que implementa `IDomainEvent`.

---

## PASO 2 — El Command: un record inmutable con los datos de entrada

**Archivo:** `src/RenewableEnergy.Application/Features/Mediciones/Commands/RegistrarMedicion/RegistrarMedicionCommand.cs`

```csharp
public sealed record RegistrarMedicionCommand(
    Guid ProyectoId,
    DateTime FechaMedicion,
    decimal EnergiaGeneradaKWh) : IRequest<Result<RegistrarMedicionResponse>>;
```

Convenciones:
- Carpeta por caso de uso: `Features/{Entidad}/Commands/{Accion}{Entidad}/`.
- Nombre: `{Accion}{Entidad}Command`.
- Solo **primitivos y enums** (es un mensaje, no arrastra entidades).
- El tipo de retorno (`Result<...>`) declara en la firma que puede fallar.

---

## PASO 3 — El Validator: FluentValidation valida la FORMA de la petición

**Archivo:** `.../RegistrarMedicion/RegistrarMedicionValidator.cs`

```csharp
public sealed class RegistrarMedicionValidator : AbstractValidator<RegistrarMedicionCommand>
{
    public RegistrarMedicionValidator()
    {
        RuleFor(c => c.ProyectoId)
            .NotEmpty().WithMessage("El identificador del proyecto es obligatorio.");

        RuleFor(c => c.FechaMedicion)
            .NotEmpty().WithMessage("La fecha de la medición es obligatoria.")
            .LessThanOrEqualTo(_ => DateTime.UtcNow)
            .WithMessage("La fecha de la medición no puede ser futura.");

        RuleFor(c => c.EnergiaGeneradaKWh)
            .GreaterThanOrEqualTo(0).WithMessage("La energía generada no puede ser negativa.");
    }
}
```

**No hay que registrarlo en ningún sitio**: `AddValidatorsFromAssembly` lo
descubre y el `ValidationBehavior` del pipeline lo ejecuta ANTES del handler.
Si falla, el cliente recibe un **400** con la lista de errores y el handler
nunca se ejecuta.

> ¿Duplica la validación del dominio (paso 1)? Sí, y es intencionado: el
> validator produce un 400 amable con todos los errores juntos; el dominio es
> la última línea de defensa de sus invariantes. Ver `docs/PATRONES.md`.

---

## PASO 4 — El Handler: la orquestación del caso de uso

**Archivo:** `.../RegistrarMedicion/RegistrarMedicionHandler.cs`

```csharp
internal sealed class RegistrarMedicionHandler(
    IProyectoRepository proyectoRepository,
    IUnitOfWork unitOfWork,
    IDistributedCache cache,
    IMapper mapper,
    ILogger<RegistrarMedicionHandler> logger)
    : IRequestHandler<RegistrarMedicionCommand, Result<RegistrarMedicionResponse>>
{
    public async Task<Result<RegistrarMedicionResponse>> Handle(
        RegistrarMedicionCommand request, CancellationToken cancellationToken)
    {
        var proyecto = await proyectoRepository.GetByIdAsync(request.ProyectoId, cancellationToken);

        if (proyecto is null)
        {
            return Result.Failure<RegistrarMedicionResponse>(Error.NotFound(
                "Proyecto.NoEncontrado", $"No existe ningún proyecto con id '{request.ProyectoId}'."));
        }

        Domain.Entities.Medicion medicion;

        try
        {
            medicion = proyecto.RegistrarMedicion(request.FechaMedicion, request.EnergiaGeneradaKWh);
        }
        catch (DomainException ex)
        {
            return Result.Failure<RegistrarMedicionResponse>(
                Error.Conflict("Medicion.OperacionInvalida", ex.Message));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync(CacheKeys.ProyectoDetalle(proyecto.Id), cancellationToken);

        return mapper.Map<RegistrarMedicionResponse>(medicion);
    }
}
```

Fíjate en lo que hace y lo que NO hace:

| Hace | No hace |
|---|---|
| Cargar el agregado por el repositorio | Validar la entrada (paso 3 ya lo hizo) |
| Delegar la regla en el dominio | Contener reglas de negocio |
| Traducir `DomainException` → `Result.Conflict` | Conocer EF Core ni SQL |
| UN `SaveChangesAsync` al final | Devolver códigos HTTP |
| Invalidar la caché afectada | Llamar a otros handlers |

El DTO de salida (`RegistrarMedicionResponse`) es un `record` en el mismo
directorio, y su mapeo se añade en el **paso 8**.

---

## PASO 5 — El contrato del repositorio (si necesitas un método nuevo)

**Archivo:** `src/RenewableEnergy.Domain/Interfaces/IProyectoRepository.cs`

Para esta feature bastó `GetByIdAsync`, que ya existía. Si tu caso de uso
necesita una consulta nueva, **la interfaz se toca primero** (en Domain, en
términos de negocio, sin nada de EF):

```csharp
/// <summary>Busca un proyecto por Id. Null si no existe.</summary>
Task<ProyectoRenovable?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
```

Para las lecturas del lado query se creó además un repositorio de solo
lectura: `IMedicionRepository.GetByProyectoAsync(...)` (lo usa la query
`ObtenerMediciones`).

---

## PASO 6 — La implementación del repositorio (Infrastructure)

**Archivo:** `src/RenewableEnergy.Infrastructure/Persistence/Repositories/MedicionRepository.cs`

Aquí SÍ vive EF Core:

```csharp
internal sealed class MedicionRepository(ApplicationDbContext context) : IMedicionRepository
{
    public async Task<IReadOnlyList<Medicion>> GetByProyectoAsync(
        Guid proyectoId, CancellationToken cancellationToken = default)
    {
        return await context.Mediciones
            .AsNoTracking()                                    // lectura pura: sin tracking
            .Where(m => m.ProyectoRenovableId == proyectoId)
            .OrderByDescending(m => m.FechaMedicion)
            .ToListAsync(cancellationToken);
    }
}
```

Y se registra en `Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<IMedicionRepository, MedicionRepository>();
```

Si tu entidad es nueva, añade también su configuración Fluent API en
`Persistence/Configurations/` (tabla, precisión de decimales, índices,
conversiones de value objects...). Mira `MedicionConfiguration.cs` de modelo.

---

## PASO 7 — El endpoint: controller fino que traduce HTTP ⇄ MediatR

**Archivo:** `src/RenewableEnergy.API/Controllers/MedicionesController.cs`

```csharp
[HttpPost]
[Authorize]
public async Task<IActionResult> RegistrarMedicion(
    Guid proyectoId,
    [FromBody] RegistrarMedicionRequest request,
    CancellationToken cancellationToken)
{
    var command = new RegistrarMedicionCommand(
        proyectoId, request.FechaMedicion, request.EnergiaGeneradaKWh);

    var result = await mediator.Send(command, cancellationToken);

    if (result.IsFailure)
    {
        return result.ToProblem();   // NotFound→404, Conflict→409...
    }

    return CreatedAtAction(nameof(ObtenerMediciones), new { proyectoId, version = "1" }, result.Value);
}
```

- El DTO de entrada (`RegistrarMedicionRequest`) vive en `API/Dto/` — el
  contrato HTTP evoluciona sin tocar Application.
- El `proyectoId` viene de la RUTA (recurso anidado REST) y el resto del body.
- `result.ToProblem()` (en `API/Extensions/ResultExtensions.cs`) traduce el
  `ErrorType` a código HTTP en un único sitio.

---

## PASO 8 — Mapeo y Swagger

**Archivo:** `src/RenewableEnergy.Application/Mappings/ProyectoMappingProfile.cs`

Añade el mapeo Entity → DTO al perfil:

```csharp
CreateMap<Medicion, RegistrarMedicionResponse>()
    .ForMember(d => d.ProyectoId, o => o.MapFrom(s => s.ProyectoRenovableId));
```

El test `ProyectoMappingProfileTests.Configuracion_DeberiaSerValida` fallará
si olvidas mapear algún campo (por eso existe).

**Swagger** se actualiza SOLO: al arrancar en Development, el endpoint aparece
en `http://localhost:5000/swagger` con sus tipos de request/response. Los
atributos `[ProducesResponseType]` del controller documentan los códigos de
error posibles.

---

## PASO 9 — Tests (los cuatro niveles)

| Nivel | Archivo real | Qué cubre |
|---|---|---|
| Dominio | `tests/RenewableEnergy.Domain.Tests/Entities/ProyectoRenovableTests.cs` | `RegistrarMedicion` en activo/borrador/fecha futura + evento |
| Caso de uso | `tests/RenewableEnergy.Application.Tests/Features/Mediciones/RegistrarMedicionHandlerTests.cs` | Flujo feliz, 404, DomainException→Conflict (con Moq) |
| Integración | `tests/RenewableEnergy.Infrastructure.Tests/Repositories/ProyectoRepositoryTests.cs` | Roundtrip real contra SQL Server (Testcontainers) |
| E2E | `tests/RenewableEnergy.API.Tests/Controllers/ProyectosControllerTests.cs` (`FlujoCompleto_...`) | HTTP real: crear → activar → medir → consultar |

Ejemplo del test de handler (patrón AAA + Builder):

```csharp
[Fact]
public async Task Handle_EnProyectoBorrador_DeberiaDevolverConflict()
{
    var proyecto = new ProyectoRenovableBuilder().Build();   // SIN activar

    _repositorioMock
        .Setup(r => r.GetByIdAsync(proyecto.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(proyecto);

    var result = await _handler.Handle(
        new RegistrarMedicionCommand(proyecto.Id, DateTime.UtcNow.AddHours(-1), 100m),
        CancellationToken.None);

    result.IsFailure.Should().BeTrue();
    result.Error!.Tipo.Should().Be(ErrorType.Conflict);
}
```

---

## PASO 10 — Migración (solo si cambió el modelo de datos)

Registrar Medición no cambió el esquema (la tabla `Mediciones` ya existía en
`InitialCreate`). Si TU feature añade entidades o columnas:

```bash
# 1. Genera la migración (revisa SIEMPRE el archivo generado antes de aplicarla)
dotnet dotnet-ef migrations add NombreDescriptivo \
  -p src/RenewableEnergy.Infrastructure \
  -s src/RenewableEnergy.Infrastructure \
  --output-dir Persistence/Migrations

# 2. Aplícala a tu BD local (docker compose debe estar arrancado)
dotnet dotnet-ef database update \
  -p src/RenewableEnergy.Infrastructure \
  -s src/RenewableEnergy.Infrastructure
```

En Development la API también aplica las migraciones pendientes al arrancar.
En producción las aplica el pipeline de CI/CD, nunca el pod.

---

## ✅ Checklist final de la feature

- [ ] La regla de negocio está en el DOMINIO (no en el handler).
- [ ] Command/Validator/Handler/Response en su carpeta de `Features/`.
- [ ] El handler devuelve `Result` y no conoce HTTP ni EF.
- [ ] Repositorio: interfaz en Domain, implementación en Infrastructure.
- [ ] Controller fino: construir command → `Send` → `ToProblem()`/`Ok`.
- [ ] Mapeo añadido al Profile (el test de configuración lo verifica).
- [ ] Tests en los 4 niveles (dominio, handler, integración, E2E).
- [ ] Migración generada y revisada si cambió el esquema.
- [ ] `dotnet build` sin warnings y `dotnet test` en verde.
- [ ] Petición añadida a la colección de Postman (`.postman/`).
