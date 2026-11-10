FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY tools/Mawrid.MockErp/ tools/Mawrid.MockErp/
RUN dotnet publish tools/Mawrid.MockErp/Mawrid.MockErp.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8090
EXPOSE 8090
USER $APP_UID
ENTRYPOINT ["dotnet", "Mawrid.MockErp.dll"]
