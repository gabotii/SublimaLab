# syntax=docker/dockerfile:1
# Multi-stage build for the SublimaLab API, which also hosts (and prerenders) the Blazor storefront.
# Build context: repository root.
#   default target: the API image
#   --target migrations: a small image that applies pending PostgreSQL migrations, then exits

# Design studio (React + three.js) bundle.
FROM node:22-alpine AS customizer
WORKDIR /src/src/SublimaLab.Customizer
COPY src/SublimaLab.Customizer/package.json src/SublimaLab.Customizer/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/SublimaLab.Customizer/ ./
RUN npx tsc --noEmit && npx vite build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached unless project files change).
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig dotnet-tools.json ./
COPY src/SublimaLab.Domain/SublimaLab.Domain.csproj src/SublimaLab.Domain/
COPY src/SublimaLab.Application/SublimaLab.Application.csproj src/SublimaLab.Application/
COPY src/SublimaLab.Infrastructure/SublimaLab.Infrastructure.csproj src/SublimaLab.Infrastructure/
COPY src/SublimaLab.Migrations.Sqlite/SublimaLab.Migrations.Sqlite.csproj src/SublimaLab.Migrations.Sqlite/
COPY src/SublimaLab.Migrations.Postgres/SublimaLab.Migrations.Postgres.csproj src/SublimaLab.Migrations.Postgres/
COPY src/SublimaLab.Blazor/SublimaLab.Blazor.csproj src/SublimaLab.Blazor/
COPY src/SublimaLab.Api/SublimaLab.Api.csproj src/SublimaLab.Api/
RUN dotnet restore src/SublimaLab.Api/SublimaLab.Api.csproj

COPY src/ src/
COPY --from=customizer /src/src/SublimaLab.Blazor/wwwroot/customizer/ src/SublimaLab.Blazor/wwwroot/customizer/
RUN dotnet publish src/SublimaLab.Api/SublimaLab.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false /p:SkipCustomizerBuild=true

# PostgreSQL migration bundle: a self-contained executable that applies pending migrations.
FROM build AS migrations-build
RUN dotnet tool restore \
 && dotnet ef migrations bundle \
      --project src/SublimaLab.Migrations.Postgres --startup-project src/SublimaLab.Migrations.Postgres \
      --context PostgresAppDbContext --configuration Release --self-contained -r linux-x64 --output /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrations
COPY --from=migrations-build /out/efbundle /efbundle
USER $APP_UID
# Pass the connection string: docker run ... --connection "Host=...;Database=...;Username=...;Password=..."
ENTRYPOINT ["/efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

# Writable data directory for uploads (mount a volume or swap for blob storage in production).
RUN mkdir -p /app/App_Data && chown -R $APP_UID /app/App_Data

# Run as the image's built-in non-root user.
USER $APP_UID
EXPOSE 8080

# Orchestrators should probe GET /health/live (liveness) and GET /health/ready (database).
ENTRYPOINT ["dotnet", "SublimaLab.Api.dll"]
