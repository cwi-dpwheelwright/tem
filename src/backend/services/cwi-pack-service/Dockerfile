FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/services/cwi-pack-service/cwi-pack-service.sln --locked-mode
RUN dotnet build src/backend/services/cwi-pack-service/cwi-pack-service.sln -c Release --no-restore
RUN dotnet publish src/backend/services/cwi-pack-service/src/Cwi.PackService.Api/Cwi.PackService.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
RUN apt-get update && apt-get upgrade -y && rm -rf /var/lib/apt/lists/*
RUN getent group app >/dev/null || addgroup --system app && \
    id -u app >/dev/null 2>&1 || adduser --system --ingroup app app
COPY --from=build --chown=app:app /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8082
USER app
ENTRYPOINT ["dotnet", "Cwi.PackService.Api.dll"]
