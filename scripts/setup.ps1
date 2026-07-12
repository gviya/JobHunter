# ==================================================================
# setup.ps1 — Puesta en marcha del entorno de desarrollo (Windows).
#
# USO:  .\scripts\setup.ps1          (desde la raíz del repositorio)
#       .\scripts\setup.ps1 -SinDocker   (si no tienes Docker)
#
# Hace, en orden: comprueba requisitos → levanta la infraestructura
# con Docker → restaura y compila → aplica migraciones → lanza tests.
# ==================================================================
param(
    [switch]$SinDocker
)

$ErrorActionPreference = "Stop"

function Paso($mensaje) { Write-Host "`n==> $mensaje" -ForegroundColor Cyan }
function Ok($mensaje)   { Write-Host "    OK: $mensaje" -ForegroundColor Green }

# ── 1. Requisitos ──────────────────────────────────────────────────
Paso "Comprobando requisitos"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "No se encontró el SDK de .NET. Instálalo desde https://dotnet.microsoft.com/download"
}
Ok "SDK de .NET $(dotnet --version)"

$dockerDisponible = $false
if (-not $SinDocker) {
    try {
        docker info | Out-Null
        if ($LASTEXITCODE -eq 0) { $dockerDisponible = $true; Ok "Docker en marcha" }
    } catch {}
    if (-not $dockerDisponible) {
        Write-Host "    AVISO: Docker no disponible. Continuo sin infraestructura." -ForegroundColor Yellow
    }
}

# ── 2. Infraestructura (SQL Server, RabbitMQ, Redis, Seq) ──────────
if ($dockerDisponible) {
    Paso "Levantando la infraestructura con docker compose"
    docker compose -f docker/docker-compose.yml up -d sqlserver rabbitmq redis seq
    if ($LASTEXITCODE -ne 0) { throw "docker compose falló" }
    Ok "Servicios arrancando (los healthchecks tardan ~30s)"
}

# ── 3. Restaurar y compilar ─────────────────────────────────────────
Paso "Restaurando y compilando la solución"
dotnet build RenewableEnergy.sln -c Debug
if ($LASTEXITCODE -ne 0) { throw "La compilación falló" }
Ok "Compilación correcta"

# ── 4. Migraciones (solo si hay BD donde aplicarlas) ────────────────
if ($dockerDisponible) {
    Paso "Aplicando migraciones de EF Core"
    # Reintentos: SQL Server tarda en aceptar conexiones tras arrancar.
    $intentos = 0
    do {
        $intentos++
        dotnet dotnet-ef database update -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure
        if ($LASTEXITCODE -eq 0) { break }
        Write-Host "    SQL Server aún no está listo (intento $intentos de 5)..." -ForegroundColor Yellow
        Start-Sleep -Seconds 10
    } while ($intentos -lt 5)
    if ($LASTEXITCODE -ne 0) { throw "No se pudieron aplicar las migraciones" }
    Ok "Base de datos actualizada"
}

# ── 5. Tests ────────────────────────────────────────────────────────
Paso "Ejecutando los tests"
dotnet test RenewableEnergy.sln --no-build
if ($LASTEXITCODE -ne 0) { throw "Hay tests en rojo" }
Ok "Todos los tests en verde"

# ── Resumen ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "Entorno listo. Próximos pasos:" -ForegroundColor Green
Write-Host "  dotnet run --project src/RenewableEnergy.API"
Write-Host "  Swagger:  http://localhost:5000/swagger"
Write-Host "  Login:    demo@renovables.demo / Demo1234!"
if ($dockerDisponible) {
    Write-Host "  Seq:      http://localhost:5341"
    Write-Host "  RabbitMQ: http://localhost:15672 (guest/guest)"
}
