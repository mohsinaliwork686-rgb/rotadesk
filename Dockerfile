# One image for the whole app: the React front end is built first and served by the API from its wwwroot, so the
# browser sees one origin and the session cookie needs no CORS.

FROM node:22-alpine AS web
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
# Vite writes the build to ../src/Rotadesk.Api/wwwroot.
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/Rotadesk.Domain/Rotadesk.Domain.csproj src/Rotadesk.Domain/
COPY src/Rotadesk.Infrastructure/Rotadesk.Infrastructure.csproj src/Rotadesk.Infrastructure/
COPY src/Rotadesk.Api/Rotadesk.Api.csproj src/Rotadesk.Api/
RUN dotnet restore src/Rotadesk.Api/Rotadesk.Api.csproj
COPY src/ src/
COPY --from=web /src/src/Rotadesk.Api/wwwroot src/Rotadesk.Api/wwwroot
RUN dotnet publish src/Rotadesk.Api/Rotadesk.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=api /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Rotadesk.Api.dll"]
