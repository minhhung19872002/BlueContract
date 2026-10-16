# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching.
COPY Directory.Build.props global.json BlueProcure.sln ./
COPY src/BlueProcure.Domain/BlueProcure.Domain.csproj src/BlueProcure.Domain/
COPY src/BlueProcure.Application/BlueProcure.Application.csproj src/BlueProcure.Application/
COPY src/BlueProcure.Infrastructure/BlueProcure.Infrastructure.csproj src/BlueProcure.Infrastructure/
COPY src/BlueProcure.Api/BlueProcure.Api.csproj src/BlueProcure.Api/
RUN dotnet restore src/BlueProcure.Api/BlueProcure.Api.csproj

COPY src/ src/
RUN dotnet publish src/BlueProcure.Api/BlueProcure.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Ho_Chi_Minh
COPY --from=build /app/publish .
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
# Built-in non-root user of the official .NET images.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "BlueProcure.Api.dll"]
