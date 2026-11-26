# Build context: repository root (see docker-compose.yml)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/Crawler.Core/Crawler.Core.csproj src/Crawler.Core/
COPY src/Crawler.Infrastructure/Crawler.Infrastructure.csproj src/Crawler.Infrastructure/
COPY src/Crawler.Api/Crawler.Api.csproj src/Crawler.Api/
RUN dotnet restore src/Crawler.Api/Crawler.Api.csproj
COPY src/ src/
RUN dotnet publish src/Crawler.Api/Crawler.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV Urls=http://+:8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Crawler.Api.dll"]
