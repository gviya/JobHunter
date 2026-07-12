#!/bin/bash
# ==================================================================
# setup.sh — Puesta en marcha del entorno de desarrollo (Linux/Mac).
#
# USO:  ./scripts/setup.sh            (desde la raíz del repositorio)
#       ./scripts/setup.sh --no-docker
#
# Hace, en orden: comprueba requisitos → levanta la infraestructura
# con Docker → restaura y compila → aplica migraciones → lanza tests.
# ==================================================================
set -euo pipefail

CYAN='\033[0;36m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; NC='\033[0m'
paso() { echo -e "\n${CYAN}==> $1${NC}"; }
ok()   { echo -e "    ${GREEN}OK: $1${NC}"; }
aviso(){ echo -e "    ${YELLOW}AVISO: $1${NC}"; }

SIN_DOCKER=false
[[ "${1:-}" == "--no-docker" ]] && SIN_DOCKER=true

# ── 1. Requisitos ──────────────────────────────────────────────────
paso "Comprobando requisitos"
command -v dotnet >/dev/null || { echo "Falta el SDK de .NET"; exit 1; }
ok "SDK de .NET $(dotnet --version)"

DOCKER_OK=false
if ! $SIN_DOCKER && docker info >/dev/null 2>&1; then
  DOCKER_OK=true
  ok "Docker en marcha"
else
  aviso "Docker no disponible. Continuo sin infraestructura."
fi

# ── 2. Infraestructura ─────────────────────────────────────────────
if $DOCKER_OK; then
  paso "Levantando la infraestructura con docker compose"
  docker compose -f docker/docker-compose.yml up -d sqlserver rabbitmq redis seq
  ok "Servicios arrancando (los healthchecks tardan ~30s)"
fi

# ── 3. Restaurar y compilar ────────────────────────────────────────
paso "Restaurando y compilando la solución"
dotnet build RenewableEnergy.sln -c Debug
ok "Compilación correcta"

# ── 4. Migraciones ─────────────────────────────────────────────────
if $DOCKER_OK; then
  paso "Aplicando migraciones de EF Core"
  for intento in 1 2 3 4 5; do
    if dotnet dotnet-ef database update -p src/RenewableEnergy.Infrastructure -s src/RenewableEnergy.Infrastructure; then
      break
    fi
    aviso "SQL Server aún no está listo (intento $intento de 5)..."
    sleep 10
  done
  ok "Base de datos actualizada"
fi

# ── 5. Tests ───────────────────────────────────────────────────────
paso "Ejecutando los tests"
dotnet test RenewableEnergy.sln --no-build
ok "Todos los tests en verde"

# ── Resumen ────────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}Entorno listo. Próximos pasos:${NC}"
echo "  dotnet run --project src/RenewableEnergy.API"
echo "  Swagger:  http://localhost:5000/swagger"
echo "  Login:    demo@renovables.demo / Demo1234!"
if $DOCKER_OK; then
  echo "  Seq:      http://localhost:5341"
  echo "  RabbitMQ: http://localhost:15672 (guest/guest)"
fi
