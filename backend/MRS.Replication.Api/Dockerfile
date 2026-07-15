FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MRS.Replication.Shared/MRS.Replication.Shared.csproj MRS.Replication.Shared/
COPY MRS.Replication.Api/MRS.Replication.Api.csproj MRS.Replication.Api/
RUN dotnet restore MRS.Replication.Api/MRS.Replication.Api.csproj

COPY MRS.Replication.Shared/ MRS.Replication.Shared/
COPY MRS.Replication.Api/ MRS.Replication.Api/
RUN dotnet publish MRS.Replication.Api/MRS.Replication.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MRS.Replication.Api.dll"]
