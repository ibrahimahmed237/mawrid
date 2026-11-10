# ---- Build stage: restore and publish with the full SDK ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the files restore needs first, so Docker can cache the restore layer.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Mawrid.Domain/Mawrid.Domain.csproj src/Mawrid.Domain/
COPY src/Mawrid.Application/Mawrid.Application.csproj src/Mawrid.Application/
COPY src/Mawrid.Infrastructure/Mawrid.Infrastructure.csproj src/Mawrid.Infrastructure/
COPY src/Mawrid.Api/Mawrid.Api.csproj src/Mawrid.Api/
RUN dotnet restore src/Mawrid.Api/Mawrid.Api.csproj

COPY src/ src/
RUN dotnet publish src/Mawrid.Api/Mawrid.Api.csproj -c Release -o /app --no-restore

# ---- Runtime stage: small image, no SDK, non-root user ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# The official image ships a non-root "app" user; APP_UID is its id.
USER $APP_UID
ENTRYPOINT ["dotnet", "Mawrid.Api.dll"]
