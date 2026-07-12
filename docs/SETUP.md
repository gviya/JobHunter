# 🚀 Guía de inicio (SETUP)

## Requisitos previos

| Herramienta | Versión | Para qué |
|---|---|---|
| **.NET SDK** | 10.0.x (la fija `global.json`) | Compilar y ejecutar |
| **Docker Desktop** | Actual | SQL Server, RabbitMQ, Redis, Seq y tests de integración |
| **IDE** | VS 2026 / Rider / VS Code | Opcional pero recomendable |

> Sin Docker también puedes trabajar: la API arranca (sin BD fallará el seed,
> pero Swagger funciona) y `dotnet test` pasa igualmente — los tests de
> integración se OMITEN automáticamente cuando no detectan Docker.

---

## Opción A — Script automático (recomendada)

```powershell
# Windows (PowerShell, desde la raíz del repo)
.\scripts\setup.ps1
```

```bash
# Linux / Mac
./scripts/setup.sh
```

El script comprueba requisitos, levanta la infraestructura con Docker, compila,
aplica las migraciones (con reintentos mientras SQL Server arranca) y ejecuta
los tests. Al terminar te dice cómo arrancar la API.

---

## Opción B — Docker Compose completo (API incluida)

```bash
# 1. (Opcional) copia y ajusta las variables de entorno
cp .env.example .env

# 2. Levanta TODO: API + SQL Server + PostgreSQL + RabbitMQ + Redis + Seq
docker compose -f docker/docker-compose.yml up -d

# 3. Abre Swagger
#    http://localhost:5000/swagger
```

La API en el contenedor corre en Development: aplica migraciones y siembra los
datos demo automáticamente. Servicios incluidos:

| Servicio | URL | Credenciales |
|---|---|---|
| API + Swagger | http://localhost:5000/swagger | — |
| Seq (logs) | http://localhost:5341 | — |
| RabbitMQ UI | http://localhost:15672 | guest / guest |
| SQL Server | localhost,1433 | sa / Dev_Password123! |
| PostgreSQL | localhost:5432 | postgres / postgres |
| Redis | localhost:6379 | — |

---

## Opción C — Manual, paso a paso

```bash
# 1. Levanta SOLO la infraestructura (sin la API)
docker compose -f docker/docker-compose.yml up -d sqlserver rabbitmq redis seq

# 2. Aplica las migraciones (dotnet-ef es tool local: está en dotnet-tools.json)
dotnet tool restore
dotnet dotnet-ef database update -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure

# 3. Arranca la API desde tu IDE o con:
dotnet run --project src/RenewableEnergy.API

# 4. Swagger: http://localhost:5000/swagger
```

**Primer request:** haz login en `POST /api/v1/auth/login` con el usuario demo
(`demo@renovables.demo` / `Demo1234!`), copia el token y púlsalo en el botón
*Authorize* de Swagger. O importa la colección de `.postman/` (guarda el token sola).

---

## Ejecutar los tests

```bash
dotnet test                                        # todos (integración se omite sin Docker)
dotnet test --filter "Category!=Integration"       # excluir integración explícitamente
dotnet test --filter "Category=Integration"        # SOLO integración (requiere Docker)
dotnet test --collect:"XPlat Code Coverage"        # con informe de cobertura
```

| Proyecto | Qué prueba | Necesita |
|---|---|---|
| Domain.Tests | Reglas del agregado, VOs, Specifications | Nada |
| Application.Tests | Handlers (Moq), validators, mapeos | Nada |
| Architecture.Tests | Dependencias entre capas (ArchUnitNET) | Nada |
| API.Tests | HTTP E2E con SQLite en memoria | Nada |
| Infrastructure.Tests | Repos contra SQL Server real | **Docker** |

---

## Migraciones de EF Core

```bash
# Crear una migración nueva (revisa SIEMPRE el archivo generado)
dotnet dotnet-ef migrations add MiCambio -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure --output-dir Persistence/Migrations

# Aplicarla
dotnet dotnet-ef database update -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure

# Deshacer la última (si no está aplicada)
dotnet dotnet-ef migrations remove -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure
```

No hace falta que la API esté configurada: `ApplicationDbContextFactory` da a
`dotnet-ef` un contexto mínimo de tiempo de diseño.

---

## Cambiar a PostgreSQL (proveedor alternativo)

1. En `src/RenewableEnergy.Infrastructure/DependencyInjection.cs`, comenta
   `UseSqlServer` y descomenta `UseNpgsql` (el paquete ya está referenciado).
2. En `ApplicationDbContextFactory.cs`, cambia también a `UseNpgsql`.
3. **Regenera las migraciones** (las de SQL Server no sirven para PostgreSQL):
   ```bash
   rm -r src/RenewableEnergy.Infrastructure/Persistence/Migrations
   dotnet dotnet-ef migrations add InitialCreate -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure --output-dir Persistence/Migrations
   dotnet dotnet-ef database update -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure
   ```
4. El compose ya incluye PostgreSQL; la cadena está en `appsettings.Development.json` ("PostgreSql").

---

## Problemas frecuentes

| Síntoma | Causa probable | Solución |
|---|---|---|
| `Login failed for user 'sa'` al arrancar | SQL Server aún inicializándose | Espera ~30 s (healthcheck) y reintenta |
| Migraciones no se aplican en la API | No estás en Development | Solo Development migra al arrancar; usa `dotnet-ef database update` |
| Tests de integración fallan en local | Docker parado | Arranca Docker Desktop (o se omiten solos) |
| 401 en Swagger | Falta el token | Login → botón Authorize → pegar token |
| `Jwt:Secret debe tener al menos 32...` | Falta el secreto | En dev viene en appsettings.Development.json; si usas otro entorno, exporta `Jwt__Secret` |
