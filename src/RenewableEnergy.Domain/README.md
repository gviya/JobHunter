# 🎯 RenewableEnergy.Domain — El corazón

La capa MÁS IMPORTANTE y la más protegida: aquí vive el NEGOCIO en C# puro.
**No depende de nadie** (ni EF Core, ni ASP.NET, ni Application). Su único
paquete es `MediatR.Contracts` — solo interfaces, para que los eventos sean
publicables.

## Mapa de la capa

| Carpeta | Qué contiene | Empieza por |
|---|---|---|
| `Common/` | BaseEntity (Id + auditoría + eventos), AggregateRoot, IDomainEvent, DomainException | `BaseEntity.cs` |
| `Entities/` | El agregado **ProyectoRenovable** y sus hijas Medicion, Instalacion (+ Usuario) | `ProyectoRenovable.cs` ⭐ |
| `ValueObjects/` | Coordenadas, PotenciaKW, Email — conceptos auto-validados e inmutables | `Coordenadas.cs` |
| `Enums/` | TipoEnergia, EstadoProyecto (con las transiciones documentadas) | `EstadoProyecto.cs` |
| `DomainEvents/` | Hechos de negocio en pasado: ProyectoCreado, ProyectoActivado, MedicionRegistrada | `ProyectoCreado.cs` |
| `Specifications/` | Criterios de consulta con nombre, combinables con And/Or/Not | `Specification.cs` |
| `Interfaces/` | CONTRATOS de persistencia (IProyectoRepository, IUnitOfWork...) — sin EF | `IProyectoRepository.cs` |

## Las 5 reglas de esta capa

1. **Nada externo entra**: si necesitas un paquete de infraestructura aquí, el diseño está mal.
2. **Estado siempre válido**: constructores privados + factories `Create()` que validan.
3. **Todo cambio pasa por la raíz** del agregado: `proyecto.RegistrarMedicion(...)`,
   jamás `new Medicion(...)` desde fuera (la factory de Medicion es `internal`).
4. **Los hechos se anuncian**: los métodos de negocio levantan eventos de dominio;
   quién reacciona no es asunto de la entidad.
5. **Sin DataAnnotations**: el mapeo a BD se define con Fluent API en Infrastructure.

Los tests de `tests/RenewableEnergy.Domain.Tests` prueban esta capa sin un solo
mock — esa facilidad de testeo es el síntoma de que la capa está sana.
