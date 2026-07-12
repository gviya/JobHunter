# 🌐 Referencia de la API

Base local: `http://localhost:5000` · Swagger interactivo: `/swagger` (solo Development)
Todas las rutas están versionadas: `/api/v{n}/...` (actual: `v1`).

## Autenticación

| Mecanismo | Cabecera | Se usa en |
|---|---|---|
| **JWT Bearer** | `Authorization: Bearer {token}` | Escrituras de proyectos y mediciones |
| **API Key** | `X-Api-Key: {clave}` | Endpoints internos `/ops` (máquina-a-máquina) |

Obtén el token con el usuario demo (lo crea el seeder en Development):

```http
POST /api/v1/auth/login
Content-Type: application/json

{ "email": "demo@renovables.demo", "password": "Demo1234!" }
```

**200 OK** → `{ "token": "eyJ...", "expiraEnUtc": "...", "email": "...", "nombre": "..." }`
**401** si las credenciales no son válidas (mismo mensaje exista o no el email).

---

## Proyectos

### `GET /api/v1/proyectos` — listado paginado *(público)*

Query params: `pagina` (≥1), `tamanoPagina` (1–100), `tipo`
(Solar|Eolica|Hidraulica|Biomasa|Geotermica), `soloActivos` (bool).

```json
{
  "items": [
    {
      "id": "guid", "nombre": "Parque Solar Madrid Norte",
      "tipo": "Solar", "estado": "Activo", "potenciaKW": 500.0,
      "latitud": 40.4168, "longitud": -3.7038, "createdAt": "2026-07-11T..."
    }
  ],
  "pageNumber": 1, "pageSize": 10, "totalCount": 3, "totalPages": 1,
  "hasPreviousPage": false, "hasNextPage": false
}
```

### `GET /api/v1/proyectos/{id}` — detalle *(público, cacheado 2 min)*

Incluye `mediciones`, `instalaciones` y el calculado `totalEnergiaGeneradaKWh`.
**404** si no existe.

### `POST /api/v1/proyectos` — crear *(JWT)*

```json
{
  "nombre": "Parque Solar Madrid Norte",
  "tipo": "Solar",
  "potenciaKW": 500.0,
  "coordenadas": { "latitud": 40.4168, "longitud": -3.7038 },
  "descripcion": "opcional"
}
```

**201 Created** + cabecera `Location` (nace en estado `Borrador`).
**400** validación (errores campo a campo en `errors`) · **409** nombre duplicado · **401** sin token.

### `PUT /api/v1/proyectos/{id}` — actualizar *(JWT)*

Mismo body sin `tipo` (la tecnología no es editable). **409** si el proyecto
está en estado terminal (Completado/Cancelado) o el nombre nuevo ya existe.

### `POST /api/v1/proyectos/{id}/activar` — transición Borrador → Activo *(JWT)*

Operación de NEGOCIO explícita (no un PATCH genérico de estado). **409** si no
está en Borrador. Levanta el evento `ProyectoActivado`.

### `DELETE /api/v1/proyectos/{id}` — eliminar *(JWT)*

**204 No Content**. Regla: solo `Borrador`/`Cancelado` se borran físicamente
(**409** en caso contrario — los proyectos que produjeron conservan histórico).

---

## Mediciones (recurso anidado)

### `GET /api/v1/proyectos/{proyectoId}/mediciones` *(público)*

Lista ordenada por fecha descendente. **404** si el proyecto no existe.

### `POST /api/v1/proyectos/{proyectoId}/mediciones` *(JWT)*

```json
{ "fechaMedicion": "2026-07-10T12:00:00Z", "energiaGeneradaKWh": 1240.5 }
```

**201** · **409** si el proyecto no está `Activo` (regla del agregado) ·
**400** si la fecha es futura o la energía negativa.

---

## Otros endpoints

| Ruta | Métod. | Auth | Descripción |
|---|---|---|---|
| `/api/v1/catalogo/tipos-energia` | GET | — | Enum de tipos (implementado con **Minimal APIs**) |
| `/api/v1/catalogo/estados-proyecto` | GET | — | Enum de estados del ciclo de vida |
| `/api/v1/ops/info` | GET | API Key | Info del despliegue (entorno, versión) |
| `/health` | GET | — | Estado completo (incluye BD) |
| `/health/ready` | GET | — | Readiness probe (Kubernetes) |
| `/health/live` | GET | — | Liveness probe (Kubernetes) |

---

## Formato de error (RFC 7807 — ProblemDetails)

Todos los errores comparten estructura:

```json
{
  "status": 400,
  "type": "ValidationFailure",
  "title": "Error de validación",
  "detail": "Se han producido uno o más errores de validación.",
  "errors": [
    { "propiedad": "PotenciaKW", "mensaje": "La potencia debe ser mayor que cero." }
  ]
}
```

| HTTP | Cuándo |
|---|---|
| 400 | Entrada inválida (FluentValidation) |
| 401 | Sin token o credenciales incorrectas |
| 403 | API Key inválida / sin permisos |
| 404 | Recurso inexistente |
| 409 | Conflicto de negocio (duplicado, transición inválida) |
| 422 | Invariante de dominio violada (DomainException no traducida) |
| 429 | Rate limiting superado |
| 500 | Error inesperado (detalle SOLO en logs, nunca al cliente) |

> La colección de Postman en `.postman/` trae todos estos requests con
> ejemplos y tests automáticos; el login guarda `{{token}}` solo.
