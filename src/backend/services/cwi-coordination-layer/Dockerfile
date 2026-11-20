FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/services/cwi-coordination-layer/cwi-coordination-layer.sln --locked-mode
RUN dotnet build src/backend/services/cwi-coordination-layer/cwi-coordination-layer.sln -c Release --no-restore
RUN dotnet publish src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Api/Cwi.Coordination.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
RUN getent group app >/dev/null || addgroup --system app && \
    id -u app >/dev/null 2>&1 || adduser --system --ingroup app app
COPY --from=build --chown=app:app /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8081
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s CMD curl -fsS http://localhost:8081/health || exit 1
ENTRYPOINT ["dotnet", "Cwi.Coordination.Api.dll"]
