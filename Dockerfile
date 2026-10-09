# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props global.json Nelt.slnx ./
COPY src/Nelt.Domain/Nelt.Domain.csproj src/Nelt.Domain/
COPY src/Nelt.Application/Nelt.Application.csproj src/Nelt.Application/
COPY src/Nelt.Infrastructure/Nelt.Infrastructure.csproj src/Nelt.Infrastructure/
COPY src/Nelt.Web/Nelt.Web.csproj src/Nelt.Web/
RUN dotnet restore src/Nelt.Web/Nelt.Web.csproj
COPY src/ src/
RUN dotnet publish src/Nelt.Web/Nelt.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    Storage__RootPath=/data/storage
COPY --from=build /app .
RUN mkdir -p /data/storage && chown -R $APP_UID /data
USER $APP_UID
VOLUME ["/data"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "Nelt.Web.dll"]
