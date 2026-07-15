FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MRS.Replication.Shared/MRS.Replication.Shared.csproj MRS.Replication.Shared/
COPY MRS.Replication.Watchdog/MRS.Replication.Watchdog.csproj MRS.Replication.Watchdog/
RUN dotnet restore MRS.Replication.Watchdog/MRS.Replication.Watchdog.csproj

COPY MRS.Replication.Shared/ MRS.Replication.Shared/
COPY MRS.Replication.Watchdog/ MRS.Replication.Watchdog/
RUN dotnet publish MRS.Replication.Watchdog/MRS.Replication.Watchdog.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MRS.Replication.Watchdog.dll"]
