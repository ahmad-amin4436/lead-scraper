# LeadMine API.
#
# Hosts the REST surface and the stale-job reaper. The reaper lives here rather
# than in the worker because this is the process guaranteed to be up — in the
# worker, the failure it exists to recover from would also take out the recovery.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Restore against the project files alone, so a code-only change reuses the
# cached restore layer.
COPY LeadMine.slnx ./
COPY src/LeadMine.Domain/*.csproj ./src/LeadMine.Domain/
COPY src/LeadMine.Application/*.csproj ./src/LeadMine.Application/
COPY src/LeadMine.Infrastructure/*.csproj ./src/LeadMine.Infrastructure/
COPY src/LeadMine.Api/*.csproj ./src/LeadMine.Api/
RUN dotnet restore src/LeadMine.Api/LeadMine.Api.csproj

COPY . .
RUN dotnet publish src/LeadMine.Api/LeadMine.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

WORKDIR /app

# wget for the compose health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

EXPOSE 8080

# Don't run as root.
USER $APP_UID

ENTRYPOINT ["dotnet", "LeadMine.Api.dll"]
