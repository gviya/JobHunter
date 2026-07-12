# 🌐 RenewableEnergy.API — La capa de presentación

Traduce HTTP ⇄ casos de uso y compone la aplicación (es la *composition root*:
el único proyecto que referencia Application e Infrastructure a la vez).

## Mapa de la capa

| Carpeta | Qué contiene |
|---|---|
| `Controllers/` | Proyectos, Mediciones (recurso anidado), Auth (JWT), Ops (API Key). **Finos**: construir command → `Send` → traducir Result |
| `Dto/` | Contratos de ENTRADA HTTP (requests) — separados de los commands |
| `Middleware/` | ExceptionHandlingMiddleware (errores → ProblemDetails) y RequestLoggingMiddleware |
| `Extensions/` | `AddApi` (JWT, Swagger, versionado, rate limiting, OTel), `UseApi` (el pipeline en orden), `ResultExtensions.ToProblem()` |
| `Filters/` | ApiKeyAuthFilter (autenticación máquina-a-máquina) |
| `Configuration/` | RateLimitSettings (patrón Options) |
| `Program.cs` | Arranque: Serilog → Key Vault → DI por capas → pipeline |

## El pipeline HTTP (orden y porqué)

```
ExceptionHandling → RequestLogging → HTTPS → RateLimiter → AuthN → AuthZ → Endpoints
```

ExceptionHandling primero (captura los errores de todo lo demás);
Authentication antes que Authorization (saber quién eres antes de decidir qué puedes).

## Dos estilos de endpoint, a propósito

- **Controllers** (`ProyectosController`): el estilo clásico que piden las ofertas.
- **Minimal APIs** (`/api/v1/catalogo/*`, en `WebApplicationExtensions`): la
  alternativa moderna para endpoints simples. Compáralos.

## Seguridad

| Mecanismo | Uso |
|---|---|
| JWT Bearer (`[Authorize]`) | Escrituras. Token en `POST /api/v1/auth/login` (demo@renovables.demo / Demo1234!) |
| API Key (`X-Api-Key`) | Endpoints internos `/ops` |
| Rate limiting | Cupo por usuario/IP, HTTP 429 al superarlo |

## Reglas de esta capa

- CERO lógica de negocio (lo vigila un test de arquitectura: los controllers
  no pueden tocar repositorios).
- Nunca se devuelven entidades de dominio: siempre DTOs/ViewModels.
- Errores SIEMPRE como ProblemDetails (RFC 7807) — ver `docs/API.md`.
