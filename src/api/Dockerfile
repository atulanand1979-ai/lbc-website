# Local development container for the Functions API (Linux).
# Production runs as SWA managed functions; this is for running the API locally
# (e.g. on machines where Windows Smart App Control blocks locally built DLLs).
# Build context: repository root.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json ./
COPY src/Lbc.Core/Lbc.Core.csproj src/Lbc.Core/
COPY src/Lbc.Infrastructure/Lbc.Infrastructure.csproj src/Lbc.Infrastructure/
COPY src/api/Lbc.Api/Lbc.Api.csproj src/api/Lbc.Api/
RUN dotnet restore src/api/Lbc.Api/Lbc.Api.csproj
COPY src/Lbc.Core/ src/Lbc.Core/
COPY src/Lbc.Infrastructure/ src/Lbc.Infrastructure/
COPY src/api/Lbc.Api/ src/api/Lbc.Api/
RUN dotnet publish src/api/Lbc.Api/Lbc.Api.csproj -c Release -o /home/site/wwwroot --no-restore

FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated10.0
ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    FUNCTIONS_WORKER_RUNTIME=dotnet-isolated
COPY --from=build /home/site/wwwroot /home/site/wwwroot
