# 🏛️ RenewableEnergy — Plantilla de Clean Architecture en .NET 10

Plantilla **profesional y educativa** de Clean Architecture con el dominio de
energías renovables. Doble propósito:

1. **Aprender y consultar**: cada archivo lleva comentarios XML en español que
   explican *qué es, por qué existe y cómo funciona* dentro de la arquitectura.
2. **Portafolio**: demuestra el stack que piden las ofertas backend .NET —
   CQRS, DDD táctico, Outbox, mensajería, tests en 4 niveles, Docker, K8s, CI/CD.

```
┌─────────────────────────────────────────┐
│           Presentation / API            │  Controllers, Middleware, JWT, Swagger
├─────────────────────────────────────────┤
│             Application                 │  CQRS (MediatR), Validators, Result
├─────────────────────────────────────────┤
│               Domain                    │  Entidades, VOs, Eventos, Specs  ← el centro
├─────────────────────────────────────────┤
│            Infrastructure               │  EF Core, Outbox, MassTransit, Redis
└─────────────────────────────────────────┘
        Las dependencias apuntan HACIA DENTRO
```

## ⚡ Quick start

```bash
# Opción exprés: todo en Docker (API + SQL Server + RabbitMQ + Redis + Seq)
docker compose -f docker/docker-compose.yml up -d
# → Swagger:  http://localhost:5000/swagger
# → Login:    demo@renovables.demo / Demo1234!

# Opción desarrollo: script guiado (comprueba, levanta, migra y testea)
.\scripts\setup.ps1          # Windows
./scripts/setup.sh           # Linux/Mac

# Tests (los de integración se omiten solos si no hay Docker)
dotnet test
```

Guía completa de instalación y problemas frecuentes: [`docs/SETUP.md`](docs/SETUP.md).

## 🗺️ Por dónde empezar a estudiar

| Quiero entender... | Lee |
|---|---|
| La arquitectura y el flujo de un request | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) |
| **Cómo añadir una feature paso a paso** ⭐ | [`docs/GUIA_NUEVAS_FEATURES.md`](docs/GUIA_NUEVAS_FEATURES.md) |
| Cada patrón (CQRS, Outbox, Result, Specification...) | [`docs/PATRONES.md`](docs/PATRONES.md) |
| **Testing desde cero** (nunca has escrito un test) | [`docs/GUIA_TESTING.md`](docs/GUIA_TESTING.md) |
| Las reglas de código del proyecto | [`docs/CONVENTIONS.md`](docs/CONVENTIONS.md) |
| Los endpoints y sus códigos de error | [`docs/API.md`](docs/API.md) |
| **Practicar con ejercicios guiados** (hay huecos dejados a propósito) | [`docs/EJERCICIOS.md`](docs/EJERCICIOS.md) |
| Repasar antes de una entrevista (preguntas → código del repo) | [`docs/PREGUNTAS_ENTREVISTA.md`](docs/PREGUNTAS_ENTREVISTA.md) |
| Cada capa por dentro | El `README.md` de cada proyecto en `src/` |

Rutas de lectura del CÓDIGO (de dentro hacia fuera, como se construye):

1. `src/RenewableEnergy.Domain/Entities/ProyectoRenovable.cs` — el agregado y sus reglas.
2. `src/RenewableEnergy.Application/Features/Proyectos/Commands/CrearProyecto/` — un caso de uso completo.
3. `src/RenewableEnergy.Infrastructure/Persistence/Interceptors/OutboxInterceptor.cs` — la magia del Outbox.
4. `src/RenewableEnergy.API/Controllers/ProyectosController.cs` — el HTTP fino.
5. `tests/` — cómo se prueba cada nivel.

## 📦 Estructura del repositorio

```
├── src/
│   ├── RenewableEnergy.Domain/           # Negocio puro (sin dependencias)
│   ├── RenewableEnergy.Application/      # Casos de uso CQRS
│   ├── RenewableEnergy.Infrastructure/   # EF Core, Outbox, MassTransit, Redis
│   └── RenewableEnergy.API/              # Controllers, JWT, Swagger
├── tests/
│   ├── RenewableEnergy.Domain.Tests/         # Unitarios de dominio (sin mocks)
│   ├── RenewableEnergy.Application.Tests/    # Handlers con Moq + Builder
│   ├── RenewableEnergy.Architecture.Tests/   # ArchUnitNET: capas vigiladas
│   ├── RenewableEnergy.Infrastructure.Tests/ # Testcontainers (SQL Server real)
│   └── RenewableEnergy.API.Tests/            # E2E con WebApplicationFactory
├── docker/            # Dockerfile multi-stage + compose con healthchecks
├── k8s/               # Deployment (probes), Service, ConfigMap, Ingress TLS
├── .github/workflows/ # CI/CD: build+test → Sonar → imagen → deploy manual
├── .postman/          # Colección con tests automáticos + 3 environments
├── scripts/           # setup.ps1 / setup.sh / generar.sh (scaffolding)
└── docs/              # Toda la documentación en español
```

## 🧰 Stack

**.NET 10 · C# 14** — records, primary constructors, collection expressions
**Patrones** — Clean Architecture, CQRS, DDD táctico (agregados, VOs, eventos de dominio), Repository + UoW, Specification, Outbox, Result, Pipeline Behaviors, Builder
**Librerías** — MediatR · FluentValidation · AutoMapper · EF Core 10 (SQL Server, PostgreSQL alternativo) · MassTransit + RabbitMQ (Azure Service Bus documentado) · Redis · Serilog + Seq · OpenTelemetry · Polly · JWT Bearer · Asp.Versioning · Swashbuckle
**Tests** — xUnit · AwesomeAssertions · Moq · Testcontainers · ArchUnitNET · WebApplicationFactory
**DevOps** — Docker multi-stage · docker-compose · GitHub Actions · Kubernetes · Azure Key Vault
**Gobernanza** — Central Package Management (`Directory.Packages.props`) · `global.json` · `.editorconfig`

> Nota de licencias: se fijan las **últimas versiones open source** de MediatR
> (12.5), AutoMapper (14) y MassTransit (8.5) — las versiones posteriores son
> comerciales. La decisión está documentada en `Directory.Packages.props`.

## ✅ Estado de calidad

- `dotnet build` — sin errores ni warnings (`-warnaserror` en CI).
- `dotnet test` — 111 tests en verde + 6 de integración (corren donde hay Docker, p. ej. en CI).
- Cobertura de líneas: **Domain 82 %** y **Application 81 %** (donde vive el negocio).
- 9 reglas de arquitectura ejecutables (las capas no se pueden romper sin que el CI avise).

## 📚 Origen

Evolución del arquetipo `Arquetipo` original (Mediator + Mapster + PostgreSQL),
conservado en el historial de git (`git log --oneline`). El scaffolding genérico
`scripts/generar.sh` permite crear soluciones nuevas desde cero.
