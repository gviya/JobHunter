# ==================================================================
# Dockerfile MULTI-STAGE para la API.
#
# ¿POR QUÉ MULTI-STAGE? (pregunta habitual de entrevista)
# La imagen del SDK pesa >1 GB e incluye compiladores que en producción
# sobran (y amplían la superficie de ataque). Se compila en una etapa
# con SDK y se copia SOLO el resultado a una imagen de runtime (~220 MB).
#
# CONTEXTO DE BUILD: la raíz del repositorio.
#   docker build -f docker/Dockerfile -t renewable-energy-api .
# ==================================================================

# ── ETAPA 1: BUILD (imagen SDK completa) ──────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero SOLO los archivos de dependencias. Docker cachea cada capa:
# si no cambian los .csproj, el restore no se repite en cada build
# (el paso más lento). Cambiar un .cs NO invalida esta capa.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/RenewableEnergy.Domain/RenewableEnergy.Domain.csproj src/RenewableEnergy.Domain/
COPY src/RenewableEnergy.Application/RenewableEnergy.Application.csproj src/RenewableEnergy.Application/
COPY src/RenewableEnergy.Infrastructure/RenewableEnergy.Infrastructure.csproj src/RenewableEnergy.Infrastructure/
COPY src/RenewableEnergy.API/RenewableEnergy.API.csproj src/RenewableEnergy.API/

RUN dotnet restore src/RenewableEnergy.API/RenewableEnergy.API.csproj

# Ahora sí, todo el código fuente.
COPY src/ src/

RUN dotnet build src/RenewableEnergy.API/RenewableEnergy.API.csproj \
    --no-restore -c Release

# ── ETAPA 2: PUBLISH ──────────────────────────────────────────────
FROM build AS publish
RUN dotnet publish src/RenewableEnergy.API/RenewableEnergy.API.csproj \
    --no-build -c Release -o /app/publish

# ── ETAPA 3: RUNTIME (imagen mínima, sin SDK) ─────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Puerto interno del contenedor (Kestrel escucha aquí).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

# SEGURIDAD: nunca ejecutes como root dentro del contenedor.
# La imagen aspnet trae el usuario "app" preparado para esto.
USER app

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "RenewableEnergy.API.dll"]
