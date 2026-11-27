# syntax=docker/dockerfile:1
# myDocs – Image für linux/amd64 (Synology DS1520+).
# Build: docker build --secret id=nuget_auth,env=NuGetPackageSourceCredentials_eckiscorner -t mydocs .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props nuget.config .editorconfig MyDocs.slnx ./
COPY src/MyDocs.Shared/MyDocs.Shared.csproj src/MyDocs.Shared/
COPY src/MyDocs.ApiClient/MyDocs.ApiClient.csproj src/MyDocs.ApiClient/
COPY src/MyDocs.UI/MyDocs.UI.csproj src/MyDocs.UI/
COPY src/MyDocs.Web/MyDocs.Web.csproj src/MyDocs.Web/
COPY src/MyDocs.Server/MyDocs.Server.csproj src/MyDocs.Server/
# MyCommon.* kommt aus GitHub Packages; die Anmeldung als BuildKit-Secret (nuget.config, 73).
RUN --mount=type=secret,id=nuget_auth,env=NuGetPackageSourceCredentials_eckiscorner \
    dotnet restore src/MyDocs.Server/MyDocs.Server.csproj

COPY src/ src/
ARG VERSION=0.1.0
RUN dotnet publish src/MyDocs.Server/MyDocs.Server.csproj \
        -c Release -o /app --no-restore \
        -p:Version=${VERSION} -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    MyDocs__DataDirectory=/data \
    DOTNET_gcServer=0

RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
COPY --from=build /app .

USER $APP_UID
EXPOSE 8080
VOLUME ["/data"]

ENTRYPOINT ["dotnet", "MyDocs.Server.dll"]
