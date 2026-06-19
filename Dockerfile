# syntax=docker/dockerfile:1.4
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy everything (solution, projects, and source)
COPY . .

# Restore with GitHub Packages NuGet auth injected as a BuildKit secret.
# The PAT is never baked into image layers.
RUN --mount=type=secret,id=nuget_token \
    dotnet nuget add source \
        "https://nuget.pkg.github.com/BoomBustFantasy/index.json" \
        --name "BoomBustFantasy" \
        --username "BoomBustFantasy" \
        --password "$(cat /run/secrets/nuget_token)" \
        --store-password-in-clear-text \
    && dotnet restore

WORKDIR /src/EmailService
RUN dotnet publish -c Release -o /app

# Runtime stage
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app
COPY --from=build /app ./

# Create log directory
RUN mkdir -p /app/logs

# Run as non-root user for security
RUN useradd -m -u 1001 appuser && chown -R appuser:appuser /app
USER appuser

ENTRYPOINT ["dotnet", "EmailService.dll"]
