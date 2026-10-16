# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first for layer caching.
COPY Directory.Build.props global.json BlueContract.sln ./
COPY src/BlueContract.Domain/BlueContract.Domain.csproj src/BlueContract.Domain/
COPY src/BlueContract.Application/BlueContract.Application.csproj src/BlueContract.Application/
COPY src/BlueContract.Infrastructure/BlueContract.Infrastructure.csproj src/BlueContract.Infrastructure/
COPY src/BlueContract.Api/BlueContract.Api.csproj src/BlueContract.Api/
RUN dotnet restore src/BlueContract.Api/BlueContract.Api.csproj

COPY src/ src/
RUN dotnet publish src/BlueContract.Api/BlueContract.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    TZ=Asia/Ho_Chi_Minh
COPY --from=build /app/publish .
RUN mkdir -p /app/logs && chown -R $APP_UID /app/logs
# Built-in non-root user of the official .NET images.
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "BlueContract.Api.dll"]
