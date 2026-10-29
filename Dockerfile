# ═══════════════════════════════════════════════════════════════
# AlgoTrade — one container = frontend + API + Binance poller
#
#   docker build -t algotrade .
#   docker run -p 8080:10000 \
#     -e Turso__Url=libsql://algotrade-xxx.turso.io \
#     -e Turso__Token=<JWT> \
#     algotrade
#
# Multi-stage: sdk image compiles, slim aspnet runtime ships ~110 MB.
# ═══════════════════════════════════════════════════════════════

# ── build stage ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# restore first (only project files) — layer cache keeps rebuilds fast
COPY AlgoTrade.sln ./
COPY src/AlgoTrade.Core/AlgoTrade.Core.csproj      src/AlgoTrade.Core/
COPY src/AlgoTrade.Data/AlgoTrade.Data.csproj      src/AlgoTrade.Data/
COPY src/AlgoTrade.Api/AlgoTrade.Api.csproj        src/AlgoTrade.Api/
COPY tests/AlgoTrade.Tests/AlgoTrade.Tests.csproj  tests/AlgoTrade.Tests/
RUN dotnet restore AlgoTrade.sln

# publish just the Api project (pulls Core + Data transitively)
COPY src/ src/
RUN dotnet publish src/AlgoTrade.Api/AlgoTrade.Api.csproj \
    -c Release -o /app/publish --no-restore

# ── runtime stage ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Render probes one port; .NET 8+ reads ASPNETCORE_HTTP_PORTS natively
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "AlgoTrade.Api.dll"]
