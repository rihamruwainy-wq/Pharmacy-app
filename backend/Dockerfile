# Medora API (ASP.NET Core 10) - production image.
# Build (from the backend folder):  docker build -t medora-api .
# Secrets (connection string, JWT key, AI key) come from environment variables at runtime.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props dotnet-tools.json Medora.slnx ./
COPY src/ src/
COPY tests/ tests/
RUN dotnet restore Medora.slnx
RUN dotnet test Medora.slnx -c Release --no-restore
RUN dotnet publish src/Medora.Api/Medora.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

# Chiseled = distroless Ubuntu: no shell or package manager, runs as non-root by default.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 ASPNETCORE_ENVIRONMENT=Production DOTNET_gcServer=1
COPY --from=build /out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Medora.Api.dll"]
