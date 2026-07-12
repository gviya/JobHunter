# 🏋️ Ejercicios guiados

La mejor forma de interiorizar esta arquitectura es **modificarla**. Estos
ejercicios están ordenados de menor a mayor dificultad; cada uno indica qué
concepto entrena, qué archivos tocarás y pistas concretas. Algunos son huecos
dejados A PROPÓSITO en la plantilla para que los completes tú.

La red de seguridad ya existe: si rompes una regla de capas, los tests de
`Architecture.Tests` te avisarán; si olvidas un mapeo, fallará
`ProyectoMappingProfileTests`. Trabaja en una rama (`git checkout -b ejercicio-1`)
y compara con `main` al terminar.

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

## ¿Cómo sé que lo hice bien?

Para cada ejercicio, en este orden:

1. `dotnet build` — sin warnings.
2. `dotnet test` — todo verde, incluidos los tests NUEVOS que escribiste
   (si no escribiste tests, el ejercicio no está terminado).
3. Los tests de arquitectura siguen pasando (no rompiste las capas).
4. Pruébalo de verdad: Swagger o Postman contra la API corriendo.
5. `git diff main` — ¿tocaste solo las capas que esperabas tocar?
