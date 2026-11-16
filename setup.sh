#!/usr/bin/env bash
# Medora - one-time setup on a new macOS/Linux machine.   Run:  bash setup.sh
# Installs project dependencies (not system tools), starts SQL Server in Docker, runs the tests.
set -euo pipefail
cd "$(dirname "$0")"

need() { command -v "$1" >/dev/null 2>&1 || { echo "MISSING: $1 -> $2"; MISSING=1; }; }
MISSING=0
need node   "Node.js 20+: https://nodejs.org"
need dotnet ".NET SDK 10: https://dotnet.microsoft.com/download"
need docker "Docker Desktop (SQL Server runs in a container on macOS/Linux): https://www.docker.com/products/docker-desktop"
[ "$MISSING" = 0 ] || { echo "Install the missing tools and run setup again."; exit 1; }

echo "== Database (SQL Server in Docker) =="
docker start medora-sql >/dev/null 2>&1 || docker run -d --name medora-sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Dev_Passw0rd!' -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest

echo "== API (.NET) =="
(cd backend && dotnet tool restore && dotnet test Medora.slnx)
dotnet user-secrets set ConnectionStrings:Medora 'Server=localhost,1433;Database=Medora;User Id=sa;Password=Dev_Passw0rd!;TrustServerCertificate=True' --project backend/src/Medora.Api

echo "== Mobile app (Expo) =="
(cd mobile && npm ci --no-audit --no-fund && npx tsc --noEmit)

echo
echo "Set your free Groq key yourself (https://console.groq.com -> API Keys):"
echo "  dotnet user-secrets set Ai:ApiKey <gsk_...> --project backend/src/Medora.Api"
echo
echo "Done. Start it with two terminals:"
echo "  1) dotnet run --project backend/src/Medora.Api --launch-profile http   (API on http://localhost:5280)"
echo "  2) cd mobile && npm run web     (or: npx expo start)"
