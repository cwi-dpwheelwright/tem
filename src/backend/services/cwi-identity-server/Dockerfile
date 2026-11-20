FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/backend/services/cwi-identity-server/cwi-identity-server.sln --locked-mode
RUN dotnet build src/backend/services/cwi-identity-server/cwi-identity-server.sln -c Release --no-restore
RUN dotnet publish src/backend/services/cwi-identity-server/src/Cwi.Identity.Api/Cwi.Identity.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8085
EXPOSE 8085
RUN chown -R app:app /app
USER app
ENTRYPOINT ["dotnet", "Cwi.Identity.Api.dll"]
