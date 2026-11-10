# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# ONE parameterised Dockerfile for every .NET service (build context = /backend).
#
#   docker build --build-arg PROJECT=src/Services/Catalog/Catalog.Api/Catalog.Api.csproj \
#                --build-arg ENTRY=Catalog.Api.dll -t basecamp/catalog .
#
# Multi-stage: the big SDK image compiles; only the small runtime image ships.
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG PROJECT
WORKDIR /src

# Copy build config first so `restore` is cached until package versions change.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet restore "$PROJECT"
RUN dotnet publish "$PROJECT" -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ARG ENTRY
WORKDIR /app
COPY --from=build /app .
# "Urls" (unprefixed) overrides the localhost URL in appsettings.json.
ENV Urls=http://+:8080 \
    ENTRY_DLL=${ENTRY}
EXPOSE 8080
# .NET 8+ images ship a non-root user; don't run as root in production.
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$ENTRY_DLL\""]
