# JobHunter AI

Sistema en .NET 10 que busca ofertas de empleo backend .NET en varias fuentes,
las deduplica, las analiza con Claude Haiku (API de Anthropic), calcula una
puntuación personalizada según un perfil de candidato y envía un informe
diario por email. Todo corre en un único proceso, con Hangfire como
scheduler y SQLite como almacenamiento — sin infraestructura externa.

```
┌─────────────────────────────────────────┐
│                  Api                    │  Composition root, endpoints, Hangfire
├─────────────────────────────────────────┤
│              Application                │  CQRS (MediatR): RunDailyPipeline, GetLatestReport
├─────────────────────────────────────────┤
│                Domain                   │  JobOffer, JobScore, UserProfile  ← el centro
├─────────────────────────────────────────┤
│             Infrastructure              │  EF Core+SQLite, providers, Claude, Ollama, MailKit
└─────────────────────────────────────────┘
        Las dependencias apuntan HACIA DENTRO
```

## Qué hace el pipeline diario

1. **Busca** en paralelo en todas las fuentes activas (Adzuna, Greenhouse,
   Lever — InfoJobs pendiente de OAuth2).
2. **Deduplica** exacto por `(ExternalId, SourceProvider)` y semántico por
   similitud coseno de embeddings (Ollama) contra ofertas recientes de la
   misma empresa.
3. **Analiza** cada oferta nueva con Claude Haiku contra el perfil del
   candidato (resumen, seniority, techs, pros/contras, recomendación).
4. **Puntúa** (modo de trabajo, cliente final vs. consultora, salario, stack
   conocido/excluido) y genera el **informe HTML** con el top 15.
5. **Envía** el informe por email (MailKit/SMTP) si hay ofertas en el top.

## Estructura del repositorio

```
├── src/
│   ├── JobHunter.Domain/           # Entidades + interfaces, sin dependencias externas
│   ├── JobHunter.Application/      # Casos de uso CQRS (RunDailyPipeline, GetLatestReport)
│   ├── JobHunter.Infrastructure/   # EF Core+SQLite, providers HTTP, Claude, Ollama, MailKit, Hangfire job
│   └── JobHunter.Api/              # Composition root, endpoints mínimos, Program.cs
├── tests/
│   ├── JobHunter.Application.Tests/    # Scoring (JobScoreCalculator)
│   ├── JobHunter.Infrastructure.Tests/ # Persistencia SQLite, cosine similarity, deserialización Adzuna
│   └── JobHunter.Architecture.Tests/   # ArchUnitNET: reglas de capas vigiladas
├── Directory.Packages.props   # Central Package Management: versiones NuGet
├── Directory.Build.props      # TargetFramework, Nullable, etc. comunes a todos los proyectos
└── global.json                # SDK de .NET fijado
```

## Stack

**.NET 10 · C# 14** — primary constructors, collection expressions, records
**Patrones** — Clean Architecture, CQRS (MediatR), Repository, Options, pipeline behaviors (logging/performance/validation)
**Persistencia** — EF Core 10 + SQLite (migraciones en `Infrastructure/Persistence/Migrations`)
**Scheduler** — Hangfire (storage SQLite), dashboard en `/hangfire`
**Fuentes de empleo** — Adzuna (API REST), Greenhouse y Lever (boards públicos de empresas objetivo)
**IA** — Claude Haiku vía HttpClient puro contra la Messages API de Anthropic; embeddings vía Ollama local
**Email** — MailKit (SMTP)
**Tests** — xUnit · AwesomeAssertions · ArchUnitNET

## Configuración

Los valores no sensibles viven en `src/JobHunter.Api/appsettings.json` (con
placeholders vacíos). Los secretos (API keys, contraseñas) **no van en ese
fichero** — usa User Secrets de .NET en desarrollo:

```powershell
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..." --project src/JobHunter.Api
dotnet user-secrets set "Adzuna:AppId" "..." --project src/JobHunter.Api
dotnet user-secrets set "Adzuna:AppKey" "..." --project src/JobHunter.Api
dotnet user-secrets set "Email:Username" "..." --project src/JobHunter.Api
dotnet user-secrets set "Email:Password" "..." --project src/JobHunter.Api
dotnet user-secrets set "Email:From" "..." --project src/JobHunter.Api
dotnet user-secrets set "Email:To" "..." --project src/JobHunter.Api
```

Secciones configurables: `Anthropic`, `Adzuna`, `InfoJobs` (sin implementar
todavía), `TargetCompanies:{Greenhouse,Lever}` (board tokens, no son
secretos), `Ollama`, `Email`, `Schedule:CronExpression`.

## Arrancar en local

```powershell
dotnet run --project src/JobHunter.Api
```

Aplica migraciones automáticamente, crea `data/*.db` y arranca el servidor
de Hangfire. Endpoints:

- `GET  /health` — estado.
- `GET  /hangfire` — dashboard del scheduler.
- `POST /jobs/run-now` — lanza el pipeline sin esperar al cron.
- `GET  /reports/latest` — último informe generado (HTML).

## Tests

```powershell
dotnet test
```

## Fuera de alcance (pendiente)

- `InfoJobsProvider` — esqueleto sin implementar (falta el flujo OAuth2).
- Scraping con Playwright (Tecnoempleo, WTTJ).
- Empaquetado Docker / despliegue.

## Créditos
- **gviya** — Desarrollo principal, arquitectura y decisiones técnicas
- Asistido por IA (Claude) para generación y revisión de código