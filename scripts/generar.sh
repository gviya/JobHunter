#!/bin/bash
set -euo pipefail

# =============================================================================
# generar.sh — Clean Architecture .NET scaffold
# Uso: ./generar.sh <NombreProyecto> [--no-docker]
# =============================================================================

# ── Colores ──────────────────────────────────────────────────────────────────
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
ok()   { echo -e "${GREEN}✅ $1${NC}"; }
info() { echo -e "${CYAN}ℹ️  $1${NC}"; }
warn() { echo -e "${YELLOW}⚠️  $1${NC}"; }
die()  { echo -e "${RED}❌ $1${NC}"; exit 1; }

# ── Argumentos ───────────────────────────────────────────────────────────────
[ -z "${1:-}" ] && die "Uso: $0 <NombreProyecto> [--no-docker]"
solutionName="$1"
SKIP_DOCKER=false
[[ "${2:-}" == "--no-docker" ]] && SKIP_DOCKER=true

# ── Validación nombre ─────────────────────────────────────────────────────────
[[ "$solutionName" =~ ^[a-zA-Z][a-zA-Z0-9_.]*$ ]] \
  || die "Nombre inválido. Usa solo letras, números, puntos o guiones bajos."

[ -d "$solutionName" ] && die "El directorio '$solutionName' ya existe."

# ── Dependencias ──────────────────────────────────────────────────────────────
command -v dotnet &>/dev/null || die "dotnet CLI no encontrado."
$SKIP_DOCKER || command -v docker &>/dev/null || warn "docker no encontrado — se omitirá docker-compose."

DOTNET_MAJOR=$(dotnet --version | cut -d. -f1)
[ "$DOTNET_MAJOR" -lt 9 ] && die "Se requiere .NET 9 o superior. Versión actual: $(dotnet --version)"

# ── Scaffold ─────────────────────────────────────────────────────────────────
srcDir="src"
testDir="test"

mkdir -p "$solutionName"
cd "$solutionName" || die "No se pudo entrar en el directorio '$solutionName'."

info "Creando estructura de carpetas..."
mkdir -p "$srcDir" "$testDir"

# ── Solución (.slnx) ──────────────────────────────────────────────────────────
info "Creando solución (.slnx)..."
dotnet new sln -n "$solutionName" --format slnx

# ── Proyectos ─────────────────────────────────────────────────────────────────
info "Creando proyectos..."
dotnet new webapi   -n "${solutionName}.Api"            -o "$srcDir/${solutionName}.Api"            --no-openapi
dotnet new classlib -n "${solutionName}.Application"    -o "$srcDir/${solutionName}.Application"
dotnet new classlib -n "${solutionName}.Domain"         -o "$srcDir/${solutionName}.Domain"
dotnet new classlib -n "${solutionName}.Infrastructure" -o "$srcDir/${solutionName}.Infrastructure"
dotnet new xunit    -n "${solutionName}.Domain.Tests"       -o "$testDir/${solutionName}.Domain.Tests"
dotnet new xunit    -n "${solutionName}.Application.Tests"  -o "$testDir/${solutionName}.Application.Tests"
dotnet new xunit    -n "${solutionName}.Integration.Tests"  -o "$testDir/${solutionName}.Integration.Tests"

# ── Limpiar Class1.cs generados ───────────────────────────────────────────────
info "Limpiando archivos de plantilla..."
find "$srcDir" -name "Class1.cs" -delete
find "$srcDir" -name "WeatherForecast.cs" -delete

# ── Referencias ───────────────────────────────────────────────────────────────
info "Configurando referencias entre proyectos..."

# Application → Domain
dotnet add "$srcDir/${solutionName}.Application/${solutionName}.Application.csproj" reference \
           "$srcDir/${solutionName}.Domain/${solutionName}.Domain.csproj"

# Infrastructure → Application + Domain
dotnet add "$srcDir/${solutionName}.Infrastructure/${solutionName}.Infrastructure.csproj" reference \
           "$srcDir/${solutionName}.Application/${solutionName}.Application.csproj" \
           "$srcDir/${solutionName}.Domain/${solutionName}.Domain.csproj"

# Api → Application + Infrastructure
dotnet add "$srcDir/${solutionName}.Api/${solutionName}.Api.csproj" reference \
           "$srcDir/${solutionName}.Application/${solutionName}.Application.csproj" \
           "$srcDir/${solutionName}.Infrastructure/${solutionName}.Infrastructure.csproj"

# Tests → capas relevantes
dotnet add "$testDir/${solutionName}.Domain.Tests/${solutionName}.Domain.Tests.csproj" reference \
           "$srcDir/${solutionName}.Domain/${solutionName}.Domain.csproj"

dotnet add "$testDir/${solutionName}.Application.Tests/${solutionName}.Application.Tests.csproj" reference \
           "$srcDir/${solutionName}.Application/${solutionName}.Application.csproj" \
           "$srcDir/${solutionName}.Domain/${solutionName}.Domain.csproj"

dotnet add "$testDir/${solutionName}.Integration.Tests/${solutionName}.Integration.Tests.csproj" reference \
           "$srcDir/${solutionName}.Application/${solutionName}.Application.csproj" \
           "$srcDir/${solutionName}.Infrastructure/${solutionName}.Infrastructure.csproj"

# ── Agregar proyectos a la solución ───────────────────────────────────────────
info "Registrando proyectos en la solución..."
dotnet sln "${solutionName}.slnx" add \
  "$srcDir/${solutionName}.Api/${solutionName}.Api.csproj" \
  "$srcDir/${solutionName}.Application/${solutionName}.Application.csproj" \
  "$srcDir/${solutionName}.Domain/${solutionName}.Domain.csproj" \
  "$srcDir/${solutionName}.Infrastructure/${solutionName}.Infrastructure.csproj" \
  "$testDir/${solutionName}.Domain.Tests/${solutionName}.Domain.Tests.csproj" \
  "$testDir/${solutionName}.Application.Tests/${solutionName}.Application.Tests.csproj" \
  "$testDir/${solutionName}.Integration.Tests/${solutionName}.Integration.Tests.csproj"

# ── Paquetes NuGet ────────────────────────────────────────────────────────────
info "Instalando paquetes NuGet..."

# -- Domain (sin dependencias externas, solo contratos base)
dotnet add "$srcDir/${solutionName}.Domain/${solutionName}.Domain.csproj" package \
  Microsoft.Extensions.Logging.Abstractions

# -- Application
APP="$srcDir/${solutionName}.Application/${solutionName}.Application.csproj"
dotnet add "$APP" package Microsoft.Extensions.Configuration.Abstractions
dotnet add "$APP" package Microsoft.Extensions.Logging.Abstractions
dotnet add "$APP" package FluentValidation
dotnet add "$APP" package FluentValidation.DependencyInjectionExtensions
dotnet add "$APP" package Mediator.SourceGenerator      --version "3.0.*"
dotnet add "$APP" package Mediator.Abstractions         --version "3.0.*"
dotnet add "$APP" package ErrorOr

# -- Infrastructure (SQL Server)
INFRA="$srcDir/${solutionName}.Infrastructure/${solutionName}.Infrastructure.csproj"
dotnet add "$INFRA" package Microsoft.EntityFrameworkCore
dotnet add "$INFRA" package Microsoft.EntityFrameworkCore.SqlServer
dotnet add "$INFRA" package Microsoft.EntityFrameworkCore.Tools
dotnet add "$INFRA" package Microsoft.EntityFrameworkCore.Design
dotnet add "$INFRA" package Newtonsoft.Json

# -- Api
API="$srcDir/${solutionName}.Api/${solutionName}.Api.csproj"
dotnet add "$API" package Microsoft.EntityFrameworkCore.Design
dotnet add "$API" package Scalar.AspNetCore

# -- Tests
DTESTS="$testDir/${solutionName}.Domain.Tests/${solutionName}.Domain.Tests.csproj"
ATESTS="$testDir/${solutionName}.Application.Tests/${solutionName}.Application.Tests.csproj"
ITESTS="$testDir/${solutionName}.Integration.Tests/${solutionName}.Integration.Tests.csproj"

for TPROJ in "$DTESTS" "$ATESTS" "$ITESTS"; do
  dotnet add "$TPROJ" package FluentAssertions
  dotnet add "$TPROJ" package NSubstitute
done

dotnet add "$ITESTS" package Microsoft.AspNetCore.Mvc.Testing
dotnet add "$ITESTS" package Microsoft.EntityFrameworkCore.InMemory
dotnet add "$ITESTS" package Respawn

# ── .gitignore ────────────────────────────────────────────────────────────────
info "Generando .gitignore..."
dotnet new gitignore

# ── docker-compose para SQL Server ───────────────────────────────────────────
if ! $SKIP_DOCKER; then
  info "Generando docker-compose.yml con SQL Server 2022..."
  cat > docker-compose.yml << EOF
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: ${solutionName,,}-sqlserver
    environment:
      SA_PASSWORD: "Dev_Password123!"
      ACCEPT_EULA: "Y"
      MSSQL_PID: "Developer"
    ports:
      - "1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql
    healthcheck:
      test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P 'Dev_Password123!' -Q 'SELECT 1' -No || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s

volumes:
  sqlserver_data:
EOF

  cat > docker-compose.override.yml << EOF
# Override para desarrollo local
services:
  sqlserver:
    ports:
      - "1433:1433"
EOF
  ok "docker-compose.yml generado."
fi

# ── appsettings base ─────────────────────────────────────────────────────────
info "Configurando appsettings.Development.json..."
APPSETTINGS="$srcDir/${solutionName}.Api/appsettings.Development.json"
cat > "$APPSETTINGS" << EOF
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost,1433;Database=${solutionName}Db;User Id=sa;Password=Dev_Password123!;TrustServerCertificate=True;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  }
}
EOF

# ── README rápido ─────────────────────────────────────────────────────────────
cat > README.md << EOF
# ${solutionName}

Clean Architecture .NET con SQL Server.

## Requisitos
- .NET 9+
- Docker (para SQL Server local)

## Arrancar en local

\`\`\`bash
# 1. Levantar SQL Server
docker compose up -d

# 2. Aplicar migraciones
dotnet ef database update -p src/${solutionName}.Infrastructure -s src/${solutionName}.Api

# 3. Ejecutar API
dotnet run --project src/${solutionName}.Api
\`\`\`

## Crear una migración
\`\`\`bash
dotnet ef migrations add <NombreMigración> -p src/${solutionName}.Infrastructure -s src/${solutionName}.Api
\`\`\`

## Conexión SQL Server (cliente visual)
- **Host**: localhost, **Puerto**: 1433
- **Usuario**: sa / **Contraseña**: Dev_Password123!
- Clientes recomendados: Azure Data Studio, DBeaver, SSMS
EOF

# ── Resumen final ─────────────────────────────────────────────────────────────
echo ""
ok "Proyecto '${solutionName}' generado con éxito."
echo ""
echo -e "${CYAN}Estructura:${NC}"
echo "  ${solutionName}/"
echo "  ├── src/"
echo "  │   ├── ${solutionName}.Api"
echo "  │   ├── ${solutionName}.Application"
echo "  │   ├── ${solutionName}.Domain"
echo "  │   └── ${solutionName}.Infrastructure"
echo "  ├── test/"
echo "  │   ├── ${solutionName}.Domain.Tests"
echo "  │   ├── ${solutionName}.Application.Tests"
echo "  │   └── ${solutionName}.Integration.Tests"
echo "  └── ${solutionName}.slnx"
echo ""
echo -e "${YELLOW}Próximos pasos:${NC}"
echo "  cd ${solutionName}"
$SKIP_DOCKER || echo "  docker compose up -d"
echo "  dotnet build"
echo "  # Después de configurar tu DbContext:"
echo "  dotnet ef migrations add InitialCreate -p src/${solutionName}.Infrastructure -s src/${solutionName}.Api"