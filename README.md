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
   Lever, JSearch, Jooble — InfoJobs pendiente de OAuth2) con las keywords
   del perfil. Greenhouse y Lever devuelven el board completo de la empresa,
   así que sus ofertas pasan antes un filtro por título (rol técnico, techs
   del perfil o términos de las keywords) para no analizar puestos de
   ventas, marketing, etc.
2. **Filtra por ubicación** en dos pasos: primero país (España o remoto sin
   especificar), después desplazamiento — el remoto pasa siempre (prioridad
   máxima); híbrido/presencial solo si su localización es compatible con la
   ciudad y zonas del perfil (los municipios excluidos se descartan antes de
   gastar análisis de IA). El modo de trabajo sale de los datos
   estructurados de la fuente si los hay (`workplaceType` de Lever,
   `job_is_remote` de JSearch) y si no de una heurística por campos: título
   y ubicación mandan sobre la descripción, y "híbrido con 2 días de
   teletrabajo" o "no es remoto" ya no cuentan como remoto.
3. **Deduplica** en tres pasos: exacto por `(ExternalId, SourceProvider)`;
   por empresa + título normalizados (sin acentos ni marcadores de género),
   dentro de la ejecución y contra los últimos 30 días, quedándose con la
   versión de descripción más completa — funciona sin Ollama, así que la
   misma oferta publicada en Adzuna, JSearch y Jooble se analiza una sola
   vez; y semántico por similitud coseno de embeddings (Ollama) contra
   ofertas recientes de la misma empresa. Dos vacantes con el mismo título
   en la misma empresa en ese plazo cuentan como una.
4. **Analiza** cada oferta nueva con IA (Claude Haiku u Ollama local) contra
   el perfil del candidato (resumen, seniority, techs, pros/contras,
   recomendación). La IA también dice si la empresa es cliente final o
   consultora/agencia y cuál es el modo de trabajo real según el texto; si
   lo sabe, corrige lo que supuso la fuente antes de puntuar. Las que
   fallaron en ejecuciones anteriores (IA caída, créditos agotados...) se
   reintentan mientras estén dentro de la ventana del informe
   (`ReportWindowDays`, 7 días por defecto).
5. **Puntúa** (modo de trabajo/zona, cliente final vs. consultora, salario,
   stack con pesos 0-10, techs excluidas, calidad de la información) y genera
   el **informe HTML** con el top 15 de los últimos `ReportWindowDays` días
   (lo puntuado en esta ejecución va marcado como "Nueva"), con un filtro
   interactivo en el navegador para excluir/exigir términos (p. ej. "sin
   inglés", "solo remoto") y chips por tecnología.
6. **Envía** el informe por email (MailKit/SMTP) solo si el top trae alguna
   oferta nueva. El job no se reintenta automáticamente ni se ejecuta dos
   veces a la vez (cron + `/jobs/run-now`): cada ejecución gasta cuota.

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

### Seguridad de los endpoints

`/jobs/run-now` (dispara el pipeline, que consume cuota de APIs de pago),
`/reports/latest` (expone el informe con datos personales) y el dashboard de
Hangfire (`/hangfire`) están protegidos:

- **Peticiones locales** (loopback / misma máquina): siempre permitidas — el
  uso normal en local no requiere configurar nada. Excepción: una petición con
  cabeceras de proxy (`X-Forwarded-For`, `Forwarded`, `X-Real-IP`) **nunca**
  cuenta como local, aunque llegue desde loopback — con un proxy inverso en la
  misma máquina todo el tráfico externo entra por loopback y, sin esa regla,
  pasaría sin clave.
- **Desde fuera de la máquina**: hay que aportar `Security:ApiKey`. Los
  endpoints la esperan en la cabecera `X-Api-Key`; el dashboard, al abrirse en
  el navegador, la acepta como query string `?apiKey=...` (al validarla deja
  una cookie HttpOnly limitada a `/hangfire` para que el polling AJAX interno
  del dashboard siga autorizado).
- Si no se configura ninguna clave, **solo** se admite acceso local: el
  servicio nunca queda abierto por accidente al exponerlo en red.

```powershell
dotnet user-secrets set "Security:ApiKey" "una-clave-larga-aleatoria" --project src/JobHunter.Api
```

`/jobs/run-now` además está limitado a 3 ejecuciones/minuto **por IP** (rate
limiting particionado: un cliente remoto no puede agotar el cupo del usuario
local). Fuera de `Development` se fuerza HTTPS (redirección + HSTS); restringe
también `AllowedHosts` al host real en producción.

Nota sobre logs: `System.Net.Http` está en `Warning` en la configuración de
Serilog a propósito — a `Information` el `HttpClientFactory` escribe la URL
completa de cada petición saliente, y Adzuna/Jooble llevan la API key en la
URL. No lo bajes de nivel.

### Perfil personal (`appsettings.Local.json`)

Las preferencias del candidato — tecnologías con su peso 0-10, tecnologías
excluidas, ciudad y zonas para el filtro de desplazamiento, keywords de
búsqueda, salario — viven en `src/JobHunter.Api/appsettings.Local.json`.
Ese fichero está en `.gitignore`: **es privado y no se sube al repositorio**.

Para crear el tuyo, copia la plantilla comentada y rellénala con tu perfil:

```powershell
Copy-Item src/JobHunter.Api/appsettings.Local.template.json src/JobHunter.Api/appsettings.Local.json
```

Cada campo está explicado en la propia plantilla
([appsettings.Local.template.json](src/JobHunter.Api/appsettings.Local.template.json)).
Resumen del modelo de puntuación/filtrado que alimenta:

- **`TechWeights`** — escala 0-10 por tecnología (8-10 experiencia alta,
  4-7 media, 1-3 básica). Cada tech presente en la oferta suma su peso
  (tope +40). Las de `ExcludedTechs` restan fuerte (-12 por señal, tope -36).
- **Ubicación** — remoto 100% pasa siempre y puntúa el máximo (+30);
  híbrido solo si la localización menciona `City` o una `PreferredZones`
  (+15, +5 si es zona preferida) y no menciona una `ExcludedAreas`;
  presencial igual pero penalizado (-10). Lo que no cumple se descarta
  antes del análisis de IA; si la IA revela después que una oferta "remota"
  es híbrida/presencial y no encaja (o el perfil no acepta ese modo), se
  hunde (-25/-30).
- **Cliente final** +20, consultora/agencia -10, no se sabe 0 (lo decide la
  IA leyendo la oferta, no la fuente).
- **Calidad de la información** — según la longitud de la descripción ya
  limpia de HTML (de -25 a +8). Adzuna y Jooble solo dan un extracto, así
  que en sus ofertas cuenta 0: la longitud no dice nada de la oferta real.

Si no existe `appsettings.Local.json`, la aplicación arranca con un perfil
de ejemplo genérico (backend .NET) definido en `UserProfile.CreateDefault()`.

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